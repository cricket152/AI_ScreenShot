using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ScreenShot.App;

// Measurements for shadow and input only; never changes the HWND's drawing region.
internal sealed class PanelGeometry(nint handle,FrameworkElement root,params Border[] panes)
{
    internal sealed record Shape(int Left,int Top,int Right,int Bottom,int Diameter,double Opacity=1);
    internal IReadOnlyList<Shape> Shapes {get;private set;}=[];

    internal void Update()
    {
        if(root.ActualWidth<=0||!Native.GetWindowRect(handle,out var window))return;
        var origin=new Native.Point();if(!Native.ClientToScreen(handle,ref origin))return;
        double scale=Math.Max(96u,Native.GetDpiForWindow(handle))/96d;
        Shapes=panes.Where(pane=>pane.Visibility==Visibility.Visible&&pane.ActualWidth>0&&pane.ActualHeight>0&&pane.Opacity>0)
            .Select(pane=>
            {
                var position=pane.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                double radius=Math.Min(pane.CornerRadius.TopLeft,Math.Min(pane.ActualWidth,pane.ActualHeight)/2);
                return new Shape((int)Math.Round(position.X*scale)+origin.X-window.Left,(int)Math.Round(position.Y*scale)+origin.Y-window.Top,
                    (int)Math.Round((position.X+pane.ActualWidth)*scale)+origin.X-window.Left,(int)Math.Round((position.Y+pane.ActualHeight)*scale)+origin.Y-window.Top,
                    (int)Math.Round(radius*2*scale),pane.Opacity);
            }).ToArray();
    }

    internal bool Contains(int x,int y)=>Shapes.Any(shape=>
    {
        if(x<shape.Left||x>=shape.Right||y<shape.Top||y>=shape.Bottom)return false;
        double radius=shape.Diameter/2d;
        double dx=x-Math.Clamp(x,shape.Left+radius,shape.Right-radius),dy=y-Math.Clamp(y,shape.Top+radius,shape.Bottom-radius);
        return dx*dx+dy*dy<=radius*radius;
    });
}
