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
    internal Settings Settings {get;}=Settings.Load();
    internal BrowserBridge Bridge {get;private set;}=null!;
    internal CodexTarget Codex {get;private set;}=null!;
    public App(){Native.SetCurrentProcessExplicitAppUserModelID("AIScreenShot.Desktop");InitializeComponent();UnhandledException+=(_,e)=>{tray?.Notify("应用错误",e.Message);e.Handled=true;};}
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        bool overlayTest=Environment.GetCommandLineArgs().Contains("--overlay-test");
        bool toolbarTest=Environment.GetCommandLineArgs().Contains("--toolbar-test");
        bool clipboardTest=Environment.GetCommandLineArgs().Contains("--clipboard-test");
        instance=new Mutex(true,overlayTest?"Local\\AIScreenShot.OverlayDiagnostics":toolbarTest?"Local\\AIScreenShot.ToolbarDiagnostics":clipboardTest?"Local\\AIScreenShot.ClipboardDiagnostics":"Local\\AIScreenShot.App",out bool created);if(!created){Exit();return;}
        if(overlayTest)
        {
            string output=Path.Combine(AppContext.BaseDirectory,"diagnostics");Directory.CreateDirectory(output);
            try{File.WriteAllText(Path.Combine(output,"overlay-test.txt"),RuntimeDiagnostics.VerifyOverlay());}
            catch(Exception e){File.WriteAllText(Path.Combine(output,"overlay-test.txt"),"FAIL: "+e.GetType().Name+": "+e.Message);Environment.ExitCode=1;}
            instance.ReleaseMutex();instance.Dispose();instance=null;Exit();return;
        }
        if(clipboardTest){RunClipboardTest();return;}
        string[] launchArguments=Environment.GetCommandLineArgs();int browserIndex=Array.IndexOf(launchArguments,"--browser");
        if(browserIndex>=0&&browserIndex+1<launchArguments.Length&&launchArguments[browserIndex+1] is "Chrome" or "Edge"){Settings.Browser=launchArguments[browserIndex+1];Settings.Save();}
        var dispatcher=Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if(toolbarTest){Codex=new(dispatcher);RunToolbarTest();return;}
        Bridge=new(Settings);Codex=new(dispatcher);
        tray=new(Settings,()=>dispatcher.TryEnqueue(BeginCapture),()=>dispatcher.TryEnqueue(ShowSettings),()=>dispatcher.TryEnqueue(Shutdown));
        if(Environment.GetCommandLineArgs().Contains("--capture"))BeginCapture();
        if(Environment.GetCommandLineArgs().Contains("--settings"))ShowSettings();
        if(Environment.GetCommandLineArgs().Contains("--smoke-test"))RunSmokeTest();
        tray.Notify("AI ScreenShot 已启动","按 Ctrl+Alt+S 框选截图；右键托盘打开设置。");
    }
    private async void RunClipboardTest()
    {
        string output=Path.Combine(AppContext.BaseDirectory,"diagnostics");Directory.CreateDirectory(output);
        try{await File.WriteAllTextAsync(Path.Combine(output,"clipboard-test.txt"),await RuntimeDiagnostics.VerifyClipboardBackupAsync());}
        catch(Exception e){await File.WriteAllTextAsync(Path.Combine(output,"clipboard-test.txt"),"FAIL: "+e.GetType().Name+": "+e.Message);Environment.ExitCode=1;}
        finally{Shutdown();}
    }
    internal void BeginCapture()
    {
        if(Toolbar?.IsSending==true)return;
        EndCaptureSession();
        try
        {
            Toolbar?.AppWindow.Hide();Native.DwmFlush();
            var screenshot=new DesktopCapture();capturing=true;
            overlay=new SelectionOverlay(screenshot,async rect=>
            {
                if(rect==null){EndCaptureSession();return;}
                int version=++selectionVersion;
                try
                {
                    var image=await screenshot.CropAsync(rect.Value);
                    if(!capturing||version!=selectionVersion)return;
                    Toolbar??=new ToolbarWindow(this);Toolbar.Present(image,rect.Value);
                }
                catch(Exception e){if(version==selectionVersion){EndCaptureSession();tray?.Notify("截图失败",e.Message);}}
            },()=>{selectionVersion++;Toolbar?.AppWindow.Hide();},EndCaptureSession);
        }
        catch(Exception e){capturing=false;tray?.Notify("截图失败",e.Message);}
    }
    internal void EndCaptureSession()
    {
        selectionVersion++;capturing=false;
        var current=overlay;overlay=null;current?.Close();Toolbar?.AppWindow.Hide();
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
        if(capturing)EndCaptureSession();
        if(settingsWindow!=null){settingsWindow.Activate();return;}
        settingsWindow=new Window{Title="AI ScreenShot 设置"};var window=settingsWindow;
        var panel=new StackPanel{Padding=new Thickness(24),Spacing=12};
        panel.Children.Add(new TextBlock{Text="AI ScreenShot",FontSize=24});
        var browser=new ComboBox{Header="ChatGPT 浏览器",ItemsSource=new[]{"Edge","Chrome"},SelectedItem=Settings.Browser,HorizontalAlignment=HorizontalAlignment.Stretch};panel.Children.Add(browser);
        var key=new TextBox{Header="截图快捷键字母或数字",Text=((char)Settings.HotkeyKey).ToString(),MaxLength=1};panel.Children.Add(key);
        var mods=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12};var ctrl=new CheckBox{Content="Ctrl",IsChecked=(Settings.HotkeyModifiers&2)!=0};var alt=new CheckBox{Content="Alt",IsChecked=(Settings.HotkeyModifiers&1)!=0};var shift=new CheckBox{Content="Shift",IsChecked=(Settings.HotkeyModifiers&4)!=0};var win=new CheckBox{Content="Win",IsChecked=(Settings.HotkeyModifiers&8)!=0};foreach(var check in new[]{ctrl,alt,shift,win})mods.Children.Add(check);panel.Children.Add(mods);
        var feedback=new TextBlock{Text=Bridge.Status,TextWrapping=TextWrapping.Wrap};panel.Children.Add(feedback);
        var apply=new Button{Content="保存设置"};apply.Click+=(_,_)=>
        {
            string letter=key.Text.ToUpperInvariant();uint modifiers=(ctrl.IsChecked==true?2u:0)|(alt.IsChecked==true?1u:0)|(shift.IsChecked==true?4u:0)|(win.IsChecked==true?8u:0);
            if(letter.Length!=1||!char.IsAsciiLetterOrDigit(letter[0])||modifiers==0){feedback.Text="请输入一个英文字母或数字，并选择至少一个修饰键";return;}
            if(tray?.ChangeHotkey(modifiers,letter[0])!=true){tray?.ChangeHotkey(Settings.HotkeyModifiers,Settings.HotkeyKey);feedback.Text="快捷键已被占用，原快捷键已恢复";return;}
            Settings.HotkeyModifiers=modifiers;Settings.HotkeyKey=letter[0];Settings.Browser=browser.SelectedItem?.ToString()??"Edge";Settings.Save();feedback.Text="设置已保存 · "+Bridge.Status;
        };panel.Children.Add(apply);
        var checkBridge=new Button{Content="检测扩展连接"};checkBridge.Click+=(_,_)=>feedback.Text=Bridge.Status;panel.Children.Add(checkBridge);
        var instructions=new TextBlock{Text="扩展安装：打开 edge://extensions 或 chrome://extensions，启用开发者模式并加载发布目录的 extension 文件夹。复制扩展 ID，按 README 运行注册脚本，扩展会自动连接；程序重启后自动重连。",TextWrapping=TextWrapping.Wrap};panel.Children.Add(instructions);
        var folder=new Button{Content="打开程序目录"};folder.Click+=(_,_)=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",AppContext.BaseDirectory){UseShellExecute=true});panel.Children.Add(folder);
        window.Content=new ScrollViewer{Content=panel};window.AppWindow.Resize(new Windows.Graphics.SizeInt32(560,620));window.Closed+=(_,_)=>settingsWindow=null;window.Activate();
    }
    private void Shutdown(){EndCaptureSession();ClipboardService.ReleaseRetained();Bridge?.Dispose();tray?.Dispose();instance?.ReleaseMutex();instance?.Dispose();Exit();}
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
                var result=await Codex.SendAsync(new SendRequest(fixture,"这是一条 AI ScreenShot 截图工具的端到端测试，请只回复“截图与提示词已收到”，不要修改文件或执行命令。",new Workspace(arguments[targetIndex+1])),CancellationToken.None);
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
