using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ScreenShot.App;

/// <summary>Shared design resources for native controls created in code.</summary>
internal static class UiDesign
{
    internal static void Apply(FrameworkElement element,string styleKey)
        =>element.Style=(Style)Application.Current.Resources[styleKey];

    internal static double Number(string token)=>Convert.ToDouble(Application.Current.Resources[token]);
    internal static CornerRadius Corner(string token)=>(CornerRadius)Application.Current.Resources[token];
    internal static Thickness Thickness(string token)=>Application.Current.Resources[token] is Thickness value?value:new Thickness(Number(token));
    internal static FontIcon Icon(string glyph,double size=16)
        =>new(){Glyph=glyph,FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=size,IsTextScaleFactorEnabled=false};

    internal static void Label(FrameworkElement element,string text)
    {
        AutomationProperties.SetName(element,text);
        ToolTipService.SetToolTip(element,text);
    }
}
