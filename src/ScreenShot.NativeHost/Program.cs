using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ScreenShot.Core;

// stdout is reserved exclusively for Native Messaging frames.
if(args.Length==0 || !args[0].StartsWith("chrome-extension://",StringComparison.Ordinal)) return;
using var pipe=new NamedPipeClientStream(".",BridgeProtocol.PipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
try { await pipe.ConnectAsync(5000); }
catch(TimeoutException) { return; }
using var pipeReader=new StreamReader(pipe,Encoding.UTF8,false,4096,true);
using var pipeWriter=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};
var input=Console.OpenStandardInput(); var output=Console.OpenStandardOutput();
async Task BrowserToApp()
{
    var header=new byte[4];
    while(true)
    {
        await input.ReadExactlyAsync(header);
        var length=BinaryPrimitives.ReadInt32LittleEndian(header);
        if(length is <1 or >1024*1024) throw new InvalidDataException();
        var bytes=new byte[length]; await input.ReadExactlyAsync(bytes);
        using var json=JsonDocument.Parse(bytes);
        await pipeWriter.WriteLineAsync(Encoding.UTF8.GetString(bytes));
    }
}
async Task AppToBrowser()
{
    while(await pipeReader.ReadLineAsync() is { } line)
    {
        byte[] bytes=Encoding.UTF8.GetBytes(line);
        if(bytes.Length>1024*1024) throw new InvalidDataException();
        var header=new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header,bytes.Length);
        await output.WriteAsync(header); await output.WriteAsync(bytes); await output.FlushAsync();
    }
}
try { await await Task.WhenAny(BrowserToApp(),AppToBrowser()); }
catch(Exception e) when(e is IOException or JsonException) { /* Disconnection ends the bridge. */ }
