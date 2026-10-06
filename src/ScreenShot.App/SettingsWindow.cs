using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using ScreenShot.Core;

namespace ScreenShot.App;

internal sealed class SettingsWindow : Window
{
    private readonly App app;
    private readonly Func<uint,uint,bool> changeHotkey;
    private readonly ComboBox browser;
    private readonly TextBox key;
    private readonly TextBox codexExecutable;
    private readonly TextBlock codexDirectory;
    private readonly CheckBox ctrl,alt,shift,win;
    private readonly InfoBar feedback=new(){IsOpen=true,IsClosable=false,Severity=InfoBarSeverity.Informational};
    private readonly NavigationView navigation=new();
    private readonly ScrollViewer pageScroller=new(){HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalContentAlignment=HorizontalAlignment.Stretch};
    private readonly TextBlock pageTitle=new();
    private readonly TextBlock pageDescription=new(){TextWrapping=TextWrapping.Wrap};
    private readonly Dictionary<string,(string Title,string Description,StackPanel Content)> pages=[];
    private readonly Dictionary<string,SurfaceEditor> surfaceEditors=[];
    private Button? saveButton;
    private readonly CheckBox hideWindowBase=new(){Content="隐藏"};
    private readonly ToggleSwitch enableAnimations=new(){OnContent="开启",OffContent="关闭"};
    private readonly ToggleSwitch showBorders=new(){OnContent="开启",OffContent="关闭"};
    private readonly Slider borderThickness=new(){Minimum=0.5,Maximum=3,StepFrequency=0.5,SmallChange=0.5};
    private readonly TextBlock borderThicknessLabel=new(){VerticalAlignment=VerticalAlignment.Center,MinWidth=48,TextAlignment=TextAlignment.Right};
    private readonly ComboBox appTheme=new(){ItemsSource=new[]{"明","暗","系统"}};
    private readonly ComboBox accentMode=new(){ItemsSource=new[]{"跟随 Windows","自定义颜色"}};
    private readonly ColorPicker accentPicker=new(){IsAlphaEnabled=false,IsAlphaSliderVisible=false,IsAlphaTextInputVisible=false,IsHexInputVisible=true};
    private readonly Button accentButton=new(){HorizontalAlignment=HorizontalAlignment.Right};
    private readonly Border accentSwatch=new(){Width=20,Height=20,CornerRadius=new CornerRadius(10)};
    private readonly TextBlock accentHex=new(){VerticalAlignment=VerticalAlignment.Center};
    private static readonly AppThemeMode[] ThemeChoices=[AppThemeMode.Light,AppThemeMode.Dark,AppThemeMode.System];

    internal TextBox DefaultPromptEditor {get;}
    internal PanelAppearance AppearanceDraft=>ReadAppearance();
    internal ComboBox ThemeSelector=>appTheme;
    internal ComboBox AccentSelector=>accentMode;
    internal ColorPicker AccentPicker=>accentPicker;
    internal Button AccentPickerButton=>accentButton;
    internal ToggleSwitch BorderToggle=>showBorders;
    internal Slider BorderWidthSlider=>borderThickness;

    internal SettingsWindow(App app,Func<uint,uint,bool> changeHotkey)
    {
        this.app=app;this.changeHotkey=changeHotkey;Title="AI ScreenShot 设置";
        SystemBackdrop=new MicaBackdrop();
        var settings=app.Settings;
        hideWindowBase.IsChecked=settings.PanelAppearance.HideWindowBase;enableAnimations.IsOn=settings.PanelAppearance.EnableAnimations;
        showBorders.IsOn=settings.PanelAppearance.ShowBorders;borderThickness.Value=settings.PanelAppearance.BorderThickness;
        appTheme.SelectedIndex=Array.IndexOf(ThemeChoices,settings.PanelAppearance.Theme);
        accentMode.SelectedIndex=(int)settings.PanelAppearance.AccentMode;accentPicker.Color=AppThemeController.ParseColor(settings.PanelAppearance.CustomAccentColor);
        browser=new(){ItemsSource=new[]{"Edge","Chrome"},SelectedItem=settings.Browser,MinHeight=UiDesign.Number("SS.ControlHeight"),CornerRadius=UiDesign.Corner("SS.Corner.Control")};
        key=new(){Text=((char)settings.HotkeyKey).ToString(),MaxLength=1,MinHeight=UiDesign.Number("SS.ControlHeight"),MaxWidth=UiDesign.Number("SS.BallSize"),CornerRadius=UiDesign.Corner("SS.Corner.Control"),HorizontalContentAlignment=HorizontalAlignment.Center};
        codexExecutable=new(){Text=settings.CodexExecutable??"",PlaceholderText="codex.exe 的完整路径",MinHeight=UiDesign.Number("SS.ControlHeight"),CornerRadius=UiDesign.Corner("SS.Corner.Control")};
        codexDirectory=new(){Text=settings.CodexDirectory??"尚未选择工作目录",TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true};
        UiDesign.Apply(codexDirectory,"SS.CaptionTextStyle");
        DefaultPromptEditor=new()
        {
            Header="默认提示词",
            HeaderTemplate=(DataTemplate)XamlReader.Load("<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><TextBlock Visibility=\"Collapsed\" /></DataTemplate>"),
            PlaceholderText="未输入提示词时发送给图片的提示词",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,
            MinHeight=UiDesign.Number("SS.Settings.PromptMinHeight"),MaxHeight=UiDesign.Number("SS.Settings.PromptMaxHeight"),MaxLength=16000,Text=settings.DefaultPrompt,CornerRadius=UiDesign.Corner("SS.Corner.Control")
        };
        ctrl=new(){Content="Ctrl",IsChecked=(settings.HotkeyModifiers&2)!=0};
        alt=new(){Content="Alt",IsChecked=(settings.HotkeyModifiers&1)!=0};
        shift=new(){Content="Shift",IsChecked=(settings.HotkeyModifiers&4)!=0};
        win=new(){Content="Win",IsChecked=(settings.HotkeyModifiers&8)!=0};
        BuildPages();
        BuildShell();
        app.Themes.Track(this);app.Themes.Changed+=UpdateAccentPreview;UpdateAccentPreview();
        SetFeedback(app.Bridge?.Status??"扩展未连接");
        ResizeToWorkArea();
        Closed+=(_,_)=>{app.Themes.Changed-=UpdateAccentPreview;app.ApplyAppearance(app.Settings.PanelAppearance);};
    }

    private void BuildPages()
    {
        var general=Page("general","常规与快捷键","设置默认提示词和全局截图快捷键。", "\uE713");
        general.Children.Add(SettingRow("默认提示词","未填写发送提示词时使用；清空并保存可恢复内置提示词。",DefaultPromptEditor));
        general.Children.Add(SettingRow("截图快捷键","使用一个英文字母或数字。",key));
        var modifiers=new Grid{ColumnSpacing=UiDesign.Number("SS.Spacing.S"),RowSpacing=UiDesign.Number("SS.Spacing.XS")};
        modifiers.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});modifiers.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        modifiers.RowDefinitions.Add(new(){Height=GridLength.Auto});modifiers.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var checks=new[]{ctrl,alt,shift,win};
        for(int index=0;index<checks.Length;index++)
        {checks[index].MinWidth=0;Grid.SetColumn(checks[index],index%2);Grid.SetRow(checks[index],index/2);modifiers.Children.Add(checks[index]);}
        general.Children.Add(SettingRow("修饰键","至少选择一个；保存时检查快捷键是否被占用。",modifiers));

        var appearance=Page("appearance","外观与材质","分别调节悬浮面板的底板、工具条和消息背景。","\uE790");
        foreach(var selector in new[]{appTheme,accentMode}){selector.MinHeight=UiDesign.Number("SS.ControlHeight");selector.CornerRadius=UiDesign.Corner("SS.Corner.Control");}
        appearance.Children.Add(SettingRow("应用主题","统一设置窗口、悬浮入口和消息面板；系统模式自动跟随 Windows。",appTheme));
        UiDesign.Apply(accentButton,"SS.QuietButtonStyle");UiDesign.Apply(accentHex,"SS.CaptionTextStyle");
        var colorLabel=new StackPanel{Orientation=Orientation.Horizontal,Spacing=UiDesign.Number("SS.Spacing.S")};
        colorLabel.Children.Add(accentSwatch);colorLabel.Children.Add(accentHex);colorLabel.Children.Add(UiDesign.Icon("\uE790"));accentButton.Content=colorLabel;
        accentPicker.Width=UiDesign.Number("SS.Settings.ControlWidth");UiDesign.Label(accentPicker,"自定义主题颜色");UiDesign.Label(accentButton,"选择自定义主题颜色");
        accentButton.Flyout=new Flyout{Content=accentPicker,Placement=Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight};
        var colorEditor=new StackPanel{Spacing=UiDesign.Number("SS.Spacing.S")};colorEditor.Children.Add(accentMode);colorEditor.Children.Add(accentButton);
        appearance.Children.Add(SettingRow("主题颜色","用于按钮、选中状态和链接；自定义颜色可用色谱或 RGB / HEX 输入。",colorEditor));
        appearance.Children.Add(SettingRow("面板边框","底板、工具栏和消息栏的边框跟随主题颜色；隐藏底板时仅显示两栏边框。",showBorders));
        UiDesign.Apply(borderThicknessLabel,"SS.CaptionTextStyle");UiDesign.Label(borderThickness,"面板边框粗细，逻辑像素");
        var borderEditor=new Grid{ColumnSpacing=UiDesign.Number("SS.Spacing.S")};borderEditor.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});borderEditor.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        borderEditor.Children.Add(borderThickness);Grid.SetColumn(borderThicknessLabel,1);borderEditor.Children.Add(borderThicknessLabel);
        appearance.Children.Add(SettingRow("边框粗细","0.5–3 逻辑像素，随显示器 DPI 缩放；关闭边框后保留所选粗细。",borderEditor));
        appearance.Children.Add(Body("显示窗口后方当前可见的桌面画面。失去焦点时仍保持材质效果；文字、截图和按钮保持清晰。"));
        appearance.Children.Add(SettingRow("隐藏窗口底板","去掉公共背景和窗口外框，只保留工具栏、消息栏各自的材质与边框。",hideWindowBase));
        appearance.Children.Add(SettingRow("界面过渡动画","用于工具栏缩小/扩展、两栏显隐和悬浮球展开；同时遵循 Windows 动画开关。",enableAnimations));
        AddSurfaceEditor(appearance,"base","窗口底板","覆盖悬浮面板的外缘与两栏之间的留白。",app.Settings.PanelAppearance.Base);
        AddSurfaceEditor(appearance,"toolbar","工具条","轻透的胶囊表面，操作按钮保留原生反馈。",app.Settings.PanelAppearance.Toolbar);
        AddSurfaceEditor(appearance,"messages","消息面板","建议保留较高底色，减少后方内容对阅读的干扰。",app.Settings.PanelAppearance.Messages);
        void PreviewAppearance()
        {surfaceEditors["base"].SetEnabled(hideWindowBase.IsChecked!=true);borderThickness.IsEnabled=showBorders.IsOn;borderThicknessLabel.Text=$"{borderThickness.Value:0.#} px";UpdateAccentPreview();app.ApplyAppearance(ReadAppearance());}
        appTheme.SelectionChanged+=(_,_)=>PreviewAppearance();accentMode.SelectionChanged+=(_,_)=>PreviewAppearance();
        accentPicker.ColorChanged+=(_,_)=>PreviewAppearance();
        hideWindowBase.Checked+=(_,_)=>PreviewAppearance();hideWindowBase.Unchecked+=(_,_)=>PreviewAppearance();
        enableAnimations.Toggled+=(_,_)=>PreviewAppearance();PreviewAppearance();
        showBorders.Toggled+=(_,_)=>PreviewAppearance();borderThickness.ValueChanged+=(_,_)=>PreviewAppearance();
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=UiDesign.Number("SS.Spacing.S")};
        var preview=ActionButton("预览面板","\uE7B3");
        preview.Click+=(_,_)=>
        {
            app.Panel??=new FloatingPanelWindow(app);app.ApplyAppearance(ReadAppearance());app.Panel.Open();
            SetFeedback("预览已打开；调整会即时生效，保存后保留。关闭设置会恢复未保存的外观。");
        };
        var reset=ActionButton("恢复默认","\uE777");
        reset.Click+=(_,_)=>
        {
            var defaults=new PanelAppearance();surfaceEditors["base"].Set(defaults.Base);
            surfaceEditors["toolbar"].Set(defaults.Toolbar);surfaceEditors["messages"].Set(defaults.Messages);
            hideWindowBase.IsChecked=defaults.HideWindowBase;enableAnimations.IsOn=defaults.EnableAnimations;
            showBorders.IsOn=defaults.ShowBorders;borderThickness.Value=defaults.BorderThickness;
            appTheme.SelectedIndex=Array.IndexOf(ThemeChoices,defaults.Theme);accentMode.SelectedIndex=(int)defaults.AccentMode;accentPicker.Color=AppThemeController.ParseColor(defaults.CustomAccentColor);
            app.ApplyAppearance(ReadAppearance());SetFeedback("已恢复默认预览，点击保存外观后保留。");
        };
        actions.Children.Add(preview);actions.Children.Add(reset);appearance.Children.Add(actions);
        appearance.Children.Add(Caption("滑块表示背景底色的不透明度：0% 更通透，100% 为纯色。Acrylic 保留模糊；透明色层不额外模糊，会叠加在底板上。Windows 关闭透明效果或启用高对比度时使用可读的纯色背景。"));

        var codex=Page("codex","Codex","选择工作目录和 CLI，检测 provider 连接。","\uE756");
        var chooseDirectory=ActionButton("选择目录…","\uE8B7");
        chooseDirectory.Click+=async(_,_)=>
        {
            chooseDirectory.IsEnabled=false;
            try
            {
                if(await CodexDirectoryPicker.PickAsync(this,app.Settings))
                {codexDirectory.Text=app.Settings.CodexDirectory;SetFeedback("Codex 工作目录已保存",InfoBarSeverity.Success);}
            }
            catch(Exception error){SetFeedback("选择工作目录失败："+error.Message,InfoBarSeverity.Error);}
            finally{chooseDirectory.IsEnabled=true;}
        };
        codex.Children.Add(SettingRow("工作目录","Codex 在所选目录中处理截图。",chooseDirectory,codexDirectory));
        codex.Children.Add(SettingRow("Codex CLI","留空自动查找；填写时使用 codex.exe 的完整路径。",codexExecutable));
        var checkCodex=ActionButton("检测 provider","\uE9CE");
        checkCodex.Click+=async(_,_)=>
        {
            checkCodex.IsEnabled=false;SetFeedback("正在检测 Codex provider…");
            try{SetFeedback(await app.Codex.CheckProviderAsync(CancellationToken.None));}
            catch(Exception error){SetFeedback("Codex provider 检测失败："+error.Message,InfoBarSeverity.Error);}
            finally{checkCodex.IsEnabled=true;}
        };
        codex.Children.Add(SettingRow("连接检测","检查当前 CLI、登录状态与图片模型是否就绪。",checkCodex));

        var chatgpt=Page("chatgpt","ChatGPT","选择官网发送所用的浏览器并查看扩展连接。","\uE8F2");
        chatgpt.Children.Add(SettingRow("ChatGPT 浏览器","发送截图到 ChatGPT 官网时使用。",browser));
        var checkBridge=ActionButton("检测连接","\uE9CE");
        checkBridge.Click+=(_,_)=>SetFeedback(app.Bridge?.Status??"扩展未连接");
        chatgpt.Children.Add(SettingRow("浏览器扩展","扩展会自动连接，程序重启后自动重连。",checkBridge));
        chatgpt.Children.Add(SectionTitle("安装扩展"));
        chatgpt.Children.Add(Body("打开 edge://extensions 或 chrome://extensions，启用开发者模式并加载发布目录的 extension 文件夹。复制扩展 ID，按 README 运行注册脚本。"));

        var about=Page("about","关于","轻量的 Windows 截图与 AI 工具。","\uE946");
        about.Children.Add(SectionTitle("AI ScreenShot"));
        about.Children.Add(Body("截图、查看与发送，在桌面上完成。使用原生 WinUI 控件，外观跟随 Windows 浅色与深色主题。"));
        about.Children.Add(Body("C# · .NET 10 · WinUI 3 · Windows App SDK"));
        var folder=ActionButton("打开程序目录","\uE8B7");
        folder.Click+=(_,_)=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",AppContext.BaseDirectory){UseShellExecute=true});
        about.Children.Add(SettingRow("程序目录","查看程序、扩展和发布文件。",folder));
    }

    private void BuildShell()
    {
        UiDesign.Label(navigation,"设置分类");
        navigation.PaneDisplayMode=NavigationViewPaneDisplayMode.Auto;
        navigation.IsPaneOpen=true;
        navigation.IsBackButtonVisible=NavigationViewBackButtonVisible.Collapsed;
        navigation.IsSettingsVisible=false;
        navigation.OpenPaneLength=UiDesign.Number("SS.Settings.NavigationWidth");
        navigation.CompactPaneLength=UiDesign.Number("SS.BallSize");
        navigation.ExpandedModeThresholdWidth=UiDesign.Number("SS.Settings.ExpandedBreakpoint");
        navigation.CompactModeThresholdWidth=UiDesign.Number("SS.Settings.CompactBreakpoint");
        navigation.PaneTitle="设置";
        navigation.SelectionChanged+=(_,args)=>
        {
            if(args.SelectedItem is NavigationViewItem{Tag:string tag}&&pages.TryGetValue(tag,out var page))
            {
                pageTitle.Text=page.Title;pageDescription.Text=page.Description;
                if(saveButton!=null)
                {string label=tag=="appearance"?"保存外观":"保存设置";saveButton.Content=ActionContent(label,"\uE74E");UiDesign.Label(saveButton,label);}
                pageScroller.Content=page.Content;pageScroller.ChangeView(null,0,null,true);
                if(navigation.IsLoaded&&navigation.DisplayMode==NavigationViewDisplayMode.Minimal)navigation.IsPaneOpen=false;
            }
        };

        var content=new Grid{Padding=UiDesign.Thickness("SS.Spacing.XL"),RowSpacing=UiDesign.Number("SS.Spacing.L")};
        content.RowDefinitions.Add(new(){Height=GridLength.Auto});
        content.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        content.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var heading=new StackPanel{Spacing=UiDesign.Number("SS.Spacing.XS"),MaxWidth=UiDesign.Number("SS.Settings.ContentMaxWidth"),HorizontalAlignment=HorizontalAlignment.Stretch};
        UiDesign.Apply(pageTitle,"SS.TitleTextStyle");UiDesign.Apply(pageDescription,"SS.CaptionTextStyle");
        heading.Children.Add(pageTitle);heading.Children.Add(pageDescription);content.Children.Add(heading);
        Grid.SetRow(pageScroller,1);content.Children.Add(pageScroller);

        var footer=new Grid{RowSpacing=UiDesign.Number("SS.Spacing.S"),MaxWidth=UiDesign.Number("SS.Settings.ContentMaxWidth")};
        footer.RowDefinitions.Add(new(){Height=GridLength.Auto});footer.RowDefinitions.Add(new(){Height=GridLength.Auto});
        footer.Children.Add(feedback);
        var save=ActionButton("保存设置","\uE74E");saveButton=save;UiDesign.Apply(save,"SS.PrimaryButtonStyle");save.HorizontalAlignment=HorizontalAlignment.Right;
        save.Click+=(_,_)=>{if(navigation.SelectedItem is NavigationViewItem{Tag:"appearance"})SaveAppearance();else SaveSettings();};Grid.SetRow(save,1);footer.Children.Add(save);
        Grid.SetRow(footer,2);content.Children.Add(footer);navigation.Content=content;

        var root=new Border{RequestedTheme=ElementTheme.Default,Child=navigation};UiDesign.Apply(root,"SS.WindowSurfaceStyle");Content=root;
        navigation.SelectedItem=navigation.MenuItems[0];
    }

    private StackPanel Page(string tag,string title,string description,string glyph)
    {
        var content=new StackPanel{Spacing=UiDesign.Number("SS.Spacing.M"),MaxWidth=UiDesign.Number("SS.Settings.ContentMaxWidth"),HorizontalAlignment=HorizontalAlignment.Stretch};
        pages.Add(tag,(title,description,content));
        navigation.MenuItems.Add(new NavigationViewItem{Tag=tag,Content=title,Icon=UiDesign.Icon(glyph)});
        return content;
    }

    private static Border SettingRow(string title,string description,FrameworkElement control,FrameworkElement? detail=null)
    {
        var row=new Grid{RowSpacing=UiDesign.Number("SS.Spacing.S")};
        var surface=new Border{Child=row};UiDesign.Apply(surface,"SS.SettingRowStyle");
        row.RowDefinitions.Add(new(){Height=GridLength.Auto});row.RowDefinitions.Add(new(){Height=GridLength.Auto});
        row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var labels=new StackPanel{Spacing=UiDesign.Number("SS.Spacing.XS"),VerticalAlignment=VerticalAlignment.Center};
        var label=new TextBlock{Text=title,TextWrapping=TextWrapping.Wrap};UiDesign.Apply(label,"SS.BodyTextStyle");
        labels.Children.Add(label);
        var helper=new TextBlock{Text=description,TextWrapping=TextWrapping.Wrap};UiDesign.Apply(helper,"SS.CaptionTextStyle");labels.Children.Add(helper);
        if(detail!=null)labels.Children.Add(detail);
        row.Children.Add(labels);row.Children.Add(control);control.VerticalAlignment=VerticalAlignment.Center;UiDesign.Label(control,title);AutomationProperties.SetHelpText(control,description);
        bool bounded=control is TextBox or ComboBox or Grid or StackPanel;
        void UpdateLayout(double width)
        {
            bool stacked=width<UiDesign.Number("SS.Settings.RowStackBreakpoint");
            row.ColumnDefinitions[1].Width=stacked?new GridLength(0):GridLength.Auto;
            row.ColumnSpacing=stacked?0:UiDesign.Number("SS.Spacing.XL");
            Grid.SetColumn(control,stacked?0:1);Grid.SetRow(control,stacked?1:0);
            control.HorizontalAlignment=stacked?HorizontalAlignment.Stretch:HorizontalAlignment.Right;
            control.Width=stacked||!bounded?double.NaN:UiDesign.Number("SS.Settings.ControlWidth");
        }
        row.SizeChanged+=(_,args)=>UpdateLayout(args.NewSize.Width);UpdateLayout(0);
        return surface;
    }

    private static Button ActionButton(string text,string glyph)
    {
        var button=new Button();UiDesign.Apply(button,"SS.QuietButtonStyle");UiDesign.Label(button,text);
        button.Content=ActionContent(text,glyph);return button;
    }

    private static StackPanel ActionContent(string text,string glyph)
    {
        var content=new StackPanel{Orientation=Orientation.Horizontal,Spacing=UiDesign.Number("SS.Spacing.S")};
        content.Children.Add(UiDesign.Icon(glyph));content.Children.Add(new TextBlock{Text=text});return content;
    }

    private static TextBlock Caption(string text)
    {var label=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap};UiDesign.Apply(label,"SS.CaptionTextStyle");return label;}

    private void AddSurfaceEditor(StackPanel page,string id,string title,string description,SurfaceAppearance value)
    {
        var editor=new SurfaceEditor(title,value,()=>app.ApplyAppearance(ReadAppearance()));
        surfaceEditors.Add(id,editor);page.Children.Add(SettingRow(title,description,editor.Content));
    }

    private PanelAppearance ReadAppearance()=>new()
    {Base=surfaceEditors["base"].Value,Toolbar=surfaceEditors["toolbar"].Value,Messages=surfaceEditors["messages"].Value,
        HideWindowBase=hideWindowBase.IsChecked==true,EnableAnimations=enableAnimations.IsOn,
        Theme=ThemeChoices[Math.Clamp(appTheme.SelectedIndex,0,ThemeChoices.Length-1)],AccentMode=(AccentColorMode)Math.Max(0,accentMode.SelectedIndex),CustomAccentColor=AppThemeController.ToHex(accentPicker.Color),ShowBorders=showBorders.IsOn,BorderThickness=borderThickness.Value};

    private void UpdateAccentPreview()
    {
        bool custom=accentMode.SelectedIndex==(int)AccentColorMode.Custom;
        accentButton.IsEnabled=custom;
        var color=custom?accentPicker.Color:app.Themes.AccentColor;
        accentSwatch.Background=new SolidColorBrush(color);accentHex.Text=AppThemeController.ToHex(color);
        ToolTipService.SetToolTip(accentButton,custom?"选择自定义主题颜色":"当前 Windows 主题颜色；选择自定义颜色后可编辑");
    }

    private void SaveAppearance()
    {
        var previous=app.Settings.PanelAppearance;
        try
        {
            app.Settings.PanelAppearance=ReadAppearance();app.Settings.Save();
            app.ApplyAppearance(app.Settings.PanelAppearance);SetFeedback("外观已保存",InfoBarSeverity.Success);
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException)
        {app.Settings.PanelAppearance=previous;SetFeedback("保存外观失败："+error.Message,InfoBarSeverity.Error);}
    }

    private sealed class SurfaceEditor
    {
        private readonly ComboBox material=new(){ItemsSource=new[]{"Acrylic 毛玻璃","轻透 Acrylic","透明色层 · 无额外模糊"},HorizontalAlignment=HorizontalAlignment.Stretch};
        private readonly Slider opacity=new(){Minimum=0,Maximum=100,StepFrequency=1,SmallChange=1,LargeChange=10,HorizontalAlignment=HorizontalAlignment.Stretch};
        private readonly TextBlock percentage=Caption("");
        private bool changing;
        internal StackPanel Content {get;}=new(){Spacing=UiDesign.Number("SS.Spacing.XS")};
        internal SurfaceAppearance Value=>new((SurfaceMaterial)material.SelectedIndex,opacity.Value/100d);
        internal void SetEnabled(bool enabled){material.IsEnabled=enabled;opacity.IsEnabled=enabled;percentage.Opacity=enabled?1:0.5;}

        internal SurfaceEditor(string title,SurfaceAppearance value,Action changed)
        {
            material.MinHeight=UiDesign.Number("SS.ControlHeight");material.CornerRadius=UiDesign.Corner("SS.Corner.Control");
            UiDesign.Label(material,title+"材质");UiDesign.Label(opacity,title+"底色不透明度");
            AutomationProperties.SetHelpText(opacity,"0 更通透，100 为纯色；仅调整背景，不改变文字和按钮。");
            var sliderRow=new Grid{ColumnSpacing=UiDesign.Number("SS.Spacing.S")};
            sliderRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});sliderRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            percentage.MinWidth=UiDesign.Number("SS.ControlHeight");percentage.TextAlignment=TextAlignment.Right;percentage.VerticalAlignment=VerticalAlignment.Center;
            sliderRow.Children.Add(opacity);Grid.SetColumn(percentage,1);sliderRow.Children.Add(percentage);
            Content.Children.Add(material);Content.Children.Add(sliderRow);Set(value);
            material.SelectionChanged+=(_,_)=>{if(!changing)changed();};
            opacity.ValueChanged+=(_,_)=>{percentage.Text=$"{opacity.Value:0}%";if(!changing)changed();};
        }

        internal void Set(SurfaceAppearance value)
        {changing=true;value=value.Normalize();material.SelectedIndex=(int)value.Material;opacity.Value=Math.Round(value.Opacity*100);percentage.Text=$"{opacity.Value:0}%";changing=false;}
    }

    private static TextBlock SectionTitle(string text)
    {var label=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap};UiDesign.Apply(label,"SS.SectionTextStyle");return label;}

    private static TextBlock Body(string text)
    {var label=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap};UiDesign.Apply(label,"SS.BodyTextStyle");return label;}

    private void SetFeedback(string message,InfoBarSeverity severity=InfoBarSeverity.Informational)
    {feedback.Severity=severity;feedback.Message=message;feedback.IsOpen=true;}

    private void SaveSettings()
    {
        var settings=app.Settings;
        string letter=key.Text.ToUpperInvariant();uint modifiers=(ctrl.IsChecked==true?2u:0)|(alt.IsChecked==true?1u:0)|(shift.IsChecked==true?4u:0)|(win.IsChecked==true?8u:0);
        if(letter.Length!=1||!char.IsAsciiLetterOrDigit(letter[0])||modifiers==0)
        {SetFeedback("请输入一个英文字母或数字，并选择至少一个修饰键",InfoBarSeverity.Warning);return;}
        string executable=codexExecutable.Text.Trim();
        if(executable.Length>0)
        {try{_=CodexProviderExecutable.Resolve(executable);}catch(Exception error){SetFeedback(error.Message,InfoBarSeverity.Error);return;}}
        if(!changeHotkey(modifiers,letter[0]))
        {changeHotkey(settings.HotkeyModifiers,settings.HotkeyKey);SetFeedback("快捷键已被占用，原快捷键已恢复",InfoBarSeverity.Warning);return;}
        settings.HotkeyModifiers=modifiers;settings.HotkeyKey=letter[0];settings.Browser=browser.SelectedItem?.ToString()??"Edge";
        settings.CodexExecutable=executable.Length==0?null:executable;settings.DefaultPrompt=DefaultPromptEditor.Text;settings.Save();
        DefaultPromptEditor.Text=settings.DefaultPrompt;SetFeedback("设置已保存 · "+(app.Bridge?.Status??"扩展未连接"),InfoBarSeverity.Success);
    }

    private void ResizeToWorkArea()
    {
        nint handle=WinRT.Interop.WindowNative.GetWindowHandle(this);
        double scale=Math.Max(1,Native.GetDpiForWindow(handle))/96d;
        var workArea=DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Primary).WorkArea;
        int margin=(int)Math.Round(UiDesign.Number("SS.Spacing.XXL")*scale);
        int width=Math.Min((int)Math.Round(UiDesign.Number("SS.Settings.WindowWidth")*scale),Math.Max(1,workArea.Width-margin));
        int height=Math.Min((int)Math.Round(UiDesign.Number("SS.Settings.WindowHeight")*scale),Math.Max(1,workArea.Height-margin));
        AppWindow.Resize(new(width,height));
        AppWindow.Move(new(workArea.X+(workArea.Width-width)/2,workArea.Y+(workArea.Height-height)/2));
    }
}
