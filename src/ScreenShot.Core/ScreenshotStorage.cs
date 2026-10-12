using System.Collections.Concurrent;

namespace ScreenShot.Core;

public sealed record ScreenshotStorageInfo(int FileCount,long TotalBytes,int UnusedCount,long UnusedBytes,bool CanClean,string? Notice=null);
public sealed record ScreenshotCleanupResult(int DeletedCount,long FreedBytes,int SkippedCount,int FailedCount);
public sealed class ScreenshotCleanupPlan
{
    internal sealed record Candidate(string Name,long Length,DateTime ModifiedUtc);
    internal string DirectoryPath {get;}
    internal IReadOnlyList<Candidate> Candidates {get;}
    public ScreenshotStorageInfo Info {get;}
    internal ScreenshotCleanupPlan(string directory,ScreenshotStorageInfo info,IReadOnlyList<Candidate> candidates)
    {DirectoryPath=directory;Info=info;Candidates=candidates;}
}

public static class ScreenshotStorage
{
    public static string Folder(string directory)=>Path.Combine(Path.GetFullPath(directory),"screenshots");
    internal static void CheckFolder(string directory)
    {
        foreach(string path in new[]{directory,Folder(directory)})
            if(Directory.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)
                throw new IOException("截图目录是链接，无法安全清理。请检查本地存储位置。");
    }
    internal static bool GeneratedImage(string name)=>Path.GetFileName(name)==name&&
        string.Equals(Path.GetExtension(name),".png",StringComparison.OrdinalIgnoreCase)&&Guid.TryParseExact(Path.GetFileNameWithoutExtension(name),"N",out _);
    private static void CheckIndex(string directory)
    {
        if(!File.Exists(Path.Combine(directory,"conversations.db")))throw new IOException("未找到对话索引，为保护截图已取消清理。");
        if(Directory.EnumerateFiles(directory,"conversations.db.damaged-*").Any())throw new IOException("存在损坏的对话备份，为保护旧截图已取消清理。");
    }
    public static ScreenshotCleanupPlan Prepare(string directory,CancellationToken token=default)
    {
        directory=Path.GetFullPath(directory);token.ThrowIfCancellationRequested();CheckFolder(directory);
        var files=new List<ScreenshotCleanupPlan.Candidate>();int count=0;long bytes=0;
        if(Directory.Exists(Folder(directory)))
            foreach(string path in Directory.EnumerateFiles(Folder(directory),"*",SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested();var file=new FileInfo(path);
                if(!string.Equals(file.Extension,".png",StringComparison.OrdinalIgnoreCase)||(file.Attributes&FileAttributes.ReparsePoint)!=0)continue;
                count++;bytes+=file.Length;
                if(GeneratedImage(file.Name))files.Add(new(file.Name,file.Length,file.LastWriteTimeUtc));
            }
        if(files.Count==0)return new(directory,new(count,bytes,0,0,true),[]);
        try
        {
            CheckIndex(directory);
            using var store=new ConversationStore(directory,allowRecovery:false);
            var referenced=store.ScreenshotReferences();
            var unused=files.Where(file=>!referenced.Contains(file.Name)).ToArray();
            return new(directory,new(count,bytes,unused.Length,unused.Sum(file=>file.Length),true),unused);
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or InvalidDataException)
        {return new(directory,new(count,bytes,0,0,false,"对话索引无法完整读取，暂不清理截图。"),[]);}
    }
    public static ScreenshotCleanupResult Clean(ScreenshotCleanupPlan plan,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        if(!plan.Info.CanClean)throw new InvalidOperationException("无法确认截图的使用状态，未执行清理。");
        if(plan.Candidates.Count==0)return new(0,0,0,0);
        CheckFolder(plan.DirectoryPath);CheckIndex(plan.DirectoryPath);
        using var store=new ConversationStore(plan.DirectoryPath,allowRecovery:false);
        return store.CleanScreenshots(plan,token);
    }
}

public sealed partial class ConversationStore
{
    private sealed class StoreAccess
    {internal object Gate {get;}=new();internal long Revision;internal HashSet<string> PendingImages {get;}=new(StringComparer.OrdinalIgnoreCase);}
    private static readonly ConcurrentDictionary<string,StoreAccess> StoreLocks=new(StringComparer.OrdinalIgnoreCase);
    internal HashSet<string> ScreenshotReferences()
    {
        lock(gate)return Load(requireAll:true).SelectMany(conversation=>conversation.Messages.Select(message=>message.Image)
            .Append(conversation.DraftImage).OfType<string>()).Concat(access.PendingImages).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
    internal ScreenshotCleanupResult CleanScreenshots(ScreenshotCleanupPlan plan,CancellationToken token)
    {
        HashSet<string> referenced;long revision;
        lock(gate){referenced=ScreenshotReferences();revision=access.Revision;}
        int deleted=0,skipped=0,failed=0;long bytes=0;
        foreach(var candidate in plan.Candidates)
        {
            token.ThrowIfCancellationRequested();
            lock(gate)
            {
                if(revision!=access.Revision){referenced=ScreenshotReferences();revision=access.Revision;}
                if(referenced.Contains(candidate.Name)){skipped++;continue;}
                ScreenshotStorage.CheckFolder(DirectoryPath);
                string path=Path.GetFullPath(Path.Combine(ScreenshotStorage.Folder(DirectoryPath),candidate.Name));
                if(!ScreenshotStorage.GeneratedImage(candidate.Name)||!string.Equals(Path.GetDirectoryName(path),ScreenshotStorage.Folder(DirectoryPath),StringComparison.OrdinalIgnoreCase))
                    throw new IOException("截图路径不在本地存储目录内，已停止清理。");
                try
                {
                    var file=new FileInfo(path);
                    if(!file.Exists||(file.Attributes&FileAttributes.ReparsePoint)!=0||file.Length!=candidate.Length||file.LastWriteTimeUtc!=candidate.ModifiedUtc){skipped++;continue;}
                    File.Delete(path);deleted++;bytes+=candidate.Length;
                }
                catch(Exception error) when(error is IOException or UnauthorizedAccessException){failed++;}
            }
        }
        return new(deleted,bytes,skipped,failed);
    }
}
