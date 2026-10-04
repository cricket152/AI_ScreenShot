using ScreenShot.Core;
using System.Text.Json;

int checks=0;
checks+=await CodexProviderTests.RunAsync();
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
int busyReadAttempts=0,busyReadGuards=0;
string recoveredClipboard=await ScreenShot.App.ClipboardAccessPolicy.RetryAsync(async()=>
{
    await Task.Yield();
    busyReadAttempts++;
    if(busyReadAttempts==1)throw clipboardFailure;
    if(busyReadAttempts==2)throw new InvalidOperationException("Wrapped temporary clipboard contention",clipboardFailure);
    return "generated snapshot";
},()=>busyReadGuards++,CancellationToken.None,TimeSpan.Zero,3);
Check(recoveredClipboard=="generated snapshot"&&busyReadAttempts==3&&busyReadGuards==3,"Asynchronous clipboard reads recover after direct and wrapped temporary lock failures");
int permanentlyBusyAttempts=0,permanentlyBusyGuards=0;
try
{
    await ScreenShot.App.ClipboardAccessPolicy.RetryAsync(()=>{permanentlyBusyAttempts++;throw clipboardFailure;},()=>permanentlyBusyGuards++,CancellationToken.None,TimeSpan.Zero,3);
    Check(false,"Permanent clipboard contention must finish with an observable failure");
}
catch(System.Runtime.InteropServices.COMException error)
{
    Check(ReferenceEquals(error,clipboardFailure)&&permanentlyBusyAttempts==3&&permanentlyBusyGuards==3,"Permanent clipboard contention has a finite retry budget and preserves the original error");
}
int invalidClipboardAttempts=0;
var invalidClipboardData=new System.Runtime.InteropServices.COMException("Invalid generated data",unchecked((int)0x800401D3));
try
{
    await ScreenShot.App.ClipboardAccessPolicy.RetryAsync(()=>{invalidClipboardAttempts++;throw invalidClipboardData;},()=>{},CancellationToken.None,TimeSpan.Zero,3);
    Check(false,"Invalid clipboard data must not be retried as contention");
}
catch(System.Runtime.InteropServices.COMException error)
{
    Check(ReferenceEquals(error,invalidClipboardData)&&invalidClipboardAttempts==1,"Non-contention clipboard failures propagate without replay");
}
using(var cancelledAccess=new CancellationTokenSource())
{
    cancelledAccess.Cancel();int attempts=0,guards=0;
    try
    {
        await ScreenShot.App.ClipboardAccessPolicy.RetryAsync(()=>attempts++,()=>guards++,cancelledAccess.Token,TimeSpan.Zero,3);
        Check(false,"Cancelled clipboard access must not begin");
    }
    catch(OperationCanceledException){Check(attempts==0&&guards==0,"Cancellation before clipboard access prevents guards and provider calls");}
}
using(var cancelledByGuard=new CancellationTokenSource())
{
    int attempts=0,guards=0;
    try
    {
        await ScreenShot.App.ClipboardAccessPolicy.RetryAsync(()=>attempts++,()=>{guards++;cancelledByGuard.Cancel();},cancelledByGuard.Token,TimeSpan.Zero,3);
        Check(false,"Cancellation during the access guard must prevent provider access");
    }
    catch(OperationCanceledException){Check(attempts==0&&guards==1,"Cancellation after the guard is checked before clipboard provider access");}
}
using(var cancelledRetry=new CancellationTokenSource())
{
    int attempts=0,guards=0;
    try
    {
        await ScreenShot.App.ClipboardAccessPolicy.RetryAsync(()=>{attempts++;cancelledRetry.Cancel();throw clipboardFailure;},()=>guards++,cancelledRetry.Token,TimeSpan.FromMilliseconds(100),3);
        Check(false,"Cancellation while waiting for a busy clipboard must end retrying");
    }
    catch(OperationCanceledException){Check(attempts==1&&guards==1,"Cancellation interrupts clipboard retry delay without another attempt");}
}
int changedClipboardAttempts=0,changedClipboardGuards=0;uint generatedSequence=10;
try
{
    await ScreenShot.App.ClipboardAccessPolicy.RetryAsync(()=>{changedClipboardAttempts++;generatedSequence++;throw clipboardFailure;},()=>
    {
        changedClipboardGuards++;if(generatedSequence!=10)throw new InvalidOperationException("Generated clipboard was replaced by a user");
    },CancellationToken.None,TimeSpan.Zero,3);
    Check(false,"User clipboard replacement must block a pending write retry");
}
catch(InvalidOperationException)
{
    Check(changedClipboardAttempts==1&&changedClipboardGuards==2&&generatedSequence==11,"Every write retry rechecks clipboard ownership and preserves an intervening user replacement");
}
int contentWrites=0,writeNotifications=0,contentFlushes=0,writeGuards=0,flushGuards=0;
await ScreenShot.App.ClipboardAccessPolicy.WriteAsync(()=>contentWrites++,()=>writeNotifications++,()=>
{
    contentFlushes++;if(contentFlushes<3)throw clipboardFailure;
},()=>writeGuards++,()=>flushGuards++,CancellationToken.None,TimeSpan.Zero,3);
Check(contentWrites==1&&writeNotifications==1&&contentFlushes==3&&writeGuards==1&&flushGuards==3,"Temporary Flush contention retries only persistence and never replays successful SetContent");
contentWrites=0;writeNotifications=0;contentFlushes=0;
try
{
    await ScreenShot.App.ClipboardAccessPolicy.WriteAsync(()=>contentWrites++,()=>writeNotifications++,()=>{contentFlushes++;throw clipboardFailure;},()=>{},()=>{},CancellationToken.None,TimeSpan.Zero,3);
    Check(false,"Permanent Flush contention must remain observable after a successful write");
}
catch(System.Runtime.InteropServices.COMException error)
{
    Check(ReferenceEquals(error,clipboardFailure)&&contentWrites==1&&writeNotifications==1&&contentFlushes==3,"A failed Flush preserves partial-write state without repeating SetContent or its notification");
}
contentWrites=0;writeNotifications=0;contentFlushes=0;
try
{
    await ScreenShot.App.ClipboardAccessPolicy.WriteAsync(()=>contentWrites++,()=>{writeNotifications++;throw clipboardFailure;},()=>contentFlushes++,()=>{},()=>{},CancellationToken.None,TimeSpan.Zero,3);
    Check(false,"A failing successful-write notification must not cause content replay");
}
catch(System.Runtime.InteropServices.COMException error)
{
    Check(ReferenceEquals(error,clipboardFailure)&&contentWrites==1&&writeNotifications==1&&contentFlushes==0,"A notification failure after SetContent is not treated as a retryable write");
}
contentWrites=0;writeNotifications=0;contentFlushes=0;generatedSequence=20;
try
{
    await ScreenShot.App.ClipboardAccessPolicy.WriteAsync(()=>{contentWrites++;generatedSequence=21;},()=>writeNotifications++,()=>
    {
        contentFlushes++;generatedSequence=22;throw clipboardFailure;
    },()=>{if(generatedSequence!=20)throw new InvalidOperationException("Generated clipboard changed before SetContent");},()=>
    {
        if(generatedSequence!=21)throw new InvalidOperationException("Generated clipboard changed before Flush");
    },CancellationToken.None,TimeSpan.Zero,3);
    Check(false,"User clipboard replacement during Flush contention must stop persistence retries");
}
catch(InvalidOperationException)
{
    Check(contentWrites==1&&writeNotifications==1&&contentFlushes==1&&generatedSequence==22,"A user replacement between Flush attempts is neither overwritten nor flushed by this send");
}
int busyWriteAttempts=0;writeNotifications=0;contentFlushes=0;
await ScreenShot.App.ClipboardAccessPolicy.WriteAsync(()=>{busyWriteAttempts++;if(busyWriteAttempts<3)throw clipboardFailure;},()=>writeNotifications++,()=>contentFlushes++,()=>{},()=>{},CancellationToken.None,TimeSpan.Zero,3);
Check(busyWriteAttempts==3&&writeNotifications==1&&contentFlushes==1,"SetContent contention recovers before a single successful-write notification and Flush");
using(var cancelledAfterWrite=new CancellationTokenSource())
{
    contentWrites=0;writeNotifications=0;contentFlushes=0;
    try
    {
        await ScreenShot.App.ClipboardAccessPolicy.WriteAsync(()=>{contentWrites++;cancelledAfterWrite.Cancel();},()=>writeNotifications++,()=>contentFlushes++,()=>{},()=>{},cancelledAfterWrite.Token,TimeSpan.Zero,3);
        Check(false,"Cancellation after a successful clipboard write must stop persistence");
    }
    catch(OperationCanceledException)
    {
        Check(contentWrites==1&&writeNotifications==1&&contentFlushes==0,"A write completed during cancellation still records ownership exactly once for safe cleanup");
    }
}
var clipboardContext=new ClipboardRetryContext();
var pendingClipboardRead=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
int contextAttempts=0;bool contextPreserved=true;
var originalClipboardContext=SynchronizationContext.Current;
Task<string> contextualClipboardRead;
SynchronizationContext.SetSynchronizationContext(clipboardContext);
try
{
    contextualClipboardRead=ScreenShot.App.ClipboardAccessPolicy.RetryAsync(()=>++contextAttempts==1?pendingClipboardRead.Task:Task.FromResult("contextual snapshot"),()=>contextPreserved&=ReferenceEquals(SynchronizationContext.Current,clipboardContext),CancellationToken.None,TimeSpan.Zero,3);
}
finally{SynchronizationContext.SetSynchronizationContext(originalClipboardContext);}
pendingClipboardRead.SetException(clipboardFailure);
Check(await contextualClipboardRead=="contextual snapshot"&&contextAttempts==2&&contextPreserved&&clipboardContext.Posts>0,"Asynchronous contention retries retain the caller synchronization context required by WinRT clipboard access");
var shortUser=ScreenShot.App.CodexObservationPolicy.User("new-user",["123"],"123");
Check(shortUser.MatchesPrompt,"Short numeric prompts match user message role text");
Check(ScreenShot.App.CodexObservationPolicy.User("split",["第一行","第二行"],"第一行\r\n第二行").MatchesPrompt,"Split multiline user text matches prompt");
Check(ScreenShot.App.CodexObservationPolicy.User("punctuation",["你好","，","世界"],"你好，世界").MatchesPrompt,"Inline text fragments do not introduce spaces into prompt matching");
Check(ScreenShot.App.CodexObservationPolicy.User("timestamp",["123","23:32"],"123").MatchesPrompt,"User message timestamp is excluded from prompt evidence");
Check(ScreenShot.App.CodexObservationPolicy.User("literal-time",["23:32"],"23:32").MatchesPrompt,"A literal time prompt remains message content");
Check(!ScreenShot.App.CodexObservationPolicy.User("isolated-real-time",["123","23:32"],"123",includesMetadata:false).MatchesPrompt,"An isolated user body never removes a real trailing time to manufacture a prompt match");
Check(ScreenShot.App.CodexObservationPolicy.User("isolated-time-lines",["23:32","23:32","第一行","第一行","23:32"],"23:32\n23:32\n第一行\n第一行\n23:32",includesMetadata:false).MatchesPrompt,"Repeated text and multiple literal time lines remain intact in an isolated user body");
var selectedChat=new ScreenShot.App.CodexObservedChat("selected-chat","generated-title");
ScreenShot.App.CodexConversationSnapshot Snapshot(params ScreenShot.App.CodexObservedUser[] users)
    =>new("area",users,selectedChat,true,true,false,false,null);
var submissionPolicy=new ScreenShot.App.CodexObservationPolicy(Snapshot());
Check(submissionPolicy.Observe(Snapshot(shortUser) with {ComposerCleared=false})==ScreenShot.App.CodexObservationState.Waiting,"Visible draft cannot confirm submission");
Check(submissionPolicy.Observe(Snapshot(shortUser) with {HasSend=false,HasStop=true})==ScreenShot.App.CodexObservationState.Submitted,"New matching user message and cleared draft confirm submission while thinking");
var nonemptyComposerPolicy=new ScreenShot.App.CodexObservationPolicy(Snapshot());
Check(nonemptyComposerPolicy.Observe(Snapshot(shortUser) with {ComposerCleared=false,HasSend=false,HasStop=true})==ScreenShot.App.CodexObservationState.Submitted,"Matching new user role plus Stop confirms submission despite an accessibility placeholder or new draft");
var fastReplyPolicy=new ScreenShot.App.CodexObservationPolicy(Snapshot());
Check(fastReplyPolicy.Observe(Snapshot(shortUser) with {ComposerCleared=false,HasReplyFooter=true,Reply="generated final"})==ScreenShot.App.CodexObservationState.Completed,"Matching new user role plus a completed assistant footer confirms a fast response without a blank text pattern");
Check(submissionPolicy.Observe(Snapshot())==ScreenShot.App.CodexObservationState.Waiting&&submissionPolicy.Submitted,"Temporarily empty accessibility tree does not lose confirmed submission");
bool keptWaiting=true;
for(int i=0;i<1000;i++)keptWaiting&=submissionPolicy.Observe(Snapshot(shortUser) with {HasStop=true})==ScreenShot.App.CodexObservationState.Submitted;
Check(keptWaiting,"Repeated thinking observations have no fixed response cutoff");
var rebuiltUser=shortUser with {RuntimeId="rebuilt-user"};
Check(submissionPolicy.Observe(Snapshot(rebuiltUser) with {AreaId="rebuilt-area"})==ScreenShot.App.CodexObservationState.Submitted,"Unchanged selected chat and user history allow accessibility subtree rebinding");
Check(submissionPolicy.Observe(Snapshot(rebuiltUser with {RuntimeId="ambiguous-user"}) with {SelectedChat=selectedChat with {RuntimeId="different-sidebar-node"},HasReplyFooter=true,Reply="generated reply"})==ScreenShot.App.CodexObservationState.Waiting,"Same text in a different unidentified chat cannot be returned as the reply");
Check(submissionPolicy.Observe(Snapshot(rebuiltUser) with {HasReplyFooter=false,Reply="generated code block"})==ScreenShot.App.CodexObservationState.Submitted,"Code block copy does not replace response footer evidence");
Check(submissionPolicy.Observe(Snapshot(rebuiltUser) with {HasReplyFooter=true,HasStop=true,Reply="generated partial"})==ScreenShot.App.CodexObservationState.Submitted,"Response content while Stop is available remains pending");
Check(submissionPolicy.Observe(Snapshot(rebuiltUser) with {HasReplyFooter=true,HasSend=false,Reply="generated final"})==ScreenShot.App.CodexObservationState.Completed,"A confirmed empty composer with a voice button can finish without Send");
Check(submissionPolicy.Observe(Snapshot(rebuiltUser) with {HasReplyFooter=true,HasSend=false,ComposerCleared=false,Reply="generated final"})==ScreenShot.App.CodexObservationState.Submitted,"Missing composer controls and unreadable draft cannot prove completion");
Check(submissionPolicy.Observe(Snapshot(rebuiltUser) with {HasReplyFooter=true,Reply="generated final"})==ScreenShot.App.CodexObservationState.Completed,"Assistant footer plus Send without Stop confirms completed output");
Check(submissionPolicy.Observe(Snapshot(rebuiltUser) with {SelectedChat=selectedChat with {Fingerprint="renamed-title"},HasReplyFooter=true,Reply="generated final"})==ScreenShot.App.CodexObservationState.Completed,"Automatic chat title updates retain the existing user anchor");
var laterUser=ScreenShot.App.CodexObservationPolicy.User("later-user",["other"],"123");
Check(submissionPolicy.Observe(Snapshot(rebuiltUser,laterUser))==ScreenShot.App.CodexObservationState.ForeignConversation,"A later user message stops reply association with the submitted request");
var existingPolicy=new ScreenShot.App.CodexObservationPolicy(Snapshot(shortUser));
Check(existingPolicy.Observe(Snapshot(shortUser))==ScreenShot.App.CodexObservationState.Waiting,"An existing same-prompt user message is not a new submission");
Check(existingPolicy.Observe(Snapshot(shortUser,rebuiltUser))==ScreenShot.App.CodexObservationState.Submitted,"A repeated prompt is identified by a new user role after baseline messages");
var navigationPolicy=new ScreenShot.App.CodexObservationPolicy(Snapshot());
navigationPolicy.Observe(Snapshot(shortUser));
Check(navigationPolicy.Observe(Snapshot(laterUser) with {SelectedChat=new("other-chat","other-title")})==ScreenShot.App.CodexObservationState.ForeignConversation,"Explicitly different selected chat and user history stop observation");
var oldSidebarChat=new ScreenShot.App.CodexObservedChat("old-sidebar-row","old-title");
var emptyNewChat=Snapshot() with {SelectedChat=null,SidebarChats=[oldSidebarChat]};
var oldChatPolicy=new ScreenShot.App.CodexObservationPolicy(emptyNewChat);
Check(oldChatPolicy.Observe(Snapshot(shortUser) with {SelectedChat=oldSidebarChat,SidebarChats=[oldSidebarChat]})==ScreenShot.App.CodexObservationState.ForeignConversation,"An existing sidebar chat with the same short prompt cannot bind as the newly created chat");
var newSidebarChat=new ScreenShot.App.CodexObservedChat("new-sidebar-row","unique-new-title");
var newChatPolicy=new ScreenShot.App.CodexObservationPolicy(emptyNewChat);
Check(newChatPolicy.Observe(Snapshot(shortUser) with {SelectedChat=newSidebarChat,SidebarChats=[oldSidebarChat,newSidebarChat]})==ScreenShot.App.CodexObservationState.Submitted,"The new sidebar row can be bound to its matching newly submitted user message");
Check(newChatPolicy.Observe(Snapshot(rebuiltUser) with {AreaId="rebuilt-area",SelectedChat=newSidebarChat with {RuntimeId="rebuilt-sidebar"},SidebarChats=[oldSidebarChat,newSidebarChat with {RuntimeId="rebuilt-sidebar"}]})==ScreenShot.App.CodexObservationState.Submitted,"Unique newly created chat identity and full user history survive a complete UIA rebuild");
var renameRebuildPolicy=new ScreenShot.App.CodexObservationPolicy(emptyNewChat);
renameRebuildPolicy.Observe(Snapshot(shortUser) with {SelectedChat=newSidebarChat,SidebarChats=[oldSidebarChat,newSidebarChat]});
Check(renameRebuildPolicy.Observe(Snapshot(rebuiltUser) with {SelectedChat=new("rebuilt-and-renamed","renamed-title")})==ScreenShot.App.CodexObservationState.Waiting,"A simultaneous title change and subtree rebuild remains pending when identity is ambiguous");
Check(newChatPolicy.Observe(Snapshot() with {SelectedChat=oldSidebarChat,SidebarChats=[oldSidebarChat,newSidebarChat]})==ScreenShot.App.CodexObservationState.ForeignConversation,"Switching to a known preexisting empty chat is explicit navigation evidence");
var streamPolicy=new ScreenShot.App.CodexReplyStreamPolicy();
Check(streamPolicy.Observe(ScreenShot.App.CodexObservationState.Waiting,"unbound text","unbound status",null)==null,"Unconfirmed chat text is never streamed");
Check(streamPolicy.Observe(ScreenShot.App.CodexObservationState.Submitted,null,"正在思考",null) is {Phase:ScreenShot.App.CodexReplyPhase.Thinking,Text:null,Status:"正在思考"},"Thinking status can precede response text");
Check(streamPolicy.Observe(ScreenShot.App.CodexObservationState.Submitted,"第一段","正在读取文件",null) is {Text:"第一段",Status:"正在读取文件"},"Visible work status and partial content stream together");
Check(streamPolicy.Observe(ScreenShot.App.CodexObservationState.Submitted,"第一段","正在读取文件",null)==null,"Unchanged public snapshots do not flood the message pane");
Check(streamPolicy.Observe(ScreenShot.App.CodexObservationState.Submitted,"第一段\n第二段","正在处理",null) is {Text:"第一段\n第二段"},"Progress text replaces the full snapshot without duplicate appends");
Check(streamPolicy.Observe(ScreenShot.App.CodexObservationState.Completed,"final","已输出", "final") is {Phase:ScreenShot.App.CodexReplyPhase.Thinking},"One completed-looking snapshot cannot end the task early");
Check(streamPolicy.Observe(ScreenShot.App.CodexObservationState.Waiting,null,null,null)==null,"An incomplete accessibility tree preserves displayed content");
Check(streamPolicy.Observe(ScreenShot.App.CodexObservationState.Completed,"final","已输出", "final") is not {Phase:ScreenShot.App.CodexReplyPhase.Completed},"A tree gap resets consecutive completion evidence");
Check(streamPolicy.Observe(ScreenShot.App.CodexObservationState.Completed,"changed final","已输出", "changed final") is not {Phase:ScreenShot.App.CodexReplyPhase.Completed},"Changing final text keeps the stream open");
Check(streamPolicy.Observe(ScreenShot.App.CodexObservationState.Completed,"changed final","已输出", "changed final") is {Phase:ScreenShot.App.CodexReplyPhase.Completed,Text:"changed final"},"Two stable final snapshots end the task with the final reply");
Check(streamPolicy.Observe(ScreenShot.App.CodexObservationState.ForeignConversation,"foreign text","foreign status","foreign reply")==null,"Switched conversation content is never streamed");
var fixtureWindow=new CodexScopeFixture("Chrome_WidgetWin_1",boundary:true);
var fixtureDocument=new CodexScopeFixture("",fixtureWindow,boundary:true);
var fixtureSidebar=new CodexScopeFixture("group/sidebar-rail",fixtureDocument);
var fixtureFrame=new CodexScopeFixture("_MainContentFrame_generated self-end",fixtureDocument);
var fixtureWelcome=new CodexScopeFixture("welcome-layout",fixtureFrame);
var fixtureComposerBranch=new CodexScopeFixture("_ComposerLayoutBody_generated",fixtureWelcome);
var fixtureComposer=new CodexScopeFixture("ProseMirror",fixtureComposerBranch,"composer");
CodexScopeFixture? FindFixtureScope(CodexScopeFixture editor)=>ScreenShot.App.CodexScopePolicy.FindScope(editor,node=>node.Parent,node=>node.ClassName,node=>node.Boundary);
var emptyFixtureScope=FindFixtureScope(fixtureComposer);
Check(ReferenceEquals(emptyFixtureScope,fixtureFrame),"Empty welcome composer binds to MainContent frame without a scroll ancestor");
Check(ScreenShot.App.CodexScopePolicy.FindScroll(emptyFixtureScope!.Descendants().ToArray(),node=>node.ClassName)==null,"The empty welcome tree legitimately has no message scroll");
Check(!emptyFixtureScope.Descendants().Contains(fixtureSidebar),"Prepared input scope excludes sibling sidebar content");
bool ValidateFixtureScope(CodexScopeFixture scope)=>ScreenShot.App.CodexScopePolicy.ValidateScope(scope.Descendants().ToArray(),node=>node.ClassName,node=>node.Role=="composer",node=>node.Role=="workspace-new-chat");
Check(ValidateFixtureScope(emptyFixtureScope),"The empty welcome tree passes the same isolated-scope validator as production Prepare");
var mixedSidebar=new CodexScopeFixture("group/sidebar-rail",fixtureFrame);
Check(!ValidateFixtureScope(fixtureFrame),"A sidebar descendant mixed into MainContent scope is rejected by the production validator");
fixtureFrame.Children.Remove(mixedSidebar);
var duplicatedComposer=new CodexScopeFixture("ProseMirror",fixtureFrame,"composer");
Check(!ValidateFixtureScope(fixtureFrame),"Two composer branches inside the same named scope are rejected by the production validator");
fixtureFrame.Children.Remove(duplicatedComposer);
var mixedProjectButton=new CodexScopeFixture("_Button_generated",fixtureFrame,"workspace-new-chat");
Check(!ValidateFixtureScope(fixtureFrame),"Workspace navigation buttons inside a candidate scope prevent using its messages");
fixtureFrame.Children.Remove(mixedProjectButton);
var blankValue=ScreenShot.App.CodexScopePolicy.DraftText(true,"",true,"generated placeholder");
Check(ScreenShot.App.CodexScopePolicy.CanStartNewChat(1,0,blankValue,false),"Actual empty ValuePattern permits a welcome draft even if TextPattern exposes a placeholder");
Check(!ScreenShot.App.CodexScopePolicy.CanStartNewChat(1,0,ScreenShot.App.CodexScopePolicy.DraftText(true,"generated placeholder",true,"generated placeholder"),false),"A nonempty value is never guessed empty from a matching label");
Check(!ScreenShot.App.CodexScopePolicy.CanStartNewChat(1,0,"old prompt",false),"An old nonempty draft cannot be reused as a new chat");
Check(!ScreenShot.App.CodexScopePolicy.CanStartNewChat(1,1,"",false),"An existing user conversation with an empty composer is not a new chat");
Check(!ScreenShot.App.CodexScopePolicy.CanStartNewChat(1,0,"",true),"Existing attachments prevent a new-chat readiness result");
Check(!ScreenShot.App.CodexScopePolicy.CanStartNewChat(1,0,ScreenShot.App.CodexScopePolicy.DraftText(false,null,false,null),false),"Unreadable draft cannot be treated as empty");
Check(!ScreenShot.App.CodexScopePolicy.CanStartNewChat(2,0,"",false),"Multiple input composers cannot be treated as the target new chat");
var sidebarComposer=new CodexScopeFixture("ProseMirror",fixtureSidebar,"composer");
Check(FindFixtureScope(sidebarComposer)==null,"A sidebar editor cannot become the chat scope");
var unscopedComposer=new CodexScopeFixture("ProseMirror",fixtureDocument,"composer");
Check(FindFixtureScope(unscopedComposer)==null,"Scope lookup never falls back to the whole Document or Window");
var dynamicScroll=new CodexScopeFixture("group/thread-scroll-layout relative h-full flex-1",fixtureFrame);
var generatedUserRole=new CodexScopeFixture("sr-only",dynamicScroll,"user");
var generatedUserText=new CodexScopeFixture("",dynamicScroll,"user-text");
var generatedAssistantRole=new CodexScopeFixture("sr-only",dynamicScroll,"assistant");
var generatedReplyText=new CodexScopeFixture("",dynamicScroll,"reply-text");
var generatedCodeCopy=new CodexScopeFixture("code-block-copy",dynamicScroll,"code-copy");
var generatedReplyFooter=new CodexScopeFixture("contents",dynamicScroll,"footer");
Check(ReferenceEquals(ScreenShot.App.CodexScopePolicy.FindScroll(fixtureFrame.Descendants().ToArray(),node=>node.ClassName),dynamicScroll),"A new message scroll appears as a sibling of the welcome composer inside the same prepared scope");
CodexScopeFixture? RecoverFixtureScroll(CodexScopeFixture scope,IReadOnlyList<CodexScopeFixture> nodes)=>ScreenShot.App.CodexScopePolicy.DiscoverScroll(scope,nodes,node=>node.ClassName,node=>node.Role is "user" or "assistant",node=>node.Parent,node=>node.RuntimeId,node=>node.Boundary);
Check(ReferenceEquals(RecoverFixtureScroll(fixtureFrame,[generatedAssistantRole]),dynamicScroll),"A role ancestor recovers the omitted scroll in the same captured scope");
Check(RecoverFixtureScroll(fixtureSidebar,[generatedAssistantRole])==null,"A message role from a sibling scope cannot recover another chat's scroll");
Check(ReferenceEquals(RecoverFixtureScroll(fixtureFrame,[dynamicScroll,generatedUserRole,generatedAssistantRole]),dynamicScroll),"Flat and role-ancestor scroll discoveries are deduplicated by identity");
Check(RecoverFixtureScroll(fixtureFrame,[])==null,"An empty welcome page still legitimately has no message scroll");
Check(ReferenceEquals(FindFixtureScope(fixtureComposer),fixtureFrame),"Welcome-to-message transition retains the captured MainContent scope");
var messageFixtureNodes=dynamicScroll.Descendants().ToArray();
var fixtureReplyBounds=ScreenShot.App.CodexScopePolicy.ReplyBounds(messageFixtureNodes,node=>node.Role=="user",node=>node.Role=="assistant",node=>node.Role=="footer");
Check(fixtureReplyBounds is {} bounds&&messageFixtureNodes[bounds.Start]==generatedReplyText&&bounds.Count==2,"Reply footer bounds use the message role region and retain content past code-block copies");
var fixtureProgress=ScreenShot.App.CodexScopePolicy.ProgressBounds(messageFixtureNodes,node=>node.Role=="user",node=>node.Role=="assistant");
Check(fixtureProgress is {} partial&&messageFixtureNodes[partial.Start]==generatedReplyText,"Stream text begins after the assistant role belonging to the latest user");
Check(ScreenShot.App.CodexScopePolicy.ProgressBounds([generatedAssistantRole,generatedReplyText],node=>node.Role=="user",node=>node.Role=="assistant")==null,"An unanchored assistant reply cannot be streamed");
Check(ScreenShot.App.CodexScopePolicy.ProgressBounds([generatedAssistantRole,generatedReplyText,generatedUserRole,generatedUserText],node=>node.Role=="user",node=>node.Role=="assistant")==null,"An earlier assistant reply is not reused for a later user request");
fixtureFrame.Children.Remove(dynamicScroll);fixtureFrame.Children.Insert(0,dynamicScroll);
Check(ScreenShot.App.CodexScopePolicy.ReplyBounds(dynamicScroll.Descendants().ToArray(),node=>node.Role=="user",node=>node.Role=="assistant",node=>node.Role=="footer")==fixtureReplyBounds,"Moving composer after its sibling message scroll does not alter response completion bounds");
var temporarilyNestedComposer=new CodexScopeFixture("ProseMirror",dynamicScroll,"composer");
dynamicScroll.Children.Remove(temporarilyNestedComposer);dynamicScroll.Children.Insert(0,temporarilyNestedComposer);
Check(ScreenShot.App.CodexScopePolicy.ReplyBounds(dynamicScroll.Descendants().ToArray(),node=>node.Role=="user",node=>node.Role=="assistant",node=>node.Role=="footer") is {} nestedBounds&&nestedBounds.Count==2,"A composer before the message roles is not used as a false response boundary");
var extraScroll=new CodexScopeFixture("group/thread-scroll-layout",fixtureFrame);
var extraAssistant=new CodexScopeFixture("sr-only",extraScroll,"assistant");
try {RecoverFixtureScroll(fixtureFrame,[generatedAssistantRole,extraAssistant]);Check(false,"Ambiguous role ancestors must not pick the first message region");}
catch(InvalidOperationException){Check(true,"Multiple message regions recovered from roles are rejected");}
try {ScreenShot.App.CodexScopePolicy.FindScroll(fixtureFrame.Descendants().ToArray(),node=>node.ClassName);Check(false,"Multiple scroll regions must be rejected");}
catch(InvalidOperationException){Check(true,"Multiple scroll regions are rejected instead of arbitrarily reading another conversation");}
var observationDocument=new CodexScopeFixture("",boundary:true);
var observationFrame=new CodexScopeFixture("_MainContentFrame_observed",observationDocument);
var observationComposer=new CodexScopeFixture("ProseMirror",observationFrame,"composer");
CodexScopeFixture[] DiscoverFixtureScopes(params CodexScopeFixture[] flattened)=>ScreenShot.App.CodexScopePolicy.DiscoverScopes(flattened,"_MainContentFrame_",node=>node.ClassName,node=>node.Role=="composer",FindFixtureScope,node=>node.RuntimeId);
var flattenedComposerScope=DiscoverFixtureScopes(observationComposer);
Check(flattenedComposerScope.Length==1&&ReferenceEquals(flattenedComposerScope[0],observationFrame),"Observer reacquires the captured MainContent scope through composer parents when flattened UIA descendants omit every layout ancestor");
Check(flattenedComposerScope[0].RuntimeId==observationFrame.RuntimeId,"Parent-discovered scope retains the prepared runtime identity instead of widening to the window");
Check(DiscoverFixtureScopes(observationFrame,observationComposer).Length==1,"Scope exposed both by flattened descendants and composer parents is deduplicated");
var observationFrameAlias=new CodexScopeFixture("_MainContentFrame_observed",runtimeId:observationFrame.RuntimeId);
Check(DiscoverFixtureScopes(observationFrameAlias,observationComposer).Length==1,"Distinct UIA wrappers of the same runtime scope are deduplicated by runtime identity");
var extraObservationComposer=new CodexScopeFixture("ProseMirror",observationFrame,"composer");
Check(DiscoverFixtureScopes(observationComposer,extraObservationComposer).Length==1&&!ValidateFixtureScope(observationFrame),"Multiple editors that share a scope do not duplicate its candidate or bypass the unique-composer validator");
var secondObservationFrame=new CodexScopeFixture("_MainContentFrame_second",observationDocument);
var secondObservationComposer=new CodexScopeFixture("ProseMirror",secondObservationFrame,"composer");
var distinctObservationScopes=DiscoverFixtureScopes(observationComposer,secondObservationComposer);
Check(distinctObservationScopes.Length==2&&distinctObservationScopes.Select(node=>node.RuntimeId).Distinct().Count()==2,"Editors in different scope identities remain separate candidates instead of becoming an arbitrary unique fallback");
var observationViewport=new CodexScopeFixture("_MainContentViewport_other",observationDocument);
Check(DiscoverFixtureScopes(observationViewport,observationComposer).All(node=>ScreenShot.App.CodexScopePolicy.ScopeKey(node.ClassName)=="_MainContentFrame_"),"Scope discovery preserves the captured scope kind when other main-layout kinds appear in the flattened tree");
Check(DiscoverFixtureScopes(sidebarComposer,unscopedComposer).Length==0,"Sidebar or unscoped editors cannot supply a window-wide observation candidate");
Check(DiscoverFixtureScopes(observationFrame).Length==1,"A known flattened scope remains available when the composer is temporarily absent");
Check(DiscoverFixtureScopes().Length==0,"An incomplete empty tree remains an observation miss without fabricating a scope");
var userMessageContainer=new CodexScopeFixture("user-message-container");
var bodyUserRole=new CodexScopeFixture("sr-only",userMessageContainer,"user");
var hiddenUserLabel=new CodexScopeFixture("",bodyUserRole,"role-text",name:"You said:");
var userBody=new CodexScopeFixture("rounded-lg bg-user-message text-primary",userMessageContainer,"user-body");
var userParagraph=new CodexScopeFixture("whitespace-pre-wrap",userBody);
var userBodyText=new CodexScopeFixture("",userParagraph,"text",name:"请描述并分析这张截图");
var localizedTimestamp=new CodexScopeFixture("text-secondary",userMessageContainer);
var localizedTimestampText=new CodexScopeFixture("",localizedTimestamp,"text",name:"星期日12:09");
CodexScopeFixture[] FollowingUserRole(CodexScopeFixture container,CodexScopeFixture role)=>container.Descendants().SkipWhile(node=>node!=role).Skip(1).ToArray();
(int Start,int Count)? UserBodyFixtureBounds(CodexScopeFixture role,IReadOnlyList<CodexScopeFixture> following)=>ScreenShot.App.CodexScopePolicy.UserBodyBounds(role,following,node=>node.Parent,node=>node.ClassName,node=>node.RuntimeId);
var followingBodyRole=FollowingUserRole(userMessageContainer,bodyUserRole);
var userBodyBounds=UserBodyFixtureBounds(bodyUserRole,followingBodyRole);
Check(userBodyBounds is {} isolatedBody&&followingBodyRole[isolatedBody.Start]==userParagraph&&isolatedBody.Count==2,"User body bounds exclude the bubble root, hidden role labels and a localized weekday timestamp sibling");
string[] IsolatedFixtureParts(IReadOnlyList<CodexScopeFixture> following,(int Start,int Count) bounds)=>following.Skip(bounds.Start).Take(bounds.Count).Where(node=>node.Role=="text").Select(node=>node.Name).ToArray();
var isolatedPromptUser=ScreenShot.App.CodexObservationPolicy.User(bodyUserRole.RuntimeId,IsolatedFixtureParts(followingBodyRole,userBodyBounds!.Value),"请描述并分析这张截图",includesMetadata:false);
Check(isolatedPromptUser.MatchesPrompt,"A real prompt body followed by a localized weekday timestamp still confirms the submitted message");
var localizedCompletedPolicy=new ScreenShot.App.CodexObservationPolicy(Snapshot());
Check(localizedCompletedPolicy.Observe(Snapshot(isolatedPromptUser) with {HasSend=false,HasReplyFooter=true,Reply="generated final"})==ScreenShot.App.CodexObservationState.Completed,"Isolated user evidence lets an already completed voice-composer reply bind and finish instead of waiting for submission");
var repeatedBodyContainer=new CodexScopeFixture("user-message-container");
var repeatedBodyRole=new CodexScopeFixture("sr-only",repeatedBodyContainer,"user");
var repeatedBody=new CodexScopeFixture("bg-user-message",repeatedBodyContainer);
var repeatedParagraph=new CodexScopeFixture("",repeatedBody);
foreach(string line in new[]{"第一行","第一行","23:32","23:32"})_ = new CodexScopeFixture("",repeatedParagraph,"text",name:line);
_ = new CodexScopeFixture("",repeatedBodyContainer,"text",name:"Sunday 12:09");
var repeatedFollowing=FollowingUserRole(repeatedBodyContainer,repeatedBodyRole);
var repeatedBounds=UserBodyFixtureBounds(repeatedBodyRole,repeatedFollowing);
Check(repeatedBounds is {Count:5}&&IsolatedFixtureParts(repeatedFollowing,repeatedBounds.Value).SequenceEqual(new[]{"第一行","第一行","23:32","23:32"}),"Body isolation preserves duplicate lines and time-shaped body text while excluding English weekday metadata");
Check(ScreenShot.App.CodexObservationPolicy.User(repeatedBodyRole.RuntimeId,IsolatedFixtureParts(repeatedFollowing,repeatedBounds!.Value),"第一行\n第一行\n23:32\n23:32",includesMetadata:false).MatchesPrompt,"Multiline repeated body text and literal time lines match the full prompt exactly");
try {UserBodyFixtureBounds(bodyUserRole,[userBody,userParagraph,localizedTimestamp,localizedTimestampText,userBodyText]);Check(false,"A noncontiguous user body must not confirm an incomplete prompt");}
catch(InvalidOperationException){Check(true,"A rebuilt noncontiguous body is rejected instead of silently dropping remaining prompt text");}
var legacyUserContainer=new CodexScopeFixture("user-message-container");
var legacyUserRole=new CodexScopeFixture("sr-only",legacyUserContainer,"user");
_ = new CodexScopeFixture("prefix-bg-user-message",legacyUserContainer,"text",name:"123");
Check(UserBodyFixtureBounds(legacyUserRole,FollowingUserRole(legacyUserContainer,legacyUserRole))==null,"An unknown legacy body keeps the existing fallback without mistaking a class substring for the known body token");
var secondUserBody=new CodexScopeFixture("bg-user-message",userMessageContainer);
_ = new CodexScopeFixture("",secondUserBody,"text",name:"other");
try {UserBodyFixtureBounds(bodyUserRole,FollowingUserRole(userMessageContainer,bodyUserRole));Check(false,"Multiple user bodies must not select the first candidate");}
catch(InvalidOperationException){Check(true,"Multiple user bodies in one role region are rejected as ambiguous");}
var foreignBodyContainer=new CodexScopeFixture("user-message-container");
var foreignBodyRole=new CodexScopeFixture("sr-only",foreignBodyContainer,"user");
var foreignBodyWrapper=new CodexScopeFixture("unidentified-wrapper",foreignBodyContainer);
var foreignUserBody=new CodexScopeFixture("bg-user-message",foreignBodyWrapper);
_ = new CodexScopeFixture("",foreignUserBody,"text",name:"123");
try {UserBodyFixtureBounds(foreignBodyRole,FollowingUserRole(foreignBodyContainer,foreignBodyRole));Check(false,"A known body in another parent must not be used for this user role");}
catch(InvalidOperationException){Check(true,"A known user body outside the role's direct parent is rejected instead of using legacy fallback");}
var readyDocument=new CodexScopeFixture("",boundary:true);
var readySidebar=new CodexScopeFixture("group/sidebar-rail",readyDocument);
var readyFrame=new CodexScopeFixture("_MainContentFrame_ready",readyDocument);
var deepComposerParent=readyFrame;
for(int depth=0;depth<12;depth++)deepComposerParent=new CodexScopeFixture("composer-layout",deepComposerParent);
var deepComposer=new CodexScopeFixture("ProseMirror",deepComposerParent,"composer");
var projectSelectorParent=new CodexScopeFixture("_ActiveProjectSelectorTrigger_generated",readyFrame);
var projectSelector=new CodexScopeFixture("_ComposerFooterDropdown_generated",projectSelectorParent,"button",name:"切换项目：TargetProject");
var sidebarSelectorParent=new CodexScopeFixture("_ActiveProjectSelectorTrigger_generated",readySidebar);
var sidebarSelector=new CodexScopeFixture("_ComposerFooterDropdown_generated",sidebarSelectorParent,"button",name:"切换项目：TargetProject");
var welcomeProjectLabel=new CodexScopeFixture("underline",readyFrame,"text",name:"TargetProject");
string readyDraft="";
bool FixtureProjectSelector(CodexScopeFixture node)=>node.Role=="button"&&node.Parent is {} parent&&ScreenShot.App.CodexScopePolicy.IsProjectSelector(node.ClassName,parent.ClassName);
bool FixtureAttachmentButton(CodexScopeFixture node)=>node.Role=="button"&&ScreenShot.App.CodexScopePolicy.IsDraftAttachmentName(node.Name);
CodexScopeFixture? ReadyFixtureComposer(params CodexScopeFixture[] flattened)=>ScreenShot.App.CodexScopePolicy.NewChatComposer(flattened,node=>node.Role=="composer",FindFixtureScope,node=>node.Descendants().ToArray(),(nodes,editor)=>
    ScreenShot.App.CodexScopePolicy.ValidateScope(nodes,node=>node.ClassName,node=>node.Role=="composer",node=>node.Role=="workspace-new-chat")
    &&ScreenShot.App.CodexScopePolicy.CanStartNewChat(nodes.Count(node=>node.Role=="composer"),nodes.Count(node=>node.Role=="user"),readyDraft,nodes.Any(FixtureAttachmentButton))
    &&ScreenShot.App.CodexScopePolicy.HasWorkspace(nodes,"TargetProject",FixtureProjectSelector,node=>node.Name));
Check(ReferenceEquals(ReadyFixtureComposer(deepComposer),deepComposer),"New-chat readiness uses the isolated scope through more than eight composer ancestors and a sibling project selector even when flat descendants omit the scope");
Check(!ScreenShot.App.CodexScopePolicy.IsProjectSelector("prefix_ComposerFooterDropdown_generated","_ActiveProjectSelectorTrigger_generated"),"Project selector recognition requires the actual class token prefix");
projectSelectorParent.Children.Remove(projectSelector);
Check(ReadyFixtureComposer(deepComposer,sidebarSelector,welcomeProjectLabel)==null,"Same workspace names in the sidebar or welcome heading cannot prove the composer project");
var wrongSelector=new CodexScopeFixture("_ComposerFooterDropdown_generated",projectSelectorParent,"button",name:"切换项目：OtherProject");
Check(ReadyFixtureComposer(deepComposer)==null,"A different semantic project selection rejects readiness despite a matching welcome label");
projectSelectorParent.Children.Remove(wrongSelector);projectSelectorParent.Children.Add(projectSelector);
var duplicateSelector=new CodexScopeFixture("_ComposerFooterDropdown_generated",projectSelectorParent,"button",name:"切换项目：TargetProject");
Check(ReadyFixtureComposer(deepComposer)==null,"Multiple semantic project selectors remain ambiguous instead of choosing the first");
projectSelectorParent.Children.Remove(duplicateSelector);
readyDraft="old draft";
Check(ReadyFixtureComposer(deepComposer)==null,"A matching project selector does not bypass a nonempty user draft");
readyDraft="";
var readyOldUser=new CodexScopeFixture("sr-only",readyFrame,"user");
Check(ReadyFixtureComposer(deepComposer)==null,"An old user message blocks the shared new-chat readiness operation");
readyFrame.Children.Remove(readyOldUser);
var readyAttachment=new CodexScopeFixture("",readyFrame,"button",name:"Remove attachment");
Check(ReadyFixtureComposer(deepComposer)==null,"An existing image attachment blocks the shared new-chat readiness operation");
readyFrame.Children.Remove(readyAttachment);
var readyMixedSidebar=new CodexScopeFixture("group/sidebar-rail",readyFrame);
Check(ReadyFixtureComposer(deepComposer)==null,"Scoped project evidence does not bypass sidebar isolation validation");
readyFrame.Children.Remove(readyMixedSidebar);
var readySecondComposer=new CodexScopeFixture("ProseMirror",readyFrame,"composer");
Check(ReadyFixtureComposer(deepComposer,readySecondComposer)==null,"The shared readiness read refuses multiple window composers");
readyFrame.Children.Remove(readySecondComposer);
CodexScopeFixture? FixtureDraftAttachment(CodexScopeFixture editor)
{
    var scope=FindFixtureScope(editor);if(scope==null)return null;
    var nodes=scope.Descendants().ToArray();
    if(!ScreenShot.App.CodexScopePolicy.ValidateScope(nodes,node=>node.ClassName,node=>node.Role=="composer",node=>node.Role=="workspace-new-chat")||nodes.Any(node=>node.Role=="user")
        ||!ScreenShot.App.CodexScopePolicy.HasWorkspace(nodes,"TargetProject",FixtureProjectSelector,node=>node.Name))return null;
    return ScreenShot.App.CodexScopePolicy.FindDraftAttachment(nodes,FixtureAttachmentButton);
}
var misleadingScreenshotButton=new CodexScopeFixture("sidebar-item",readySidebar,"button",name:"AI_ScreenShot");
new CodexScopeFixture("",readySidebar,"button",name:"Remove screenshot.png");
Check(!ScreenShot.App.CodexScopePolicy.IsDraftAttachmentName(misleadingScreenshotButton.Name)&&FixtureDraftAttachment(deepComposer)==null,"A sidebar AI_ScreenShot workspace or a sidebar Remove screenshot action cannot acknowledge an image paste");
var actualDraftAttachment=new CodexScopeFixture("",deepComposerParent,"button",name:"Remove image");
Check(ReferenceEquals(FixtureDraftAttachment(deepComposer),actualDraftAttachment),"A real image removal button inside the isolated draft confirms attachment readiness");
deepComposerParent.Children.Remove(actualDraftAttachment);
var siblingDraftAttachment=new CodexScopeFixture("",readyFrame,"button",name:"Remove screenshot.png");
Check(ReferenceEquals(FixtureDraftAttachment(deepComposer),siblingDraftAttachment),"An attachment displayed as an editor-body sibling remains valid inside the same empty isolated draft scope");
var duplicateAttachment=new CodexScopeFixture("",readyFrame,"button",name:"移除附件");
Check(FixtureDraftAttachment(deepComposer)==null,"Multiple draft image attachments are not silently treated as the single requested screenshot");
readyFrame.Children.Remove(duplicateAttachment);
readyFrame.Children.Add(readyOldUser);
Check(FixtureDraftAttachment(deepComposer)==null,"An attachment in an old user conversation is not used to confirm a new-chat paste");
readyFrame.Children.Remove(readyOldUser);readyFrame.Children.Remove(siblingDraftAttachment);
const string generatedPlaceholder="generated prompt placeholder";
var placeholderEditor=new CodexScopeFixture("ProseMirror",role:"composer",name:generatedPlaceholder);
var placeholderBranch=new CodexScopeFixture("placeholder",placeholderEditor,"group");
var placeholderInline=new CodexScopeFixture("",placeholderBranch,"group");
var placeholderLabel=new CodexScopeFixture("",placeholderInline,"text",name:generatedPlaceholder);
var placeholderBreak=new CodexScopeFixture("ProseMirror-trailingBreak",placeholderBranch,"text");
string? ReadFixturePlaceholder(CodexScopeFixture editor,string? draft)=>ScreenShot.App.CodexScopePolicy.DraftWithoutPlaceholder(draft,editor.Name,editor,node=>node.Children,node=>node.ClassName,node=>node.IsControl is false&&node.IsContent is false,node=>node.Role=="group",node=>node.Role=="text",node=>node.Name);
Check(ReadFixturePlaceholder(placeholderEditor,generatedPlaceholder)=="","The isolated raw placeholder group, matching label and empty trailing break prove an empty draft even when Value/Text include placeholder text");
Check(ReadFixturePlaceholder(placeholderEditor,null)==null,"Missing Value/Text is not fabricated from a known placeholder label");
Check(ReadFixturePlaceholder(placeholderEditor,"actual draft")=="actual draft","A Value differing from the placeholder label remains actual draft content");
var placeholderExtraText=new CodexScopeFixture("",placeholderBranch,"text",name:generatedPlaceholder);
Check(ReadFixturePlaceholder(placeholderEditor,generatedPlaceholder)==generatedPlaceholder,"An additional nonempty raw text prevents discarding draft content even if both texts match the placeholder");
placeholderBranch.Children.Remove(placeholderExtraText);
var placeholderExtraBranch=new CodexScopeFixture("",placeholderEditor,"group");
Check(ReadFixturePlaceholder(placeholderEditor,generatedPlaceholder)==generatedPlaceholder,"Any raw editor branch outside the sole placeholder container prevents treating a Value as empty");
placeholderEditor.Children.Remove(placeholderExtraBranch);
var actualPlaceholderEditor=new CodexScopeFixture("ProseMirror",role:"composer",name:generatedPlaceholder);
var actualPlaceholderParagraph=new CodexScopeFixture("",actualPlaceholderEditor,"group");
new CodexScopeFixture("",actualPlaceholderParagraph,"text",name:generatedPlaceholder);
new CodexScopeFixture("ProseMirror-trailingBreak",actualPlaceholderParagraph,"text");
Check(ReadFixturePlaceholder(actualPlaceholderEditor,generatedPlaceholder)==generatedPlaceholder,"A user who types the exact placeholder text in an ordinary paragraph keeps a nonempty draft");
placeholderBranch.Children.Remove(placeholderBreak);
Check(ReadFixturePlaceholder(placeholderEditor,generatedPlaceholder)==generatedPlaceholder,"A partial raw tree without the known empty trailing break cannot prove an empty draft");
placeholderBranch.Children.Add(placeholderBreak);
Check(ScreenShot.App.CodexScopePolicy.CanStartNewChat(1,0,ReadFixturePlaceholder(placeholderEditor,generatedPlaceholder),false),"The shared placeholder normalization reaches the production new-chat draft guard");
CodexScopeFixture FlaggedPlaceholderEditor(bool? control,bool? content)
{
    var editor=new CodexScopeFixture("ProseMirror",role:"composer",name:generatedPlaceholder);
    var branch=new CodexScopeFixture("placeholder",editor,"group",isControl:control,isContent:content);
    new CodexScopeFixture("",branch,"text",name:generatedPlaceholder);
    new CodexScopeFixture("ProseMirror-trailingBreak",branch,"text");
    return editor;
}
Check(ReadFixturePlaceholder(FlaggedPlaceholderEditor(true,false),generatedPlaceholder)==generatedPlaceholder,"A placeholder-named raw group exposed as an actual control cannot prove the draft empty");
Check(ReadFixturePlaceholder(FlaggedPlaceholderEditor(false,true),generatedPlaceholder)==generatedPlaceholder,"A placeholder-named raw group exposed as actual content cannot prove the draft empty");
Check(ReadFixturePlaceholder(FlaggedPlaceholderEditor(null,null),generatedPlaceholder)==generatedPlaceholder,"Unknown raw content/control flags cannot prove the draft empty");
Check(ScreenShot.App.CodexScopePolicy.ConfirmPlaceholderDraft(generatedPlaceholder,generatedPlaceholder)=="","The already-proven placeholder is empty only when the same Value/Text remains stable on reread");
Check(ScreenShot.App.CodexScopePolicy.ConfirmPlaceholderDraft(generatedPlaceholder,"new actual draft")=="new actual draft","A changed Value/Text after placeholder inspection retains the newly read actual draft");
Check(ScreenShot.App.CodexScopePolicy.ConfirmPlaceholderDraft(generatedPlaceholder,"")==generatedPlaceholder,"A Value/Text tree transition does not discard the original nonempty observation");
var auxiliaryWindow=new CodexWindowFixture("quick-chat",[]);
var sendingWindow=new CodexWindowFixture("main",["MathLearning","AI_ScreenShot"]);
var otherWorkspaceWindow=new CodexWindowFixture("other-workspace",["CS_Work"]);
bool MatchesRequestedWorkspace(CodexWindowFixture window)=>window.Workspaces.Contains("MathLearning",StringComparer.Ordinal);
Check(ReferenceEquals(ScreenShot.App.CodexWindowPolicy.Select(new[]{auxiliaryWindow,sendingWindow},MatchesRequestedWorkspace),sendingWindow),"An auxiliary QuickChat window without workspace actions cannot shadow the real sending window");
Check(ReferenceEquals(ScreenShot.App.CodexWindowPolicy.Select(new[]{sendingWindow},MatchesRequestedWorkspace),sendingWindow),"The actual main window with the requested workspace can be selected");
Check(ScreenShot.App.CodexWindowPolicy.Select(new[]{auxiliaryWindow,otherWorkspaceWindow},MatchesRequestedWorkspace)==null,"No matching main window returns an observation miss so the locator can wait");
try
{
    ScreenShot.App.CodexWindowPolicy.Select(new[]{sendingWindow,new CodexWindowFixture("second-main",["MathLearning"])},MatchesRequestedWorkspace);
    Check(false,"Multiple matching sending windows must be rejected");
}
catch(InvalidOperationException){Check(true,"Multiple matching sending windows report ambiguity instead of choosing the first one");}
Check(ReferenceEquals(ScreenShot.App.CodexWindowPolicy.Select(new[]{otherWorkspaceWindow,sendingWindow},MatchesRequestedWorkspace),sendingWindow),"Window selection matches the requested workspace instead of any Codex workspace");
int windowObservationCount=0;
var appearedWindow=await ScreenShot.App.CodexAutomation.WaitAsync(()=>
{
    windowObservationCount++;
    return ScreenShot.App.CodexWindowPolicy.Select(windowObservationCount<3?new[]{auxiliaryWindow}:new[]{auxiliaryWindow,sendingWindow},MatchesRequestedWorkspace);
},CancellationToken.None,TimeSpan.FromSeconds(2));
Check(ReferenceEquals(appearedWindow,sendingWindow)&&windowObservationCount==3,"A real main window appearing after auxiliary-only observations is reacquired by the normal bounded preparation wait");
Console.WriteLine($"PASS: {checks} core, Codex automation and clipboard backup assertions");

sealed class CodexScopeFixture
{
    private static int nextId;
    internal string RuntimeId {get;}
    internal string ClassName {get;}
    internal CodexScopeFixture? Parent {get;}
    internal string Role {get;}
    internal string Name {get;}
    internal bool? IsControl {get;}
    internal bool? IsContent {get;}
    internal bool Boundary {get;}
    internal List<CodexScopeFixture> Children {get;}=[];
    internal CodexScopeFixture(string className,CodexScopeFixture? parent=null,string role="",bool boundary=false,string? runtimeId=null,string name="",bool? isControl=false,bool? isContent=false)
    {
        RuntimeId=runtimeId??"fixture-"+(++nextId);ClassName=className;Parent=parent;Role=role;Boundary=boundary;Name=name;IsControl=isControl;IsContent=isContent;parent?.Children.Add(this);
    }
    internal IEnumerable<CodexScopeFixture> Descendants()
    {
        foreach(var child in Children)
        {
            yield return child;
            foreach(var descendant in child.Descendants())yield return descendant;
        }
    }
}
sealed record CodexWindowFixture(string Id,IReadOnlyList<string> Workspaces);

sealed class ClipboardRetryContext : SynchronizationContext
{
    private int posts;
    internal int Posts=>Volatile.Read(ref posts);
    public override void Post(SendOrPostCallback callback,object? state)
    {
        Interlocked.Increment(ref posts);
        ThreadPool.QueueUserWorkItem(_=>
        {
            var previous=Current;SetSynchronizationContext(this);
            try{callback(state);}
            finally{SetSynchronizationContext(previous);}
        });
    }
}
