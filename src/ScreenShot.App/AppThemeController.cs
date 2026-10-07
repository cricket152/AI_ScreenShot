using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using ScreenShot.Core;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace ScreenShot.App;

internal sealed class AppThemeController : IDisposable
{
    private readonly UISettings systemSettings=new();
    private readonly DispatcherQueue dispatcher=DispatcherQueue.GetForCurrentThread();
    private readonly Dictionary<Window,Scope> windows=[];
    private readonly Dictionary<string,Border> probes=[];
    private bool disposed;
    internal PanelAppearance Appearance {get;private set;}
    internal Color AccentColor=>Appearance.AccentMode==AccentColorMode.Custom?ParseColor(Appearance.CustomAccentColor):systemSettings.GetColorValue(UIColorType.Accent);
    internal ElementTheme EffectiveTheme=>Appearance.Theme switch
    {
        AppThemeMode.Light=>ElementTheme.Light,AppThemeMode.Dark=>ElementTheme.Dark,
        _=>Contrast(systemSettings.GetColorValue(UIColorType.Foreground),Microsoft.UI.Colors.White)<Contrast(systemSettings.GetColorValue(UIColorType.Foreground),Microsoft.UI.Colors.Black)?ElementTheme.Dark:ElementTheme.Light
    };
    internal event Action? Changed;

    private static readonly Dictionary<string,string[]> BrushAliases=new()
    {
        ["AccentFillColorDefaultBrush"]=["AccentButtonBackground","NavigationViewSelectionIndicatorForeground","SliderThumbBackground","SliderTrackValueFill","ToggleSwitchFillOn","ToggleSwitchStrokeOn","CheckBoxCheckBackgroundFillChecked","CheckBoxCheckBackgroundStrokeChecked","CheckBoxCheckBackgroundFillIndeterminate","CheckBoxCheckBackgroundStrokeIndeterminate","ProgressRingForegroundThemeBrush","ProgressBarForeground"],
        ["AccentFillColorSecondaryBrush"]=["AccentButtonBackgroundPointerOver","SliderThumbBackgroundPointerOver","SliderTrackValueFillPointerOver","ToggleSwitchFillOnPointerOver","ToggleSwitchStrokeOnPointerOver","CheckBoxCheckBackgroundFillCheckedPointerOver","CheckBoxCheckBackgroundStrokeCheckedPointerOver"],
        ["AccentFillColorTertiaryBrush"]=["AccentButtonBackgroundPressed","SliderThumbBackgroundPressed","SliderTrackValueFillPressed","ToggleSwitchFillOnPressed","ToggleSwitchStrokeOnPressed","CheckBoxCheckBackgroundFillCheckedPressed","CheckBoxCheckBackgroundStrokeCheckedPressed"],
        ["TextOnAccentFillColorPrimaryBrush"]=["AccentButtonForeground","AccentButtonForegroundPointerOver","ToggleSwitchKnobFillOn","ToggleSwitchKnobFillOnPointerOver","ToggleSwitchKnobFillOnPressed","CheckBoxCheckGlyphForegroundChecked","CheckBoxCheckGlyphForegroundCheckedPointerOver","CheckBoxCheckGlyphForegroundIndeterminate"],
        ["TextOnAccentFillColorSecondaryBrush"]=["AccentButtonForegroundPressed","CheckBoxCheckGlyphForegroundCheckedPressed"],
        ["AccentTextFillColorPrimaryBrush"]=["HyperlinkForeground","HyperlinkButtonForeground","InfoBarHyperlinkButtonForeground"],
        ["AccentTextFillColorSecondaryBrush"]=["HyperlinkForegroundPointerOver","HyperlinkButtonForegroundPointerOver"],
        ["AccentTextFillColorTertiaryBrush"]=["HyperlinkForegroundPressed","HyperlinkButtonForegroundPressed"],
        ["AccentFillColorSelectedTextBackgroundBrush"]=["TextControlSelectionHighlightColor"],
        ["SystemControlHighlightAccentBrush"]=[]
    };

    internal AppThemeController(PanelAppearance appearance)
    {Appearance=appearance.Normalize();systemSettings.ColorValuesChanged+=SystemColorsChanged;}

    internal SolidColorBrush PanelBorderBrush(FrameworkElement root)
    {
        var scope=windows.Values.FirstOrDefault(item=>ReferenceEquals(item.Root,root));
        if(scope==null||scope.ThemeSettings.HighContrast)return SystemBrush("SystemControlHighlightAccentBrush",root.ActualTheme);
        return (SolidColorBrush)(root.ActualTheme==ElementTheme.Dark?scope.Dark:scope.Light)["SystemControlHighlightAccentBrush"];
    }
    internal bool IsHighContrast(FrameworkElement root)=>windows.Values.FirstOrDefault(scope=>ReferenceEquals(scope.Root,root))?.ThemeSettings.HighContrast==true;

    internal void Track(Window window)
    {
        if(disposed||windows.ContainsKey(window)||window.Content is not FrameworkElement root)return;
        window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory,"Assets","AppIcon.ico"));
        var scope=new Scope(root,window);windows.Add(window,scope);window.Closed+=WindowClosed;scope.ThemeSettings.Changed+=HighContrastChanged;
        root.Resources.MergedDictionaries.Add(scope.Resources);root.ActualThemeChanged+=RootThemeChanged;Apply(scope);RootThemeChanged(root,new object());
    }

    internal void Apply(PanelAppearance appearance)
    {
        if(disposed)return;
        Appearance=appearance.Normalize();foreach(var scope in windows.Values)Apply(scope);Changed?.Invoke();
    }

    private void Apply(Scope scope)
    {
        UpdatePalette(scope.Light,ElementTheme.Light);UpdatePalette(scope.Dark,ElementTheme.Dark);
        scope.Root.RequestedTheme=EffectiveTheme;
    }

    private void UpdatePalette(ResourceDictionary resources,ElementTheme theme)
    {
        Color accent=AccentColor;
        Color foreground=Contrast(accent,Microsoft.UI.Colors.White)>=Contrast(accent,Microsoft.UI.Colors.Black)?Microsoft.UI.Colors.White:Microsoft.UI.Colors.Black;
        Color hover=Mix(accent,foreground==Microsoft.UI.Colors.White?Microsoft.UI.Colors.Black:Microsoft.UI.Colors.White,0.1);
        Color pressed=Mix(accent,foreground==Microsoft.UI.Colors.White?Microsoft.UI.Colors.Black:Microsoft.UI.Colors.White,0.2);
        Color background=SystemBrush("SolidBackgroundFillColorBaseBrush",theme).Color;
        Color text=accent;Color textTarget=theme==ElementTheme.Dark?Microsoft.UI.Colors.White:Microsoft.UI.Colors.Black;
        for(int step=0;step<20&&Contrast(text,background)<4.5;step++)text=Mix(text,textTarget,0.12);
        foreach(var entry in BrushAliases)
        {
            Color color=entry.Key switch
            {
                "AccentFillColorSecondaryBrush"=>hover,"AccentFillColorTertiaryBrush"=>pressed,
                "TextOnAccentFillColorPrimaryBrush" or "TextOnAccentFillColorSecondaryBrush"=>foreground,
                "AccentTextFillColorPrimaryBrush" or "AccentTextFillColorSecondaryBrush" or "AccentTextFillColorTertiaryBrush"=>text,_=>accent
            };
            double opacity=1;
            if(Appearance.AccentMode==AccentColorMode.System)
            {
                var source=SystemBrush(entry.Key,theme);opacity=source.Opacity;
                color=entry.Key switch
                {
                    "AccentFillColorDefaultBrush" or "AccentFillColorSecondaryBrush" or "AccentFillColorTertiaryBrush"=>systemSettings.GetColorValue(theme==ElementTheme.Dark?UIColorType.AccentLight2:UIColorType.AccentDark1),
                    "AccentTextFillColorPrimaryBrush" or "AccentTextFillColorSecondaryBrush" or "AccentTextFillColorTertiaryBrush"=>systemSettings.GetColorValue(theme==ElementTheme.Dark?UIColorType.AccentLight3:UIColorType.AccentDark2),
                    "SystemControlHighlightAccentBrush" or "AccentFillColorSelectedTextBackgroundBrush"=>systemSettings.GetColorValue(UIColorType.Accent),_=>source.Color
                };
            }
            if(!resources.TryGetValue(entry.Key,out var value))
            {
                var brush=new SolidColorBrush(color){Opacity=opacity};resources[entry.Key]=brush;
                foreach(var alias in entry.Value)resources[alias]=brush;
            }
            else{var brush=(SolidColorBrush)value;brush.Color=color;brush.Opacity=opacity;}
        }
    }

    private SolidColorBrush SystemBrush(string key,ElementTheme theme)
    {
        if(!probes.TryGetValue(key,out var probe))
        {probe=(Border)XamlReader.Load($"<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Background=\"{{ThemeResource {key}}}\" />");probes.Add(key,probe);}
        probe.RequestedTheme=theme;return (SolidColorBrush)probe.Background;
    }

    private void RootThemeChanged(FrameworkElement sender,object args)
    {
        // Backdrop configurations follow the root theme themselves; this only updates native title bars.
        var window=windows.FirstOrDefault(item=>ReferenceEquals(item.Value.Root,sender)).Key;
        if(window==null)return;
        uint dark=sender.ActualTheme==ElementTheme.Dark?1u:0u;
        Native.DwmSetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(window),20,ref dark,sizeof(uint));
    }
    private void SystemColorsChanged(UISettings sender,object args)
    {dispatcher.TryEnqueue(()=>{if(!disposed){foreach(var scope in windows.Values)Apply(scope);Changed?.Invoke();}});}
    private void HighContrastChanged(Microsoft.UI.System.ThemeSettings sender,object args)=>SystemColorsChanged(systemSettings,args);
    private void WindowClosed(object sender,WindowEventArgs args)
    {if(sender is Window window&&windows.Remove(window,out var scope)){window.Closed-=WindowClosed;scope.Root.ActualThemeChanged-=RootThemeChanged;scope.ThemeSettings.Changed-=HighContrastChanged;}}

    internal static Color ParseColor(string hex)
    {hex=PanelAppearance.NormalizeAccentColor(hex);return Color.FromArgb(255,Convert.ToByte(hex.Substring(1,2),16),Convert.ToByte(hex.Substring(3,2),16),Convert.ToByte(hex.Substring(5,2),16));}
    internal static string ToHex(Color color)=>$"#{color.R:X2}{color.G:X2}{color.B:X2}";
    internal static double Contrast(Color first,Color second)
    {
        static double Luminance(Color color)
        {static double Linear(byte channel){double value=channel/255d;return value<=0.04045?value/12.92:Math.Pow((value+0.055)/1.055,2.4);}return Linear(color.R)*0.2126+Linear(color.G)*0.7152+Linear(color.B)*0.0722;}
        double a=Luminance(first),b=Luminance(second);return (Math.Max(a,b)+0.05)/(Math.Min(a,b)+0.05);
    }
    private static Color Mix(Color color,Color target,double amount)
        =>Color.FromArgb(255,(byte)Math.Round(color.R+(target.R-color.R)*amount),(byte)Math.Round(color.G+(target.G-color.G)*amount),(byte)Math.Round(color.B+(target.B-color.B)*amount));

    public void Dispose()
    {
        if(disposed)return;disposed=true;systemSettings.ColorValuesChanged-=SystemColorsChanged;
        foreach(var entry in windows){entry.Key.Closed-=WindowClosed;entry.Value.Root.ActualThemeChanged-=RootThemeChanged;entry.Value.ThemeSettings.Changed-=HighContrastChanged;}
        windows.Clear();probes.Clear();Changed=null;
    }
    private sealed class Scope
    {
        internal FrameworkElement Root {get;}
        internal Microsoft.UI.System.ThemeSettings ThemeSettings {get;}
        internal ResourceDictionary Resources {get;}=new();
        internal ResourceDictionary Light {get;}=new();
        internal ResourceDictionary Dark {get;}=new();
        internal Scope(FrameworkElement root,Window window)
        {Root=root;ThemeSettings=Microsoft.UI.System.ThemeSettings.CreateForWindowId(window.AppWindow.Id);Resources.ThemeDictionaries["Light"]=Light;Resources.ThemeDictionaries["Dark"]=Dark;Resources.ThemeDictionaries["HighContrast"]=new ResourceDictionary();}
    }
}
