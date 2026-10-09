using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ScreenShot.Core;
using Windows.UI.ViewManagement;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;

namespace ScreenShot.App;

internal sealed partial class FloatingPanelWindow : Window
{
    private sealed class ConversationView(Conversation data)
    {
        internal Conversation Data=data;
        internal Grid Header=null!,Content=null!;
        internal readonly Dictionary<string,MarkdownMessageView> MessageViews=[];
        internal readonly Dictionary<string,TextBlock> MessageHeadings=[];
        internal readonly Button send=new(){MinWidth=76,VerticalAlignment=VerticalAlignment.Bottom};
        internal readonly ComboBox composerMode=new(){ItemsSource=new[]{"Codex","Agent"},SelectedIndex=0,Width=96,MinHeight=28,Height=28,FontSize=12,Padding=new Thickness(8,0,6,0)};
        internal readonly Button capture=new(){HorizontalAlignment=HorizontalAlignment.Left};
        internal readonly DropDownButton workspace=new(){Content="选择工作区",MaxWidth=204,VerticalAlignment=VerticalAlignment.Center};
        internal readonly TextBox prompt=new(){PlaceholderText="输入提示词；有截图时可留空使用默认提示词",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=64,MaxHeight=100,MaxLength=16000};
        internal readonly TextBlock status=new(){Text="就绪 · 输入提示词或截取屏幕"};
        internal readonly TextBlock dimensions=new(){Text="还没有截图",VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};
        internal readonly ProgressRing progress=new(){Width=16,Height=16,IsActive=false,Visibility=Visibility.Collapsed};
        internal readonly Button stopWaiting=new(){Content="停止等待",Visibility=Visibility.Collapsed};
        internal readonly StackPanel messages=new(){Spacing=UiDesign.Number("SS.Spacing.M")};
        internal readonly ScrollViewer scroll=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        internal readonly Button thumbnail=new(){Padding=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Left,Visibility=Visibility.Collapsed};
        internal readonly Image draftImage=new(){Width=64,Height=40,Stretch=Stretch.Uniform};
        internal readonly StackPanel empty=new(){Spacing=UiDesign.Number("SS.Spacing.M"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        internal readonly HashSet<string> submittedTargets=[];
        internal readonly HashSet<(string Target,string Prompt)> submittedTextPrompts=[];
        internal CancellationTokenSource? waitingCancellation=null;
        internal MarkdownMessageView? latestResponse=null;
        internal TextBlock? latestResponseHeading=null;
        internal TextBlock? latestResponseModel=null;
        internal byte[] png=[];
        internal int imageVersion=0;
        internal int operationVersion=0;
        internal bool busy=false;
        internal int TitleVersion;
        internal long messageInteractionVersion=0;
        internal double? requestedMessageOffset=null;
    }
    private ConversationView current=new(new Conversation());
    private readonly Dictionary<string,ConversationView> conversations=[];
    private readonly StackPanel tabs=new(){Orientation=Orientation.Horizontal,Spacing=2};
    private readonly Grid chatHost=new();
    private readonly ScrollViewer tabScroll=new(){HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollMode=ScrollMode.Enabled,VerticalScrollMode=ScrollMode.Disabled,IsHorizontalScrollChainingEnabled=false};
    private readonly ScrollBar tabOverflow=new(){Orientation=Orientation.Horizontal,IndicatorMode=ScrollingIndicatorMode.MouseIndicator,Height=10,Visibility=Visibility.Collapsed};
    private bool updatingTabOverflow;
    private string? visibleTabId;
    internal ScrollBar TabOverflow=>tabOverflow;
    internal ScrollViewer TabViewport=>tabScroll;
    internal Microsoft.UI.Xaml.Controls.Primitives.ToggleButton SelectedTab=>tabs.Children.OfType<Grid>().Select(row=>(Microsoft.UI.Xaml.Controls.Primitives.ToggleButton)row.Children[0]).Single(tab=>tab.IsChecked==true);
    private ConversationStore? store;
    private readonly string? conversationDirectory;
    private string? storageWarning;
    private bool restoring;
    private readonly App app;
    private readonly Button codex=ActionButton("Codex","\uE756","发送截图到 Codex");
    private readonly Button agent=ActionButton("Agent","\uE8F2","发送截图到 Agent");
    private readonly Button copy=ActionButton("复制","\uE8C8","复制截图");
    private readonly Button save=ActionButton("另存为","\uE74E","将截图另存为 PNG");
    private readonly Button more=ActionButton("更多","\uE712","更多操作");
    private Button send=>current.send;
    private ComboBox composerMode=>current.composerMode;
    private readonly Button paneMenuButton=new(){Content=UiDesign.Icon("\uE70D")};
    private readonly MenuFlyout paneMenu=new();
    private readonly MenuFlyoutItem toolbarVisibilityItem=new(),messageVisibilityItem=new(),toolbarSizeItem=new();
    private Button capture=>current.capture;
    private DropDownButton workspace=>current.workspace;
    private TextBox prompt=>current.prompt;
    private TextBlock status=>current.status;
    private TextBlock dimensions=>current.dimensions;
    private ProgressRing progress=>current.progress;
    private Button stopWaiting=>current.stopWaiting;
    private StackPanel messages=>current.messages;
    private ScrollViewer scroll=>current.scroll;
    private Button thumbnail=>current.thumbnail;
    private Image draftImage=>current.draftImage;
    private StackPanel empty=>current.empty;
    private HashSet<string> submittedTargets=>current.submittedTargets;
    private HashSet<(string Target,string Prompt)> submittedTextPrompts=>current.submittedTextPrompts;
    private readonly CancellationTokenSource lifetime=new();
    private readonly Func<SendRequest,Action<CodexReplyUpdate>,CancellationToken,Task<SendResult>> sendCodex;
    private readonly Func<SendRequest,Action<AgentReplyUpdate>,CancellationToken,Task<SendResult>> sendAgent;
    private readonly Grid layout;
    private readonly Grid toolGrid;
    private Grid messageHeader=>current.Header;
    private readonly PanelResizeController resizeController;
    private readonly PanelMoveController moveController;
    private readonly AdjustableBackdrop toolbarBackdrop,messageBackdrop;
    private readonly TransparentPanelBackdrop transparentHost;
    private readonly UiTransition transition=new();
    private readonly UISettings visualSettings=new();
    private PanelAppearance appearance;
    private bool toolbarCompact;
    private bool hiddenForOperation;
    private readonly Grid outer;
    private readonly Border toolbarStroke,messageStroke;
    private readonly PanelGeometry geometry;
    private readonly PanelInputTransparency inputTransparency;
    private readonly PanelShadowWindow shadowWindow;
    internal PanelShadowWindow ShadowWindow=>shadowWindow;
    private bool expanded,closed;
    private bool toolbarVisible=true,messageVisible=true;
    private double logicalWidth=544,toolbarHeight=56,messageHeight=540;
    private ResizeStart? resizeStart;
    private Native.Rect? moveStart;
    private bool manuallyPositioned;
    private Window? preview;
    private CancellationTokenSource? waitingCancellation {get=>current.waitingCancellation;set=>current.waitingCancellation=value;}
    private MarkdownMessageView? latestResponse {get=>current.latestResponse;set=>current.latestResponse=value;}
    private TextBlock? latestResponseHeading {get=>current.latestResponseHeading;set=>current.latestResponseHeading=value;}
    private TextBlock? latestResponseModel {get=>current.latestResponseModel;set=>current.latestResponseModel=value;}
    private byte[] png {get=>current.png;set=>current.png=value;}
    private int imageVersion {get=>current.imageVersion;set=>current.imageVersion=value;}
    private int operationVersion {get=>current.operationVersion;set=>current.operationVersion=value;}
    private bool busy {get=>current.busy;set=>current.busy=value;}
    private long messageInteractionVersion {get=>current.messageInteractionVersion;set=>current.messageInteractionVersion=value;}
    private double? requestedMessageOffset {get=>current.requestedMessageOffset;set=>current.requestedMessageOffset=value;}
    public bool IsBusy=>busy;
    public nint Handle {get;}
    internal Border ToolbarSurface {get;}
    internal Border MessageSurface {get;}
    internal IReadOnlyList<AdjustableBackdrop> MaterialBackdrops=>[toolbarBackdrop,messageBackdrop];
    internal byte? HostAlpha=>transparentHost.Alpha;
    internal IReadOnlyList<PanelGeometry.Shape> PaneShapes=>geometry.Shapes;
    internal bool IsExpanded=>expanded;
    internal bool IsProcessing=>progress.IsActive;
    internal bool CanSendToCodex=>codex.IsEnabled;
    internal bool CanSendToAgent=>agent.IsEnabled;
    internal string ResponseModelName=>latestResponseModel?.Text??"";
    internal MarkdownMessageView? ResponseView=>latestResponse;
    internal bool CanSendFromComposer=>send.IsEnabled;
    internal Button ComposerSendButton=>send;
    internal ComboBox ComposerModeSelector=>composerMode;
    private string ComposerTarget=>composerMode.SelectedIndex==1?"Agent":"Codex";
    internal Task? ComposerSendTask {get;private set;}
    internal string PromptText {get=>prompt.Text;set=>prompt.Text=value;}
    internal bool IsToolbarVisible=>toolbarVisible;
    internal bool IsMessageVisible=>messageVisible;
    internal Button VisibilityMenuButton=>paneMenuButton;
    internal MenuFlyoutItem ToolbarVisibilityItem=>toolbarVisibilityItem;
    internal MenuFlyoutItem MessageVisibilityItem=>messageVisibilityItem;
    internal MenuFlyoutItem ToolbarSizeItem=>toolbarSizeItem;
    internal bool IsToolbarCompact=>toolbarCompact;
    internal bool IsTransitioning=>transition.IsRunning;
    internal PanelAppearance Appearance=>appearance;
    internal IReadOnlyList<Border> EdgeStrokes=>[toolbarStroke,messageStroke];
    internal bool VisibilityMenuOpen=>paneMenu.IsOpen;
    internal bool VisibilityMenuInToolbar=>ReferenceEquals(paneMenuButton.Parent,toolGrid);
    internal TextBlock MessageDragTitle=>(TextBlock)messageHeader.Children[0];
    internal bool CanStopWaiting=>stopWaiting.Visibility==Visibility.Visible&&stopWaiting.IsEnabled;
    internal string StatusText=>status.Text;
    internal string ResponseText=>latestResponse?.Text??"";
    internal int ResponseRenderedBlockCount=>latestResponse?.RenderedBlockCount??0;
    internal string ResponseHeading=>latestResponseHeading?.Text??"";
    internal int MessageCount=>messages.Children.Count;
    internal double MessageScrollOffset=>scroll.VerticalOffset;
    internal double MessageScrollHeight=>scroll.ScrollableHeight;
    internal void ScrollMessages(double offset){RecordMessageInteraction();scroll.ChangeView(null,offset,null,true);}
    internal bool PreviewVisible=>preview!=null&&Native.IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(preview));
    internal bool PreviewOnTop=>preview?.AppWindow.Presenter is OverlappedPresenter {IsAlwaysOnTop:true};

    public FloatingPanelWindow(App app,Func<SendRequest,Action<CodexReplyUpdate>,CancellationToken,Task<SendResult>>? sendCodex=null,
        Func<SendRequest,Action<AgentReplyUpdate>,CancellationToken,Task<SendResult>>? sendAgent=null,string? conversationDirectory=null)
    {
        this.conversationDirectory=conversationDirectory;
        this.app=app;appearance=app.Settings.PanelAppearance;this.sendCodex=sendCodex??app.Codex.SendWithReplyAsync;this.sendAgent=sendAgent??app.Agent.SendWithReplyAsync;Title="AI ScreenShot · 消息";Handle=WinRT.Interop.WindowNative.GetWindowHandle(this);
        Native.SetWindowLongPtr(Handle,-20,(Native.GetWindowLongPtr(Handle,-20)|0x80)&~0x40000);
        var presenter=OverlappedPresenter.Create();presenter.SetBorderAndTitleBar(false,false);presenter.IsResizable=false;presenter.IsMaximizable=false;presenter.IsMinimizable=false;presenter.IsAlwaysOnTop=true;AppWindow.SetPresenter(presenter);
        // No native frame surrounds the transparent host; pane grips provide resizing.
        Native.SetWindowLongPtr(Handle,-16,Native.GetWindowLongPtr(Handle,-16)&~0x00c40000);
        Native.SetWindowLongPtr(Handle,-20,(Native.GetWindowLongPtr(Handle,-20)|0x00200000)&~0x00020300);
        if(!Native.SetWindowPos(Handle,0,0,0,0,0,0x01|0x02|0x04|0x10|0x20))throw new InvalidOperationException("无法移除悬浮宿主的非客户区外框");
        UiDesign.Apply(paneMenuButton,"SS.IconButtonStyle");
        var tools=new StackPanel{Orientation=Orientation.Horizontal,Spacing=UiDesign.Number("SS.Spacing.XS"),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center};
        foreach(var button in new[]{codex,agent,copy,save,more})tools.Children.Add(button);
        toolGrid=new Grid{ColumnSpacing=UiDesign.Number("SS.Spacing.S")};toolGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});toolGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});toolGrid.Children.Add(tools);
        ConfigurePaneMenu();Grid.SetColumn(paneMenuButton,1);toolGrid.Children.Add(paneMenuButton);
        ToolbarSurface=Surface("SS.ToolbarSurfaceStyle");ToolbarSurface.Height=toolbarHeight;ToolbarSurface.Padding=new Thickness(UiDesign.Number("SS.Spacing.M"),UiDesign.Number("SS.Spacing.S"),UiDesign.Number("SS.Spacing.M"),UiDesign.Number("SS.Spacing.S"));ToolbarSurface.Child=toolGrid;
        current.Data.Workspace=DefaultWorkspace();
        current.Content=BuildConversationView();conversations.Add(current.Data.Id,current);
        var content=new Grid{RowSpacing=8};content.RowDefinitions.Add(new(){Height=GridLength.Auto});content.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var tabRow=new Grid{ColumnSpacing=4};tabRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});tabRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});tabRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        tabScroll.Content=tabs;Label(tabScroll,"对话标签：横向滚动查看");
        var tabViewport=new Grid{RowSpacing=3};tabViewport.RowDefinitions.Add(new(){Height=GridLength.Auto});tabViewport.RowDefinitions.Add(new(){Height=GridLength.Auto});
        tabViewport.Children.Add(tabScroll);Grid.SetRow(tabOverflow,1);tabViewport.Children.Add(tabOverflow);tabRow.Children.Add(tabViewport);
        UiDesign.Apply(tabOverflow,"SS.TabScrollBarStyle");Label(tabOverflow,"横向滚动对话标签");
        tabScroll.ViewChanged+=(_,_)=>UpdateTabOverflow();tabScroll.SizeChanged+=(_,_)=>UpdateTabOverflow();tabs.SizeChanged+=(_,_)=>UpdateTabOverflow();
        tabOverflow.ValueChanged+=(_,e)=>{if(!updatingTabOverflow)tabScroll.ChangeView(e.NewValue,null,null,true);};
        var newChat=new Button{Content=UiDesign.Icon("\uE710",14),MinWidth=28,MinHeight=28,Padding=new Thickness(4),VerticalAlignment=VerticalAlignment.Top};UiDesign.Apply(newChat,"SS.QuietButtonStyle");Label(newChat,"新建对话（Ctrl+T）");newChat.Click+=(_,_)=>NewConversation();Grid.SetColumn(newChat,1);tabRow.Children.Add(newChat);
        var history=new Button{Content=UiDesign.Icon("\uE81C",14),MinWidth=28,MinHeight=28,Padding=new Thickness(4),VerticalAlignment=VerticalAlignment.Top};UiDesign.Apply(history,"SS.QuietButtonStyle");Label(history,"历史对话：重新打开或彻底删除");history.Click+=async(_,_)=>await ShowHistoryAsync();Grid.SetColumn(history,2);tabRow.Children.Add(history);
        content.Children.Add(tabRow);Grid.SetRow(chatHost,1);content.Children.Add(chatHost);chatHost.Children.Add(current.Content);
        MessageSurface=Surface("SS.MessageSurfaceStyle");MessageSurface.Child=new Grid{Children={content}};
        var root=new Grid{Padding=UiDesign.Thickness("SS.Spacing.M"),RowSpacing=UiDesign.Number("SS.Spacing.M")};layout=root;root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.Children.Add(ToolbarSurface);Grid.SetRow(MessageSurface,1);root.Children.Add(MessageSurface);
        outer=root;Content=root;transparentHost=new(Handle);SystemBackdrop=transparentHost;
        geometry=new(Handle,root,ToolbarSurface,MessageSurface);inputTransparency=new(Handle,geometry);shadowWindow=new(Handle);outer.LayoutUpdated+=WindowLayoutUpdated;
        resizeController=new PanelResizeController(Handle,ResizePanel);resizeController.Attach(ToolbarSurface,PanelResizePane.Toolbar);resizeController.Attach(MessageSurface,PanelResizePane.Message);
        toolbarBackdrop=AttachMaterial(ToolbarSurface,appearance.Toolbar);
        messageBackdrop=AttachMaterial(MessageSurface,appearance.Messages);
        toolbarStroke=AttachStroke(ToolbarSurface);messageStroke=AttachStroke(MessageSurface);
        ApplyAppearance(appearance);
        visualSettings.AnimationsEnabledChanged+=SystemAnimationsChanged;
        moveController=new PanelMoveController(Handle,MovePanel,()=>!closed&&expanded&&!resizeController.IsDragging&&!paneMenu.IsOpen);
        moveController.Attach(ToolbarSurface);moveController.Attach(MessageSurface);
        ToolTipService.SetToolTip(ToolbarSurface,"按住空白区域左键拖动，可移动工具栏和消息栏");
        ToolTipService.SetToolTip(MessageDragTitle,"按住标题或空白区域左键拖动，可移动工具栏和消息栏");
        outer.AddHandler(UIElement.KeyDownEvent,new KeyEventHandler((_,e)=>{if(e.Key==VirtualKey.T&&Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)){NewConversation();e.Handled=true;}else if(e.Key==VirtualKey.Escape){app.CollapseFloatingPanel();e.Handled=true;}}),true);
        codex.Click+=async(_,_)=>await SendCodexAsync();agent.Click+=async(_,_)=>await SendAsync("Agent");
        copy.Click+=async(_,_)=>{try{await ClipboardService.ImageAsync(png,token:lifetime.Token);if(!closed)SetStatus("图片已复制");}catch(Exception e){if(!closed)SetStatus("复制失败："+ErrorText(e));}};
        save.Click+=async(_,_)=>await SaveAsync();
        ConfigureMore();RestoreConversations();RenderTabs();UpdateButtons();
        app.Themes.Track(this);
        app.Themes.Changed+=UpdateStrokeBrush;outer.ActualThemeChanged+=StrokeThemeChanged;UpdateStrokeBrush();
        Closed+=(_,_)=>{closed=true;inputTransparency.Dispose();shadowWindow.Dispose();app.Themes.Changed-=UpdateStrokeBrush;outer.ActualThemeChanged-=StrokeThemeChanged;outer.LayoutUpdated-=WindowLayoutUpdated;transition.Dispose();visualSettings.AnimationsEnabledChanged-=SystemAnimationsChanged;toolbarBackdrop.Dispose();messageBackdrop.Dispose();moveController.Dispose();resizeController.Dispose();foreach(var view in conversations.Values){view.operationVersion++;view.imageVersion++;}lifetime.Cancel();store?.Dispose();ClosePreview();lifetime.Dispose();app.Panel=null;app.EndCaptureSession(false);};
    }

    private Grid BuildConversationView()
    {
        var view=current;
        UiDesign.Apply(send,"SS.PrimaryButtonStyle");send.Content=ActionContent("发送","\uE724");
        UiDesign.Apply(capture,"SS.QuietButtonStyle");capture.Content=ActionContent("截取屏幕","\uE722");
        UiDesign.Apply(stopWaiting,"SS.QuietButtonStyle");stopWaiting.FontSize=UiDesign.Number("SS.Typography.Caption");
        UiDesign.Apply(prompt,"SS.PromptTextBoxStyle");
        UiDesign.Apply(status,"SS.CaptionTextStyle");UiDesign.Apply(dimensions,"SS.CaptionTextStyle");dimensions.TextWrapping=TextWrapping.NoWrap;
        workspace.FontSize=UiDesign.Number("SS.Typography.Caption");workspace.MinHeight=UiDesign.Number("SS.ControlHeight");workspace.CornerRadius=UiDesign.Corner("SS.Corner.Control");

        var messageGrid=new Grid{RowSpacing=UiDesign.Number("SS.Spacing.M"),Padding=UiDesign.Thickness("SS.Padding.Surface")};
        foreach(var height in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})messageGrid.RowDefinitions.Add(new RowDefinition{Height=height});
        var header=new Grid{ColumnSpacing=UiDesign.Number("SS.Spacing.S")};current.Header=header;header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});for(int i=0;i<3;i++)header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        header.Children.Add(StyledText("消息","SS.SectionTextStyle",VerticalAlignment.Center));
        Grid.SetColumn(workspace,1);header.Children.Add(workspace);
        Label(stopWaiting,"Codex provider：请求中断本次生成；Agent：停止读取回复。不会重新提交截图");stopWaiting.Click+=(_,_)=>StopWaiting();Grid.SetColumn(stopWaiting,2);header.Children.Add(stopWaiting);
        messageGrid.Children.Add(header);
        var stateRow=new Grid{ColumnSpacing=UiDesign.Number("SS.Spacing.S")};stateRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});stateRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});stateRow.Children.Add(progress);Grid.SetColumn(status,1);stateRow.Children.Add(status);
        AutomationProperties.SetLiveSetting(status,Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        Grid.SetRow(stateRow,1);messageGrid.Children.Add(stateRow);
        var conversation=new Grid();scroll.Content=messages;scroll.Padding=new Thickness(0,0,UiDesign.Number("SS.Spacing.XS"),UiDesign.Number("SS.Spacing.XS"));conversation.Children.Add(scroll);
        scroll.AddHandler(UIElement.PointerWheelChangedEvent,new PointerEventHandler((_,_)=>RecordMessageInteraction()),true);
        scroll.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((_,_)=>RecordMessageInteraction()),true);
        scroll.AddHandler(UIElement.KeyDownEvent,new KeyEventHandler((_,e)=>{if(e.Key is VirtualKey.Up or VirtualKey.Down or VirtualKey.PageUp or VirtualKey.PageDown or VirtualKey.Home or VirtualKey.End or VirtualKey.Space)RecordMessageInteraction();}),true);
        var emptyIcon=UiDesign.Icon("\uE722",UiDesign.Number("SS.ControlHeight"));emptyIcon.Opacity=0.65;empty.Children.Add(emptyIcon);
        var emptyTitle=StyledText("从提示词或截图开始","SS.SectionTextStyle");emptyTitle.HorizontalAlignment=HorizontalAlignment.Center;empty.Children.Add(emptyTitle);
        var emptyDescription=StyledText("输入提示词即可对话，也可以截取屏幕一起发送。","SS.CaptionTextStyle");emptyDescription.MaxWidth=300;emptyDescription.TextAlignment=TextAlignment.Center;empty.Children.Add(emptyDescription);
        var firstCapture=new Button{Content=ActionContent("截取屏幕","\uE722"),HorizontalAlignment=HorizontalAlignment.Center};UiDesign.Apply(firstCapture,"SS.PrimaryButtonStyle");firstCapture.Click+=(_,_)=>app.BeginCapture();empty.Children.Add(firstCapture);conversation.Children.Add(empty);
        Grid.SetRow(conversation,2);messageGrid.Children.Add(conversation);
        var draft=new Grid{ColumnSpacing=UiDesign.Number("SS.Spacing.S")};draft.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});draft.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});draft.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        UiDesign.Apply(thumbnail,"SS.QuietButtonStyle");thumbnail.CornerRadius=UiDesign.Corner("SS.Corner.Control");
        thumbnail.Content=draftImage;Label(thumbnail,"查看当前截图详情");thumbnail.Click+=async(_,_)=>await ShowPreviewAsync(png);draft.Children.Add(thumbnail);Grid.SetColumn(dimensions,1);draft.Children.Add(dimensions);
        capture.Click+=(_,_)=>app.BeginCapture();Label(capture,"截取新的屏幕区域");Grid.SetColumn(capture,2);draft.Children.Add(capture);
        var composer=new Grid{ColumnSpacing=UiDesign.Number("SS.Spacing.S")};composer.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});composer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        AutomationProperties.SetName(prompt,"对话或截图提示词");composer.Children.Add(prompt);
        composerMode.CornerRadius=UiDesign.Corner("SS.Corner.Control");Label(composerMode,"发送模式：Codex 或 Agent");
        AutomationProperties.SetHelpText(composerMode,"选择输入框旁发送按钮使用的模式；工具栏按钮仍分别发送到各自目标");
        var sendActions=new StackPanel{Spacing=UiDesign.Number("SS.Spacing.XS"),VerticalAlignment=VerticalAlignment.Bottom};
        send.HorizontalAlignment=HorizontalAlignment.Stretch;sendActions.Children.Add(composerMode);sendActions.Children.Add(send);
        Grid.SetColumn(sendActions,1);composer.Children.Add(sendActions);
        composerMode.SelectionChanged+=(_,_)=>{view.Data.Client=view.composerMode.SelectedIndex==1?"Agent":"Codex";if(!restoring){Persist(view);if(ReferenceEquals(view,current))UpdateButtons();}};
        prompt.TextChanged+=(_,_)=>{if(ReferenceEquals(view,current))UpdateButtons();if(!restoring){view.Data.Draft=view.prompt.Text;Persist(view);}};
        send.Click+=async(_,_)=>{ComposerSendTask=SendAsync(ComposerTarget);await ComposerSendTask;};
        var inputRegion=new StackPanel{Spacing=UiDesign.Number("SS.Spacing.S")};inputRegion.Children.Add(draft);inputRegion.Children.Add(composer);
        var inputSurface=Surface("SS.ComposerSurfaceStyle");inputSurface.Padding=UiDesign.Thickness("SS.Spacing.M");inputSurface.Child=inputRegion;Grid.SetRow(inputSurface,3);messageGrid.Children.Add(inputSurface);
        ConfigureWorkspace();return messageGrid;
    }

    private static AdjustableBackdrop AttachMaterial(Border surface,SurfaceAppearance appearance)
    {
        var tintSurface=new Border{CornerRadius=surface.CornerRadius,IsHitTestVisible=false};
        var backdrop=new AdjustableBackdrop(appearance,tintSurface);
        var material=new SystemBackdropElement{SystemBackdrop=backdrop,CornerRadius=surface.CornerRadius,IsHitTestVisible=false};
        // Material, outline and shadow share the full pane bounds. A transparent
        // layout border would inset the material and leave a clear seam outside it.
        surface.BorderThickness=new Thickness(0);
        surface.Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var wrapper=(Grid)surface.Child;wrapper.Children.Insert(0,material);wrapper.Children.Insert(1,tintSurface);
        return backdrop;
    }

    internal void ApplyAppearance(PanelAppearance appearance)
    {
        if(closed)return;
        this.appearance=appearance=appearance.Normalize();
        toolbarBackdrop.Update(appearance.Toolbar);messageBackdrop.Update(appearance.Messages);
        foreach(var backdrop in MaterialBackdrops)backdrop.SetAccent(app.Themes.AccentColor,appearance.AccentMix);
        uint borderColor=0xfffffffeu;
        if(Native.DwmSetWindowAttribute(Handle,34,ref borderColor,sizeof(uint))<0)throw new InvalidOperationException("无法更新悬浮窗口外框");
        uint cornerPolicy=1;
        if(Native.DwmSetWindowAttribute(Handle,33,ref cornerPolicy,sizeof(uint))<0)throw new InvalidOperationException("无法关闭透明宿主的系统圆角");
        foreach(var stroke in EdgeStrokes)stroke.BorderThickness=new Thickness(appearance.ShowBorders?appearance.BorderThickness:0);
        if(!appearance.EnableAnimations&&transition.IsRunning)FinishVisualTransition();
        UpdatePaneVisuals();
    }

    private void WindowLayoutUpdated(object? sender,object args)=>UpdatePaneVisuals();
    private void StrokeThemeChanged(FrameworkElement sender,object args)=>UpdateStrokeBrush();
    private void UpdateStrokeBrush()
    {if(closed)return;foreach(var stroke in EdgeStrokes)stroke.BorderBrush=app.Themes.PanelBorderBrush(outer);foreach(var backdrop in MaterialBackdrops)backdrop.SetAccent(app.Themes.AccentColor,appearance.AccentMix);UpdatePaneVisuals();}
    private static Border AttachStroke(Border surface)
    {
        // Overlay strokes preserve content insets and animation geometry when their width changes.
        surface.BorderBrush=new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var stroke=new Border{IsHitTestVisible=false};
        stroke.CornerRadius=surface.CornerRadius;
        ((Grid)surface.Child).Children.Add(stroke);return stroke;
    }
    private void UpdatePaneVisuals()
    {
        if(closed)return;
        geometry.Update();
        inputTransparency.Update();
        shadowWindow.Update(geometry.Shapes,appearance,Math.Max(96u,Native.GetDpiForWindow(Handle))/96d,app.Themes.IsHighContrast(outer));
    }

    private void SystemAnimationsChanged(UISettings sender,object args)
    {DispatcherQueue.TryEnqueue(()=>{if(!closed&&!visualSettings.AnimationsEnabled)FinishVisualTransition();});}

    private static Border Surface(string styleKey)
    {
        var border=new Border();UiDesign.Apply(border,styleKey);return border;
    }
    private static TextBlock StyledText(string text,string styleKey,VerticalAlignment alignment=VerticalAlignment.Top)
    {
        var label=new TextBlock{Text=text,VerticalAlignment=alignment};UiDesign.Apply(label,styleKey);return label;
    }
    private static StackPanel ActionContent(string text,string glyph)
    {
        var content=new StackPanel{Orientation=Orientation.Horizontal,Spacing=UiDesign.Number("SS.Spacing.XS"),VerticalAlignment=VerticalAlignment.Center};
        content.Children.Add(UiDesign.Icon(glyph,UiDesign.Number("SS.Typography.Body")));content.Children.Add(new TextBlock{Text=text,FontSize=UiDesign.Number("SS.Typography.Caption"),VerticalAlignment=VerticalAlignment.Center});return content;
    }
    private static Button ActionButton(string text,string glyph,string label)
    {
        var button=new Button{Content=new Viewbox{Child=ActionContent(text,glyph),Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly}};UiDesign.Apply(button,"SS.ToolbarButtonStyle");Label(button,label);return button;
    }
    private static void Label(FrameworkElement element,string text)=>UiDesign.Label(element,text);
    private static string ErrorText(Exception e)=>string.IsNullOrWhiteSpace(e.Message)?$"{e.GetType().Name} (0x{e.HResult:X8})":e.Message;
    private void ConfigurePaneMenu()
    {
        Label(paneMenuButton,"调整工具栏和消息栏");paneMenuButton.Flyout=paneMenu;
        paneMenu.Items.Add(toolbarVisibilityItem);paneMenu.Items.Add(messageVisibilityItem);
        paneMenu.Items.Add(new MenuFlyoutSeparator());paneMenu.Items.Add(toolbarSizeItem);
        paneMenu.Opening+=(_,_)=>UpdatePaneMenuLabels();
        toolbarVisibilityItem.Click+=(_,_)=>{paneMenu.Hide();ToggleToolbar();};
        messageVisibilityItem.Click+=(_,_)=>{paneMenu.Hide();ToggleMessage();};
        toolbarSizeItem.Click+=(_,_)=>{paneMenu.Hide();ToggleToolbarSize();};
        UpdatePaneMenuLabels();
    }
    private void UpdatePaneMenuLabels()
    {
        toolbarVisibilityItem.Text=toolbarVisible?"隐藏工具栏":"显示工具栏";
        messageVisibilityItem.Text=messageVisible?"隐藏消息栏":"显示消息栏";
        toolbarSizeItem.Text=toolbarCompact?"扩展工具栏":"缩小工具栏";toolbarSizeItem.IsEnabled=toolbarVisible;
    }
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
                    bool selected=app.Codex.IsProvider?current.Data.Workspace?.DirectoryPath==item.DirectoryPath:current.Data.Workspace?.Name==item.Name;
                    var entry=new MenuFlyoutItem{Text=(selected?"✓ ":"")+(app.Codex.IsProvider?item.DirectoryPath:item.Name)};
                    entry.Click+=(_,_)=>
                    {
                        if(app.Codex.IsProvider&&item.DirectoryPath!=null)CodexDirectoryPicker.Select(app.Settings,item.DirectoryPath);
                        else{app.Settings.Workspace=item.Name;app.Settings.Save();}
                        current.Data.Workspace=item;Persist(current);RefreshWorkspace();SetStatus(app.Codex.IsProvider?"Codex 工作目录已选择":"Codex 工作区已选择");
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
        var view=current;
        try{if(await CodexDirectoryPicker.PickAsync(this,app.Settings)&&!closed){view.Data.Workspace=DefaultWorkspace();Persist(view);RefreshWorkspace();SetStatus("Codex 工作目录已选择");}}
        catch(Exception error){if(!closed)SetStatus("选择工作目录失败："+ErrorText(error));}
        finally{view.busy=false;if(!closed){UpdateButtons();app.SetFloatingOperationVisible(true);}}
    }
    private void ConfigureMore()
    {
        var menu=new MenuFlyout();more.Flyout=menu;
        void Add(string text,Action action){var item=new MenuFlyoutItem{Text=text};item.Click+=(_,_)=>action();menu.Items.Add(item);}
        Add("截取新截图",app.BeginCapture);
        Add("发送到两端",async()=>{await SendAsync("Agent");await SendAsync("Codex");});
        Add("复制提示词",async()=>{try{await ClipboardService.TextAsync(new SendRequest([],prompt.Text,null,app.Settings.DefaultPrompt).EffectivePrompt,token:lifetime.Token);if(!closed)SetStatus("提示词已复制");}catch(Exception e){if(!closed)SetStatus(ErrorText(e));}});
        Add("清空当前对话",()=>{if(busy)return;operationVersion++;current.TitleVersion++;messages.Children.Clear();current.MessageViews.Clear();current.MessageHeadings.Clear();latestResponse=null;latestResponseHeading=null;latestResponseModel=null;current.Data.Messages.Clear();current.Data.ThreadId=null;current.Data.TurnId=null;Persist(current);empty.Visibility=Visibility.Visible;SetStatus("当前对话已清空");});
        Add("退出截图",()=>{app.EndCaptureSession(false);SetStatus("已退出截图，当前图片仍可使用");});
        Add("设置",app.ShowSettings);Add("隐藏悬浮球",app.HideFloatingBall);
    }
    private void RefreshWorkspace()
    {
        workspace.Content=app.Codex.IsProvider?(current.Data.Workspace?.DirectoryPath is {} path?CodexDirectoryPicker.DisplayName(path):"选择工作目录"):current.Data.Workspace?.Name??"选择工作区";
        ToolTipService.SetToolTip(workspace,current.Data.Workspace?.DirectoryPath??"选择工作区");
    }
    public void Open()
    {
        if(closed)return;
        bool opening=!Native.IsWindowVisible(Handle);expanded=true;hiddenForOperation=false;
        RefreshWorkspace();if(opening)Position();TransitionPaneState(opening);
    }
    private void ClosePreview(){preview?.Close();preview=null;}
    public void Collapse()
    {if(closed)return;expanded=false;paneMenu.Hide();moveController.Cancel();resizeController.Cancel();ClosePreview();TransitionPaneState();}
    internal void HideForOperation()
    {
        if(closed)return;hiddenForOperation=true;FinishVisualTransition();
        paneMenu.Hide();moveController.Cancel();resizeController.Cancel();ClosePreview();shadowWindow.Hide();AppWindow.Hide();
    }
    internal void RestoreAfterOperation(){if(!closed&&expanded)Open();}
    internal void ToggleToolbar()
    {
        if(closed)return;
        moveController.Cancel();resizeController.Cancel();toolbarVisible=!toolbarVisible;
        if(!toolbarVisible)messageVisible=true;
        TransitionPaneState();
    }
    internal void ToggleMessage()
    {
        if(closed)return;
        moveController.Cancel();resizeController.Cancel();ClosePreview();messageVisible=!messageVisible;
        if(!messageVisible){toolbarVisible=true;app.EndCaptureSession(false);}
        TransitionPaneState();
    }

    internal void ToggleToolbarSize()
    {
        if(closed||!toolbarVisible)return;
        moveController.Cancel();resizeController.Cancel();toolbarCompact=!toolbarCompact;TransitionPaneState();
    }

    private void MovePaneMenu()
    {
        paneMenu.Hide();
        var menuHost=toolbarVisible?toolGrid:messageHeader;
        if(!menuHost.Children.Contains(paneMenuButton))
        {
            toolGrid.Children.Remove(paneMenuButton);messageHeader.Children.Remove(paneMenuButton);
            Grid.SetColumn(paneMenuButton,toolbarVisible?1:3);menuHost.Children.Add(paneMenuButton);
        }
        UpdatePaneMenuLabels();
    }

    private void ApplyPaneVisibility()
    {
        moveController.Cancel();MovePaneMenu();
        ToolbarSurface.Visibility=toolbarVisible?Visibility.Visible:Visibility.Collapsed;
        MessageSurface.Visibility=messageVisible?Visibility.Visible:Visibility.Collapsed;
        layout.RowSpacing=toolbarVisible&&messageVisible?12:0;
        foreach(var button in new[]{copy,save,more})
        {button.Visibility=toolbarCompact?Visibility.Collapsed:Visibility.Visible;button.Opacity=1;button.IsHitTestVisible=!toolbarCompact;}
    }

    private void TransitionPaneState(bool opening=false)
    {
        transition.Stop();
        bool shown=Native.IsWindowVisible(Handle);
        double oldToolbarHeight=ToolbarSurface.Height,oldMessageHeight=MessageSurface.Height;
        double oldGap=layout.RowSpacing;
        double oldToolbarWidth=double.IsFinite(ToolbarSurface.Width)?ToolbarSurface.Width:Math.Max(1,ToolbarSurface.ActualWidth);
        bool toolbarCollapsed=ToolbarSurface.Visibility==Visibility.Collapsed,messageCollapsed=MessageSurface.Visibility==Visibility.Collapsed;
        MovePaneMenu();ResizeAfterVisibilityChange(keepCurrentSize:true);
        ToolbarSurface.HorizontalAlignment=HorizontalAlignment.Left;
        ToolbarSurface.Height=toolbarCollapsed||opening?0:oldToolbarHeight;MessageSurface.Height=messageCollapsed||opening?0:oldMessageHeight;
        ToolbarSurface.Width=opening?ToolbarWidth:oldToolbarWidth;
        if(toolbarCollapsed||opening)ToolbarSurface.Opacity=0;
        if(messageCollapsed||opening)MessageSurface.Opacity=0;
        if(toolbarVisible)ToolbarSurface.Visibility=Visibility.Visible;
        if(messageVisible)MessageSurface.Visibility=Visibility.Visible;
        layout.RowSpacing=opening?0:oldGap;
        var tracks=new List<UiTransition.Track>
        {
            HeightTrack(ToolbarSurface,expanded&&toolbarVisible?toolbarHeight:0),HeightTrack(MessageSurface,expanded&&messageVisible?messageHeight:0),
            OpacityTrack(ToolbarSurface,expanded&&toolbarVisible?1:0),OpacityTrack(MessageSurface,expanded&&messageVisible?1:0),
            new(ToolbarSurface,"Width",()=>ToolbarSurface.Width,value=>ToolbarSurface.Width=value,ToolbarWidth),
            new(layout,"RowSpacing",()=>layout.RowSpacing,value=>layout.RowSpacing=value,expanded&&toolbarVisible&&messageVisible?UiDesign.Number("SS.Spacing.M"):0)
        };
        foreach(var button in new[]{copy,save,more})
        {
            if(!toolbarCompact&&button.Visibility==Visibility.Collapsed){button.Opacity=0;button.Visibility=Visibility.Visible;}
            button.IsHitTestVisible=!toolbarCompact;tracks.Add(OpacityTrack(button,toolbarCompact?0:1));
        }
        outer.UpdateLayout();UpdatePaneVisuals();
        if(expanded&&!hiddenForOperation){AppWindow.Show(false);UpdatePaneVisuals();}
        transition.Start(tracks,appearance.EnableAnimations&&visualSettings.AnimationsEnabled&&(shown||expanded)&&!hiddenForOperation,CommitVisualState);
    }

    private static UiTransition.Track HeightTrack(FrameworkElement element,double target)
        =>new(element,"Height",()=>element.Height,value=>element.Height=value,target);
    private static UiTransition.Track OpacityTrack(UIElement element,double target)
        =>new(element,"Opacity",()=>element.Opacity,value=>element.Opacity=value,target);

    private void CommitVisualState()
    {
        if(closed)return;
        if(!expanded||hiddenForOperation){shadowWindow.Hide();AppWindow.Hide();}
        ApplyPaneVisibility();ToolbarSurface.Opacity=MessageSurface.Opacity=1;
        ResizeAfterVisibilityChange();
        outer.UpdateLayout();UpdatePaneVisuals();
    }

    private void FinishVisualTransition(){transition.Stop();CommitVisualState();}

    private double WindowChromeWidth=>Native.GetWindowRect(Handle,out var bounds)&&Native.GetClientRect(Handle,out var client)?Math.Max(0,(bounds.Right-bounds.Left-client.Right+client.Left)/(Math.Max(96u,Native.GetDpiForWindow(Handle))/96d)):0;
    private double ToolbarWidth=>toolbarCompact?Math.Min(Math.Max(1,logicalWidth-HostInsets-WindowChromeWidth),UiDesign.Number("SS.ToolbarButtonWidth")*2+UiDesign.Number("SS.ControlHeight")+UiDesign.Number("SS.Spacing.S")+UiDesign.Number("SS.Spacing.XS")+HostInsets):Math.Max(1,logicalWidth-HostInsets-WindowChromeWidth);
    private double EffectiveLogicalWidth=>toolbarVisible&&toolbarCompact&&!messageVisible?ToolbarWidth+HostInsets+WindowChromeWidth:logicalWidth;

    private void ResizeAfterVisibilityChange(bool keepCurrentSize=false)
    {
        if(!Native.GetWindowRect(Handle,out var bounds)||!TryWorkArea(bounds,out var work))return;
        double scale=Math.Max(96u,Native.GetDpiForWindow(Handle))/96d;
        FitToWorkArea(work,scale);UpdateLayoutDimensions();
        int width=(int)Math.Round(EffectiveLogicalWidth*scale),height=(int)Math.Round(LogicalHeight*scale);
        if(keepCurrentSize){width=Math.Max(width,bounds.Right-bounds.Left);height=Math.Max(height,bounds.Bottom-bounds.Top);}
        int x=Math.Clamp(bounds.Left,work.Left,Math.Max(work.Left,work.Right-width));
        int y=Math.Clamp(bounds.Top,work.Top,Math.Max(work.Top,work.Bottom-height));
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x,y,width,height));
        AlignBallWithToolbar();
    }
    private double HostInsets=>UiDesign.Number("SS.Spacing.M")*2;
    private double LogicalHeight=>HostInsets+(messageVisible?messageHeight:0)+(toolbarVisible?toolbarHeight:0)+(toolbarVisible&&messageVisible?12:0);
    private static bool TryWorkArea(Native.Rect bounds,out Native.Rect work)
    {
        var info=new Native.MonitorInfo{Size=(uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MonitorInfo>()};
        bool found=Native.GetMonitorInfo(Native.MonitorFromPoint(new Native.Point{X=(bounds.Left+bounds.Right)/2,Y=(bounds.Top+bounds.Bottom)/2},2),ref info);
        work=info.Work;return found;
    }
    private void FitToWorkArea(Native.Rect work,double scale)
    {
        logicalWidth=Math.Min(logicalWidth,(work.Right-work.Left)/scale);
        double available=(work.Bottom-work.Top)/scale-HostInsets;
        double gap=toolbarVisible&&messageVisible?12:0;
        if(toolbarVisible)toolbarHeight=Math.Min(toolbarHeight,Math.Max(1,available-gap-(messageVisible?Math.Min(320,available/2):0)));
        if(messageVisible)messageHeight=Math.Min(messageHeight,Math.Max(1,available-(toolbarVisible?toolbarHeight+gap:0)));
    }
    private void UpdateLayoutDimensions()
    {
        ToolbarSurface.Height=toolbarHeight;MessageSurface.Height=messageHeight;
        ToolbarSurface.Width=toolbarCompact?ToolbarWidth:double.NaN;ToolbarSurface.HorizontalAlignment=toolbarCompact?HorizontalAlignment.Left:HorizontalAlignment.Stretch;
        double width=Math.Max(1,logicalWidth-HostInsets);
        // The resize controller moves surface padding into a transparent content host.
        var toolbarPadding=toolGrid.Parent is Border contentHost?contentHost.Padding:ToolbarSurface.Padding;
        double toolbarInsets=toolbarPadding.Left+toolbarPadding.Right+ToolbarSurface.BorderThickness.Left+ToolbarSurface.BorderThickness.Right;
        int count=toolbarCompact?2:5;
        double buttonWidth=Math.Clamp((ToolbarWidth-toolbarInsets-UiDesign.Number("SS.ControlHeight")-UiDesign.Number("SS.Spacing.S")-(count-1)*UiDesign.Number("SS.Spacing.XS"))/count,32,UiDesign.Number("SS.ToolbarButtonWidth"));
        foreach(var button in new[]{codex,agent,copy,save,more})button.Width=buttonWidth;
        double headerControls=36+24+(stopWaiting.Visibility==Visibility.Visible?100:0)+(toolbarVisible?0:UiDesign.Number("SS.ControlHeight"));
        workspace.MaxWidth=Math.Clamp(width-34-headerControls,64,204);
        foreach(var bubble in messages.Children.OfType<Border>())bubble.MaxWidth=Math.Max(1,width-56);
    }
    private sealed record ResizeStart(Native.Rect Bounds,Native.Rect Work,double Width,double ToolbarHeight,double MessageHeight);
    private void MovePanel(PanelMoveGesture gesture)
    {
        if(gesture.Phase==PanelMovePhase.Cancel){moveStart=null;return;}
        if(closed||!expanded)return;
        if(gesture.Phase==PanelMovePhase.Begin)
        {
            FinishVisualTransition();
            resizeController.Cancel();
            if(Native.GetWindowRect(Handle,out var bounds))moveStart=bounds;
            return;
        }
        if(moveStart is not {} start)return;
        if(gesture.Moved)
        {
            var info=new Native.MonitorInfo{Size=(uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MonitorInfo>()};
            if(Native.GetMonitorInfo(Native.MonitorFromPoint(new Native.Point{X=gesture.ScreenX,Y=gesture.ScreenY},2),ref info))
            {
                var position=PanelMovePolicy.Move(start.Left,start.Top,start.Right-start.Left,start.Bottom-start.Top,gesture.DeltaX,gesture.DeltaY,info.Work.Left,info.Work.Top,info.Work.Right,info.Work.Bottom);
                // Moving first lets Windows update the DPI before sizing the target monitor's layout.
                if(!Native.SetWindowPos(Handle,0,position.X,position.Y,0,0,0x01|0x04|0x10))throw new InvalidOperationException("无法移动悬浮面板");
                double scale=Math.Max(96u,Native.GetDpiForWindow(Handle))/96d;
                FitToWorkArea(info.Work,scale);UpdateLayoutDimensions();
                int width=(int)Math.Round(EffectiveLogicalWidth*scale),height=(int)Math.Round(LogicalHeight*scale);
                position=PanelMovePolicy.Move(start.Left,start.Top,width,height,gesture.DeltaX,gesture.DeltaY,info.Work.Left,info.Work.Top,info.Work.Right,info.Work.Bottom);
                if(!Native.SetWindowPos(Handle,0,position.X,position.Y,width,height,0x04|0x10))throw new InvalidOperationException("无法调整移动后的面板布局");
                manuallyPositioned=true;
            }
        }
        if(gesture.Phase==PanelMovePhase.End)moveStart=null;
        AlignBallWithToolbar();UpdatePaneVisuals();
    }
    private void ResizePanel(PanelResizeGesture gesture)
    {
        if(closed)return;
        if(gesture.Phase==PanelResizePhase.Cancel){resizeStart=null;return;}
        if(gesture.Phase==PanelResizePhase.Begin)
        {
            FinishVisualTransition();
            moveController.Cancel();
            if(!Native.GetWindowRect(Handle,out var bounds)||!TryWorkArea(bounds,out var work))return;
            resizeStart=new(bounds,work,logicalWidth,toolbarHeight,messageHeight);return;
        }
        if(resizeStart is not {} start)return;
        double scale=gesture.Scale;
        bool toolbar=PanelResizePolicy.ResolvePane(gesture.Pane,gesture.Edges,messageVisible)==PanelResizePane.Toolbar;
        double startHeight=toolbar?start.ToolbarHeight:start.MessageHeight;
        double otherHeight=HostInsets+(toolbar?(messageVisible?start.MessageHeight+12:0):(toolbarVisible?start.ToolbarHeight+12:0));
        double maxWidth=(gesture.Edges.HasFlag(PanelResizeEdges.Left)?start.Bounds.Right-start.Work.Left:start.Work.Right-start.Bounds.Left)/scale;
        double maxHeight=(gesture.Edges.HasFlag(PanelResizeEdges.Top)?start.Bounds.Bottom-start.Work.Top:start.Work.Bottom-start.Bounds.Top)/scale-otherHeight;
        maxWidth=Math.Max(1,maxWidth);maxHeight=Math.Max(1,maxHeight);
        bool fixedCompactWidth=toolbarCompact&&toolbarVisible&&!messageVisible;
        var limits=new PanelResizeLimits(fixedCompactWidth?start.Width:Math.Min(420,maxWidth),fixedCompactWidth?start.Width:maxWidth,Math.Min(toolbar?56:320,maxHeight),maxHeight);
        var change=PanelResizePolicy.Apply(start.Width,startHeight,gesture.Edges,gesture.DeltaX,gesture.DeltaY,limits);
        logicalWidth=change.Width;toolbarHeight=toolbar?change.Height:start.ToolbarHeight;messageHeight=toolbar?start.MessageHeight:change.Height;
        UpdateLayoutDimensions();
        int x=start.Bounds.Left+(int)Math.Round(change.LeftDelta*scale),y=start.Bounds.Top+(int)Math.Round(change.TopDelta*scale);
        int width=(int)Math.Round(EffectiveLogicalWidth*scale),height=(int)Math.Round(LogicalHeight*scale);
        x=Math.Clamp(x,start.Work.Left,Math.Max(start.Work.Left,start.Work.Right-width));
        y=Math.Clamp(y,start.Work.Top,Math.Max(start.Work.Top,start.Work.Bottom-height));
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x,y,width,height));
        AlignBallWithToolbar();
        if(gesture.Phase==PanelResizePhase.End)resizeStart=null;
    }
    internal void ResizeForDiagnostic(PanelResizePane pane,PanelResizeEdges edges,double deltaX,double deltaY)
    {
        double scale=Math.Max(96u,Native.GetDpiForWindow(Handle))/96d;
        ResizePanel(new(pane,edges,PanelResizePhase.Begin,0,0,scale));
        ResizePanel(new(pane,edges,PanelResizePhase.Update,deltaX,deltaY,scale));
        ResizePanel(new(pane,edges,PanelResizePhase.End,deltaX,deltaY,scale));
    }
    internal bool ResizeAtForDiagnostic(Windows.Foundation.Point point,double deltaX,double deltaY,PanelResizePane pane,PanelResizeEdges edges)
    {
        if(!resizeController.TryGetGripAt((UIElement)Content,point,out var hitPane,out var hitEdges)||hitPane!=pane||hitEdges!=edges)return false;
        ResizeForDiagnostic(hitPane,hitEdges,deltaX,deltaY);return true;
    }
    internal bool MoveAtForDiagnostic(Windows.Foundation.Point point,int deltaX,int deltaY)
    {
        if(closed||!expanded||resizeController.IsDragging||paneMenu.IsOpen||!moveController.TryGetDragAreaAt((UIElement)Content,point,out _)||!Native.GetWindowRect(Handle,out var bounds))return false;
        double scale=Math.Max(96u,Native.GetDpiForWindow(Handle))/96d;
        int screenX=bounds.Left+(int)Math.Round(point.X*scale),screenY=bounds.Top+(int)Math.Round(point.Y*scale);
        bool moved=PanelMovePolicy.ReachedThreshold(deltaX,deltaY,scale);
        MovePanel(new(PanelMovePhase.Begin,0,0,false,screenX,screenY));
        MovePanel(new(PanelMovePhase.Update,deltaX,deltaY,moved,screenX+deltaX,screenY+deltaY));
        MovePanel(new(PanelMovePhase.End,deltaX,deltaY,moved,screenX+deltaX,screenY+deltaY));return true;
    }
    internal bool CanMoveSourceForDiagnostic(DependencyObject source)
        =>PanelMoveController.IsEligibleSource(source,ToolbarSurface)||PanelMoveController.IsEligibleSource(source,MessageSurface);
    private void Position()
    {
        if(manuallyPositioned){ResizeAfterVisibilityChange();return;}
        Native.Point anchor;
        if(app.Ball is {} ball&&Native.GetWindowRect(ball.Handle,out var rect))anchor=new(){X=(rect.Left+rect.Right)/2,Y=(rect.Top+rect.Bottom)/2};
        else anchor=new(){X=Native.GetSystemMetrics(0)-1,Y=Native.GetSystemMetrics(1)/3};
        var info=new Native.MonitorInfo{Size=(uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MonitorInfo>()};
        if(!Native.GetMonitorInfo(Native.MonitorFromPoint(anchor,2),ref info))throw new InvalidOperationException("无法读取悬浮面板的显示器工作区");
        AppWindow.Move(new Windows.Graphics.PointInt32(info.Work.Left,info.Work.Top));double scale=Native.GetDpiForWindow(Handle)/96d;
        FitToWorkArea(info.Work,scale);UpdateLayoutDimensions();
        int width=(int)Math.Round(EffectiveLogicalWidth*scale),height=(int)Math.Round(LogicalHeight*scale);
        bool right=anchor.X>(info.Work.Left+info.Work.Right)/2;
        int gap=(int)Math.Round(BallHostGap*scale);
        int ballSize=app.Ball==null?0:(int)Math.Round(FloatingBallWindow.LogicalSize*Math.Max(96u,Native.GetDpiForWindow(app.Ball.Handle))/96d);
        int x=right?anchor.X-ballSize/2-gap-width:anchor.X+ballSize/2+gap;
        x=Math.Clamp(x,info.Work.Left,info.Work.Right-width);
        int y=Math.Clamp(anchor.Y-(int)Math.Round(BallCenterOffset*scale),info.Work.Top,info.Work.Bottom-height);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x,y,width,height));
        AlignBallWithToolbar();
    }
    public async Task PresentAsync(byte[] image,PixelRect region,Func<bool>? stillCurrent=null)
    {
        if(closed)return;
        var view=current;
        int version=++view.imageVersion;
        var bitmap=await LoadBitmapAsync(image,160);if(closed||version!=view.imageVersion||stillCurrent?.Invoke()==false)return;
        view.png=image;view.submittedTargets.Clear();view.prompt.Text="";messageVisible=true;
        view.dimensions.Text=$"当前截图 · {region.Width} × {region.Height}";view.thumbnail.Visibility=Visibility.Visible;
        bool hasWorkspace=app.Codex.IsProvider?view.Data.Workspace?.DirectoryPath!=null:view.Data.Workspace!=null;
        view.draftImage.Source=bitmap;SetStatus(hasWorkspace?"截图已就绪":app.Codex.IsProvider?"截图已就绪 · 请选择 Codex 工作目录":"截图已就绪 · 请选择 Codex 工作区",destination:view);PersistImage(view,image);if(ReferenceEquals(view,current)){UpdateButtons();Open();}
        if(ReferenceEquals(view,current))view.prompt.Focus(FocusState.Programmatic);
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
        app.Themes.Track(window);
        var presenter=OverlappedPresenter.Create();presenter.IsAlwaysOnTop=true;window.AppWindow.SetPresenter(presenter);
        Native.GetWindowRect(Handle,out var bounds);var info=new Native.MonitorInfo{Size=(uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MonitorInfo>()};
        if(Native.GetMonitorInfo(Native.MonitorFromPoint(new Native.Point{X=(bounds.Left+bounds.Right)/2,Y=(bounds.Top+bounds.Bottom)/2},2),ref info))
        {
            int width=Math.Min(960,info.Work.Right-info.Work.Left),height=Math.Min(640,info.Work.Bottom-info.Work.Top);
            window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(info.Work.Left+(info.Work.Right-info.Work.Left-width)/2,info.Work.Top+(info.Work.Bottom-info.Work.Top-height)/2,width,height));
        }
        window.Activate();
    }
    private async Task AddUserMessageAsync(SendRequest request,string target,ConversationView? destination=null)
    {
        var view=destination??current;
        view.empty.Visibility=Visibility.Collapsed;
        var body=new StackPanel{Spacing=UiDesign.Number("SS.Spacing.S")};body.Children.Add(StyledText("你 · "+target,"SS.CaptionTextStyle"));
        if(request.Png.Length>0)
        {
            var image=new Image{Source=await LoadBitmapAsync(request.Png,360),Width=200,Height=112,Stretch=Stretch.Uniform};
            if(closed)return;
            var button=new Button{Content=image,Padding=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Left};UiDesign.Apply(button,"SS.QuietButtonStyle");button.CornerRadius=UiDesign.Corner("SS.Corner.Control");Label(button,"查看这条消息的截图详情");button.Click+=async(_,_)=>await ShowPreviewAsync(request.Png);body.Children.Add(button);
        }
        body.Children.Add(new MarkdownMessageView{Text=request.EffectivePrompt});
        AddBubble(body,false,view);
    }
    private Border AddBubble(UIElement content,bool assistant,ConversationView? destination=null)
    {
        var view=destination??current;
        var bubble=Surface(assistant?"SS.AssistantMessageStyle":"SS.UserMessageStyle");bubble.MaxWidth=Math.Max(1,logicalWidth-82);
        bubble.HorizontalAlignment=assistant?HorizontalAlignment.Right:HorizontalAlignment.Left;bubble.Child=content;
        view.messages.Children.Add(bubble);
        // Settle inserted bubbles before a fast reply captures its follow-bottom offset.
        if(ReferenceEquals(view,current)){scroll.UpdateLayout();ScrollMessageToBottom();}return bubble;
    }
    private void SetStatus(string text,bool processing=false,ConversationView? destination=null)
    {
        var view=destination??current;
        view.status.Text=text;view.progress.IsActive=processing;view.progress.Visibility=processing?Visibility.Visible:Visibility.Collapsed;
    }
    private void RecordMessageInteraction(){messageInteractionVersion++;requestedMessageOffset=null;}
    private void ScrollMessageToBottom()
    {
        requestedMessageOffset=scroll.ScrollableHeight;
        scroll.ChangeView(null,requestedMessageOffset,null,true);
    }
    private void FollowMessageBottom(double previousOffset,long interactionVersion,double? pendingOffset,ConversationView? destination=null)
    {
        var view=destination??current;
        DispatcherQueue.TryEnqueue(()=>
        {
            if(closed||!ReferenceEquals(view,current)||!expanded||!messageVisible||messageInteractionVersion!=interactionVersion)return;
            // ChangeView can land after a reply snapshot. That requested move is not user scrolling.
            if(Math.Abs(scroll.VerticalOffset-previousOffset)>1&&(!pendingOffset.HasValue||Math.Abs(scroll.VerticalOffset-pendingOffset.Value)>1))return;
            // Layout may expand after the streamed text changes; do not override a user's intervening scroll.
            scroll.UpdateLayout();ScrollMessageToBottom();
        });
    }
    private bool CanSendTo(string target)=>!busy&&(png.Length>0?!submittedTargets.Contains(target):
        !string.IsNullOrWhiteSpace(prompt.Text)&&!submittedTextPrompts.Contains((target,prompt.Text.Trim())));
    private void MarkSubmitted(string target,SendRequest request,ConversationView? destination=null)
    {
        var view=destination??current;
        if(request.Png.Length>0)view.submittedTargets.Add(target);
        else view.submittedTextPrompts.Add((target,request.Prompt.Trim()));
    }
    private void UpdateButtons()
    {
        codex.IsEnabled=CanSendTo("Codex");agent.IsEnabled=CanSendTo("Agent");
        send.IsEnabled=ComposerTarget=="Agent"?agent.IsEnabled:codex.IsEnabled;composerMode.IsEnabled=!busy;
        Label(send,(png.Length>0?"发送截图和提示词到 ":"发送提示词到 ")+ComposerTarget);UpdateLayoutDimensions();
        copy.IsEnabled=save.IsEnabled=!busy&&png.Length>0;more.IsEnabled=capture.IsEnabled=workspace.IsEnabled=prompt.IsEnabled=!busy;
    }
    internal Task SendCodexAsync()=>SendAsync("Codex");
    internal void StopWaiting()
    {
        if(waitingCancellation==null||waitingCancellation.IsCancellationRequested)return;
        stopWaiting.IsEnabled=false;waitingCancellation.Cancel();
    }
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
        if(closed||!CanSendTo(target))return;
        var view=current;
        void SendStatus(string text,bool processing=false)=>SetStatus(text,processing,view);
        if(target=="Codex"&&(app.Codex.IsProvider?view.Data.Workspace?.DirectoryPath==null:view.Data.Workspace==null)){SendStatus(app.Codex.IsProvider?"先在消息标题栏选择 Codex 工作目录":"先在消息标题栏选择 Codex 工作区");view.workspace.Focus(FocusState.Programmatic);return;}
        bool isCodex=target=="Codex";
        view.busy=true;if(ReferenceEquals(view,current))UpdateButtons();RenderTabs();SendStatus("正在发送到 "+target,true);
        view.stopWaiting.IsEnabled=true;view.stopWaiting.Visibility=Visibility.Visible;
        UpdateLayoutDimensions();
        var request=new SendRequest(view.png,view.prompt.Text,view.Data.Workspace,app.Settings.DefaultPrompt){Conversation=new ConversationContext{ThreadId=view.Data.ThreadId,History=AgentHistory(view),SessionChanged=(thread,turn)=>SaveSession(view,thread,turn)}};
        var userRecord=new ConversationMessage{Role="user",Client=target,Text=request.EffectivePrompt,Image=view.Data.DraftImage};
        var replyRecord=new ConversationMessage{Role="assistant",Client=target};
        view.Data.Messages.Add(userRecord);view.Data.Messages.Add(replyRecord);Persist(view);
        var response=CreateResponseView("正在发送…");
        var responseHeading=StyledText(target+" · 发送状态","SS.CaptionTextStyle");responseHeading.TextTrimming=TextTrimming.CharacterEllipsis;
        view.MessageViews[replyRecord.Id]=response;view.MessageHeadings[replyRecord.Id]=responseHeading;
        var responseModel=StyledText(isCodex?"":(app.Settings.Agent.ModelName??"").Trim(),"SS.CaptionTextStyle");
        responseModel.Visibility=isCodex?Visibility.Collapsed:Visibility.Visible;responseModel.MaxWidth=180;responseModel.TextAlignment=TextAlignment.Right;responseModel.TextTrimming=TextTrimming.CharacterEllipsis;
        ToolTipService.SetToolTip(responseModel,responseModel.Text);AutomationProperties.SetName(responseModel,"回复模型："+responseModel.Text);
        view.latestResponse=response;view.latestResponseHeading=responseHeading;view.latestResponseModel=responseModel;
        bool submitted=false,replyFinished=false,receivingUpdates=true,receivedReplyText=false;int operation=++view.operationVersion;
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);view.waitingCancellation=cancellation;
        try
        {
            ClosePreview();app.EndCaptureSession(false);
            await AddUserMessageAsync(request,target,view);
            if(closed)return;
            var replyHeader=new Grid{ColumnSpacing=UiDesign.Number("SS.Spacing.S")};replyHeader.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});replyHeader.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            replyHeader.Children.Add(responseHeading);Grid.SetColumn(responseModel,1);replyHeader.Children.Add(responseModel);
            var body=new StackPanel{Spacing=UiDesign.Number("SS.Spacing.S")};body.Children.Add(replyHeader);body.Children.Add(response);AddBubble(body,true,view);
            SendResult result;
            void ReceiveUpdate(CodexReplyUpdate update,string? modelName=null)
            {
                void ApplyUpdate()
                {
                    if(closed||!receivingUpdates||replyFinished||operation!=view.operationVersion)return;
                    if(!isCodex&&!string.IsNullOrWhiteSpace(modelName))
                    {responseModel.Text=modelName;ToolTipService.SetToolTip(responseModel,modelName);AutomationProperties.SetName(responseModel,"回复模型："+modelName);}
                    double previousOffset=view.scroll.VerticalOffset;
                    long interactionVersion=view.messageInteractionVersion;double? pendingOffset=view.requestedMessageOffset;
                    bool followBottom=ReferenceEquals(view,current)&&messageVisible&&(view.scroll.ScrollableHeight-previousOffset<=16||pendingOffset.HasValue&&Math.Abs(view.scroll.ScrollableHeight-pendingOffset.Value)<=1),deferredRender=false;
                    if(update.Phase==CodexReplyPhase.Submitted)
                    {
                        submitted=true;MarkSubmitted(target,request,view);view.stopWaiting.Visibility=Visibility.Visible;
                        responseHeading.Text=target+" · 正在处理";response.Text="正在思考 / 正在处理…";SendStatus("消息已提交 · "+target+" 正在思考 / 正在处理",true);
                    }
                    else if(update.Phase==CodexReplyPhase.Thinking)
                    {
                        string workStatus=string.IsNullOrWhiteSpace(update.Status)?"正在思考 / 正在处理":update.Status.Trim();
                        responseHeading.Text=target+" · "+workStatus;SendStatus(target+" · "+workStatus,true);
                        if(!string.IsNullOrWhiteSpace(update.Text))
                        {
                            receivedReplyText=true;deferredRender=true;
                            response.SetStreamingText(update.Text,followBottom?()=>FollowMessageBottom(previousOffset,interactionVersion,pendingOffset,view):null);
                        }
                        else if(update.Text!=null&&!receivedReplyText)response.Text=workStatus+"…";
                    }
                    else if(update.Phase==CodexReplyPhase.Completed)
                    {
                        replyFinished=true;view.stopWaiting.Visibility=Visibility.Collapsed;responseHeading.Text=target;
                        response.Text=string.IsNullOrEmpty(update.Text)?update.Status??target+" 已完成，未返回回复正文。":update.Text;SendStatus(target+" 输出完成");
                    }
                    else if(update.Phase==CodexReplyPhase.Unavailable)
                    {
                        replyFinished=true;view.stopWaiting.Visibility=Visibility.Collapsed;responseHeading.Text=target+" · 回复状态";
                        string replyStatus=update.Status??update.Text??"消息已提交 · 回复连接已断开";
                        if(update.Status!=null&&!string.IsNullOrWhiteSpace(update.Text)){receivedReplyText=true;response.Text=update.Text;}
                        if(!receivedReplyText)response.Text=replyStatus;SendStatus(replyStatus);
                    }
                    replyRecord.Text=receivedReplyText||update.Phase==CodexReplyPhase.Completed?update.Text??response.Text:response.Text;replyRecord.Model=responseModel.Text;replyRecord.State=update.Phase==CodexReplyPhase.Completed?"Sent":update.Phase==CodexReplyPhase.Unavailable?"Unknown":update.Phase.ToString();userRecord.State=replyRecord.State;Persist(view);
                    if(followBottom&&!deferredRender)FollowMessageBottom(previousOffset,interactionVersion,pendingOffset,view);
                }
                if(DispatcherQueue.HasThreadAccess)ApplyUpdate();else DispatcherQueue.TryEnqueue(ApplyUpdate);
            }
            if(isCodex)result=await sendCodex(request,update=>ReceiveUpdate(update),cancellation.Token);
            else result=await sendAgent(request,update=>ReceiveUpdate(new(update.Phase switch{AgentReplyPhase.Submitted=>CodexReplyPhase.Submitted,AgentReplyPhase.Thinking=>CodexReplyPhase.Thinking,AgentReplyPhase.Completed=>CodexReplyPhase.Completed,_=>CodexReplyPhase.Unavailable},update.Text,update.Status),update.ModelName),cancellation.Token);
            await FlushReplyUpdatesAsync();
            if(closed||operation!=view.operationVersion)return;
            userRecord.State=result.State.ToString();
            if(!replyFinished)replyRecord.State=result.State==SendState.Sent?"Unknown":result.State.ToString();Persist(view);
            if(result.State!=SendState.Failed)MarkSubmitted(target,request,view);
            if(result.State!=SendState.Sent)
            {
                responseHeading.Text=target+" · 发送状态";if(isCodex||!receivedReplyText)response.Text=result.Message;SendStatus(result.Message);
            }
            else if(!replyFinished)
            {
                responseHeading.Text=target+" · 回复状态";
                if(!receivedReplyText)response.Text=isCodex?"消息已提交，请到 Codex 查看回复。":"请求已发送，Agent 未返回完整回复；不会自动重发";SendStatus(isCodex?"消息已提交 · 请到 Codex 查看回复":"请求已发送，Agent 未返回完整回复；不会自动重发");
            }
        }
        catch(Exception e)
        {
            await FlushReplyUpdatesAsync();
            if(closed||operation!=view.operationVersion)return;
            string replyStatus;
            if(submitted)
            {
                MarkSubmitted(target,request,view);responseHeading.Text=target+" · 回复状态";
                replyStatus=!isCodex?"Agent 回复读取已停止或失败；远端结果未知，不会自动重发":e is OperationCanceledException?"消息已提交 · 已停止等待回复；未确认 Codex 是否停止生成，已保留收到的内容。":"消息已提交 · 回复读取失败，请到 Codex 查看处理结果。";
                if(!receivedReplyText)response.Text=replyStatus;
            }
            else{responseHeading.Text=target+" · 发送状态";replyStatus=response.Text="发送失败："+ErrorText(e);}
            replyRecord.State=userRecord.State=submitted?"Unknown":"Failed";SendStatus(replyStatus);
        }
        finally
        {
            replyRecord.Text=response.Text;replyRecord.Model=responseModel.Text;Persist(view);
            if(store!=null&&replyRecord.State=="Sent"&&!view.Data.TitleRequested){view.Data.TitleRequested=true;Persist(view);_ = GenerateTitleAsync(view,request,target,replyRecord.Text);}
            receivingUpdates=false;if(view.waitingCancellation==cancellation)view.waitingCancellation=null;view.busy=false;
            if(!closed){view.stopWaiting.Visibility=Visibility.Collapsed;if(ReferenceEquals(view,current))UpdateButtons();RenderTabs();}
        }
    }
    internal Task SendAgentAsync()=>SendAsync("Agent");
    private async Task SaveAsync()
    {
        if(closed||busy||png.Length==0)return;
        var view=current;var image=png;
        busy=true;UpdateButtons();app.SetFloatingOperationVisible(false);
        try
        {
            var picker=new FileSavePicker{SuggestedFileName="Screenshot-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")};picker.FileTypeChoices.Add("PNG 图片",new List<string>{".png"});WinRT.Interop.InitializeWithWindow.Initialize(picker,Handle);
            var file=await picker.PickSaveFileAsync();if(file!=null){await FileIO.WriteBytesAsync(file,image);SetStatus("图片已保存",destination:view);}else SetStatus("已取消另存为",destination:view);
        }
        catch(Exception e){SetStatus("保存失败："+ErrorText(e),destination:view);}
        finally{view.busy=false;if(!closed){UpdateButtons();app.SetFloatingOperationVisible(true);}}
    }
    internal async Task ShowDesignPreviewAsync(byte[] fixture)
    {
        await PresentAsync(fixture,new PixelRect(0,0,320,200));
        await AddUserMessageAsync(new(fixture,"请解释截图中的信息，并给出下一步建议。",null),"布局示例");
        var reply=new StackPanel{Spacing=UiDesign.Number("SS.Spacing.S")};reply.Children.Add(StyledText("Codex · 布局示例","SS.CaptionTextStyle"));reply.Children.Add(CreateResponseView("## 截图分析\n\n这是**回复的显示位置**，支持 Markdown。\n\n- 等待期间显示正在思考\n- 完成后展示结果\n\n```text\n生成图布局示例\n```\n\n| 项目 | 状态 |\n| --- | --- |\n| 原生渲染 | 已启用 |"));AddBubble(reply,true);
        SetStatus("布局预览 · 使用生成图片和示例文本");
    }
    internal void ShowProcessingPreview(){SetStatus("布局预览 · Codex 正在思考 / 正在处理",true);}
}
