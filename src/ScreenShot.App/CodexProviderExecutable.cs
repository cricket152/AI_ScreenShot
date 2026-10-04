using System.ComponentModel;
using System.Diagnostics;

namespace ScreenShot.App;

internal static class CodexProviderExecutable
{
    internal static string Resolve(string? configured)
    {
        if(!string.IsNullOrWhiteSpace(configured))
        {
            if(!Path.IsPathFullyQualified(configured)||!configured.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||!File.Exists(configured))
                throw new InvalidOperationException("请在设置中选择有效的 Codex CLI 可执行文件（codex.exe）");
            return Path.GetFullPath(configured);
        }
        string desktopBin=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
        if(DesktopCandidate(desktopBin,RunningExecutables()) is string desktop)return desktop;
        foreach(string directory in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries))
        {
            string root=directory.Trim().Trim('"');
            foreach(string relative in new[]{"codex.exe",Path.Combine("node_modules","@openai","codex","node_modules","@openai","codex-win32-x64","vendor","x86_64-pc-windows-msvc","bin","codex.exe"),Path.Combine("node_modules","@openai","codex","vendor","x86_64-pc-windows-msvc","codex","codex.exe")})
            {
                string candidate=Path.Combine(root,relative);
                if(Path.IsPathFullyQualified(candidate)&&File.Exists(candidate))return Path.GetFullPath(candidate);
            }
        }
        throw new InvalidOperationException("未找到 Codex CLI；请安装并登录 Codex CLI，或在设置中选择 codex.exe");
    }

    internal static string? DesktopCandidate(string binDirectory,IEnumerable<string> runningExecutables)
    {
        string root=Path.GetFullPath(binDirectory);
        foreach(string candidate in runningExecutables)
            if(InDesktopBin(candidate,root)&&File.Exists(candidate))return Path.GetFullPath(candidate);

        string[] directories;
        try{directories=Directory.GetDirectories(root);}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException){return null;}
        var installed=new List<(string Path,DateTime Modified)>();
        foreach(string directory in directories)
        {
            string candidate=Path.Combine(directory,"codex.exe");
            if(!File.Exists(candidate))continue;
            try{installed.Add((candidate,File.GetLastWriteTimeUtc(candidate)));}
            catch(Exception error) when(error is IOException or UnauthorizedAccessException){ }
        }
        return installed.OrderByDescending(candidate=>candidate.Modified)
            .ThenBy(candidate=>candidate.Path,StringComparer.OrdinalIgnoreCase).Select(candidate=>candidate.Path).FirstOrDefault();
    }

    private static bool InDesktopBin(string candidate,string root)=>Path.IsPathFullyQualified(candidate)&&
        string.Equals(Path.GetFileName(candidate),"codex.exe",StringComparison.OrdinalIgnoreCase)&&
        Path.GetFullPath(candidate).StartsWith(Path.TrimEndingDirectorySeparator(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> RunningExecutables()
    {
        Process[] processes;
        try{processes=Process.GetProcessesByName("codex");}
        catch(Win32Exception){return [];}
        var paths=new List<string>();
        foreach(var process in processes)
        {
            using(process)
                try
                {
                    if(process.MainModule?.FileName is {Length:>0} path)paths.Add(path);
                }
                catch(Exception error) when(error is Win32Exception or InvalidOperationException or NotSupportedException or System.Security.SecurityException){ }
        }
        return paths;
    }
}
