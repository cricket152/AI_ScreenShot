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
public sealed record SendRequest(byte[] Png, string Prompt, Workspace? Workspace, string? DefaultPrompt=null)
{
    public ConversationContext? Conversation {get;init;}
    public bool IsTitleRequest {get;init;}
    public const string BuiltInDefaultPrompt="请描述并分析这张截图";
    public bool HasContent => Png.Length>0||!string.IsNullOrWhiteSpace(Prompt);
    public string EffectivePrompt => !string.IsNullOrWhiteSpace(Prompt) ? Prompt :
        Png.Length==0 ? "" : !string.IsNullOrWhiteSpace(DefaultPrompt) ? DefaultPrompt : BuiltInDefaultPrompt;
}
public interface ISendTarget { Task<SendResult> SendAsync(SendRequest request, CancellationToken cancellationToken); }
public sealed class Settings
{
    private PanelAppearance panelAppearance=new();
    public PanelAppearance PanelAppearance {get=>panelAppearance;set=>panelAppearance=(value??new()).Normalize();}
    private string defaultPrompt=SendRequest.BuiltInDefaultPrompt;
    public string DefaultPrompt
    {
        get=>defaultPrompt;
        set=>defaultPrompt=string.IsNullOrWhiteSpace(value)?SendRequest.BuiltInDefaultPrompt:value;
    }
    private AgentSettings agent=new();
    public AgentSettings Agent {get=>agent;set=>agent=value??new();}
    public string? Workspace { get; set; }
    public string? CodexDirectory { get; set; }
    public List<string> CodexDirectories { get; set; } = [];
    public string? CodexExecutable { get; set; }
    public uint HotkeyModifiers { get; set; } = 3;
    public uint HotkeyKey { get; set; } = 0x53;
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AIScreenShot");
    [System.Text.Json.Serialization.JsonIgnore] public string? LoadWarning {get;private set;}
    public static Settings Load()
    {
        try
        {
            string json=File.ReadAllText(Path.Combine(DirectoryPath,"settings.json"));
            var settings=JsonSerializer.Deserialize<Settings>(json)??new();
            using var document=JsonDocument.Parse(json);
            if(document.RootElement.TryGetProperty("Browser",out _))
            {
                try{settings.Save();}
                catch(Exception error) when(error is IOException or UnauthorizedAccessException)
                {settings.LoadWarning="旧官网配置已在内存移除，但配置文件无法写入；请检查 settings.json 的权限后重新保存";}
            }
            return settings;
        }
        catch (Exception e) when(e is IOException or JsonException) { return new(); }
    }
    public void Save() { Directory.CreateDirectory(DirectoryPath); File.WriteAllText(Path.Combine(DirectoryPath,"settings.json"),JsonSerializer.Serialize(this)); }
}
