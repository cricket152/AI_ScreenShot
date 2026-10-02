using ScreenShot.Core;
using System.Text.Json;

int checks=0;
void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
var virtualDesktop=new PixelRect(-1920,-200,4480,1640);
Check(PixelRect.FromPoints(200,300,-100,-50)==new PixelRect(-100,-50,300,350),"Reverse drag coordinates");
Check(new PixelRect(-2500,-500,1000,700).Clamp(virtualDesktop)==new PixelRect(-1920,-200,420,400),"Negative origin clipping");
Check(new PixelRect(5000,5000,10,10).Clamp(virtualDesktop).Width==0,"Outside virtual desktop");
Check(new SendRequest([1],"  ",null).EffectivePrompt=="请描述并分析这张截图","Empty prompt fallback");
Check(!new SendResult(SendState.Unknown,"Unknown").Retryable,"Unknown results cannot retry");
byte[] image=Enumerable.Range(0,1200000).Select(i=>(byte)(i%251)).ToArray();
var messages=BridgeProtocol.Encode(new(image,"中文\n提示词",null),"test").Select(o=>JsonSerializer.SerializeToElement(o)).ToArray();
Check(messages[0].GetProperty("length").GetInt32()==image.Length,"Image declared length");
Check(messages[^1].GetProperty("type").GetString()=="commit","Commit frame");
var parts=new List<byte>();int index=0;
foreach(var chunk in messages.Where(e=>e.GetProperty("type").GetString()=="chunk"))
{
    Check(chunk.GetProperty("index").GetInt32()==index++,"Ordered chunks");
    Check(chunk.GetProperty("data").GetString()!.Length<=256*1024,"Native message payload bound");
    parts.AddRange(Convert.FromBase64String(chunk.GetProperty("data").GetString()!));
}
Check(parts.SequenceEqual(image),"Lossless image transport");
int probes=0;
var observed=await ScreenShot.App.CodexAutomation.WaitAsync(()=>
{
    if(++probes<3)throw new InvalidOperationException("Wrapped provider error",new System.Runtime.InteropServices.COMException("Unavailable",unchecked((int)0x80040201)));
    return "fresh element";
},CancellationToken.None,TimeSpan.FromSeconds(3));
Check(observed=="fresh element"&&probes==3,"Reacquire observations after wrapped stale UIA element errors");
try
{
    await ScreenShot.App.CodexAutomation.WaitAsync<string>(()=>throw new InvalidOperationException("Unrelated failure"),CancellationToken.None,TimeSpan.FromSeconds(1));
    Check(false,"Unrelated provider errors must propagate");
}
catch(InvalidOperationException){Check(true,"Unrelated provider errors propagate");}
using(var cancelled=new CancellationTokenSource())
{
    cancelled.Cancel();int cancelledProbes=0;
    try
    {
        await ScreenShot.App.CodexAutomation.WaitAsync<string>(()=>{cancelledProbes++;return null;},cancelled.Token,TimeSpan.FromSeconds(1));
        Check(false,"Cancelled observation must stop");
    }
    catch(OperationCanceledException){Check(cancelledProbes==0,"Cancellation prevents further provider access");}
}
try
{
    await ScreenShot.App.CodexAutomation.WaitAsync<string>(()=>throw new System.Runtime.InteropServices.COMException("Unavailable",unchecked((int)0x80040201)),CancellationToken.None,TimeSpan.FromMilliseconds(100));
    Check(false,"Stale provider cannot wait indefinitely");
}
catch(TimeoutException){Check(true,"Persistent stale provider times out");}
Check(ScreenShot.App.CodexAutomation.NormalizeText("第一行\r\n  第二行\t")=="第一行 第二行","Accessible text handles whitespace differences");
int clipboardReads=0;
var backup=await ScreenShot.App.ClipboardBackupPolicy.ReadAsync(
    new[]{"Text","InvalidNativeFormat","ValidCustomFormat","WrappedInvalidFormat","Bitmap"},format=>
    {
        clipboardReads++;
        return format switch
        {
            "InvalidNativeFormat"=>Task.FromException<string>(new System.Runtime.InteropServices.COMException("Invalid clipboard data",unchecked((int)0x800401D3))),
            "WrappedInvalidFormat"=>Task.FromException<string>(new InvalidOperationException("Wrapped clipboard error",new System.Runtime.InteropServices.COMException("Invalid clipboard data",unchecked((int)0x800401D3)))),
            _=>Task.FromResult("saved "+format)
        };
    },CancellationToken.None);
Check(backup.Data.Count==3&&backup.SkippedFormats==2,"Invalid clipboard formats do not discard readable backup data");
Check(backup.Data["Text"]=="saved Text"&&backup.Data["Bitmap"]=="saved Bitmap"&&clipboardReads==5,"Clipboard backup continues to later readable formats after invalid data");
Check(backup.Data["ValidCustomFormat"]=="saved ValidCustomFormat","Readable custom clipboard data is preserved");
int emptyReads=0;
var emptyBackup=await ScreenShot.App.ClipboardBackupPolicy.ReadAsync<string>(Array.Empty<string>(),_=>{emptyReads++;return Task.FromResult("unexpected");},CancellationToken.None);
Check(emptyBackup.Data.Count==0&&emptyBackup.SkippedFormats==0&&emptyReads==0,"Empty clipboard can be backed up without provider access");
try
{
    await ScreenShot.App.ClipboardBackupPolicy.ReadAsync<string>(new[]{"UnreadableCustomFormat"},_=>Task.FromException<string>(new System.Runtime.InteropServices.COMException("Invalid clipboard data",unchecked((int)0x800401D3))),CancellationToken.None);
    Check(false,"Nonempty clipboard with no readable data must stop before replacement");
}
catch(InvalidOperationException){Check(true,"Wholly unreadable clipboard prevents replacement");}
var clipboardFailure=new System.Runtime.InteropServices.COMException("Clipboard unavailable",unchecked((int)0x800401D0));
try
{
    await ScreenShot.App.ClipboardBackupPolicy.ReadAsync<string>(new[]{"Text"},_=>Task.FromException<string>(clipboardFailure),CancellationToken.None);
    Check(false,"Other clipboard failures must propagate");
}
catch(System.Runtime.InteropServices.COMException error){Check(ReferenceEquals(error,clipboardFailure),"Clipboard access failures remain observable");}
using(var cancelledClipboard=new CancellationTokenSource())
{
    cancelledClipboard.Cancel();int reads=0;
    try
    {
        await ScreenShot.App.ClipboardBackupPolicy.ReadAsync<string>(new[]{"Text"},_=>{reads++;return Task.FromResult("unexpected");},cancelledClipboard.Token);
        Check(false,"Cancelled backup must stop");
    }
    catch(OperationCanceledException){Check(reads==0,"Cancellation prevents clipboard provider access");}
}
using(var cancellingClipboard=new CancellationTokenSource())
{
    int reads=0;
    try
    {
        await ScreenShot.App.ClipboardBackupPolicy.ReadAsync(new[]{"Text","Bitmap"},format=>
        {
            reads++;cancellingClipboard.Cancel();return Task.FromResult(format);
        },cancellingClipboard.Token);
        Check(false,"Backup cancellation must stop subsequent reads");
    }
    catch(OperationCanceledException){Check(reads==1,"Cancellation during backup does not read another clipboard format");}
}
Console.WriteLine($"PASS: {checks} core, Codex automation and clipboard backup assertions");
