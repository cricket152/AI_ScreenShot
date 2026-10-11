using System.Runtime.InteropServices;
using System.Text.Json;

namespace ScreenShot.Core;

public sealed class ConversationMessage
{
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Role {get;set;}="user";
    public string Client {get;set;}="Codex";
    public string Text {get;set;}="";
    public string? Image {get;set;}
    public string? Model {get;set;}
    public string State {get;set;}="Pending";
}
public sealed class Conversation
{
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Title {get;set;}="新对话";
    public bool ManualTitle {get;set;}
    public bool TitleRequested {get;set;}
    public bool IsOpen {get;set;}=true;
    public int Order {get;set;}
    public string Client {get;set;}="Codex";
    public Workspace? Workspace {get;set;}
    public string? ThreadId {get;set;}
    public string? TurnId {get;set;}
    public string Draft {get;set;}="";
    public string? DraftImage {get;set;}
    public HashSet<string> Images {get;set;}=[];
    public List<ConversationMessage> Messages {get;set;}=[];
    public DateTime UpdatedUtc {get;set;}=DateTime.UtcNow;
}
public sealed record AgentHistoryMessage(string Role,string Text,byte[]? Png=null);
public sealed class ConversationContext
{
    public string? ThreadId {get;init;}
    public IReadOnlyList<AgentHistoryMessage> History {get;init;}=[];
    public Action<string,string?>? SessionChanged {get;init;}
}

// Windows ships this SQLite engine. No external service, NuGet native payload or initialization is needed.
public sealed partial class ConversationStore : IDisposable
{
    private nint database;
    private readonly StoreAccess access;
    private object gate=>access.Gate;
    public string DirectoryPath {get;}
    public string? Warning {get;private set;}
    public string? SelectedId {get;private set;}
    public ConversationStore(string directory,bool allowRecovery=true)
    {
        DirectoryPath=Path.GetFullPath(directory);Directory.CreateDirectory(DirectoryPath);
        access=StoreLocks.GetOrAdd(DirectoryPath.TrimEnd(Path.DirectorySeparatorChar),_=>new());
        string path=Path.Combine(DirectoryPath,"conversations.db");
        try{lock(gate)Open(path,initialize:allowRecovery);}
        catch(SqliteFailure error) when(allowRecovery&&error.Code is (11 or 26))
        {
            Dispose();
            string recovery=path+".damaged-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            File.Move(path,recovery);
            foreach(string suffix in new[]{"-wal","-shm"})if(File.Exists(path+suffix))File.Move(path+suffix,recovery+suffix);
            try{Open(path);}catch{Dispose();throw;}
            Warning="对话数据库损坏，已保留备份并建立新数据库；旧数据可从 damaged 备份恢复。";
        }
        catch{Dispose();throw;}
    }
    private void Open(string path,bool initialize=true)
    {
        Check(sqlite3_open_v2(path,out database,(initialize?2|4:1)|0x10000,0));
        sqlite3_busy_timeout(database,3000);
        using(var check=Prepare("PRAGMA quick_check"))
        {if(check.Step()==100&&check.Text(0)!="ok")throw new SqliteFailure(11);}
        using(var version=Prepare("PRAGMA user_version"))
        {
            if(version.Step()==100)
            {
                if(version.Integer(0)>1)throw new InvalidDataException("对话数据库版本较新，请使用新版应用打开。");
                if(!initialize&&version.Integer(0)!=1)throw new InvalidDataException("对话索引未完整初始化，为保护截图已取消清理。");
            }
        }
        if(initialize)
        {
            Execute("PRAGMA journal_mode=WAL");Execute("PRAGMA synchronous=FULL");
            Execute("CREATE TABLE IF NOT EXISTS conversations(id TEXT PRIMARY KEY, data TEXT NOT NULL)");
            Execute("CREATE TABLE IF NOT EXISTS preferences(key TEXT PRIMARY KEY, value TEXT NOT NULL)");
            Execute("PRAGMA user_version=1");
        }
        else
        {
            // Inspection must reject missing schema rather than recreate an empty index and lose image references.
            using var conversations=Prepare("SELECT id,data FROM conversations LIMIT 0");
            using var preferences=Prepare("SELECT key,value FROM preferences LIMIT 0");
        }
        using var selected=Prepare("SELECT value FROM preferences WHERE key='selected'");
        if(selected.Step()==100)SelectedId=selected.Text(0);
    }
    public IReadOnlyList<Conversation> Load()=>Load(requireAll:false);
    private IReadOnlyList<Conversation> Load(bool requireAll)
    {
        lock(gate)
        {
            var result=new List<Conversation>();using var query=Prepare("SELECT id,data FROM conversations");
            while(query.Step()==100)
            {
                try
                {
                    var item=JsonSerializer.Deserialize<Conversation>(query.Text(1));
                    if(item==null||item.Id!=query.Text(0)||!Guid.TryParseExact(item.Id,"N",out _)||item.Title==null||item.Draft==null||item.Client is not ("Codex" or "Agent")||item.Messages==null||item.Messages.Any(m=>m==null||!Guid.TryParseExact(m.Id,"N",out _)||m.Role is not ("user" or "assistant")||m.Text==null||m.Client is not ("Codex" or "Agent")||m.State is not ("Pending" or "Submitted" or "Thinking" or "Sent" or "Unknown" or "Failed")))throw new JsonException();
                    item.Images??=[];
                    if(item.Messages.Select(m=>m.Id).Distinct(StringComparer.Ordinal).Count()!=item.Messages.Count)throw new JsonException();
                    foreach(var message in item.Messages.Where(m=>m.State is "Pending" or "Submitted" or "Thinking"))
                        message.State="Unknown";
                    result.Add(item);
                }
                catch(Exception error) when(error is JsonException or NotSupportedException)
                {
                    if(requireAll)throw new InvalidDataException("部分对话无法读取，为保护截图已取消清理。");
                    Warning="部分历史记录无法读取，已跳过；其他对话仍可使用，原始记录保留在数据库。";
                }
            }
            return result.OrderBy(c=>c.Order).ToArray();
        }
    }
    public void Save(Conversation conversation)
    {
        lock(gate)
        {
            conversation.UpdatedUtc=DateTime.UtcNow;
            using var statement=Prepare("INSERT INTO conversations(id,data) VALUES(?1,?2) ON CONFLICT(id) DO UPDATE SET data=excluded.data");
            statement.Bind(1,conversation.Id);statement.Bind(2,JsonSerializer.Serialize(conversation));statement.Step();
            access.PendingImages.ExceptWith((conversation.Messages??[]).Where(message=>message!=null).Select(message=>message.Image).Append(conversation.DraftImage).OfType<string>());
            access.Revision++;
        }
    }
    public void Select(string id)
    {
        lock(gate)
        {
            using var statement=Prepare("INSERT INTO preferences(key,value) VALUES('selected',?1) ON CONFLICT(key) DO UPDATE SET value=excluded.value");
            statement.Bind(1,id);statement.Step();SelectedId=id;
        }
    }
    public string SaveImage(byte[] png)
    {
        lock(gate)
        {
            string name=Guid.NewGuid().ToString("N")+".png";
            string folder=Path.Combine(DirectoryPath,"screenshots");Directory.CreateDirectory(folder);
            // Finish the image before committing its reference to SQLite.
            File.WriteAllBytes(Path.Combine(folder,name),png);return name;
        }
    }
    public void SaveDraftImage(Conversation conversation,byte[] png)
    {
        lock(gate)
        {
            // Cleanup must never observe an image between writing its bytes and saving its reference.
            conversation.DraftImage=SaveImage(png);conversation.Images.Add(conversation.DraftImage);
            // A failed database save must also preserve the image used by the in-memory draft this session.
            access.PendingImages.Add(conversation.DraftImage);access.Revision++;Save(conversation);
        }
    }
    public byte[] ReadImage(string? name)
    {
        if(name==null)return [];
        if(!Guid.TryParseExact(Path.GetFileNameWithoutExtension(name),"N",out _)||Path.GetFileName(name)!=name||Path.GetExtension(name)!=".png")return [];
        try{return File.ReadAllBytes(Path.Combine(DirectoryPath,"screenshots",name));}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException){Warning="部分截图文件缺失或无法读取，文字历史仍可使用。";return [];}
    }
    public void Delete(Conversation conversation)
    {
        lock(gate)
        {
            using var statement=Prepare("DELETE FROM conversations WHERE id=?1");statement.Bind(1,conversation.Id);statement.Step();
            access.Revision++;
            var referenced=Load().SelectMany(c=>c.Messages.Select(m=>m.Image).Append(c.DraftImage).OfType<string>().Concat(c.Images??[])).ToHashSet(StringComparer.Ordinal);
            foreach(string name in conversation.Messages.Select(m=>m.Image).Append(conversation.DraftImage).OfType<string>().Concat(conversation.Images??[]).Distinct())
            {
                if(referenced.Contains(name))continue;
                if(Path.GetFileName(name)!=name||!Guid.TryParseExact(Path.GetFileNameWithoutExtension(name),"N",out _)||Path.GetExtension(name)!=".png")continue;
                try{File.Delete(Path.Combine(DirectoryPath,"screenshots",name));}
                catch(Exception error) when(error is IOException or UnauthorizedAccessException){Warning="对话已删除，部分截图无法删除，请检查文件权限。";}
            }
        }
    }
    private Statement Prepare(string sql)=>new(this,sql);
    private void Execute(string sql){using var statement=Prepare(sql);while(statement.Step()==100){} }
    private void Check(int code){if(code!=0)throw new SqliteFailure(code&255);}
    public void Dispose(){lock(gate){if(database!=0){sqlite3_close_v2(database);database=0;}}}
    private sealed class SqliteFailure(int code):IOException("本地对话数据库不可用（SQLite "+code+"），请检查磁盘空间和权限") {public int Code=>code;}
    private sealed class Statement : IDisposable
    {
        private nint handle;private readonly ConversationStore owner;
        public Statement(ConversationStore owner,string sql){this.owner=owner;owner.Check(sqlite3_prepare_v2(owner.database,sql,-1,out handle,0));}
        public void Bind(int index,string text)=>owner.Check(sqlite3_bind_text(handle,index,text,-1,new nint(-1)));
        public int Step(){int code=sqlite3_step(handle);if(code is not (100 or 101))owner.Check(code);return code;}
        public string Text(int column)=>Marshal.PtrToStringUTF8(sqlite3_column_text(handle,column))??"";
        public int Integer(int column)=>sqlite3_column_int(handle,column);
        public void Dispose(){if(handle!=0){sqlite3_finalize(handle);handle=0;}}
    }
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)]string name,out nint db,int flags,nint vfs);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_close_v2(nint db);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(nint db,int ms);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(nint db,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,int bytes,out nint statement,nint tail);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_bind_text(nint statement,int index,[MarshalAs(UnmanagedType.LPUTF8Str)]string value,int bytes,nint destructor);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_step(nint statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern nint sqlite3_column_text(nint statement,int column);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_column_int(nint statement,int column);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_finalize(nint statement);
}
