using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ScreenShot.App;

// The HWND has no visual surface: only its XAML panes draw backgrounds.
internal sealed partial class TransparentPanelBackdrop(nint handle) : SystemBackdrop
{
    private Windows.UI.Composition.Compositor? compositor;
    private Windows.UI.Composition.CompositionColorBrush? brush;
    internal byte? Alpha=>brush?.Color.A;
    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target,XamlRoot root)
    {
        // This host always paints transparent. A queued theme callback can outlive
        // target disconnection when a panel closes, so it needs no base reconfiguration.
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target,XamlRoot root)
    {
        base.OnTargetConnected(target,root);
        compositor=new();brush=compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0,0,0,0));
        target.SystemBackdrop=brush;
        if(!EnableAlpha(handle))throw new InvalidOperationException("无法启用透明宿主的 DWM alpha 合成");
        var dc=Native.GetDC(handle);
        try{ClearBackground(handle,dc);}
        finally{if(dc!=0)Native.ReleaseDC(handle,dc);}
    }
    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        target.SystemBackdrop=null;brush?.Dispose();brush=null;compositor?.Dispose();compositor=null;
        base.OnTargetDisconnected(target);
    }
    internal static bool EnableAlpha(nint window)
    {
        var margins=new Native.Margins();
        // Enable client alpha, without any region or geometry restriction. Modern DWM
        // does not blur through this legacy API; the two material elements own blur.
        var alpha=new Native.BlurBehind{Flags=1,Enabled=1};
        return Native.DwmExtendFrameIntoClientArea(window,ref margins)>=0&&Native.DwmEnableBlurBehindWindow(window,ref alpha)>=0;
    }
    internal static bool ClearBackground(nint window,nint dc)
        =>dc!=0&&Native.GetClientRect(window,out var area)&&Native.FillRect(dc,ref area,Native.GetStockObject(4))!=0;
}
