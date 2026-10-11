using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Windowing;
using ScreenShot.Core;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace ScreenShot.App;

internal sealed class ToolbarWindow : Window
{
    private readonly App app;
    private readonly AnimationScope motion;
    private readonly TextBox prompt=new(){PlaceholderText="输入提示词…",MinWidth=160,Width=260,MaxLength=16000};
    private readonly TextBlock status=new(){Text="截图已就绪",TextWrapping=TextWrapping.Wrap,FontSize=12};
    private readonly SplitButton codex=new(){Content="Codex"};
    private readonly Button agent=new(){Content="Agent"};
    private readonly AppBarButton copy=new(){Label="复制",Icon=new SymbolIcon(Symbol.Copy)},save=new(){Label="保存",Icon=new SymbolIcon(Symbol.Save)},more=new(){Label="更多",Icon=new SymbolIcon(Symbol.More)};
    private readonly AppBarButton retake=new(){Label="重新截图",Icon=new SymbolIcon(Symbol.Camera)},close=new(){Label="退出截图",Icon=new SymbolIcon(Symbol.Cancel)};
    private readonly TextBlock dimensions=new(){VerticalAlignment=VerticalAlignment.Center,FontSize=12};
    private byte[] png=[];
    private bool sending,saving,agentPresented;
    public bool IsSending=>sending||saving;
    private readonly HashSet<string> submittedTargets=[];
    public nint Handle {get;}
    public ToolbarWindow(App app)
    {
        this.app=app;motion=app.Animations.CreateScope();Title="AI ScreenShot";Handle=WinRT.Interop.WindowNative.GetWindowHandle(this);
        Native.SetWindowLongPtr(Handle,-20,(Native.GetWindowLongPtr(Handle,-20)|0x80)&~0x40000);
        var presenter=OverlappedPresenter.Create();presenter.SetBorderAndTitleBar(false,false);presenter.IsResizable=false;presenter.IsMaximizable=false;presenter.IsMinimizable=false;presenter.IsAlwaysOnTop=true;AppWindow.SetPresenter(presenter);
        var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};
        foreach(var control in new FrameworkElement[]{prompt,codex,agent}){row.Children.Add(control);if(control is Control c)c.VerticalAlignment=VerticalAlignment.Center;}
        AutomationProperties.SetName(prompt,"截图提示词");AutomationProperties.SetName(codex,"发送到 Codex；下拉选择工作区");
        var commands=new CommandBar{Content=dimensions,DefaultLabelPosition=CommandBarDefaultLabelPosition.Collapsed,IsOpen=false,IsDynamicOverflowEnabled=true};
        foreach(var button in new[]{copy,save,retake,more,close})
        {
            commands.PrimaryCommands.Add(button);ToolTipService.SetToolTip(button,button.Label);AutomationProperties.SetName(button,button.Label);
        }
        AutomationProperties.SetName(agent,"发送截图到 Agent");
        var panel=new StackPanel{Spacing=8};panel.Children.Add(row);panel.Children.Add(commands);panel.Children.Add(status);
        var border=(Border)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Background=\"{ThemeResource FlyoutBackgroundThemeBrush}\" BorderBrush=\"{ThemeResource FlyoutBorderThemeBrush}\" CornerRadius=\"8\" Padding=\"12\" BorderThickness=\"1\" />");
        border.Child=panel;Content=border;
        motion.WatchTree(border,AnimationArea.Toolbar);
        border.AddHandler(UIElement.KeyDownEvent,new KeyEventHandler((_,e)=>{if(e.Key==VirtualKey.Escape&&!IsSending){app.EndCaptureSession();e.Handled=true;}}),true);
        close.Click+=(_,_)=>app.EndCaptureSession();retake.Click+=(_,_)=>app.BeginCapture();
        codex.Click+=async(_,_)=>await SendAsync("Codex");agent.Click+=async(_,_)=>await SendAsync("Agent");
        copy.Click+=async(_,_)=>{try{await ClipboardService.ImageAsync(png);status.Text="图片已复制";}catch(Exception e){status.Text=e.Message;}};
        save.Click+=async(_,_)=>await SaveAsync();
        var projects=new MenuFlyout();codex.Flyout=projects;
        projects.Opening+=async(_,_)=>
        {
            projects.Items.Clear();projects.Items.Add(new MenuFlyoutItem{Text="正在读取工作区…",IsEnabled=false});
            try
            {
                var list=await app.Codex.WorkspacesAsync();projects.Items.Clear();
                foreach(var workspace in list)
                {
                    var item=new MenuFlyoutItem{Text=(app.Settings.Workspace==workspace.Name?"✓ ":"")+workspace.Name};
                    item.Click+=(_,_)=>{if(app.Codex.IsProvider&&workspace.DirectoryPath!=null)CodexDirectoryPicker.Select(app.Settings,workspace.DirectoryPath);else{app.Settings.Workspace=workspace.Name;app.Settings.Save();}status.Text="Codex："+workspace.Name;};projects.Items.Add(item);
                }
                if(app.Codex.IsProvider)
                {
                    var choose=new MenuFlyoutItem{Text="选择工作目录…"};choose.Click+=async(_,_)=>{try{if(await CodexDirectoryPicker.PickAsync(this,app.Settings))status.Text="Codex 工作目录已选择";}catch(Exception error){status.Text=error.Message;}};projects.Items.Add(choose);
                }
                else if(list.Count==0)projects.Items.Add(new MenuFlyoutItem{Text="没有可识别的唯一工作区",IsEnabled=false});
            }
            catch(Exception e){projects.Items.Clear();projects.Items.Add(new MenuFlyoutItem{Text="无法读取，请打开 Codex 项目列表",IsEnabled=false});status.Text=e.Message;}
        };
        var menu=new MenuFlyout();
        void Add(string text,Action action){var item=new MenuFlyoutItem{Text=text};item.Click+=(_,_)=>action();menu.Items.Add(item);}
        Add("发送到两端",async()=>{await SendAsync("Agent");string agentResult=status.Text;await SendAsync("Codex",false);status.Text="Agent："+agentResult+"；Codex："+status.Text;});
        Add("复制提示词",async()=>{try{await ClipboardService.TextAsync(EffectivePrompt);status.Text="提示词已复制";}catch(Exception e){status.Text=e.Message;}});
        Add("设置",()=>{app.EndCaptureSession();app.ShowSettings();});more.Flyout=menu;
        Closed+=(_,_)=>{motion.Dispose();app.EndCaptureSession();app.Toolbar=null;};
        app.Themes.Track(this);
    }
    private string EffectivePrompt=>new SendRequest([],prompt.Text,null,app.Settings.DefaultPrompt).EffectivePrompt;
    public void Present(byte[] image,PixelRect region)
    {
        png=image;agentPresented=false;submittedTargets.Clear();prompt.Text="";status.Text=app.Settings.Workspace is {} name?"截图已就绪 · Codex："+name:"截图已就绪 · 请先选择 Codex 工作区";
        dimensions.Text=$"矩形截图 · {region.Width} × {region.Height}";
        var point=new Native.Point{X=region.X+region.Width/2,Y=region.Y+region.Height/2};var info=new Native.MonitorInfo{Size=(uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MonitorInfo>()};
        if(!Native.GetMonitorInfo(Native.MonitorFromPoint(point,2),ref info))throw new InvalidOperationException("无法确定截图工具栏的目标显示器");
        // Move first, then query the target monitor DPI before sizing the native window.
        AppWindow.Move(new Windows.Graphics.PointInt32(info.Work.Left,info.Work.Top));double scale=Native.GetDpiForWindow(Handle)/96d;
        int width=Math.Min((int)(620*scale),info.Work.Right-info.Work.Left),height=Math.Min((int)(156*scale),info.Work.Bottom-info.Work.Top);
        prompt.Width=Math.Max(120,width/scale-240);
        int x=Math.Clamp(region.X+(region.Width-width)/2,info.Work.Left,Math.Max(info.Work.Left,info.Work.Right-width));
        int gap=(int)Math.Round(8*scale);
        int y=region.Y+region.Height+gap;if(y+height>info.Work.Bottom)y=region.Y-height-gap;
        y=Math.Clamp(y,info.Work.Top,Math.Max(info.Work.Top,info.Work.Bottom-height));
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x,y,width,height));Activate();motion.Enter((FrameworkElement)Content,AnimationArea.Toolbar);prompt.Focus(FocusState.Programmatic);
    }
    private async Task SaveAsync()
    {
        if(IsSending)return;
        saving=true;SetBusy(true);app.SetCaptureSessionVisible(false);
        byte[] image=png;
        try
        {
            var picker=new FileSavePicker{SuggestedFileName="Screenshot-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")};picker.FileTypeChoices.Add("PNG 图片",new List<string>{".png"});
            WinRT.Interop.InitializeWithWindow.Initialize(picker,Handle);var file=await picker.PickSaveFileAsync();if(file!=null){await FileIO.WriteBytesAsync(file,image);status.Text="图片已保存";}
        }
        catch(Exception e){status.Text="保存失败："+e.Message;}
        finally{saving=false;SetBusy(false);app.SetCaptureSessionVisible(true);}
    }
    private async Task SendAsync(string target,bool dismissOnSuccess=true)
    {
        if(sending)return;
        if(submittedTargets.Contains(target)){status.Text=target+" 已提交或结果未知，本次截图不会再次发送到该目标";return;}
        if(target=="Agent")
        {
            sending=true;SetBusy(true);
            try
            {
                app.EndCaptureSession(false);app.Panel??=new FloatingPanelWindow(app);
                if(!agentPresented){await app.Panel.PresentAsync(png,new PixelRect(0,0,0,0));agentPresented=true;}
                app.Panel.PromptText=prompt.Text;await app.Panel.SendAgentAsync();status.Text=app.Panel.StatusText;
                if(!app.Panel.CanSendToAgent)submittedTargets.Add(target);
            }
            finally{sending=false;SetBusy(false);}
            return;
        }
        if(target=="Codex"&&(app.Codex.IsProvider?app.Settings.CodexDirectory==null:app.Settings.Workspace==null)){status.Text="请从 Codex 下拉菜单选择工作目录";return;}
        sending=true;SetBusy(true);status.Text="正在发送到 "+target;
        var request=new SendRequest(png,prompt.Text,app.Settings.Workspace is {} name?new Workspace(name,app.Settings.CodexDirectory):null,app.Settings.DefaultPrompt);
        app.SetCaptureSessionVisible(false);
        try
        {
            var result=await app.Codex.SendAsync(request,CancellationToken.None);
            ApplySendResult(target,result,dismissOnSuccess);
        }
        catch(Exception e){status.Text="发送失败："+e.Message;}
        finally{sending=false;SetBusy(false);app.SetCaptureSessionVisible(true);}
    }
    internal void ApplySendResult(string target,SendResult result,bool dismissOnSuccess=true)
    {
        if(result.State!=SendState.Failed)submittedTargets.Add(target);
        status.Text=result.Message;
        if(dismissOnSuccess&&target=="Codex"&&result.State==SendState.Sent)app.EndCaptureSession();
    }
    private void SetBusy(bool busy){foreach(var control in new Control[]{prompt,codex,agent,copy,save,more,retake,close})control.IsEnabled=!busy;}
}
