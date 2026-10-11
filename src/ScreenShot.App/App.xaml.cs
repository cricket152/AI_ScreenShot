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
    internal AppThemeController Themes {get;}
    internal AgentClient Agent {get;}
    internal CodexTarget Codex {get;private set;}=null!;
    public App()
    {
        Native.SetCurrentProcessExplicitAppUserModelID("AIScreenShot.Desktop");InitializeComponent();
        Themes=new(Settings.PanelAppearance);Agent=new(()=>Settings.Agent);
        UnhandledException+=(_,e)=>
        {
            StartupDiagnostics.Record(StartupDiagnostics.Stage,e.Exception);
            string[] arguments=Environment.GetCommandLineArgs();
            if(arguments.Contains("--floating-ui-test"))
            {
                Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory,"diagnostics"));
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"diagnostics","floating-unhandled.txt"),e.Exception.ToString());Environment.ExitCode=1;
            }
            tray?.Notify("应用错误",e.Message);e.Handled=true;
            // A startup failure must not leave an invisible process holding the single-instance mutex.
            if(tray==null&&!arguments.Any(a=>a.EndsWith("-test",StringComparison.Ordinal))){Environment.ExitCode=1;Exit();}
        };
    }
    internal void ApplyAppearance(PanelAppearance appearance){Themes.Apply(appearance);Panel?.ApplyAppearance(appearance);}
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        bool overlayTest=Environment.GetCommandLineArgs().Contains("--overlay-test");
        bool toolbarTest=Environment.GetCommandLineArgs().Contains("--toolbar-test");
        bool clipboardTest=Environment.GetCommandLineArgs().Contains("--clipboard-test");
        bool floatingTest=Environment.GetCommandLineArgs().Contains("--floating-ui-test");
        bool startupTest=Environment.GetCommandLineArgs().Contains("--startup-test");
        bool providerTest=Environment.GetCommandLineArgs().Contains("--codex-provider-test");
        bool codexReadyTest=Environment.GetCommandLineArgs().Contains("--codex-ready-test");
        bool codexWindowTest=codexReadyTest||Environment.GetCommandLineArgs().Contains("--codex-window-test");
        instance=new Mutex(true,overlayTest?"Local\\AIScreenShot.OverlayDiagnostics":toolbarTest?"Local\\AIScreenShot.ToolbarDiagnostics":clipboardTest?"Local\\AIScreenShot.ClipboardDiagnostics":floatingTest?"Local\\AIScreenShot.FloatingDiagnostics":startupTest?"Local\\AIScreenShot.StartupDiagnostics":providerTest?"Local\\AIScreenShot.ProviderDiagnostics":codexWindowTest?"Local\\AIScreenShot.CodexWindowDiagnostics":"Local\\AIScreenShot.App",out bool created);if(!created){Exit();return;}
        StartupDiagnostics.Record("system-dispatcher");
        // Windows.UI.Composition requires the Windows.System queue before the first backdrop.
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().EnsureSystemDispatcherQueue();
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
        string[] launchArguments=Environment.GetCommandLineArgs();
        var dispatcher=Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if(startupTest){Codex=new(dispatcher);RunStartupTest();return;}
        if(floatingTest){Codex=new(dispatcher);RunFloatingTest();return;}
        if(toolbarTest){Codex=new(dispatcher);RunToolbarTest();return;}
        StartupDiagnostics.Record("provider");Codex=new(dispatcher,Settings);
        StartupDiagnostics.Record("restore-panel");
        if(!launchArguments.Contains("--smoke-test")&&File.Exists(Path.Combine(Settings.DirectoryPath,"conversations.db")))Panel=new FloatingPanelWindow(this);
        StartupDiagnostics.Record("tray");
        tray=new(Settings,()=>dispatcher.TryEnqueue(BeginCapture),()=>dispatcher.TryEnqueue(ShowSettings),()=>dispatcher.TryEnqueue(Shutdown),()=>dispatcher.TryEnqueue(ShowFloatingBall));
        StartupDiagnostics.Record("floating-ball");if(!launchArguments.Contains("--smoke-test"))ShowFloatingBall();
        if(Environment.GetCommandLineArgs().Contains("--capture"))BeginCapture();
        if(Environment.GetCommandLineArgs().Contains("--settings"))ShowSettings();
        if(Environment.GetCommandLineArgs().Contains("--smoke-test"))RunSmokeTest();
        tray.Notify("AI ScreenShot 已启动","点击右侧悬浮球展开；Ctrl+Alt+S 截图。右键托盘可找回悬浮球。");
        StartupDiagnostics.Record("ready");
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
        var previousAppearance=Settings.PanelAppearance;
        Settings.PanelAppearance=new(){EnableAnimations=false};
        Themes.Apply(Settings.PanelAppearance);
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
                if(Math.Abs(Panel.MessageSurface.ActualWidth-Panel.ToolbarSurface.ActualWidth)>1||Panel.MessageSurface.ActualHeight<=Panel.ToolbarSurface.ActualHeight)throw new InvalidOperationException($"Toolbar/message dimensions differ: {Panel.ToolbarSurface.ActualWidth}x{Panel.ToolbarSurface.ActualHeight}, {Panel.MessageSurface.ActualWidth}x{Panel.MessageSurface.ActualHeight}; root {root.ActualWidth}x{root.ActualHeight}");
                var toolbarOrigin=Panel.ToolbarSurface.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                var messageOrigin=Panel.MessageSurface.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                if(Math.Abs(toolbarOrigin.X-messageOrigin.X)>1)throw new InvalidOperationException("Toolbar and messages are not left aligned");
                await File.WriteAllBytesAsync(Path.Combine(output,$"floating-{theme}.png"),await RuntimeDiagnostics.RenderAsync(root));
            }
            string fluentChecks=await RuntimeDiagnostics.VerifyFluentPanelAsync(Panel,Ball,output);
            string visibilityAndResizeChecks=await VerifyPanelVisibilityAndTopResizeAsync(Panel,root,output);
            string moveChecks=await VerifyPanelMoveAsync(Panel,root,output);
            ToggleFloatingPanel();if(Panel.IsExpanded||Native.IsWindowVisible(Panel.Handle))throw new InvalidOperationException("A second ball activation did not hide both panels");
            ToggleFloatingPanel();if(!Panel.IsExpanded||!Native.IsWindowVisible(Panel.Handle))throw new InvalidOperationException("Ball activation did not reopen both panels");
            Panel.ToggleToolbar();await Task.Delay(150);
            if(Panel.IsToolbarVisible||Panel.ToolbarSurface.Visibility!=Visibility.Collapsed||Panel.MessageSurface.Visibility!=Visibility.Visible)throw new InvalidOperationException("Toolbar collapse hid the message panel or did not hide the toolbar");
            await File.WriteAllBytesAsync(Path.Combine(output,"floating-toolbar-collapsed.png"),await RuntimeDiagnostics.RenderAsync(root));
            Panel.ToggleToolbar();await Task.Delay(150);
            foreach(var pane in new[]{PanelResizePane.Toolbar,PanelResizePane.Message})
            {
                Panel.ResizeForDiagnostic(pane,PanelResizeEdges.Left|PanelResizeEdges.Bottom,-32,24);await Task.Delay(150);
                if(Math.Abs(Panel.MessageSurface.ActualWidth-Panel.ToolbarSurface.ActualWidth)>1)throw new InvalidOperationException("Resizing a surface broke shared width alignment");
                Native.GetWindowRect(Panel.Handle,out var resizedBounds);
                var resizeWork=new Native.MonitorInfo{Size=(uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MonitorInfo>()};
                Native.GetMonitorInfo(Native.MonitorFromPoint(new Native.Point{X=(resizedBounds.Left+resizedBounds.Right)/2,Y=(resizedBounds.Top+resizedBounds.Bottom)/2},2),ref resizeWork);
                if(resizedBounds.Left<resizeWork.Work.Left||resizedBounds.Top<resizeWork.Work.Top||resizedBounds.Right>resizeWork.Work.Right||resizedBounds.Bottom>resizeWork.Work.Bottom)throw new InvalidOperationException("Surface resizing escaped the monitor work area");
            }
            await File.WriteAllBytesAsync(Path.Combine(output,"floating-resized.png"),await RuntimeDiagnostics.RenderAsync(root));
            var markdown=new MarkdownMessageView{Text="# 标题\n\n**粗体**、*斜体*与 `code`。\n\n> 引用\n\n- 第一项\n- 第二项\n\n```csharp\nConsole.WriteLine(\"fixture\");\n```\n\n| 项目 | 状态 |\n| --- | --- |\n| Markdown | 可用 |"};
            if(markdown.RenderedBlockCount<6||markdown.RenderedControlCount!=1||markdown.SelectionSurface.Blocks.Count<8)throw new InvalidOperationException("Native Markdown did not retain its blocks in one selectable document");
            await RuntimeDiagnostics.VerifyMessageSelectionAsync(Panel,output);
            await RuntimeDiagnostics.VerifyMathRenderingAsync(output);
            var beforeStreaming=markdown.Document;string table="| 序号 | 状态 |\n| --- | --- |";int renderedCallbacks=0;
            for(int row=0;row<120;row++)
            {
                table+=$"\n| {row} | **生成内容** |";
                markdown.SetStreamingText(table,()=>renderedCallbacks++);
            }
            if(markdown.Text!=table||!ReferenceEquals(markdown.Document,beforeStreaming)||renderedCallbacks!=0)throw new InvalidOperationException("Markdown streaming did not coalesce pending snapshots while preserving raw text");
            await Task.Delay(180);
            if(markdown.Document.Blocks.Single() is not MarkdownTable renderedTable||renderedTable.Rows.Count!=121||renderedCallbacks!=1)throw new InvalidOperationException("Markdown streaming did not render the latest complete snapshot once");
            markdown.SetStreamingText("## 最终内容",()=>renderedCallbacks++);markdown.Text="## 最终内容";
            await Task.Delay(100);
            if(markdown.Document.Blocks.Single() is not MarkdownHeading||renderedCallbacks!=1)throw new InvalidOperationException("An immediate Markdown update did not flush pending rendering and cancel its callback");
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
            string composerChecks=await VerifyComposerAndAgentAsync(fixture);
            composerChecks+=await VerifyTextOnlyComposerAsync();
            composerChecks+=await VerifyConcurrentConversationsAsync();
            composerChecks+=await VerifyCaptureRoutingAsync(output);
            composerChecks+=await VerifyReadingModeAsync(output);
            await File.WriteAllTextAsync(Path.Combine(output,"floating-ui-test.txt"),"PASS: 52 logical pixel native ball, panel creation, empty state and Light/Dark generated previews\nPASS: ball toggles both panels; toolbar arrow collapses independently and restores\nPASS: shared toolbar/message width alignment through production resize callbacks; monitor bounds retained\nPASS: native Markdown controls render headings, emphasis, quote, list, code and table\nPASS: 120 streaming Markdown snapshots retain raw text immediately, render the latest table once, and flush final content\nPASS: processing state, operation hide/restore, successful capture dismissal retains messages\nPASS: on-top details, concurrent preview replacement, collapsed/operation preview stays closed after decoding\nPASS: hidden ball remains hidden, collapse/reopen, cancelled capture does not reopen\nPASS: closing message panel dismisses capture overlay\n"+fluentChecks+visibilityAndResizeChecks+moveChecks+codexChecks+composerChecks+"No desktop screenshot, clipboard access, browser connection, or real send was performed.\n");
        }
        catch(Exception e){await File.WriteAllTextAsync(Path.Combine(output,"floating-ui-test.txt"),"FAIL: "+e.GetType().Name+": "+e.Message+"\n"+e.StackTrace);Environment.ExitCode=1;}
        finally{Settings.PanelAppearance=previousAppearance;Shutdown();}
    }
    private static async Task<string> VerifyPanelMoveAsync(FloatingPanelWindow panel,FrameworkElement root,string output)
    {
        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            var pending=new Stack<DependencyObject>();pending.Push(root);
            while(pending.TryPop(out var current))
            {
                yield return current;
                for(int index=0;index<Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(current);index++)pending.Push(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(current,index));
            }
        }
        Windows.Foundation.Point ToolbarPoint()
        {
            var origin=panel.ToolbarSurface.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
            return new(origin.X+PanelResizePolicy.GripThickness+4,origin.Y+panel.ToolbarSurface.ActualHeight/2);
        }
        Windows.Foundation.Point TitlePoint()=>panel.MessageDragTitle.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(8,panel.MessageDragTitle.ActualHeight/2));
        var textBoxes=Descendants(root).OfType<TextBox>().ToArray();var selectedText=Descendants(root).OfType<Microsoft.UI.Xaml.Controls.RichTextBlock>().First();
        if(panel.CanMoveSourceForDiagnostic(panel.VisibilityMenuButton)||panel.CanMoveSourceForDiagnostic(panel.ComposerSendButton)||textBoxes.Length==0||textBoxes.Any(box=>panel.CanMoveSourceForDiagnostic(box))||panel.CanMoveSourceForDiagnostic(panel.ComposerModeSelector)||panel.CanMoveSourceForDiagnostic(selectedText)||!panel.CanMoveSourceForDiagnostic(panel.MessageDragTitle))
            throw new InvalidOperationException("Move source filtering did not protect controls and selectable Markdown text");
        Native.GetWindowRect(panel.Handle,out var original);
        if(!panel.MoveAtForDiagnostic(ToolbarPoint(),1,1))throw new InvalidOperationException("Toolbar background did not receive a native move hit");
        Native.GetWindowRect(panel.Handle,out var clicked);
        if(clicked.Left!=original.Left||clicked.Top!=original.Top)throw new InvalidOperationException("A small movement below the drag threshold repositioned the panel");
        var toolbarOrigin=panel.ToolbarSurface.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
        if(panel.MoveAtForDiagnostic(new Windows.Foundation.Point(toolbarOrigin.X+panel.ToolbarSurface.ActualWidth/2,toolbarOrigin.Y+3),24,24))throw new InvalidOperationException("A resize grip also began moving the panel");
        foreach(bool toolbar in new[]{true,false})
        {
            Native.GetWindowRect(panel.Handle,out var before);
            var info=new Native.MonitorInfo{Size=(uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MonitorInfo>()};
            Native.GetMonitorInfo(Native.MonitorFromPoint(new Native.Point{X=(before.Left+before.Right)/2,Y=(before.Top+before.Bottom)/2},2),ref info);
            int dx=before.Left-info.Work.Left>=24?-24:24,dy=before.Top-info.Work.Top>=24?-24:24;
            var expected=PanelMovePolicy.Move(before.Left,before.Top,before.Right-before.Left,before.Bottom-before.Top,dx,dy,info.Work.Left,info.Work.Top,info.Work.Right,info.Work.Bottom);
            nint foreground=Native.GetForegroundWindow();
            if(!panel.MoveAtForDiagnostic(toolbar?ToolbarPoint():TitlePoint(),dx,dy))throw new InvalidOperationException("Toolbar/message title did not receive its move hit");
            await Task.Delay(100);Native.GetWindowRect(panel.Handle,out var after);
            if(after.Left!=expected.X||after.Top!=expected.Y||after.Right-after.Left!=before.Right-before.Left||after.Bottom-after.Top!=before.Bottom-before.Top||Native.GetForegroundWindow()!=foreground||Math.Abs(panel.ToolbarSurface.ActualWidth-panel.MessageSurface.ActualWidth)>1)
                throw new InvalidOperationException("Panel movement changed dimensions, alignment, focus or its expected screen position");
        }
        Native.GetWindowRect(panel.Handle,out var movedBounds);
        panel.Collapse();panel.Open();await Task.Delay(100);Native.GetWindowRect(panel.Handle,out var reopened);
        if(reopened.Left!=movedBounds.Left||reopened.Top!=movedBounds.Top)throw new InvalidOperationException("Reopening reset a manually moved panel beside the floating ball");
        panel.HideForOperation();panel.RestoreAfterOperation();await Task.Delay(100);Native.GetWindowRect(panel.Handle,out reopened);
        if(reopened.Left!=movedBounds.Left||reopened.Top!=movedBounds.Top)throw new InvalidOperationException("Operation restore reset a manually moved panel");
        panel.ToggleMessage();await Task.Delay(100);
        if(!panel.MoveAtForDiagnostic(ToolbarPoint(),-12,-12))throw new InvalidOperationException("Toolbar-only panel could not move");
        panel.ToggleMessage();panel.ToggleToolbar();await Task.Delay(100);
        if(!panel.MoveAtForDiagnostic(TitlePoint(),-12,-12))throw new InvalidOperationException("Message-only panel could not move");
        panel.ToggleToolbar();await Task.Delay(100);
        await File.WriteAllBytesAsync(Path.Combine(output,"floating-moved.png"),await RuntimeDiagnostics.RenderAsync(root));
        return "PASS: toolbar background and message title hit native move areas; controls, selectable text and resize grips cannot begin a move\nPASS: movement uses a drag threshold, preserves size/alignment/focus, and retains the manual position through collapse and operation restore\nPASS: toolbar-only and message-only panels remain movable\n";
    }
    private static async Task<string> VerifyPanelVisibilityAndTopResizeAsync(FloatingPanelWindow panel,FrameworkElement root,string output)
    {
        static void SingleMenuButton(FrameworkElement root)
        {
            int count=0;var elements=new Stack<DependencyObject>();elements.Push(root);
            while(elements.TryPop(out var element))
            {
                if(element is Button button&&Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button)=="调整工具栏和消息栏")count++;
                for(int index=0;index<Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element);index++)elements.Push(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element,index));
            }
            if(count!=1)throw new InvalidOperationException("The panel did not retain exactly one visibility menu button");
        }
        static async Task InvokeAsync(Button button)
        {
            var peer=new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button);
            var invoke=(Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider?)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke);
            if(invoke==null)throw new InvalidOperationException("Pane toggle has no native Invoke pattern");
            invoke.Invoke();await Task.Delay(150);
        }
        async Task SelectMenuItemAsync(bool toolbar,string expectedToolbar,string expectedMessage,bool saveMenuPreview=false)
        {
            bool beforeToolbar=panel.IsToolbarVisible,beforeMessage=panel.IsMessageVisible;
            await InvokeAsync(panel.VisibilityMenuButton);
            if(!panel.VisibilityMenuOpen||panel.ToolbarVisibilityItem.Text!=expectedToolbar||panel.MessageVisibilityItem.Text!=expectedMessage||panel.IsToolbarVisible!=beforeToolbar||panel.IsMessageVisible!=beforeMessage)
                throw new InvalidOperationException("Opening the visibility menu changed panes or did not present the expected two actions");
            SingleMenuButton(root);
            if(saveMenuPreview)
            {
                var popup=Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).First(item=>item.Child is MenuFlyoutPresenter);
                await File.WriteAllBytesAsync(Path.Combine(output,"floating-visibility-menu.png"),await RuntimeDiagnostics.RenderAsync(popup.Child));
            }
            var itemPeer=new Microsoft.UI.Xaml.Automation.Peers.MenuFlyoutItemAutomationPeer(toolbar?panel.ToolbarVisibilityItem:panel.MessageVisibilityItem);
            var itemInvoke=(Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider?)itemPeer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke);
            if(itemInvoke==null)throw new InvalidOperationException("Visibility menu action has no native Invoke pattern");
            itemInvoke.Invoke();await Task.Delay(150);
            if(panel.VisibilityMenuOpen)throw new InvalidOperationException("Selecting a visibility action did not dismiss the menu");
            SingleMenuButton(root);
        }
        foreach(var pane in new[]{PanelResizePane.Toolbar})
        {
            Native.GetWindowRect(panel.Handle,out var before);double toolbarHeight=panel.ToolbarSurface.ActualHeight;
            double x=root.ActualWidth/2,y=panel.ToolbarSurface.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point()).Y+3;
            if(!panel.ResizeAtForDiagnostic(new Windows.Foundation.Point(x,y),0,32,pane,PanelResizeEdges.Top))throw new InvalidOperationException("The displayed top resize band did not receive its visual hit");
            await Task.Delay(100);Native.GetWindowRect(panel.Handle,out var smaller);
            if(smaller.Top<=before.Top||smaller.Bottom!=before.Bottom||smaller.Bottom-smaller.Top>=before.Bottom-before.Top||Math.Abs(panel.ToolbarSurface.ActualHeight-toolbarHeight)>1)
                throw new InvalidOperationException("Top dragging did not shrink the whole panel while keeping its bottom and toolbar height");
            y=panel.ToolbarSurface.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point()).Y+3;
            if(!panel.ResizeAtForDiagnostic(new Windows.Foundation.Point(x,y),0,-32,pane,PanelResizeEdges.Top))throw new InvalidOperationException("The resized top band no longer received hits");
            await Task.Delay(100);Native.GetWindowRect(panel.Handle,out var restored);
            if(Math.Abs(restored.Top-before.Top)>1||Math.Abs(restored.Bottom-before.Bottom)>1||Math.Abs(panel.ToolbarSurface.ActualHeight-toolbarHeight)>1)
                throw new InvalidOperationException("Top dragging did not grow the panel back to its starting size");
        }
        panel.PromptText="生成的菜单折叠诊断提示词";
        string reply=panel.ResponseText;string prompt=panel.PromptText;int count=panel.MessageCount;
        double previousMessageHeight=panel.MessageSurface.ActualHeight;
        await SelectMenuItemAsync(false,"隐藏工具栏","隐藏消息栏",true);
        Native.GetWindowRect(panel.Handle,out var collapsed);
        if(panel.IsMessageVisible||panel.MessageSurface.Visibility!=Visibility.Collapsed||!panel.IsToolbarVisible||!panel.IsExpanded||!Native.IsWindowVisible(panel.Handle)||!panel.VisibilityMenuInToolbar||collapsed.Bottom-collapsed.Top>=previousMessageHeight)
            throw new InvalidOperationException("The message collapse button did not leave a visible toolbar and restore control");
        await File.WriteAllBytesAsync(Path.Combine(output,"floating-message-collapsed.png"),await RuntimeDiagnostics.RenderAsync(root));
        panel.ResizeForDiagnostic(PanelResizePane.Toolbar,PanelResizeEdges.Right,24,0);await Task.Delay(100);
        await SelectMenuItemAsync(false,"隐藏工具栏","显示消息栏");
        if(!panel.IsMessageVisible||!panel.IsToolbarVisible||panel.MessageCount!=count||panel.ResponseText!=reply||panel.PromptText!=prompt||Math.Abs(panel.MessageSurface.ActualHeight-previousMessageHeight)>1||Math.Abs(panel.MessageSurface.ActualWidth-panel.ToolbarSurface.ActualWidth)>1)
            throw new InvalidOperationException("Restoring messages lost content, height or shared width after toolbar-only resizing");
        await SelectMenuItemAsync(true,"隐藏工具栏","隐藏消息栏");
        if(panel.VisibilityMenuInToolbar||panel.IsToolbarVisible||!panel.IsMessageVisible)throw new InvalidOperationException("The single menu button did not move to the visible message header");
        await File.WriteAllBytesAsync(Path.Combine(output,"floating-menu-in-message.png"),await RuntimeDiagnostics.RenderAsync(root));
        await SelectMenuItemAsync(false,"显示工具栏","隐藏消息栏");
        if(!panel.IsToolbarVisible||panel.IsMessageVisible||!Native.IsWindowVisible(panel.Handle))throw new InvalidOperationException("Collapsing messages without a toolbar hid both panes");
        await SelectMenuItemAsync(true,"隐藏工具栏","显示消息栏");
        if(panel.IsToolbarVisible||!panel.IsMessageVisible)throw new InvalidOperationException("Toolbar collapse left the window without a visible pane");
        await SelectMenuItemAsync(true,"显示工具栏","隐藏消息栏");
        return "PASS: toolbar top and outer padding hit real native resize grips; downward/upward drags shrink/grow the full panel with a fixed bottom and toolbar height\nPASS: exactly one visibility arrow opens native visibility/size actions without changing panes; action Invoke hides/restores each pane and dismisses the menu\nPASS: menu reuses the same button in the visible pane; contents, prompt, height and shared width survive hiding and restoring\n";
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
            if(panel.MessageScrollHeight<=16||Math.Abs(panel.MessageScrollHeight-panel.MessageScrollOffset)>1)throw new InvalidOperationException($"Streamed reply did not follow the bottom of the message list (offset={panel.MessageScrollOffset:F1}, height={panel.MessageScrollHeight:F1})");
            panel.ScrollMessages(0);await Task.Delay(250);
            update(new(CodexReplyPhase.Thinking,streamedText+"\n生成图诊断新阶段","正在分析"));await Task.Delay(250);
            if(panel.MessageScrollOffset>1)throw new InvalidOperationException("A streamed update overrode the user's scroll position");
            panel.ScrollMessages(panel.MessageScrollHeight);await Task.Delay(250);
            update(new(CodexReplyPhase.Thinking,streamedText+"\n生成图诊断新阶段\n正在准备结果","正在输出"));await Task.Delay(250);
            if(Math.Abs(panel.MessageScrollHeight-panel.MessageScrollOffset)>1||Native.GetForegroundWindow()!=foreground)throw new InvalidOperationException("Following streamed output did not resume at the bottom without changing foreground focus");
            update(new(CodexReplyPhase.Thinking,streamedText+"\n生成图诊断延迟渲染阶段","正在输出"));panel.ScrollMessages(0);await Task.Delay(250);
            if(panel.MessageScrollOffset>1)throw new InvalidOperationException("A pending streamed render overrode an intervening user scroll");
            panel.ScrollMessages(panel.MessageScrollHeight);await Task.Delay(150);
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
    private async Task<string> VerifyComposerAndAgentAsync(byte[] fixture)
    {
        string previousDefault=Settings.DefaultPrompt;string? previousWorkspace=Settings.Workspace;
        try
        {
            Settings.DefaultPrompt="# 生成图默认提示词\n\n请分析**生成图片**。";Settings.Workspace="UI 诊断工作区";
            ShowSettings();
            var settingPrompt=((SettingsWindow)settingsWindow!).DefaultPromptEditor;
            static string Newlines(string value)=>value.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
            if(!Equals(settingPrompt.Header,"默认提示词")||Newlines(settingPrompt.Text)!=Newlines(Settings.DefaultPrompt)||!settingPrompt.AcceptsReturn||settingPrompt.MaxLength!=16000)throw new InvalidOperationException("Default prompt settings editor was not populated");
            string settingsChecks=await RuntimeDiagnostics.VerifySettingsDesignAsync((SettingsWindow)settingsWindow,Path.Combine(AppContext.BaseDirectory,"diagnostics"));
            settingsWindow.Close();
            var requests=new List<SendRequest>();int agentCalls=0;SendState agentState=SendState.Sent;
            Panel?.Close();
            var panel=new FloatingPanelWindow(this,(request,update,_)=>
            {
                requests.Add(request);update(new(CodexReplyPhase.Submitted));update(new(CodexReplyPhase.Thinking,"## 生成回复\n\n**正文**","正在回复"));
                update(new(CodexReplyPhase.Completed,"## 生成回复\n\n**正文**\n\n```text\nfixture\n```"));
                return Task.FromResult(new SendResult(SendState.Sent,"诊断提交成功"));
            },(request,update,_)=>
            {
                agentCalls++;
                if(Panel!.ComposerModeSelector.IsEnabled)throw new InvalidOperationException("Send mode can change during an Agent operation");
                if(request.EffectivePrompt!=Settings.DefaultPrompt||!request.Png.AsSpan().SequenceEqual(fixture))throw new InvalidOperationException("Agent request lost its effective prompt or fixture");
                update(new(AgentReplyPhase.Submitted));update(new(AgentReplyPhase.Thinking,"## Agent 回复\n\n**正文**",ModelName:"fixture-vision-model"));
                if(agentState==SendState.Sent)update(new(AgentReplyPhase.Completed,"## Agent 回复\n\n**正文**"));
                else update(new(AgentReplyPhase.Unavailable,"## Agent 回复\n\n**正文**","生成的连接中断"));
                return Task.FromResult(new SendResult(agentState,"生成的 Agent 回执"));
            });Panel=panel;
            async Task InvokeComposerAsync()
            {
                await Task.Delay(100);
                var peer=new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(panel.ComposerSendButton);
                var invoke=(Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider?)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke);
                if(invoke==null)throw new InvalidOperationException("Composer button has no native Invoke pattern");
                int before=requests.Count+agentCalls;invoke.Invoke();
                using var invokeTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while(requests.Count+agentCalls==before)await Task.Delay(20,invokeTimeout.Token);
                await (panel.ComposerSendTask??throw new InvalidOperationException("Composer button did not invoke the selected sender")).WaitAsync(TimeSpan.FromSeconds(5));
            }
            await panel.PresentAsync(fixture,new PixelRect(0,0,320,200));
            if(!panel.CanSendFromComposer)throw new InvalidOperationException("Composer send button did not enable after a capture");
            await InvokeComposerAsync();
            if(requests.Count!=1||requests[0].EffectivePrompt!=Settings.DefaultPrompt||panel.ResponseRenderedBlockCount<3||panel.ResponseView?.CopyButton?.IsEnabled!=true||panel.CanSendFromComposer||panel.MessageCount!=2)
                throw new InvalidOperationException("Composer lost default prompt, Markdown output or confirmed submission protection");
            await panel.PresentAsync(fixture,new PixelRect(0,0,320,200));panel.PromptText="**生成的手动提示词**";await InvokeComposerAsync();
            if(requests.Count!=2||requests[1].EffectivePrompt!=panel.PromptText)throw new InvalidOperationException("Manual prompt did not override the configured default");
            if(panel.ComposerModeSelector.SelectedIndex!=0)throw new InvalidOperationException("Composer default is not Codex");
            panel.ComposerModeSelector.SelectedIndex=1;
            if(!panel.CanSendFromComposer)throw new InvalidOperationException("Agent composer inherited Codex submission lock");
            panel.ComposerModeSelector.SelectedIndex=0;
            if(panel.CanSendFromComposer)throw new InvalidOperationException("Codex composer lost its submission lock on mode switch");
            panel.ComposerModeSelector.SelectedIndex=1;Settings.Workspace=null;
            int messageCount=panel.MessageCount;
            foreach(var state in new[]{SendState.Sent,SendState.Unknown})
            {
                agentState=state;await panel.PresentAsync(fixture,new PixelRect(0,0,320,200));messageCount=panel.MessageCount;int expectedCalls=agentCalls+1;
                if(panel.ComposerModeSelector.SelectedIndex!=1)throw new InvalidOperationException("New capture discarded composer mode");
                await InvokeComposerAsync();await panel.SendAgentAsync();
                if(agentCalls!=expectedCalls||panel.MessageCount!=messageCount+2||!panel.ResponseText.Contains("Agent 回复")||panel.ResponseModelName!="fixture-vision-model"||panel.IsBusy||panel.IsProcessing||panel.CanSendFromComposer||!panel.ComposerModeSelector.IsEnabled||requests.Count!=2||!Native.IsWindowVisible(panel.Handle))
                    throw new InvalidOperationException("Agent reply/model metadata failed to render or allowed an acknowledged/unknown resend");
                if(state==SendState.Sent)
                {
                    var root=(FrameworkElement)panel.Content;
                    foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark})
                    {
                        root.RequestedTheme=theme;await Task.Delay(120);
                        await File.WriteAllBytesAsync(Path.Combine(AppContext.BaseDirectory,"diagnostics",$"agent-response-model-{theme}.png"),await RuntimeDiagnostics.RenderAsync(root));
                    }
                }
            }
            return settingsChecks+"PASS: default prompt settings editor, native composer Invoke, manual override and Markdown reply rendering\nPASS: composer mode selects Codex/Agent sender independently; mode survives captures, locks during operations, and respects per-target Sent/Unknown resend protection; Agent needs no Codex workspace and reports the response model at the bubble header\n";
        }
        finally{settingsWindow?.Close();Panel?.Close();Settings.DefaultPrompt=previousDefault;Settings.Workspace=previousWorkspace;}
    }
    private async Task<string> VerifyTextOnlyComposerAsync()
    {
        string? previousWorkspace=Settings.Workspace,previousDirectory=Settings.CodexDirectory;
        try
        {
            Settings.Workspace="text diagnostic";Settings.CodexDirectory=Directory.GetCurrentDirectory();
            var sent=new List<(string Target,SendRequest Request)>();SendState agentState=SendState.Sent;
            Panel?.Close();
            var panel=new FloatingPanelWindow(this,(request,update,_)=>
            {
                sent.Add(("Codex",request));update(new(CodexReplyPhase.Submitted));update(new(CodexReplyPhase.Completed,"纯文本 Codex 回复"));
                return Task.FromResult(new SendResult(SendState.Sent,"纯文本诊断完成"));
            },async(request,update,token)=>
            {
                if(Panel!.CanSendFromComposer||Panel.ComposerModeSelector.IsEnabled)throw new InvalidOperationException("Text-only send controls remain enabled while busy");
                sent.Add(("Agent",request));await Task.Delay(20,token);
                if(agentState!=SendState.Failed)update(new(agentState==SendState.Sent?AgentReplyPhase.Completed:AgentReplyPhase.Unavailable,"纯文本 Agent 回复",ModelName:"text-diagnostic-model"));
                return new SendResult(agentState,"纯文本诊断回执");
            });Panel=panel;panel.Open();await Task.Delay(120);
            async Task SetPromptAsync(string text)
            {
                panel.PromptText=text;
                // WinUI dispatches TextChanged after a programmatic Text assignment.
                await Task.Delay(100);
            }
            async Task InvokeAsync()
            {
                if(!panel.CanSendFromComposer)throw new InvalidOperationException("Text-only composer did not enable for a valid new prompt");
                var peer=new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(panel.ComposerSendButton);
                int before=sent.Count;
                ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                using var invokeTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while(sent.Count==before)await Task.Delay(20,invokeTimeout.Token);
                await (panel.ComposerSendTask??throw new InvalidOperationException("Text-only Invoke did not dispatch")).WaitAsync(TimeSpan.FromSeconds(5));
                if(sent[^1].Request.Png.Length!=0||sent[^1].Request.EffectivePrompt!=panel.PromptText||panel.IsBusy||panel.IsProcessing)throw new InvalidOperationException("Text-only send created an image, changed the prompt or stayed busy");
            }
            foreach(int mode in new[]{0,1})
            {
                panel.ComposerModeSelector.SelectedIndex=mode;await SetPromptAsync(" \r\n\t");
                if(panel.CanSendFromComposer||panel.CanSendToCodex||panel.CanSendToAgent)throw new InvalidOperationException("Empty text-only draft can send the screenshot default");
                await SetPromptAsync("你好");await InvokeAsync();
                if(panel.CanSendFromComposer||panel.MessageCount!=(mode+1)*2)throw new InvalidOperationException("Text-only send lacks bubbles or allows duplicate submission");
            }
            Settings.Workspace=null;Settings.CodexDirectory=null;
            await SetPromptAsync("继续聊聊");await InvokeAsync();
            if(sent[^1].Target!="Agent"||panel.ResponseModelName!="text-diagnostic-model")throw new InvalidOperationException("Text-only Agent needs a Codex directory or lost model metadata");
            agentState=SendState.Unknown;await SetPromptAsync("未知结果的纯文本");await InvokeAsync();
            if(panel.CanSendFromComposer)throw new InvalidOperationException("Unknown text-only receipt became retryable");
            panel.ComposerModeSelector.SelectedIndex=0;panel.ComposerModeSelector.SelectedIndex=1;
            if(panel.CanSendFromComposer)throw new InvalidOperationException("Mode toggle unlocks an Unknown text-only request");
            await SetPromptAsync("你好");
            if(panel.CanSendFromComposer)throw new InvalidOperationException("Restoring an already submitted prompt allows a resend");
            agentState=SendState.Failed;await SetPromptAsync("本地失败可重试");await InvokeAsync();
            if(!panel.CanSendFromComposer)throw new InvalidOperationException("Failed text-only request was locked as submitted");
            return "PASS: no-capture text-only Codex/Agent native Send Invoke; whitespace disabled, unchanged prompts Sent/Unknown protected, different prompts enable another independent request, Failed remains retryable; Agent needs no Codex directory and preserves model metadata\n";
        }
        finally{Panel?.Close();Settings.Workspace=previousWorkspace;Settings.CodexDirectory=previousDirectory;}
    }
    internal void BeginCapture()
    {
        if(Toolbar?.IsSending==true||Panel?.IsBusy==true)return;
        EndCaptureSession(hidePanel:Panel?.IsFullScreen!=true);
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
        Ball??=new FloatingBallWindow(ToggleFloatingPanel,HideFloatingBall,ShowSettings);
        Ball.Present();
    }
    internal void ShowFloatingBallIfWanted(){if(ballWanted&&!capturing&&Panel is not {IsFullScreen:true,IsExpanded:true})Ball?.AppWindow.Show(false);}
    internal void HideFloatingBall(){ballWanted=false;Ball?.AppWindow.Hide();}
    internal void ToggleFloatingPanel()
    {
        if(Panel?.IsExpanded==true){CollapseFloatingPanel();return;}
        Panel??=new FloatingPanelWindow(this);Panel.Open();
    }
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
        settingsWindow=new SettingsWindow(this,(modifiers,key)=>tray?.ChangeHotkey(modifiers,key)==true);
        settingsWindow.Closed+=(_,_)=>settingsWindow=null;settingsWindow.Activate();
    }
    private void Shutdown(){ballWanted=false;EndCaptureSession();Panel?.Close();Ball?.Close();Themes.Dispose();Codex?.Dispose();ClipboardService.ReleaseRetained();Agent.Dispose();tray?.Dispose();instance?.ReleaseMutex();instance?.Dispose();Exit();}
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

        }
        catch(Exception e){await File.WriteAllTextAsync(Path.Combine(output,"runtime-error.txt"),e.GetType().Name+": "+e.Message);}
    }
}
