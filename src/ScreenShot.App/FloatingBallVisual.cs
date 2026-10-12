using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace ScreenShot.App;

// The entrance has its own artwork and brushes; tray/settings application icons stay unchanged.
internal sealed class FloatingBallVisual
{
    private readonly Button button;
    private readonly PathIcon icon;
    private readonly SolidColorBrush foreground=new(Microsoft.UI.Colors.White);
    private readonly LinearGradientBrush normal=Gradient(),hover=Gradient(),pressed=Gradient(),active=Gradient(),disabled=Gradient();
    private readonly RadialGradientBrush gloss=new(){Center=new(0.22,0.16),GradientOrigin=new(0.22,0.16),RadiusX=0.88,RadiusY=0.88};
    private readonly LinearGradientBrush edge=new(){StartPoint=new(0,0),EndPoint=new(1,1)};
    private readonly Ellipse highlight,rim;
    internal bool Active {get;set;}
    internal PathIcon Icon=>icon;
    internal Ellipse Highlight=>highlight;
    internal Ellipse Rim=>rim;
    internal Color ShadowColor {get;private set;}

    internal FloatingBallVisual(Button button,double scale=1)
    {
        this.button=button;
        // Tight 32px geometry retains the approved capture corners and central AI sparkle.
        icon=(PathIcon)XamlReader.Load("""
            <PathIcon xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Width="32" Height="32"
                Data="M 2,12 A 2,2 0 0 1 0,10 L 0,6 A 6,6 0 0 1 6,0 L 10,0 A 2,2 0 0 1 10,4 L 6,4 A 2,2 0 0 0 4,6 L 4,10 A 2,2 0 0 1 2,12 Z
                M 22,0 L 26,0 A 6,6 0 0 1 32,6 L 32,10 A 2,2 0 0 1 28,10 L 28,6 A 2,2 0 0 0 26,4 L 22,4 A 2,2 0 0 1 22,0 Z
                M 32,22 L 32,26 A 6,6 0 0 1 26,32 L 22,32 A 2,2 0 0 1 22,28 L 26,28 A 2,2 0 0 0 28,26 L 28,22 A 2,2 0 0 1 32,22 Z
                M 10,32 L 6,32 A 6,6 0 0 1 0,26 L 0,22 A 2,2 0 0 1 4,22 L 4,26 A 2,2 0 0 0 6,28 L 10,28 A 2,2 0 0 1 10,32 Z
                M 15.3,9.5 C 15.5,8.6 16.5,8.6 16.7,9.5 C 18,13.8 18.2,14 22.5,15.3 C 23.4,15.5 23.4,16.5 22.5,16.7
                C 18.2,18 18,18.2 16.7,22.5 C 16.5,23.4 15.5,23.4 15.3,22.5 C 14,18.2 13.8,18 9.5,16.7
                C 8.6,16.5 8.6,15.5 9.5,15.3 C 13.8,14 14,13.8 15.3,9.5 Z" />
            """);
        icon.Foreground=foreground;icon.IsHitTestVisible=false;
        gloss.GradientStops.Add(new(){Offset=0,Color=Color.FromArgb(26,255,255,255)});
        gloss.GradientStops.Add(new(){Offset=0.5,Color=Color.FromArgb(8,255,255,255)});
        gloss.GradientStops.Add(new(){Offset=1,Color=Color.FromArgb(0,255,255,255)});
        edge.GradientStops.Add(new(){Offset=0});edge.GradientStops.Add(new(){Offset=0.45});edge.GradientStops.Add(new(){Offset=1});
        highlight=new Ellipse{Width=48*scale,Height=48*scale,Fill=gloss,IsHitTestVisible=false};
        rim=new Ellipse{Width=48*scale,Height=48*scale,Stroke=edge,StrokeThickness=0.65*scale,IsHitTestVisible=false};
        var content=new Grid{Width=48*scale,Height=48*scale};content.Children.Add(highlight);content.Children.Add(rim);
        content.Children.Add(scale==1?icon:new Viewbox{Width=32*scale,Height=32*scale,Child=icon,IsHitTestVisible=false});button.Content=content;
        button.Resources["ButtonBackgroundPointerOver"]=hover;
        button.Resources["ButtonBackgroundPressed"]=pressed;
        button.Resources["ButtonBackgroundDisabled"]=disabled;
        foreach(string key in new[]{"ButtonForegroundPointerOver","ButtonForegroundPressed","ButtonForegroundDisabled"})button.Resources[key]=foreground;
        foreach(string key in new[]{"ButtonBorderBrushPointerOver","ButtonBorderBrushPressed","ButtonBorderBrushDisabled"})
            button.Resources[key]=new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    internal void Update(AppThemeController themes,FrameworkElement root)
    {
        bool highContrast=themes.IsHighContrast(root);
        if(highContrast)
        {
            PaintSystem(normal,themes.SystemBrush("ButtonBackground",root.ActualTheme).Color);
            PaintSystem(hover,themes.SystemBrush("ButtonBackgroundPointerOver",root.ActualTheme).Color);
            PaintSystem(pressed,themes.SystemBrush("ButtonBackgroundPressed",root.ActualTheme).Color);
            PaintSystem(active,themes.SystemBrush("ButtonBackground",root.ActualTheme).Color);
            PaintSystem(disabled,themes.SystemBrush("ButtonBackgroundDisabled",root.ActualTheme).Color);
            button.Foreground=foreground;button.Background=Active?active:normal;
            highlight.Visibility=rim.Visibility=Visibility.Collapsed;ShadowColor=Microsoft.UI.Colors.Black;return;
        }
        Color fill=AppThemeController.Mix(themes.AccentColor,Microsoft.UI.Colors.Black,root.ActualTheme==ElementTheme.Dark?0.08:0);
        Color body=Readable(fill,255,0,4.1);
        Paint(normal,body);
        Paint(hover,AppThemeController.Mix(body,Microsoft.UI.Colors.White,0.045));
        Paint(pressed,AppThemeController.Mix(body,Microsoft.UI.Colors.Black,0.10));
        Paint(active,AppThemeController.Mix(body,Microsoft.UI.Colors.White,0.025));
        Paint(disabled,AppThemeController.Mix(body,themes.SystemBrush("SolidBackgroundFillColorBaseBrush",root.ActualTheme).Color,0.35));
        button.Foreground=foreground;button.Background=Active?active:normal;
        highlight.Visibility=rim.Visibility=Visibility.Visible;
        highlight.Opacity=button.IsEnabled?1:0.35;rim.Opacity=button.IsEnabled?1:0.5;
        Color light=AppThemeController.Mix(body,Microsoft.UI.Colors.White,0.75);
        edge.GradientStops[0].Color=Color.FromArgb(210,light.R,light.G,light.B);
        edge.GradientStops[1].Color=Color.FromArgb(30,body.R,body.G,body.B);
        edge.GradientStops[2].Color=Color.FromArgb(140,light.R,light.G,light.B);
        ShadowColor=AppThemeController.Mix(themes.AccentColor,Microsoft.UI.Colors.Black,0.72);
    }

    private static LinearGradientBrush Gradient()
    {
        var brush=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(0.8,1)};
        brush.GradientStops.Add(new(){Offset=0});brush.GradientStops.Add(new(){Offset=0.48});brush.GradientStops.Add(new(){Offset=1});return brush;
    }
    private static void Paint(LinearGradientBrush brush,Color body)
    {
        const byte alpha=247;const double glossOpacity=26/255d;
        brush.GradientStops[0].Color=Readable(AppThemeController.Mix(body,Microsoft.UI.Colors.White,0.24),alpha,glossOpacity,3);
        brush.GradientStops[1].Color=Readable(body,alpha,glossOpacity,3);
        brush.GradientStops[2].Color=Readable(AppThemeController.Mix(body,Microsoft.UI.Colors.Black,0.20),alpha,glossOpacity,3);
    }
    private static void PaintSystem(LinearGradientBrush brush,Color color)
    {
        color=Readable(color,255,0,3);
        foreach(var stop in brush.GradientStops)stop.Color=color;
    }
    private static Color Readable(Color color,byte alpha,double glossOpacity,double minimum)
    {
        // White is the worst desktop background; include both glass alpha and the brightest gloss.
        for(int step=0;step<80;step++)
        {
            var onWhite=AppThemeController.Mix(color,Microsoft.UI.Colors.White,1-alpha/255d);
            var lit=AppThemeController.Mix(onWhite,Microsoft.UI.Colors.White,glossOpacity);
            if(AppThemeController.Contrast(lit,Microsoft.UI.Colors.White)>=minimum)break;
            color=AppThemeController.Mix(color,Microsoft.UI.Colors.Black,0.035);
        }
        color.A=alpha;return color;
    }
}
