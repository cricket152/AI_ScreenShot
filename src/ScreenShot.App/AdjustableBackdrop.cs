using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using ScreenShot.Core;
using Windows.UI.ViewManagement;
using WinRT;

namespace ScreenShot.App;

// Each native target owns its controller; changing a surface never fades its content.
internal sealed partial class AdjustableBackdrop : SystemBackdrop,IDisposable
{
    private readonly bool elementTarget;
    private readonly Border? tintSurface;
    private readonly SystemBackdropConfiguration configuration=new(){IsInputActive=true};
    private readonly Border colorSource=(Border)XamlReader.Load("<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Background=\"{ThemeResource SolidBackgroundFillColorBaseBrush}\" />");
    private readonly UISettings visualSettings=new();
    private SurfaceAppearance appearance;
    private ICompositionSupportsSystemBackdrop? target;
    private XamlRoot? root;
    private FrameworkElement? themeRoot;
    private Microsoft.UI.Dispatching.DispatcherQueue? dispatcher;
    private DesktopAcrylicController? acrylic;
    private Windows.UI.Composition.Compositor? tintCompositor;
    private Windows.UI.Composition.CompositionColorBrush? tint;
    private bool disposed;
    private Windows.UI.Color accentColor;
    private Windows.UI.Color effectiveTint;
    private double accentMix;

    internal AdjustableBackdrop(SurfaceAppearance appearance,Border? tintSurface=null)
    {this.appearance=appearance.Normalize();this.tintSurface=tintSurface;elementTarget=tintSurface!=null;visualSettings.AdvancedEffectsEnabledChanged+=EffectsChanged;}

    internal bool IsAlwaysActive=>configuration.IsInputActive;
    internal SurfaceAppearance Appearance=>appearance;
    internal bool IsConnected=>target!=null;
    internal bool UsesAcrylic=>acrylic!=null;
    internal SystemBackdropTheme Theme=>configuration.Theme;
    internal SystemBackdropState? State=>acrylic?.State;
    internal Windows.UI.Color TintColor=>acrylic?.TintColor??effectiveTint;
    internal bool TargetHasInputFocus=>target!=null&&root!=null&&GetDefaultSystemBackdropConfiguration(target,root).IsInputActive;

    internal void Update(SurfaceAppearance value)
    {
        if(disposed)return;
        appearance=value.Normalize();Apply();
    }

    internal void SetAccent(Windows.UI.Color color,double mix)
    {if(disposed||accentColor==color&&accentMix==mix)return;accentColor=color;accentMix=Math.Clamp(mix,0,1);Apply();}

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget,XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget,xamlRoot);
        target=connectedTarget;root=xamlRoot;themeRoot=xamlRoot.Content as FrameworkElement;dispatcher=themeRoot?.DispatcherQueue;
        if(themeRoot!=null)themeRoot.ActualThemeChanged+=ThemeChanged;
        Apply();
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop connectedTarget,XamlRoot xamlRoot)
    {
        if(disposed||target==null)return;
        base.OnDefaultSystemBackdropConfigurationChanged(connectedTarget,xamlRoot);
        root=xamlRoot;Apply();
    }

    private void Apply()
    {
        if(disposed||target==null||root==null)return;
        var defaults=GetDefaultSystemBackdropConfiguration(target,root);
        configuration.Theme=root.Content is FrameworkElement content?
            (content.ActualTheme==ElementTheme.Dark?SystemBackdropTheme.Dark:SystemBackdropTheme.Light):defaults.Theme;
        configuration.IsHighContrast=defaults.IsHighContrast;
        configuration.HighContrastBackgroundColor=defaults.HighContrastBackgroundColor;
        // A separate configuration retains material on deactivation without activating the HWND.
        configuration.IsInputActive=true;
        colorSource.RequestedTheme=configuration.Theme==SystemBackdropTheme.Dark?ElementTheme.Dark:ElementTheme.Light;
        var themeColor=((SolidColorBrush)colorSource.Background).Color;
        var color=configuration.IsHighContrast?configuration.HighContrastBackgroundColor??themeColor:themeColor;
        if(!configuration.IsHighContrast)color=Windows.UI.Color.FromArgb(255,
            (byte)Math.Round(color.R+(accentColor.R-color.R)*accentMix),(byte)Math.Round(color.G+(accentColor.G-color.G)*accentMix),(byte)Math.Round(color.B+(accentColor.B-color.B)*accentMix));
        effectiveTint=color;
        if(appearance.Material!=SurfaceMaterial.Transparent&&DesktopAcrylicController.IsSupported())
        {
            if(acrylic==null)
            {
                ReleaseTint();
                acrylic=new();acrylic.SetSystemBackdropConfiguration(configuration);
                if(!acrylic.AddSystemBackdropTarget(target))
                {ReleaseAcrylic();ApplyTint(color,1);return;}
            }
            acrylic.Kind=appearance.Material==SurfaceMaterial.AcrylicThin?DesktopAcrylicKind.Thin:DesktopAcrylicKind.Base;
            acrylic.TintColor=color;acrylic.FallbackColor=color;
            acrylic.TintOpacity=(float)appearance.Opacity;
            acrylic.LuminosityOpacity=appearance.Material==SurfaceMaterial.Acrylic?(float)(appearance.Opacity*0.18):0;
        }
        else
        {
            ReleaseAcrylic();
            ApplyTint(color,appearance.Material==SurfaceMaterial.Transparent&&!configuration.IsHighContrast&&visualSettings.AdvancedEffectsEnabled?appearance.Opacity:1);
        }
    }

    private void EffectsChanged(UISettings sender,object args)
    {dispatcher?.TryEnqueue(Apply);}
    private void ThemeChanged(FrameworkElement sender,object args)=>Apply();

    private void ApplyTint(Windows.UI.Color color,double opacity)
    {
        if(target==null)return;
        color.A=(byte)Math.Round(opacity*255);
        // External backdrop links accept system material brushes only. A native tint
        // sibling supplies the no-blur region without assigning a foreign compositor brush.
        if(tintSurface!=null){tintSurface.Background=new SolidColorBrush(color);return;}
        tintCompositor??=new();tint??=tintCompositor.CreateColorBrush();
        tint.Color=color;target.SystemBackdrop=tint;
    }

    private void ReleaseAcrylic()
    {if(acrylic==null)return;acrylic.RemoveAllSystemBackdropTargets();acrylic.Dispose();acrylic=null;}
    private void ReleaseTint()
    {
        if(tintSurface!=null)tintSurface.Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        if(tint==null)return;if(target!=null)target.SystemBackdrop=null;tint.Dispose();tint=null;tintCompositor?.Dispose();tintCompositor=null;
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        if(themeRoot!=null)themeRoot.ActualThemeChanged-=ThemeChanged;
        themeRoot=null;ReleaseAcrylic();ReleaseTint();target=null;root=null;dispatcher=null;
        base.OnTargetDisconnected(disconnectedTarget);
        // Element backdrop links are thread-affine; release them on the owning UI thread.
        if(elementTarget)disconnectedTarget.As<IDisposable>().Dispose();
    }

    public void Dispose()
    {if(disposed)return;disposed=true;visualSettings.AdvancedEffectsEnabledChanged-=EffectsChanged;if(themeRoot!=null)themeRoot.ActualThemeChanged-=ThemeChanged;ReleaseAcrylic();ReleaseTint();}
}
