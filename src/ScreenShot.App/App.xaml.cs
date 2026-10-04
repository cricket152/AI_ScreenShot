using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScreenShot.Core;

namespace ScreenShot.App;

public partial class App : Application
{
    private Mutex? instance;
    private TrayService? tray;
    private SelectionOverlay? overlay;
    private bool capturing;
    private int selectionVersion;
    private Window? settingsWindow;
    internal ToolbarWindow? Toolbar;
    internal FloatingBallWindow? Ball;
    internal FloatingPanelWindow? Panel;
    private bool ballWanted=true;
    internal Settings Settings {get;}=Settings.Load();
    internal BrowserBridge Bridge {get;private set;}=null!;
    internal CodexTarget Codex {get;private set;}=null!;
    public App(){Native.SetCurrentProcessExplicitAppUserModelID("AIScreenShot.Desktop");InitializeComponent();UnhandledException+=(_,e)=>{tray?.Notify("应用错误",e.Message);e.Handled=true;};}
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        bool overlayTest=Environment.GetCommandLineArgs().Contains("--overlay-test");
        bool toolbarTest=Environment.GetCommandLineArgs().Contains("--toolbar-test");
        bool clipboardTest=Environment.GetCommandLineArgs().Contains("--clipboard-test");
        bool floatingTest=Environment.GetCommandLineArgs().Contains("--floating-ui-test");
        bool providerTest=Environment.GetCommandLineArgs().Contains("--codex-provider-test");
        bool codexReadyTest=Environment.GetCommandLineArgs().Contains("--codex-ready-test");
        bool codexWindowTest=codexReadyTest||Environment.GetCommandLineArgs().Contains("--codex-window-test");
        instance=new Mutex(true,overlayTest?"Local\\AIScreenShot.OverlayDiagnostics":toolbarTest?"Local\\AIScreenShot.ToolbarDiagnostics":clipboardTest?"Local\\AIScreenShot.ClipboardDiagnostics":floatingTest?"Local\\AIScreenShot.FloatingDiagnostics":providerTest?"Local\\AIScreenShot.ProviderDiagnostics":codexWindowTest?"Local\\AIScreenShot.CodexWindowDiagnostics":"Local\\AIScreenShot.App",out bool created);if(!created){Exit();return;}
        if(overlayTest)
        {
            string output=Path.Combine(AppContext.BaseDirectory,"diagnostics");Directory.CreateDirectory(output);
            try{File.WriteAllText(Path.Combine(output,"overlay-test.txt"),RuntimeDiagnostics.VerifyOverlay());}
            catch(Exception e){File.WriteAllText(Path.Combine(output,"overlay-test.txt"),"FAIL: "+e.GetType().Name+": "+e.Message);Environment.ExitCode=1;}
            instance.ReleaseMutex();instance.Dispose();instance=null;Exit();return;
        }
        if(clipboardTest){RunClipboardTest();return;}
        if(providerTest){RunCodexProviderTest();return;}
        if(codexWindowTest){RunCodexWindowTest(codexReadyTest);return;}
        string[] launchArguments=Environment.GetCommandLineArgs();int browserIndex=Array.IndexOf(launchArguments,"--browser");
        if(browserIndex>=0&&browserIndex+1<launchArguments.Length&&launchArguments[browserIndex+1] is "Chrome" or "Edge"){Settings.Browser=launchArguments[browserIndex+1];Settings.Save();}
        var dispatcher=Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if(floatingTest){Codex=new(dispatcher);RunFloatingTest();return;}
        if(toolbarTest){Codex=new(dispatcher);RunToolbarTest();return;}
        Bridge=new(Settings);Codex=new(dispatcher,Settings);
        tray=new(Settings,()=>dispatcher.TryEnqueue(BeginCapture),()=>dispatcher.TryEnqueue(ShowSettings),()=>dispatcher.TryEnqueue(Shutdown),()=>dispatcher.TryEnqueue(ShowFloatingBall));
        if(!launchArguments.Contains("--smoke-test"))ShowFloatingBall();
        if(Environment.GetCommandLineArgs().Contains("--capture"))BeginCapture();
        if(Environment.GetCommandLineArgs().Contains("--settings"))ShowSettings();
        if(Environment.GetCommandLineArgs().Contains("--smoke-test"))RunSmokeTest();
        tray.Notify("AI ScreenShot 已启动","点击右侧悬浮球展开；Ctrl+Alt+S 截图。右键托盘可找回悬浮球。");
    }
    private async void RunCodexProviderTest()
    {
        string output=Path.Combine(AppContext.BaseDirectory,"diagnostics");Directory.CreateDirectory(output);
        try
        {
            using var provider=new CodexProviderTarget(Settings);
            _=await provider.CheckAsync(CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(output,"codex-provider-test.txt"),"PASS: installed Codex CLI app-server initialization, account readiness and configured image model compatibility\nNo thread, turn, screenshot, clipboard access or message submission was performed.\n");
        }
        catch(Exception error)
        {
            await File.WriteAllTextAsync(Path.Combine(output,"codex-provider-test.txt"),"FAIL: "+error.GetType().Name+": "+error.Message+"\n");Environment.ExitCode=1;
        }
        finally{instance?.ReleaseMutex();instance?.Dispose();instance=null;Exit();}
    }
    private async void RunCodexWindowTest(bool verifyReady=false)
    {
        string output=Path.Combine(AppContext.BaseDirectory,"diagnostics");Directory.CreateDirectory(output);
        string diagnosticFile=Path.Combine(output,verifyReady?"codex-ready-test.txt":"codex-window-test.txt");
        try
        {
            string result=await Task.Run(async()=>
            {
                using var automation=new FlaUI.UIA3.UIA3Automation();
                if(verifyReady)
                {
                    string[] arguments=Environment.GetCommandLineArgs();int workspaceIndex=Array.IndexOf(arguments,"--codex-ready-test")+1;
                    if(workspaceIndex==0||workspaceIndex>=arguments.Length||arguments[workspaceIndex].StartsWith("--",StringComparison.Ordinal))
                        throw new InvalidOperationException("Specify the workspace for the read-only readiness diagnostic");
                    string expectedWorkspace=arguments[workspaceIndex];
                    var readyWindow=await CodexAutomation.WaitAsync(()=>CodexWindowLocator.Locate(automation,expectedWorkspace),CancellationToken.None,TimeSpan.FromSeconds(15));
                    var editor=await CodexAutomation.WaitAsync(()=>CodexConversation.ReadyComposer(readyWindow,expectedWorkspace),CancellationToken.None,TimeSpan.FromSeconds(15));
                    if(CodexConversation.ReadDraft(editor)!=""||CodexConversation.DraftAttachment(readyWindow,expectedWorkspace)!=null)
                        throw new InvalidOperationException("The ready composer must have an empty draft and no attachment");
                    if(CodexConversation.ReadyComposer(readyWindow,"AIScreenShot.Generated.Absent."+Guid.NewGuid())!=null)
                        throw new InvalidOperationException("Another workspace cannot use this ready composer");
                    _=CodexConversation.Prepare(readyWindow,"");
                    return "PASS: requested workspace has a ready empty new-chat composer\nPASS: placeholder is interpreted as empty; no attachment is present\nPASS: another workspace cannot use the current composer\nPASS: submission context can be prepared on the real welcome page\nNo window activation, navigation, clipboard access, or message submission was performed.\n";
                }
                var window=await CodexAutomation.WaitAsync(()=>CodexWindowLocator.Locate(automation),CancellationToken.None,TimeSpan.FromSeconds(15));
                var actions=window.FindAllDescendants(cf=>cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button))
                    .Select(button=>CodexWindowLocator.NewChat.Match(button.Name)).Where(match=>match.Success).ToArray();
                var names=actions.Select(match=>match.Groups[1].Success?match.Groups[1].Value:match.Groups[2].Value)
                    .GroupBy(name=>name).Where(group=>group.Count()==1).Select(group=>group.Key).ToArray();
                if(names.Length==0)throw new InvalidOperationException("No unambiguous workspace action is available");
                var selected=await CodexAutomation.WaitAsync(()=>CodexWindowLocator.Locate(automation,names[0]),CancellationToken.None,TimeSpan.FromSeconds(15));
                if(selected.Properties.NativeWindowHandle.Value!=window.Properties.NativeWindowHandle.Value)
                    throw new InvalidOperationException("Workspace selection did not retain the discovered window");
                return $"PASS: visible Codex sending window discovered through workspace actions\nPASS: requested workspace resolves to the same window\nUnique workspace actions: {names.Length}\nNo window activation, navigation, clipboard access, or message submission was performed.\n";
            });
            await File.WriteAllTextAsync(diagnosticFile,result);
        }
        catch(Exception error)
        {
            await File.WriteAllTextAsync(diagnosticFile,$"FAIL: {error.GetType().Name}; HRESULT: 0x{error.HResult:X8}\n");
            Environment.ExitCode=1;
        }
        finally{Shutdown();}
    }
    private async void RunClipboardTest()
    {
        string output=Path.Combine(AppContext.BaseDirectory,"diagnostics");Directory.CreateDirectory(output);
        try{await File.WriteAllTextAsync(Path.Combine(output,"clipboard-test.txt"),await RuntimeDiagnostics.VerifyClipboardBackupAsync());}
        catch(Exception e){await File.WriteAllTextAsync(Path.Combine(output,"clipboard-test.txt"),"FAIL: "+e.GetType().Name+": "+e.Message);Environment.ExitCode=1;}
        finally{Shutdown();}
    }
    private async void RunFloatingTest()
    {
        string output=Path.Combine(AppContext.BaseDirectory,"diagnostics");Directory.CreateDirectory(output);
        try
        {
            ShowFloatingBall();await Task.Delay(250);
            var bounds=Ball!.GetBounds();double scale=Native.GetDpiForWindow(Ball.Handle)/96d;
            if(Math.Abs((bounds.Right-bounds.Left)/scale-52)>1||Math.Abs((bounds.Bottom-bounds.Top)/scale-52)>1)throw new InvalidOperationException("Floating ball is not 52 logical pixels");
            var ballRoot=(FrameworkElement)Ball.Content;
            await File.WriteAllBytesAsync(Path.Combine(output,"floating-ball.png"),await RuntimeDiagnostics.RenderAsync(ballRoot));
            Panel=new FloatingPanelWindow(this);Panel.Open();await Task.Delay(250);
            await File.WriteAllBytesAsync(Path.Combine(output,"floating-empty.png"),await RuntimeDiagnostics.RenderAsync((UIElement)Panel.Content));
            byte[] fixture=await RuntimeDiagnostics.FixtureAsync();await Panel.ShowDesignPreviewAsync(fixture);
            var root=(FrameworkElement)Panel.Content;
            foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark})
            {
                root.RequestedTheme=theme;await Task.Delay(400);
                if(Panel.MessageSurface.ActualWidth<=Panel.ToolbarSurface.ActualWidth||Panel.MessageSurface.ActualHeight<=Panel.ToolbarSurface.ActualHeight)throw new InvalidOperationException("Message surface must be larger than toolbar in both dimensions");
                var toolbarOrigin=Panel.ToolbarSurface.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                var messageOrigin=Panel.MessageSurface.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                if(Math.Abs(toolbarOrigin.X-messageOrigin.X)>1)throw new InvalidOperationException("Toolbar and messages are not left aligned");
                await File.WriteAllBytesAsync(Path.Combine(output,$"floating-{theme}.png"),await RuntimeDiagnostics.RenderAsync(root));
            }
            Panel.ShowProcessingPreview();await Task.Delay(250);
            await File.WriteAllBytesAsync(Path.Combine(output,"floating-processing.png"),await RuntimeDiagnostics.RenderAsync(root));
            var previewOne=Panel.ShowPreviewAsync(fixture);var previewTwo=Panel.ShowPreviewAsync(fixture);await Task.WhenAll(previewOne,previewTwo);
            if(!Panel.PreviewVisible||!Panel.PreviewOnTop)throw new InvalidOperationException("Screenshot detail preview did not open on top");
            var pendingPreview=Panel.ShowPreviewAsync(fixture);Panel.Collapse();await pendingPreview;
            if(Panel.PreviewVisible)throw new InvalidOperationException("Collapsed screenshot preview reopened after decoding");
            Panel.Open();
            var pendingOperationPreview=Panel.ShowPreviewAsync(fixture);
            SetFloatingOperationVisible(false);
            await pendingOperationPreview;
            if(Panel.PreviewVisible)throw new InvalidOperationException("Screenshot detail preview reopened during operation after decoding");
            if(Native.IsWindowVisible(Panel.Handle)||Native.IsWindowVisible(Ball.Handle))throw new InvalidOperationException("Floating windows did not hide for operation");
            SetFloatingOperationVisible(true);
            if(!Native.IsWindowVisible(Panel.Handle)||!Native.IsWindowVisible(Ball.Handle))throw new InvalidOperationException("Floating windows did not restore after operation");
            overlay=new SelectionOverlay(new DesktopCapture(new PixelRect(-120,-60,37,29),new byte[37*29*4]),_=>{});capturing=true;
            nint overlayHandle=overlay.Handle;EndCaptureSession(false);
            if(Native.IsWindowVisible(overlayHandle)||!Native.IsWindowVisible(Panel.Handle))throw new InvalidOperationException("Submitted capture did not dismiss overlay while retaining message panel");
            HideFloatingBall();SetFloatingOperationVisible(true);
            if(Native.IsWindowVisible(Ball.Handle))throw new InvalidOperationException("Manually hidden floating ball reopened");
            CollapseFloatingPanel();
            if(Native.IsWindowVisible(Panel.Handle))throw new InvalidOperationException("Collapsed message panel remains visible");
            await Panel.PresentAsync(fixture,new PixelRect(0,0,320,200),()=>false);
            if(Native.IsWindowVisible(Panel.Handle))throw new InvalidOperationException("Cancelled capture reopened message panel");
            ShowFloatingBall();Ball.SnapToEdge();Panel.Open();
            overlay=new SelectionOverlay(new DesktopCapture(new PixelRect(-120,-60,37,29),new byte[37*29*4]),_=>{});capturing=true;
            nint closedOverlayHandle=overlay.Handle;Panel.Close();
            if(Panel!=null||Native.IsWindowVisible(closedOverlayHandle))throw new InvalidOperationException("Closing message panel did not dismiss capture overlay");
            string codexChecks=await VerifyCodexPanelSendAsync(fixture);
            await File.WriteAllTextAsync(Path.Combine(output,"floating-ui-test.txt"),"PASS: 52 logical pixel native ball, panel creation, empty state and Light/Dark generated previews\nPASS: toolbar/message left alignment; messages larger in both dimensions\nPASS: processing state, operation hide/restore, successful capture dismissal retains messages\nPASS: on-top details, concurrent preview replacement, collapsed/operation preview stays closed after decoding\nPASS: hidden ball remains hidden, collapse/reopen, cancelled capture does not reopen\nPASS: closing message panel dismisses capture overlay\n"+codexChecks+"No desktop screenshot, clipboard access, browser connection, or real send was performed.\n");
        }
        catch(Exception e){await File.WriteAllTextAsync(Path.Combine(output,"floating-ui-test.txt"),"FAIL: "+e.GetType().Name+": "+e.Message);Environment.ExitCode=1;}
        finally{Shutdown();}
    }
    private async Task<string> VerifyCodexPanelSendAsync(byte[] fixture)
    {
        string? previousWorkspace=Settings.Workspace;Settings.Workspace="UI 诊断工作区";
        async Task<FloatingPanelWindow> CreatePanel(Func<SendRequest,Action<CodexReplyUpdate>,CancellationToken,Task<SendResult>> sender)
        {
            Panel?.Close();var panel=new FloatingPanelWindow(this,sender);Panel=panel;
            await panel.PresentAsync(fixture,new PixelRect(0,0,320,200));return panel;
        }
        static void Visible(FloatingPanelWindow panel,string phase)
        {
            if(!Native.IsWindowVisible(panel.Handle)||panel.ToolbarSurface.Visibility!=Visibility.Visible||panel.MessageSurface.Visibility!=Visibility.Visible)
                throw new InvalidOperationException("Codex panel was hidden during "+phase);
        }
        try
        {
            Action<CodexReplyUpdate>? update=null;int sendCount=0;
            var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var completion=new TaskCompletionSource<SendResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var panel=await CreatePanel((request,onUpdate,token)=>
            {
                if(!request.Png.AsSpan().SequenceEqual(fixture))throw new InvalidOperationException("Diagnostic request lost its generated screenshot");
                sendCount++;update=onUpdate;entered.SetResult();return completion.Task.WaitAsync(token);
            });
            overlay=new SelectionOverlay(new DesktopCapture(new PixelRect(-120,-60,37,29),new byte[37*29*4]),_=>{});capturing=true;
            nint selectedOverlay=overlay.Handle;Task sending=panel.SendCodexAsync();await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Visible(panel,"submission");
            if(Native.IsWindowVisible(selectedOverlay)||!panel.IsBusy||!panel.IsProcessing||!panel.StatusText.Contains("正在发送"))throw new InvalidOperationException("Codex submission did not dismiss the overlay and retain progress");
            update!(new(CodexReplyPhase.Submitted));nint foreground=Native.GetForegroundWindow();update(new(CodexReplyPhase.Thinking));
            Visible(panel,"reply waiting");
            if(!panel.IsProcessing||!panel.StatusText.Contains("正在思考")||panel.ResponseText.Contains("未知")||Native.GetForegroundWindow()!=foreground)throw new InvalidOperationException("Reply waiting lost progress or changed foreground focus");
            update(new(CodexReplyPhase.Thinking,"我正在读取截图。","正在读取截图"));
            if(panel.ResponseText!="我正在读取截图。"||panel.StatusText!="Codex · 正在读取截图"||panel.ResponseHeading!="Codex · 正在读取截图"||panel.MessageCount!=2)throw new InvalidOperationException("First streamed work status was not shown in the existing reply bubble");
            update(new(CodexReplyPhase.Thinking,"我正在读取截图。\n已开始分析画面。","正在分析"));
            if(panel.ResponseText!="我正在读取截图。\n已开始分析画面。"||panel.StatusText!="Codex · 正在分析"||panel.ResponseHeading!="Codex · 正在分析"||panel.MessageCount!=2||!panel.IsBusy||!panel.IsProcessing||!panel.CanStopWaiting||panel.CanSendToCodex||Native.GetForegroundWindow()!=foreground)throw new InvalidOperationException("Incremental reply text duplicated bubbles or lost live status, submission protection or foreground focus");
            update(new(CodexReplyPhase.Thinking,Status:"正在处理工具结果"));
            if(panel.ResponseText!="我正在读取截图。\n已开始分析画面。"||panel.StatusText!="Codex · 正在处理工具结果")throw new InvalidOperationException("A status-only update discarded the streamed reply");
            string streamedText=string.Join("\n",Enumerable.Range(1,48).Select(index=>"生成图诊断公开进度 "+index));
            update(new(CodexReplyPhase.Thinking,streamedText,"正在读取文件"));await Task.Delay(250);
            if(panel.MessageScrollHeight<=16||Math.Abs(panel.MessageScrollHeight-panel.MessageScrollOffset)>1)throw new InvalidOperationException("Streamed reply did not follow the bottom of the message list");
            panel.ScrollMessages(0);await Task.Delay(250);
            update(new(CodexReplyPhase.Thinking,streamedText+"\n生成图诊断新阶段","正在分析"));await Task.Delay(250);
            if(panel.MessageScrollOffset>1)throw new InvalidOperationException("A streamed update overrode the user's scroll position");
            panel.ScrollMessages(panel.MessageScrollHeight);await Task.Delay(250);
            update(new(CodexReplyPhase.Thinking,streamedText+"\n生成图诊断新阶段\n正在准备结果","正在输出"));await Task.Delay(250);
            if(Math.Abs(panel.MessageScrollHeight-panel.MessageScrollOffset)>1||Native.GetForegroundWindow()!=foreground)throw new InvalidOperationException("Following streamed output did not resume at the bottom without changing foreground focus");
            update(new(CodexReplyPhase.Completed,"生成图诊断回复"));update(new(CodexReplyPhase.Thinking,"诊断迟到进度","正在处理"));
            completion.SetResult(new(SendState.Sent,"生成图诊断提交成功"));await sending.WaitAsync(TimeSpan.FromSeconds(5));
            Visible(panel,"reply completion");
            if(panel.IsBusy||panel.IsProcessing||panel.CanStopWaiting||panel.ResponseText!="生成图诊断回复"||panel.ResponseHeading!="Codex"||Native.GetForegroundWindow()!=foreground)throw new InvalidOperationException("Reply completion did not promptly release busy state and retain its text and foreground focus");
            await panel.SendCodexAsync();if(sendCount!=1)throw new InvalidOperationException("Confirmed Codex submission was repeated");

            panel=await CreatePanel((_,onUpdate,_)=>Task.Run(()=>
            {
                onUpdate(new(CodexReplyPhase.Submitted));onUpdate(new(CodexReplyPhase.Thinking,"后台诊断首段","正在读取截图"));
                onUpdate(new(CodexReplyPhase.Thinking,"后台诊断首段\n后台诊断第二段","正在输出"));onUpdate(new(CodexReplyPhase.Completed,"后台生成图诊断回复"));
                return new SendResult(SendState.Sent,"后台生成图诊断提交成功");
            }));
            await panel.SendCodexAsync();Visible(panel,"background reply completion");
            if(panel.IsBusy||panel.IsProcessing||panel.CanSendToCodex||panel.ResponseText!="后台生成图诊断回复"||panel.ResponseHeading!="Codex")throw new InvalidOperationException("Sender completion discarded queued background reply updates");

            var confirming=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int confirmingCalls=0;
            panel=await CreatePanel(async(_,_,token)=>
            {
                confirmingCalls++;confirming.SetResult();
                try{await Task.Delay(Timeout.InfiniteTimeSpan,token);}
                catch(OperationCanceledException){return new(SendState.Unknown,"诊断提交确认已停止，结果未知");}
                throw new InvalidOperationException("Unconfirmed diagnostic submission unexpectedly completed");
            });
            sending=panel.SendCodexAsync();await confirming.Task.WaitAsync(TimeSpan.FromSeconds(5));Visible(panel,"unconfirmed submission waiting");
            if(!panel.CanStopWaiting||!panel.IsBusy||!panel.IsProcessing||panel.ResponseHeading!="Codex · 发送状态")throw new InvalidOperationException("Unconfirmed submission cannot stop waiting or was reported as a reply");
            panel.StopWaiting();await sending.WaitAsync(TimeSpan.FromSeconds(5));Visible(panel,"cancelled submission confirmation");
            if(panel.IsBusy||panel.IsProcessing||panel.CanStopWaiting||panel.CanSendToCodex||panel.ResponseHeading!="Codex · 发送状态"||!panel.ResponseText.Contains("结果未知"))throw new InvalidOperationException("Stopping confirmation lost the unknown submission receipt or resend protection");
            await panel.SendCodexAsync();if(confirmingCalls!=1)throw new InvalidOperationException("Cancelled unknown submission was repeated");

            var waiting=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            panel=await CreatePanel(async(_,onUpdate,token)=>
            {
                onUpdate(new(CodexReplyPhase.Submitted));onUpdate(new(CodexReplyPhase.Thinking));waiting.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan,token);return new(SendState.Sent,"生成图诊断提交成功");
            });
            sending=panel.SendCodexAsync();await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));panel.StopWaiting();await sending.WaitAsync(TimeSpan.FromSeconds(5));
            Visible(panel,"cancelled reply waiting");
            if(panel.IsBusy||panel.CanSendToCodex||!panel.StatusText.Contains("已停止等待")||panel.ResponseHeading!="Codex · 回复状态")throw new InvalidOperationException("Stopping reply waiting changed confirmed submission state");

            waiting=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            panel=await CreatePanel(async(_,onUpdate,token)=>
            {
                onUpdate(new(CodexReplyPhase.Submitted));onUpdate(new(CodexReplyPhase.Thinking,"停止等待前的生成图诊断正文","正在分析"));waiting.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan,token);return new(SendState.Sent,"生成图诊断提交成功");
            });
            sending=panel.SendCodexAsync();await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));panel.StopWaiting();await sending.WaitAsync(TimeSpan.FromSeconds(5));
            Visible(panel,"cancelled streamed reply waiting");
            if(panel.IsBusy||panel.IsProcessing||panel.CanStopWaiting||panel.CanSendToCodex||panel.ResponseText!="停止等待前的生成图诊断正文"||!panel.StatusText.Contains("已停止等待")||panel.ResponseHeading!="Codex · 回复状态")throw new InvalidOperationException("Stopping reply waiting discarded text that had already streamed");

            foreach(bool partialReply in new[]{true,false})
            {
                panel=await CreatePanel((_,onUpdate,_)=>Task.Run(()=>
                {
                    onUpdate(new(CodexReplyPhase.Submitted));
                    if(partialReply)onUpdate(new(CodexReplyPhase.Thinking,"连接中断前的生成图诊断正文","正在输出"));
                    onUpdate(new(CodexReplyPhase.Unavailable,"诊断回复连接已断开，请到 Codex 查看处理结果。"));
                    return new SendResult(SendState.Sent,"生成图诊断提交成功");
                }));
                await panel.SendCodexAsync();Visible(panel,"unavailable reply reading");
                string expectedReply=partialReply?"连接中断前的生成图诊断正文":"诊断回复连接已断开，请到 Codex 查看处理结果。";
                if(panel.IsBusy||panel.IsProcessing||panel.CanStopWaiting||panel.CanSendToCodex||panel.ResponseText!=expectedReply||!panel.StatusText.Contains("回复连接已断开")||panel.ResponseHeading!="Codex · 回复状态")throw new InvalidOperationException("Unavailable reply reading lost its reason or replaced already streamed text");
            }

            entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);completion=new TaskCompletionSource<SendResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            panel=await CreatePanel(async(_,onUpdate,token)=>
            {
                update=onUpdate;onUpdate(new(CodexReplyPhase.Submitted));entered.SetResult();
                var result=await completion.Task.WaitAsync(token);onUpdate(new(CodexReplyPhase.Completed,"收起后的生成图回复"));return result;
            });
            sending=panel.SendCodexAsync();await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));panel.Collapse();update!(new(CodexReplyPhase.Thinking));
            completion.SetResult(new(SendState.Sent,"生成图诊断提交成功"));await sending;
            if(Native.IsWindowVisible(panel.Handle))throw new InvalidOperationException("Asynchronous reply reopened a collapsed panel");

            entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);completion=new TaskCompletionSource<SendResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            panel=await CreatePanel((_,onUpdate,token)=>{update=onUpdate;onUpdate(new(CodexReplyPhase.Submitted));entered.SetResult();return completion.Task.WaitAsync(token);});
            sending=panel.SendCodexAsync();await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));nint closedPanel=panel.Handle;panel.Close();await sending.WaitAsync(TimeSpan.FromSeconds(5));
            update!(new(CodexReplyPhase.Completed,"关闭后的生成图回复"));
            if(Panel!=null||Native.IsWindowVisible(closedPanel))throw new InvalidOperationException("Asynchronous reply revived a closed panel");

            panel=await CreatePanel((_,onUpdate,_)=>
            {
                onUpdate(new(CodexReplyPhase.Submitted));
                onUpdate(new(CodexReplyPhase.Unavailable,"provider 终态保留的正文","provider 回复失败"));
                return Task.FromResult(new SendResult(SendState.Sent,"诊断已提交"));
            });
            await panel.SendCodexAsync();
            if(panel.ResponseText!="provider 终态保留的正文"||panel.StatusText!="provider 回复失败"||panel.IsProcessing)
                throw new InvalidOperationException("Provider terminal status overwrote retained reply text");
            panel=await CreatePanel((_,onUpdate,_)=>
            {
                onUpdate(new(CodexReplyPhase.Submitted));onUpdate(new(CodexReplyPhase.Thinking,"provider 过程文字"));
                onUpdate(new(CodexReplyPhase.Completed,"","Codex 已完成，未返回回复正文"));
                return Task.FromResult(new SendResult(SendState.Sent,"诊断已提交"));
            });
            await panel.SendCodexAsync();
            if(panel.ResponseText!="Codex 已完成，未返回回复正文"||panel.IsProcessing||panel.CanSendToCodex)
                throw new InvalidOperationException("Empty provider final answer was replaced by old commentary");
            foreach(var state in new[]{SendState.Failed,SendState.Unknown})
            {
                int calls=0;
                panel=await CreatePanel((_,_,_)=>{calls++;return Task.FromResult(new SendResult(state,state==SendState.Failed?"诊断发送失败":"诊断提交结果未知"));});
                await panel.SendCodexAsync();Visible(panel,state+" submission result");
                if(panel.IsBusy||panel.IsProcessing||panel.CanSendToCodex!=(state==SendState.Failed)||panel.ResponseHeading!="Codex · 发送状态")throw new InvalidOperationException("Submission result was confused with a Codex reply");
                if(state==SendState.Unknown){await panel.SendCodexAsync();if(calls!=1)throw new InvalidOperationException("Unknown Codex submission was repeated");}
            }
            return "PASS: injected Codex send keeps toolbar/messages visible during submission, reply waiting and completion; selection overlay ends at click\nPASS: streamed public status and growing text update one reply bubble; status-only updates preserve text\nPASS: streaming follows the bottom, respects the user's scroll, and resumes following when returned to the bottom\nPASS: waiting/completed progress keeps foreground focus; confirmed/unknown submissions cannot resend\nPASS: background Submitted/Thinking/Completed updates retain the true reply after sender completion; late progress cannot overwrite completion\nPASS: stopping unconfirmed submission returns an Unknown send receipt and prevents resend\nPASS: stopping reply waiting and unavailable reading preserve streamed text while showing the reason in status\nPASS: stopping reply waiting preserves submission; async replies do not reopen collapsed/closed panels\nPASS: Failed/Unknown are submission receipts, never assistant replies or waiting-timeout results\n";
        }
        finally{Settings.Workspace=previousWorkspace;}
    }
    internal void BeginCapture()
    {
        if(Toolbar?.IsSending==true||Panel?.IsBusy==true)return;
        EndCaptureSession();
        try
        {
            Toolbar?.AppWindow.Hide();Panel?.HideForOperation();Ball?.AppWindow.Hide();Native.DwmFlush();
            var screenshot=new DesktopCapture();capturing=true;
            overlay=new SelectionOverlay(screenshot,async rect=>
            {
                if(rect==null){EndCaptureSession();return;}
                int version=++selectionVersion;
                try
                {
                    var image=await screenshot.CropAsync(rect.Value);
                    if(!capturing||version!=selectionVersion)return;
                    Panel??=new FloatingPanelWindow(this);await Panel.PresentAsync(image,rect.Value,()=>capturing&&version==selectionVersion);
                }
                catch(Exception e){if(version==selectionVersion){EndCaptureSession();tray?.Notify("截图失败",e.Message);}}
            },()=>{selectionVersion++;Toolbar?.AppWindow.Hide();Panel?.HideForOperation();},()=>EndCaptureSession());
        }
        catch(Exception e){capturing=false;ShowFloatingBallIfWanted();tray?.Notify("截图失败",e.Message);}
    }
    internal void EndCaptureSession()=>EndCaptureSession(true);
    internal void EndCaptureSession(bool hidePanel)
    {
        selectionVersion++;capturing=false;
        var current=overlay;overlay=null;current?.Close();Toolbar?.AppWindow.Hide();
        if(hidePanel)Panel?.Collapse();ShowFloatingBallIfWanted();
    }
    internal void ShowFloatingBall()
    {
        ballWanted=true;
        Ball??=new FloatingBallWindow(OpenFloatingPanel,HideFloatingBall,ShowSettings);
        Ball.Present();
    }
    private void ShowFloatingBallIfWanted(){if(ballWanted&&!capturing)Ball?.AppWindow.Show(false);}
    internal void HideFloatingBall(){ballWanted=false;Ball?.AppWindow.Hide();}
    private void OpenFloatingPanel(){Panel??=new FloatingPanelWindow(this);Panel.Open();}
    internal void CollapseFloatingPanel(){EndCaptureSession();ShowFloatingBallIfWanted();}
    internal void SetFloatingOperationVisible(bool visible)
    {
        overlay?.SetVisible(visible);
        if(visible){Panel?.RestoreAfterOperation();ShowFloatingBallIfWanted();}
        else{Panel?.HideForOperation();Ball?.AppWindow.Hide();}
    }
    internal void SetCaptureSessionVisible(bool visible)
    {
        if(visible&&!capturing)return;
        overlay?.SetVisible(visible);
        if(visible)Toolbar?.AppWindow.Show(false);else Toolbar?.AppWindow.Hide();
    }
    private async void RunToolbarTest()
    {
        string output=Path.Combine(AppContext.BaseDirectory,"diagnostics");Directory.CreateDirectory(output);
        try
        {
            byte[] fixture=await RuntimeDiagnostics.FixtureAsync();Toolbar=new ToolbarWindow(this);
            overlay=new SelectionOverlay(new DesktopCapture(new PixelRect(-120,-60,37,29),new byte[37*29*4]),_=>{},cancelled:EndCaptureSession);capturing=true;
            nint overlayHandle=overlay.Handle;
            Toolbar.Present(fixture,new PixelRect(100,100,320,200));
            var root=(FrameworkElement)Toolbar.Content;
            foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark})
            {
                root.RequestedTheme=theme;await Task.Delay(500);
                await File.WriteAllBytesAsync(Path.Combine(output,$"toolbar-{theme}.png"),await RuntimeDiagnostics.RenderAsync(root));
                root.Measure(new Windows.Foundation.Size(root.ActualWidth,double.PositiveInfinity));
                if(root.DesiredSize.Height>root.ActualHeight+1)throw new InvalidOperationException("Toolbar content is vertically clipped");
            }
            SetCaptureSessionVisible(false);
            if(Native.IsWindowVisible(overlayHandle)||Native.IsWindowVisible(Toolbar.Handle))throw new InvalidOperationException("Capture session did not hide both windows");
            SetCaptureSessionVisible(true);
            if(!Native.IsWindowVisible(overlayHandle)||!Native.IsWindowVisible(Toolbar.Handle))throw new InvalidOperationException("Capture session did not restore both windows");
            foreach(var state in new[]{SendState.Failed,SendState.Unknown})
            {
                SetCaptureSessionVisible(false);Toolbar.ApplySendResult("Codex",new(state,"Diagnostic result"));SetCaptureSessionVisible(true);
                if(!Native.IsWindowVisible(overlayHandle)||!Native.IsWindowVisible(Toolbar.Handle))throw new InvalidOperationException("Failed/unknown Codex send dismissed the capture session");
            }
            Toolbar.ApplySendResult("Codex",new(SendState.Sent,"Diagnostic result"),false);SetCaptureSessionVisible(true);
            if(!Native.IsWindowVisible(overlayHandle)||!Native.IsWindowVisible(Toolbar.Handle))throw new InvalidOperationException("Combined send dismissed independent target feedback");
            SetCaptureSessionVisible(false);Toolbar.ApplySendResult("Codex",new(SendState.Sent,"Diagnostic result"));SetCaptureSessionVisible(true);
            if(Native.IsWindowVisible(overlayHandle)||Native.IsWindowVisible(Toolbar.Handle))throw new InvalidOperationException("Successful Codex send reopened the capture session");
            EndCaptureSession();SetCaptureSessionVisible(true);
            if(Native.IsWindowVisible(overlayHandle)||Native.IsWindowVisible(Toolbar.Handle))throw new InvalidOperationException("Dismissed capture session reopened");
            await File.WriteAllTextAsync(Path.Combine(output,"toolbar-test.txt"),"PASS: native CommandBar creation, Light/Dark rendering and content height\nPASS: overlay and toolbar hide/restore together; cancelled session stays closed\nPASS: Codex success dismisses both windows; failed/unknown results and combined-send feedback retain capture\n");
        }
        catch(Exception e){await File.WriteAllTextAsync(Path.Combine(output,"toolbar-test.txt"),"FAIL: "+e.GetType().Name+": "+e.Message);Environment.ExitCode=1;}
        finally{Toolbar?.Close();Shutdown();}
    }
    internal void ShowSettings()
    {
        if(capturing)EndCaptureSession();Panel?.Collapse();
        if(settingsWindow!=null){settingsWindow.Activate();return;}
        settingsWindow=new Window{Title="AI ScreenShot 设置"};var window=settingsWindow;
        var panel=new StackPanel{Padding=new Thickness(24),Spacing=12};
        panel.Children.Add(new TextBlock{Text="AI ScreenShot",FontSize=24});
        var browser=new ComboBox{Header="ChatGPT 浏览器",ItemsSource=new[]{"Edge","Chrome"},SelectedItem=Settings.Browser,HorizontalAlignment=HorizontalAlignment.Stretch};panel.Children.Add(browser);
        var codexDirectory=new TextBlock{Text=Settings.CodexDirectory??"尚未选择工作目录",TextWrapping=TextWrapping.Wrap};panel.Children.Add(codexDirectory);
        var chooseDirectory=new Button{Content="选择 Codex 工作目录…"};panel.Children.Add(chooseDirectory);
        var codexExecutable=new TextBox{Header="Codex CLI 可执行文件（留空自动查找）",Text=Settings.CodexExecutable??"",PlaceholderText="codex.exe 的完整路径"};panel.Children.Add(codexExecutable);
        var key=new TextBox{Header="截图快捷键字母或数字",Text=((char)Settings.HotkeyKey).ToString(),MaxLength=1};panel.Children.Add(key);
        var mods=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12};var ctrl=new CheckBox{Content="Ctrl",IsChecked=(Settings.HotkeyModifiers&2)!=0};var alt=new CheckBox{Content="Alt",IsChecked=(Settings.HotkeyModifiers&1)!=0};var shift=new CheckBox{Content="Shift",IsChecked=(Settings.HotkeyModifiers&4)!=0};var win=new CheckBox{Content="Win",IsChecked=(Settings.HotkeyModifiers&8)!=0};foreach(var check in new[]{ctrl,alt,shift,win})mods.Children.Add(check);panel.Children.Add(mods);
        var feedback=new TextBlock{Text=Bridge.Status,TextWrapping=TextWrapping.Wrap};panel.Children.Add(feedback);
        chooseDirectory.Click+=async(_,_)=>
        {
            chooseDirectory.IsEnabled=false;
            try{if(await CodexDirectoryPicker.PickAsync(window,Settings)){codexDirectory.Text=Settings.CodexDirectory;feedback.Text="Codex 工作目录已保存";}}
            catch(Exception error){feedback.Text="选择工作目录失败："+error.Message;}
            finally{chooseDirectory.IsEnabled=true;}
        };
        var checkCodex=new Button{Content="检测 Codex provider"};checkCodex.Click+=async(_,_)=>
        {
            checkCodex.IsEnabled=false;feedback.Text="正在检测 Codex provider…";
            try{feedback.Text=await Codex.CheckProviderAsync(CancellationToken.None);}
            catch(Exception error){feedback.Text="Codex provider 检测失败："+error.Message;}
            finally{checkCodex.IsEnabled=true;}
        };panel.Children.Add(checkCodex);
        var apply=new Button{Content="保存设置"};apply.Click+=(_,_)=>
        {
            string letter=key.Text.ToUpperInvariant();uint modifiers=(ctrl.IsChecked==true?2u:0)|(alt.IsChecked==true?1u:0)|(shift.IsChecked==true?4u:0)|(win.IsChecked==true?8u:0);
            if(letter.Length!=1||!char.IsAsciiLetterOrDigit(letter[0])||modifiers==0){feedback.Text="请输入一个英文字母或数字，并选择至少一个修饰键";return;}
            string executable=codexExecutable.Text.Trim();
            if(executable.Length>0){try{_=CodexProviderExecutable.Resolve(executable);}catch(Exception error){feedback.Text=error.Message;return;}}
            if(tray?.ChangeHotkey(modifiers,letter[0])!=true){tray?.ChangeHotkey(Settings.HotkeyModifiers,Settings.HotkeyKey);feedback.Text="快捷键已被占用，原快捷键已恢复";return;}
            Settings.HotkeyModifiers=modifiers;Settings.HotkeyKey=letter[0];Settings.Browser=browser.SelectedItem?.ToString()??"Edge";Settings.CodexExecutable=executable.Length==0?null:executable;Settings.Save();feedback.Text="设置已保存 · "+Bridge.Status;
        };panel.Children.Add(apply);
        var checkBridge=new Button{Content="检测扩展连接"};checkBridge.Click+=(_,_)=>feedback.Text=Bridge.Status;panel.Children.Add(checkBridge);
        var instructions=new TextBlock{Text="扩展安装：打开 edge://extensions 或 chrome://extensions，启用开发者模式并加载发布目录的 extension 文件夹。复制扩展 ID，按 README 运行注册脚本，扩展会自动连接；程序重启后自动重连。",TextWrapping=TextWrapping.Wrap};panel.Children.Add(instructions);
        var folder=new Button{Content="打开程序目录"};folder.Click+=(_,_)=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",AppContext.BaseDirectory){UseShellExecute=true});panel.Children.Add(folder);
        window.Content=new ScrollViewer{Content=panel};window.AppWindow.Resize(new Windows.Graphics.SizeInt32(560,620));window.Closed+=(_,_)=>settingsWindow=null;window.Activate();
    }
    private void Shutdown(){ballWanted=false;EndCaptureSession();Panel?.Close();Ball?.Close();Codex?.Dispose();ClipboardService.ReleaseRetained();Bridge?.Dispose();tray?.Dispose();instance?.ReleaseMutex();instance?.Dispose();Exit();}
    private async void RunSmokeTest()
    {
        string output=Path.Combine(AppContext.BaseDirectory,"diagnostics");Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output,"runtime-error.txt"),"No errors reported in this run.");
        try
        {
            ShowSettings();await Task.Delay(1000);Native.DwmFlush();
            nint settingsHandle=WinRT.Interop.WindowNative.GetWindowHandle(settingsWindow!);Native.GetWindowRect(settingsHandle,out var rect);
            var desktop=new DesktopCapture();byte[] nativeCrop=await desktop.CropAsync(new PixelRect(rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top));
            byte[] settingsPng=await RuntimeDiagnostics.RenderAsync((UIElement)settingsWindow!.Content);
            await File.WriteAllBytesAsync(Path.Combine(output,"settings.png"),settingsPng);
            // Repeated native overlay creation must not retain an expired WndProc delegate.
            for(int i=0;i<2;i++)
            {
                var probe=new SelectionOverlay(desktop,_=>{});probe.Cancel();
            }
            byte[] fixture=await RuntimeDiagnostics.FixtureAsync();await File.WriteAllBytesAsync(Path.Combine(output,"fixture.png"),fixture);
            Toolbar??=new ToolbarWindow(this);Toolbar.Present(fixture,new PixelRect(rect.Left,rect.Top,320,200));await Task.Delay(1000);Native.DwmFlush();
            Native.GetWindowRect(Toolbar.Handle,out var toolbarRect);var updatedDesktop=new DesktopCapture();
            await updatedDesktop.CropAsync(new PixelRect(toolbarRect.Left,toolbarRect.Top,toolbarRect.Right-toolbarRect.Left,toolbarRect.Bottom-toolbarRect.Top));
            await File.WriteAllBytesAsync(Path.Combine(output,"toolbar.png"),await RuntimeDiagnostics.RenderAsync((UIElement)Toolbar.Content));
            await File.WriteAllTextAsync(Path.Combine(output,"runtime.txt"),$"Native desktop capture: PASS\nPNG crop: PASS ({nativeCrop.Length} bytes)\nRepeated overlay lifecycle: PASS\nToolbar creation and native control render: PASS\n");
            try{var workspaces=await Codex.WorkspacesAsync();await File.AppendAllTextAsync(Path.Combine(output,"runtime.txt"),$"Codex unique workspaces: {workspaces.Count}\n");}
            catch(Exception e){await File.AppendAllTextAsync(Path.Combine(output,"runtime.txt"),"Codex workspace probe: "+e.GetType().Name+": "+e.Message+"\n");}
            string[] arguments=Environment.GetCommandLineArgs();int targetIndex=Array.IndexOf(arguments,"--test-codex-workspace");
            if(targetIndex>=0&&targetIndex+1<arguments.Length)
            {
                Toolbar.AppWindow.Hide();settingsWindow!.AppWindow.Hide();
                using var desktopDiagnostic=new CodexTarget(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                var result=await desktopDiagnostic.SendAsync(new SendRequest(fixture,"这是一条 AI ScreenShot 截图工具的端到端测试，请只回复“截图与提示词已收到”，不要修改文件或执行命令。",new Workspace(arguments[targetIndex+1])),CancellationToken.None);
                await File.WriteAllTextAsync(Path.Combine(output,"codex-result.txt"),result.State+": "+result.Message);
                Toolbar.AppWindow.Show(false);
            }
            if(arguments.Contains("--test-chatgpt"))
            {
                await File.WriteAllTextAsync(Path.Combine(output,"chatgpt-result.txt"),"等待 Chrome 扩展连接");
                var end=DateTime.UtcNow.AddMinutes(3);
                while(!Bridge.Status.Contains("已连接")&&DateTime.UtcNow<end)await Task.Delay(500);
                var result=await Bridge.SendAsync(new SendRequest(fixture,"这是一条 AI ScreenShot 截图工具的端到端测试，请只回复“截图与提示词已收到”。",null),CancellationToken.None);
                await File.WriteAllTextAsync(Path.Combine(output,"chatgpt-result.txt"),result.State+": "+result.Message);
            }
        }
        catch(Exception e){await File.WriteAllTextAsync(Path.Combine(output,"runtime-error.txt"),e.GetType().Name+": "+e.Message);}
    }
}
