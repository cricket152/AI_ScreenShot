using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ScreenShot.Core;

namespace ScreenShot.App;

internal sealed class BrowserBridge : IDisposable,ISendTarget
{
    private sealed record Connection(StreamWriter Writer,NamedPipeServerStream Pipe);
    private readonly ConcurrentDictionary<string,Connection> connections=new();
    private readonly ConcurrentDictionary<string,TaskCompletionSource<SendResult>> pending=new();
    private readonly CancellationTokenSource lifetime=new();
    private readonly Settings settings;
    public string Status=>connections.ContainsKey(settings.Browser)?$"{settings.Browser} 扩展已连接":$"{settings.Browser} 扩展未连接";
    public BrowserBridge(Settings settings){this.settings=settings;_ = AcceptAsync();}
    private async Task AcceptAsync()
    {
        while(!lifetime.IsCancellationRequested)
        {
            var pipe=new NamedPipeServerStream(BridgeProtocol.PipeName,PipeDirection.InOut,8,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            try{await pipe.WaitForConnectionAsync(lifetime.Token);_ = ReadConnectionAsync(pipe);}
            catch(OperationCanceledException){pipe.Dispose();break;}
        }
    }
    private async Task ReadConnectionAsync(NamedPipeServerStream pipe)
    {
        string? browser=null;Connection? connection=null;
        using(pipe)
        using(var reader=new StreamReader(pipe,Encoding.UTF8,false,4096,true))
        using(var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true})
        {
            try
            {
                string? line=await reader.ReadLineAsync(lifetime.Token);
                using var hello=JsonDocument.Parse(line??"{}");
                if(hello.RootElement.GetProperty("type").GetString()!="hello")return;
                browser=hello.RootElement.GetProperty("browser").GetString();
                if(browser is not ("Edge" or "Chrome"))return;
                connection=new(writer,pipe);
                if(!connections.TryAdd(browser,connection)){await writer.WriteLineAsync("{\"type\":\"error\",\"message\":\"同一浏览器已有连接，请关闭其他配置文件中的扩展连接\"}");return;}
                await writer.WriteLineAsync("{\"type\":\"ready\"}");
                while((line=await reader.ReadLineAsync(lifetime.Token))!=null)
                {
                    if(line.Length>1024*1024)break;
                    using var json=JsonDocument.Parse(line);var root=json.RootElement;
                    if(root.GetProperty("type").GetString()=="result" && pending.TryGetValue(root.GetProperty("id").GetString()??"",out var result))
                    {
                        var state=Enum.TryParse<SendState>(root.GetProperty("state").GetString(),out var parsed)?parsed:SendState.Unknown;
                        result.TrySetResult(new(state,root.GetProperty("message").GetString()??"发送结果未知"));
                    }
                }
            }
            catch(Exception e) when(e is IOException or JsonException or InvalidOperationException or OperationCanceledException or KeyNotFoundException){ }
            finally{if(browser!=null && connection!=null)connections.TryRemove(new KeyValuePair<string,Connection>(browser,connection));}
        }
    }
    public async Task<SendResult> SendAsync(SendRequest request,CancellationToken cancellationToken)
    {
        if(!connections.TryGetValue(settings.Browser,out var connection))return new(SendState.Failed,$"请启动 {settings.Browser} 并启用 AI ScreenShot 扩展，等待自动连接。若仍未连接，请检查 Host 注册或在扩展中点击“立即重连”；设置页提供安装说明。");
        if(request.Png.Length>64*1024*1024)return new(SendState.Failed,"截图超过第一版 64 MiB 传输上限，请缩小选区");
        string id=Guid.NewGuid().ToString("N");var completion=new TaskCompletionSource<SendResult>(TaskCreationOptions.RunContinuationsAsynchronously);pending[id]=completion;
        bool committed=false;
        try
        {
            foreach(var message in BridgeProtocol.Encode(request,id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string line=JsonSerializer.Serialize(message);if(line.Contains("\"type\":\"commit\""))committed=true;
                await connection.Writer.WriteLineAsync(line);
            }
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(210),cancellationToken);
        }
        catch(Exception e) when(e is IOException or TimeoutException or OperationCanceledException)
        {return new(committed?SendState.Unknown:SendState.Failed,committed?"发送结果未知，请检查 ChatGPT 标签页；不会自动重发":"扩展连接断开，截图仍保留");}
        finally{pending.TryRemove(id,out _);}
    }
    public void Dispose(){lifetime.Cancel();foreach(var connection in connections.Values)connection.Pipe.Dispose();lifetime.Dispose();}
}
