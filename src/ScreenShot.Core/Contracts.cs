using System.Security.Cryptography;
using System.Text.Json;

namespace ScreenShot.Core;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public static PixelRect FromPoints(int x1, int y1, int x2, int y2) => new(Math.Min(x1,x2), Math.Min(y1,y2), Math.Abs(x2-x1), Math.Abs(y2-y1));
    public PixelRect Clamp(PixelRect bounds)
    {
        int left=Math.Max(X,bounds.X), top=Math.Max(Y,bounds.Y);
        return new(left,top,Math.Max(0,Math.Min(X+Width,bounds.X+bounds.Width)-left),Math.Max(0,Math.Min(Y+Height,bounds.Y+bounds.Height)-top));
    }
}
public enum SendState { Sent, Failed, Unknown }
public sealed record SendResult(SendState State, string Message)
{
    public bool Retryable => State == SendState.Failed;
}
public sealed record Workspace(string Name, string? DirectoryPath=null);
public sealed record SendRequest(byte[] Png, string Prompt, Workspace? Workspace)
{
    public string EffectivePrompt => string.IsNullOrWhiteSpace(Prompt) ? "请描述并分析这张截图" : Prompt;
}
public interface ISendTarget { Task<SendResult> SendAsync(SendRequest request, CancellationToken cancellationToken); }
public sealed class Settings
{
    public string Browser { get; set; } = "Edge";
    public string? Workspace { get; set; }
    public string? CodexDirectory { get; set; }
    public List<string> CodexDirectories { get; set; } = [];
    public string? CodexExecutable { get; set; }
    public uint HotkeyModifiers { get; set; } = 3;
    public uint HotkeyKey { get; set; } = 0x53;
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AIScreenShot");
    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(DirectoryPath,"settings.json"))) ?? new(); }
        catch (Exception e) when(e is IOException or JsonException) { return new(); }
    }
    public void Save() { Directory.CreateDirectory(DirectoryPath); File.WriteAllText(Path.Combine(DirectoryPath,"settings.json"),JsonSerializer.Serialize(this)); }
}
public static class BridgeProtocol
{
    public const string HostName="com.aiscreenshot.bridge";
    public const int ChunkSize=192*1024; // Base64 occupies 256 KiB; framing stays below 1 MB.
    public static string PipeName => "AIScreenShot." + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Environment.UserDomainName+"\\"+Environment.UserName)))[..16];
    public static IEnumerable<object> Encode(SendRequest request, string id)
    {
        yield return new {type="begin",id,prompt=request.EffectivePrompt,length=request.Png.Length,sha256=Convert.ToHexString(SHA256.HashData(request.Png)).ToLowerInvariant()};
        for(int offset=0,index=0;offset<request.Png.Length;offset+=ChunkSize,index++)
            yield return new {type="chunk",id,index,data=Convert.ToBase64String(request.Png,offset,Math.Min(ChunkSize,request.Png.Length-offset))};
        yield return new {type="commit",id};
    }
}
