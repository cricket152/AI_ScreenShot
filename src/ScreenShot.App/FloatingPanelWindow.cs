using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ScreenShot.Core;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;

namespace ScreenShot.App;

internal sealed class FloatingPanelWindow : Window
{
    private readonly App app;
    private readonly Button codex=ActionButton("Codex","\uE756","发送截图到 Codex");
    private readonly Button chatgpt=ActionButton("ChatGPT","\uE8F2","发送截图到 ChatGPT");
    private readonly Button copy=ActionButton("复制","\uE8C8","复制截图");
    private readonly Button save=ActionButton("另存为","\uE74E","将截图另存为 PNG");
    private readonly Button more=ActionButton("更多","\uE712","更多操作");
    private readonly Button capture=new(){Content="截取屏幕",HorizontalAlignment=HorizontalAlignment.Left};
    private readonly DropDownButton workspace=new(){Content="选择工作区",MaxWidth=204,FontSize=12};
    private readonly TextBox prompt=new(){PlaceholderText="输入提示词，留空则描述并分析截图",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=64,MaxHeight=100,MaxLength=16000};
    private readonly TextBlock status=new(){Text="就绪 · 先截取一张图片",FontSize=12,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock dimensions=new(){Text="还没有截图",FontSize=12,VerticalAlignment=VerticalAlignment.Center};
    private readonly ProgressRing progress=new(){Width=16,Height=16,IsActive=false,Visibility=Visibility.Collapsed};
    private readonly Button stopWaiting=new(){Content="停止等待",FontSize=12,Visibility=Visibility.Collapsed};
    private readonly StackPanel messages=new(){Spacing=12};
    private readonly ScrollViewer scroll=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    private readonly Button thumbnail=new(){Padding=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Left,Visibility=Visibility.Collapsed};
    private readonly Image draftImage=new(){Width=80,Height=48,Stretch=Stretch.Uniform};
    private readonly StackPanel empty=new(){Spacing=12,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    private readonly HashSet<string> submittedTargets=[];
    private readonly CancellationTokenSource lifetime=new();
    private readonly Func<SendRequest,Action<CodexReplyUpdate>,CancellationToken,Task<SendResult>> sendCodex;
    private CancellationTokenSource? waitingCancellation;
    private TextBlock? latestResponse,latestResponseHeading;
    private byte[] png=[];
    private int imageVersion,operationVersion;
    private bool busy,expanded,closed;
    private Window? preview;
    public bool IsBusy=>busy;
    public nint Handle {get;}
    internal Border ToolbarSurface {get;}
    internal Border MessageSurface {get;}
    internal bool IsExpanded=>expanded;
    internal bool IsProcessing=>progress.IsActive;
    internal bool CanSendToCodex=>codex.IsEnabled;
    internal bool CanStopWaiting=>stopWaiting.Visibility==Visibility.Visible&&stopWaiting.IsEnabled;
    internal string StatusText=>status.Text;
    internal string ResponseText=>latestResponse?.Text??"";
    internal string ResponseHeading=>latestResponseHeading?.Text??"";
    internal int MessageCount=>messages.Children.Count;
    internal double MessageScrollOffset=>scroll.VerticalOffset;
    internal double MessageScrollHeight=>scroll.ScrollableHeight;
    internal void ScrollMessages(double offset)=>scroll.ChangeView(null,offset,null,true);
    internal bool PreviewVisible=>preview!=null&&Native.IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(preview));
    internal bool PreviewOnTop=>preview?.AppWindow.Presenter is OverlappedPresenter {IsAlwaysOnTop:true};

    public FloatingPanelWindow(App app,Func<SendRequest,Action<CodexReplyUpdate>,CancellationToken,Task<SendResult>>? sendCodex=null)
    {
        this.app=app;this.sendCodex=sendCodex??app.Codex.SendWithReplyAsync;Title="AI ScreenShot · 消息";Handle=WinRT.Interop.WindowNative.GetWindowHandle(this);
        Native.SetWindowLongPtr(Handle,-20,(Native.GetWindowLongPtr(Handle,-20)|0x80)&~0x40000);
        var presenter=OverlappedPresenter.Create();presenter.SetBorderAndTitleBar(false,false);presenter.IsResizable=false;presenter.IsMaximizable=false;presenter.IsMinimizable=false;presenter.IsAlwaysOnTop=true;AppWindow.SetPresenter(presenter);

        var tools=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Center};
        foreach(var button in new[]{codex,chatgpt,copy,save,more})tools.Children.Add(button);
        ToolbarSurface=Surface("FlyoutBackgroundThemeBrush",12);ToolbarSurface.Width=360;ToolbarSurface.Height=56;ToolbarSurface.Padding=new Thickness(12,6,12,6);ToolbarSurface.HorizontalAlignment=HorizontalAlignment.Left;ToolbarSurface.Child=tools;
        var messageGrid=new Grid{RowSpacing=12,Padding=new Thickness(16)};
        foreach(var height in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto})messageGrid.RowDefinitions.Add(new RowDefinition{Height=height});
        var header=new Grid{ColumnSpacing=8};header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});for(int i=0;i<3;i++)header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        header.Children.Add(new TextBlock{Text="消息",FontSize=18,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});
        Grid.SetColumn(workspace,1);header.Children.Add(workspace);
        Label(stopWaiting,"停止提交确认或回复等待；不会取消或重发已经提交的任务");stopWaiting.Click+=(_,_)=>StopWaiting();Grid.SetColumn(stopWaiting,2);header.Children.Add(stopWaiting);
        var collapse=new Button{Content=new FontIcon{Glyph="\uE70E",FontSize=14},Width=32,Height=32,Padding=new Thickness(0)};
        Label(collapse,"收起工具栏和消息栏目");collapse.Click+=(_,_)=>app.CollapseFloatingPanel();Grid.SetColumn(collapse,3);header.Children.Add(collapse);
        messageGrid.Children.Add(header);
        var stateRow=new Grid{ColumnSpacing=8};stateRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});stateRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});stateRow.Children.Add(progress);Grid.SetColumn(status,1);stateRow.Children.Add(status);
        AutomationProperties.SetLiveSetting(status,Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        Grid.SetRow(stateRow,1);messageGrid.Children.Add(stateRow);
        var conversation=new Grid();scroll.Content=messages;conversation.Children.Add(scroll);
        empty.Children.Add(new FontIcon{Glyph="\uE722",FontSize=36,Opacity=0.6});
        empty.Children.Add(new TextBlock{Text="从一张截图开始",FontSize=16,HorizontalAlignment=HorizontalAlignment.Center});
        empty.Children.Add(new TextBlock{Text="截取屏幕，填写提示词，再选择发送目标。",TextWrapping=TextWrapping.Wrap,MaxWidth=300,HorizontalAlignment=HorizontalAlignment.Center,FontSize=12,Opacity=0.72});
        var firstCapture=new Button{Content="截取屏幕",HorizontalAlignment=HorizontalAlignment.Center};firstCapture.Click+=(_,_)=>app.BeginCapture();empty.Children.Add(firstCapture);conversation.Children.Add(empty);
        Grid.SetRow(conversation,2);messageGrid.Children.Add(conversation);
        var draft=new Grid{ColumnSpacing=12};draft.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});draft.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});draft.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        thumbnail.Content=draftImage;Label(thumbnail,"查看当前截图详情");thumbnail.Click+=async(_,_)=>await ShowPreviewAsync(png);draft.Children.Add(thumbnail);Grid.SetColumn(dimensions,1);draft.Children.Add(dimensions);
        capture.Click+=(_,_)=>app.BeginCapture();Label(capture,"截取新的屏幕区域");Grid.SetColumn(capture,2);draft.Children.Add(capture);
        Grid.SetRow(draft,3);messageGrid.Children.Add(draft);
        AutomationProperties.SetName(prompt,"截图提示词");Grid.SetRow(prompt,4);messageGrid.Children.Add(prompt);
        MessageSurface=Surface("FlyoutBackgroundThemeBrush",8);MessageSurface.Child=messageGrid;
        var root=new Grid{Padding=new Thickness(12),RowSpacing=12};root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        root.Children.Add(ToolbarSurface);Grid.SetRow(MessageSurface,1);root.Children.Add(MessageSurface);
        var outer=Surface("ApplicationPageBackgroundThemeBrush",8);outer.Child=root;Content=outer;
        outer.AddHandler(UIElement.KeyDownEvent,new KeyEventHandler((_,e)=>{if(e.Key==VirtualKey.Escape){app.CollapseFloatingPanel();e.Handled=true;}}),true);
        codex.Click+=async(_,_)=>await SendCodexAsync();chatgpt.Click+=async(_,_)=>await SendAsync("ChatGPT");
        copy.Click+=async(_,_)=>{try{await ClipboardService.ImageAsync(png,token:lifetime.Token);if(!closed)SetStatus("图片已复制");}catch(Exception e){if(!closed)SetStatus("复制失败："+ErrorText(e));}};
        save.Click+=async(_,_)=>await SaveAsync();
        ConfigureWorkspace();ConfigureMore();UpdateButtons();
        Closed+=(_,_)=>{closed=true;operationVersion++;imageVersion++;lifetime.Cancel();ClosePreview();lifetime.Dispose();app.Panel=null;app.EndCaptureSession(false);};
    }

    private static Border Surface(string brush,int radius)=>(Border)Microsoft.UI.Xaml.Markup.XamlReader.Load($"<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Background=\"{{ThemeResource {brush}}}\" BorderBrush=\"{{ThemeResource FlyoutBorderThemeBrush}}\" BorderThickness=\"1\" CornerRadius=\"{radius}\" />");
    private static Button ActionButton(string text,string glyph,string label)
    {
        var content=new StackPanel{Spacing=2,HorizontalAlignment=HorizontalAlignment.Center};content.Children.Add(new FontIcon{Glyph=glyph,FontSize=18});content.Children.Add(new TextBlock{Text=text,FontSize=10,HorizontalAlignment=HorizontalAlignment.Center});
        var button=new Button{Content=content,Width=56,Height=44,Padding=new Thickness(2),BorderThickness=new Thickness(0),Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent)};Label(button,label);return button;
    }
    private static void Label(FrameworkElement element,string text){AutomationProperties.SetName(element,text);ToolTipService.SetToolTip(element,text);}
    private static string ErrorText(Exception e)=>string.IsNullOrWhiteSpace(e.Message)?$"{e.GetType().Name} (0x{e.HResult:X8})":e.Message;
    private void ConfigureWorkspace()
    {
        var menu=new MenuFlyout();workspace.Flyout=menu;Label(workspace,app.Codex.IsProvider?"选择 Codex 工作目录":"选择 Codex 工作区");
        menu.Opening+=async(_,_)=>
        {
            menu.Items.Clear();menu.Items.Add(new MenuFlyoutItem{Text="正在读取工作区…",IsEnabled=false});
            try
            {
                var list=await app.Codex.WorkspacesAsync();menu.Items.Clear();
                foreach(var item in list)
                {
                    bool selected=app.Codex.IsProvider?app.Settings.CodexDirectory==item.DirectoryPath:app.Settings.Workspace==item.Name;
                    var entry=new MenuFlyoutItem{Text=(selected?"✓ ":"")+(app.Codex.IsProvider?item.DirectoryPath:item.Name)};
                    entry.Click+=(_,_)=>
                    {
                        if(app.Codex.IsProvider&&item.DirectoryPath!=null)CodexDirectoryPicker.Select(app.Settings,item.DirectoryPath);
                        else{app.Settings.Workspace=item.Name;app.Settings.Save();}
                        RefreshWorkspace();SetStatus(app.Codex.IsProvider?"Codex 工作目录已选择":"Codex 工作区已选择");
                    };menu.Items.Add(entry);
                }
                if(app.Codex.IsProvider)
                {
                    if(list.Count>0)menu.Items.Add(new MenuFlyoutSeparator());
                    var choose=new MenuFlyoutItem{Text="选择工作目录…"};choose.Click+=async(_,_)=>await ChooseDirectoryAsync();menu.Items.Add(choose);
                }
                else if(list.Count==0)menu.Items.Add(new MenuFlyoutItem{Text="没有可识别的唯一工作区",IsEnabled=false});
            }
            catch(Exception e){menu.Items.Clear();menu.Items.Add(new MenuFlyoutItem{Text="打开 Codex 并展开项目列表后重试",IsEnabled=false});SetStatus(ErrorText(e));}
        };
    }
    private async Task ChooseDirectoryAsync()
    {
        if(closed||busy)return;
        busy=true;UpdateButtons();app.SetFloatingOperationVisible(false);
        try{if(await CodexDirectoryPicker.PickAsync(this,app.Settings)&&!closed){RefreshWorkspace();SetStatus("Codex 工作目录已选择");}}
        catch(Exception error){if(!closed)SetStatus("选择工作目录失败："+ErrorText(error));}
        finally{busy=false;if(!closed){UpdateButtons();app.SetFloatingOperationVisible(true);}}
    }
    private void ConfigureMore()
    {
        var menu=new MenuFlyout();more.Flyout=menu;
        void Add(string text,Action action){var item=new MenuFlyoutItem{Text=text};item.Click+=(_,_)=>action();menu.Items.Add(item);}
        Add("截取新截图",app.BeginCapture);
        Add("发送到两端",async()=>{await SendAsync("ChatGPT");await SendAsync("Codex");});
        Add("复制提示词",async()=>{try{await ClipboardService.TextAsync(new SendRequest([],prompt.Text,null).EffectivePrompt,token:lifetime.Token);if(!closed)SetStatus("提示词已复制");}catch(Exception e){if(!closed)SetStatus(ErrorText(e));}});
        Add("清空消息",()=>{if(busy)return;operationVersion++;messages.Children.Clear();empty.Visibility=Visibility.Visible;SetStatus("消息已清空");});
        Add("退出截图",()=>{app.EndCaptureSession(false);SetStatus("已退出截图，当前图片仍可使用");});
        Add("设置",app.ShowSettings);Add("隐藏悬浮球",app.HideFloatingBall);
    }
    private void RefreshWorkspace()
    {
        workspace.Content=app.Codex.IsProvider?(app.Settings.CodexDirectory is {} path?CodexDirectoryPicker.DisplayName(path):"选择工作目录"):app.Settings.Workspace??"选择工作区";
        ToolTipService.SetToolTip(workspace,app.Codex.IsProvider?app.Settings.CodexDirectory??"选择 Codex 工作目录":"选择 Codex 工作区");
    }
    public void Open()
    {
        if(closed)return;
        expanded=true;RefreshWorkspace();Position();AppWindow.Show(false);
    }
    private void ClosePreview(){preview?.Close();preview=null;}
    public void Collapse(){expanded=false;ClosePreview();if(!closed)AppWindow.Hide();}
    internal void HideForOperation(){ClosePreview();if(!closed)AppWindow.Hide();}
    internal void RestoreAfterOperation(){if(!closed&&expanded)AppWindow.Show(false);}
    private void Position()
    {
        Native.Point anchor;
        if(app.Ball is {} ball&&Native.GetWindowRect(ball.Handle,out var rect))anchor=new(){X=(rect.Left+rect.Right)/2,Y=(rect.Top+rect.Bottom)/2};
        else anchor=new(){X=Native.GetSystemMetrics(0)-1,Y=Native.GetSystemMetrics(1)/3};
        var info=new Native.MonitorInfo{Size=(uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MonitorInfo>()};
        if(!Native.GetMonitorInfo(Native.MonitorFromPoint(anchor,2),ref info))throw new InvalidOperationException("无法读取悬浮面板的显示器工作区");
        AppWindow.Move(new Windows.Graphics.PointInt32(info.Work.Left,info.Work.Top));double scale=Native.GetDpiForWindow(Handle)/96d;
        int width=Math.Min((int)Math.Round(544*scale),info.Work.Right-info.Work.Left),height=Math.Min((int)Math.Round(632*scale),info.Work.Bottom-info.Work.Top);
        ToolbarSurface.Width=Math.Min(360,width/scale-24);
        double buttonWidth=Math.Min(56,(ToolbarSurface.Width-56)/5);
        foreach(var button in new[]{codex,chatgpt,copy,save,more})button.Width=Math.Max(32,buttonWidth);
        bool right=anchor.X>(info.Work.Left+info.Work.Right)/2;
        int x=right?info.Work.Right-width-(int)Math.Round(60*scale):info.Work.Left+(int)Math.Round(60*scale);
        x=Math.Clamp(x,info.Work.Left,info.Work.Right-width);
        int y=Math.Clamp(anchor.Y-(int)Math.Round(28*scale),info.Work.Top,info.Work.Bottom-height);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x,y,width,height));
    }
    public async Task PresentAsync(byte[] image,PixelRect region,Func<bool>? stillCurrent=null)
    {
        if(closed)return;
        int version=++imageVersion;
        var bitmap=await LoadBitmapAsync(image,160);if(closed||version!=imageVersion||stillCurrent?.Invoke()==false)return;
        operationVersion++;png=image;submittedTargets.Clear();prompt.Text="";
        dimensions.Text=$"当前截图 · {region.Width} × {region.Height}";thumbnail.Visibility=Visibility.Visible;
        bool hasWorkspace=app.Codex.IsProvider?app.Settings.CodexDirectory!=null:app.Settings.Workspace!=null;
        draftImage.Source=bitmap;SetStatus(hasWorkspace?"截图已就绪":app.Codex.IsProvider?"截图已就绪 · 请选择 Codex 工作目录":"截图已就绪 · 请选择 Codex 工作区");UpdateButtons();Open();
        prompt.Focus(FocusState.Programmatic);
    }
    private static async Task<BitmapImage> LoadBitmapAsync(byte[] image,int width=0)
    {
        using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream.GetOutputStreamAt(0))){writer.WriteBytes(image);await writer.StoreAsync();}
        stream.Seek(0);var bitmap=new BitmapImage();if(width>0)bitmap.DecodePixelWidth=width;await bitmap.SetSourceAsync(stream);return bitmap;
    }
    internal async Task ShowPreviewAsync(byte[] image)
    {
        if(image.Length==0||!expanded||closed)return;
        if(preview!=null){preview.Close();preview=null;}
        var window=new Window{Title="截图详情 · AI ScreenShot"};preview=window;
        window.Closed+=(_,_)=>{if(preview==window)preview=null;};
        var bitmap=await LoadBitmapAsync(image);
        if(preview!=window||closed)return;
        if(!expanded){window.Close();return;}
        var imageControl=new Image{Source=bitmap,Stretch=Stretch.Uniform};
        window.Content=new ScrollViewer{Content=imageControl,Padding=new Thickness(12),ZoomMode=ZoomMode.Enabled,MinZoomFactor=0.25f,MaxZoomFactor=4};
        var presenter=OverlappedPresenter.Create();presenter.IsAlwaysOnTop=true;window.AppWindow.SetPresenter(presenter);
        Native.GetWindowRect(Handle,out var bounds);var info=new Native.MonitorInfo{Size=(uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MonitorInfo>()};
        if(Native.GetMonitorInfo(Native.MonitorFromPoint(new Native.Point{X=(bounds.Left+bounds.Right)/2,Y=(bounds.Top+bounds.Bottom)/2},2),ref info))
        {
            int width=Math.Min(960,info.Work.Right-info.Work.Left),height=Math.Min(640,info.Work.Bottom-info.Work.Top);
            window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(info.Work.Left+(info.Work.Right-info.Work.Left-width)/2,info.Work.Top+(info.Work.Bottom-info.Work.Top-height)/2,width,height));
        }
        window.Activate();
    }
    private async Task AddUserMessageAsync(SendRequest request,string target)
    {
        empty.Visibility=Visibility.Collapsed;
        var body=new StackPanel{Spacing=8};body.Children.Add(new TextBlock{Text="你 · "+target,FontSize=11,Opacity=0.7});
        var image=new Image{Source=await LoadBitmapAsync(request.Png,360),Width=200,Height=112,Stretch=Stretch.Uniform};
        if(closed)return;
        var button=new Button{Content=image,Padding=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Left};Label(button,"查看这条消息的截图详情");button.Click+=async(_,_)=>await ShowPreviewAsync(request.Png);body.Children.Add(button);
        body.Children.Add(new TextBlock{Text=request.EffectivePrompt,TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true,FontSize=13});
        AddBubble(body,false);
    }
    private Border AddBubble(UIElement content,bool assistant)
    {
        var bubble=Surface(assistant?"ControlFillColorSecondaryBrush":"CardBackgroundFillColorDefaultBrush",8);bubble.Padding=new Thickness(12);bubble.MaxWidth=356;
        bubble.HorizontalAlignment=assistant?HorizontalAlignment.Right:HorizontalAlignment.Left;bubble.Child=content;
        messages.Children.Add(bubble);while(messages.Children.Count>24)messages.Children.RemoveAt(0);
        DispatcherQueue.TryEnqueue(()=>scroll.ChangeView(null,scroll.ScrollableHeight,null));return bubble;
    }
    private void SetStatus(string text,bool processing=false)
    {
        status.Text=text;progress.IsActive=processing;progress.Visibility=processing?Visibility.Visible:Visibility.Collapsed;
    }
    private void FollowMessageBottom(double previousOffset)
    {
        DispatcherQueue.TryEnqueue(()=>
        {
            if(closed||!expanded||Math.Abs(scroll.VerticalOffset-previousOffset)>1)return;
            // Layout may expand after the streamed text changes; do not override a user's intervening scroll.
            scroll.UpdateLayout();scroll.ChangeView(null,scroll.ScrollableHeight,null,true);
        });
    }
    private void UpdateButtons()
    {
        codex.IsEnabled=!busy&&png.Length>0&&!submittedTargets.Contains("Codex");chatgpt.IsEnabled=!busy&&png.Length>0&&!submittedTargets.Contains("ChatGPT");
        copy.IsEnabled=save.IsEnabled=!busy&&png.Length>0;more.IsEnabled=capture.IsEnabled=workspace.IsEnabled=prompt.IsEnabled=!busy;
    }
    internal Task SendCodexAsync()=>SendAsync("Codex");
    internal void StopWaiting()=>waitingCancellation?.Cancel();
    private Task FlushReplyUpdatesAsync()
    {
        if(closed)return Task.CompletedTask;
        var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // A worker can finish before its queued reply updates run. Drain those updates before applying the send result.
        if(!DispatcherQueue.TryEnqueue(()=>completion.TrySetResult()))completion.TrySetResult();
        return completion.Task;
    }
    private async Task SendAsync(string target)
    {
        if(closed||busy||png.Length==0||submittedTargets.Contains(target))return;
        if(target=="Codex"&&(app.Codex.IsProvider?app.Settings.CodexDirectory==null:app.Settings.Workspace==null)){SetStatus(app.Codex.IsProvider?"先在消息标题栏选择 Codex 工作目录":"先在消息标题栏选择 Codex 工作区");workspace.Focus(FocusState.Programmatic);return;}
        bool isCodex=target=="Codex",hiddenForOperation=false;
        busy=true;UpdateButtons();SetStatus("正在发送到 "+target,true);
        stopWaiting.Visibility=isCodex?Visibility.Visible:Visibility.Collapsed;
        var request=new SendRequest(png,prompt.Text,app.Settings.Workspace is {} name?new Workspace(name,app.Settings.CodexDirectory):null);
        var response=new TextBlock{Text="正在发送…",TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true,FontSize=13};
        var responseHeading=new TextBlock{Text=target+" · 发送状态",TextWrapping=TextWrapping.Wrap,FontSize=11,Opacity=0.7};latestResponse=response;latestResponseHeading=responseHeading;
        bool submitted=false,replyFinished=false,receivingUpdates=true,receivedReplyText=false;int operation=++operationVersion;
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);waitingCancellation=cancellation;
        try
        {
            if(isCodex){ClosePreview();app.EndCaptureSession(false);}
            await AddUserMessageAsync(request,target);
            if(closed)return;
            var body=new StackPanel{Spacing=8};body.Children.Add(responseHeading);body.Children.Add(response);AddBubble(body,true);
            if(!isCodex){app.SetFloatingOperationVisible(false);hiddenForOperation=true;}
            SendResult result;
            if(isCodex)result=await sendCodex(request,update=>
            {
                void ApplyUpdate()
                {
                    if(closed||!receivingUpdates||replyFinished||operation!=operationVersion)return;
                    double previousOffset=scroll.VerticalOffset;
                    bool followBottom=scroll.ScrollableHeight-previousOffset<=16;
                    if(update.Phase==CodexReplyPhase.Submitted)
                    {
                        submitted=true;submittedTargets.Add(target);stopWaiting.Visibility=Visibility.Visible;
                        responseHeading.Text="Codex · 正在处理";response.Text="正在思考 / 正在处理…";SetStatus("消息已提交 · Codex 正在思考 / 正在处理",true);
                    }
                    else if(update.Phase==CodexReplyPhase.Thinking)
                    {
                        string workStatus=string.IsNullOrWhiteSpace(update.Status)?"正在思考 / 正在处理":update.Status.Trim();
                        responseHeading.Text="Codex · "+workStatus;SetStatus("Codex · "+workStatus,true);
                        if(!string.IsNullOrWhiteSpace(update.Text)){receivedReplyText=true;response.Text=update.Text;}
                        else if(update.Text!=null&&!receivedReplyText)response.Text=workStatus+"…";
                    }
                    else if(update.Phase==CodexReplyPhase.Completed)
                    {
                        replyFinished=true;stopWaiting.Visibility=Visibility.Collapsed;responseHeading.Text="Codex";
                        response.Text=string.IsNullOrEmpty(update.Text)?update.Status??"Codex 已完成，未返回回复正文。":update.Text;SetStatus("Codex 输出完成");
                    }
                    else if(update.Phase==CodexReplyPhase.Unavailable)
                    {
                        replyFinished=true;stopWaiting.Visibility=Visibility.Collapsed;responseHeading.Text="Codex · 回复状态";
                        string replyStatus=update.Status??update.Text??"消息已提交 · 回复连接已断开";
                        if(update.Status!=null&&!string.IsNullOrWhiteSpace(update.Text)){receivedReplyText=true;response.Text=update.Text;}
                        if(!receivedReplyText)response.Text=replyStatus;SetStatus(replyStatus);
                    }
                    if(followBottom)FollowMessageBottom(previousOffset);
                }
                if(DispatcherQueue.HasThreadAccess)ApplyUpdate();else DispatcherQueue.TryEnqueue(ApplyUpdate);
            },cancellation.Token);
            else result=await app.Bridge.SendAsync(request,cancellation.Token);
            if(isCodex)await FlushReplyUpdatesAsync();
            if(closed||operation!=operationVersion)return;
            if(result.State!=SendState.Failed)submittedTargets.Add(target);
            if(!isCodex||result.State!=SendState.Sent)
            {
                responseHeading.Text=target+" · 发送状态";response.Text=result.Message;SetStatus(result.Message);
            }
            else if(!replyFinished)
            {
                responseHeading.Text="Codex · 回复状态";
                if(!receivedReplyText)response.Text="消息已提交，请到 Codex 查看回复。";SetStatus("消息已提交 · 请到 Codex 查看回复");
            }
        }
        catch(Exception e)
        {
            if(isCodex)await FlushReplyUpdatesAsync();
            if(closed||operation!=operationVersion)return;
            string replyStatus;
            if(submitted)
            {
                submittedTargets.Add(target);responseHeading.Text="Codex · 回复状态";
                replyStatus=e is OperationCanceledException?"消息已提交 · 已停止等待回复，请到 Codex 查看处理结果。":"消息已提交 · 回复读取失败，请到 Codex 查看处理结果。";
                if(!receivedReplyText)response.Text=replyStatus;
            }
            else{responseHeading.Text=target+" · 发送状态";replyStatus=response.Text="发送失败："+ErrorText(e);}
            SetStatus(replyStatus);
        }
        finally
        {
            receivingUpdates=false;if(waitingCancellation==cancellation)waitingCancellation=null;busy=false;
            if(!closed){stopWaiting.Visibility=Visibility.Collapsed;UpdateButtons();if(hiddenForOperation)app.SetFloatingOperationVisible(true);}
        }
    }
    private async Task SaveAsync()
    {
        if(closed||busy||png.Length==0)return;
        busy=true;UpdateButtons();app.SetFloatingOperationVisible(false);
        try
        {
            var picker=new FileSavePicker{SuggestedFileName="Screenshot-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")};picker.FileTypeChoices.Add("PNG 图片",new List<string>{".png"});WinRT.Interop.InitializeWithWindow.Initialize(picker,Handle);
            var file=await picker.PickSaveFileAsync();if(file!=null){await FileIO.WriteBytesAsync(file,png);SetStatus("图片已保存");}else SetStatus("已取消另存为");
        }
        catch(Exception e){SetStatus("保存失败："+ErrorText(e));}
        finally{busy=false;if(!closed){UpdateButtons();app.SetFloatingOperationVisible(true);}}
    }
    internal async Task ShowDesignPreviewAsync(byte[] fixture)
    {
        await PresentAsync(fixture,new PixelRect(0,0,320,200));
        await AddUserMessageAsync(new(fixture,"请解释截图中的信息，并给出下一步建议。",null),"布局示例");
        var reply=new StackPanel{Spacing=8};reply.Children.Add(new TextBlock{Text="Codex · 布局示例",FontSize=11,Opacity=0.7});reply.Children.Add(new TextBlock{Text="这是回复的显示位置。\n等待期间显示正在思考，完成后展示结果。",TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true,FontSize=13});AddBubble(reply,true);
        SetStatus("布局预览 · 使用生成图片和示例文本");
    }
    internal void ShowProcessingPreview(){SetStatus("布局预览 · Codex 正在思考 / 正在处理",true);}
}
