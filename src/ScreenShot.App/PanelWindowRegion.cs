using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ScreenShot.App;

// Clip the HWND itself: a transparent backdrop does not remove the native host's rectangular frame.
internal sealed class PanelWindowRegion(nint handle,Border root,Border toolbar,Border messages)
{
    private sealed record Shape(int Left,int Top,int Right,int Bottom,int Diameter);
    private Shape[] previous=[];
    private bool applied;

    internal void Update(bool hideBase,double insetProgress)
    {
        if(root.ActualWidth<=0||!Native.GetWindowRect(handle,out var window)||!Native.GetClientRect(handle,out var client))return;
        var origin=new Native.Point();if(!Native.ClientToScreen(handle,ref origin))return;
        double scale=Math.Max(96u,Native.GetDpiForWindow(handle))/96d;
        int offsetX=origin.X-window.Left,offsetY=origin.Y-window.Top;
        var surfaces=new List<Shape>();
        foreach(var surface in new[]{toolbar,messages})
        {
            if(surface.Visibility!=Visibility.Visible||surface.Height<0.5||surface.ActualWidth<=0||surface.ActualHeight<=0)continue;
            var position=surface.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
            double radius=Math.Min(surface.CornerRadius.TopLeft,Math.Min(surface.ActualWidth,surface.ActualHeight)/2);
            surfaces.Add(new((int)Math.Floor(position.X*scale)+offsetX,(int)Math.Floor(position.Y*scale)+offsetY,
                (int)Math.Ceiling((position.X+surface.ActualWidth)*scale)+offsetX,
                (int)Math.Ceiling((position.Y+surface.ActualHeight)*scale)+offsetY,(int)Math.Round(radius*2*scale)));
        }
        Shape[] shapes=surfaces.ToArray();
        if(!hideBase&&surfaces.Count>0)
        {
            double padding=(UiDesign.Number("SS.Spacing.M")+root.BorderThickness.Right)*scale*Math.Clamp(insetProgress,0,1);
            int chromeX=window.Right-window.Left-client.Right+client.Left-offsetX;
            int chromeY=window.Bottom-window.Top-client.Bottom+client.Top-offsetY;
            shapes=[new(0,0,Math.Min(window.Right-window.Left,surfaces.Max(item=>item.Right)+(int)Math.Ceiling(padding)+chromeX),
                Math.Min(window.Bottom-window.Top,surfaces.Max(item=>item.Bottom)+(int)Math.Ceiling(padding)+chromeY),
                (int)Math.Round(root.CornerRadius.TopLeft*2*scale))];
        }
        if(applied&&shapes.SequenceEqual(previous))return;
        nint combined=Native.CreateRectRgn(0,0,0,0);
        if(combined==0)throw new Win32Exception(Marshal.GetLastWin32Error(),"无法创建悬浮窗口形状");
        try
        {
            foreach(var shape in shapes)
            {
                var region=Native.CreateRoundRectRgn(shape.Left,shape.Top,shape.Right+1,shape.Bottom+1,shape.Diameter,shape.Diameter);
                if(region==0)throw new Win32Exception(Marshal.GetLastWin32Error(),"无法创建面板圆角");
                try{if(Native.CombineRgn(combined,combined,region,2)==0)throw new InvalidOperationException("无法合并面板形状");}
                finally{Native.DeleteObject(region);}
            }
            var old=previous;bool wasApplied=applied;previous=shapes;applied=true;
            if(Native.SetWindowRgn(handle,combined,true)==0)
            {previous=old;applied=wasApplied;throw new Win32Exception(Marshal.GetLastWin32Error(),"无法更新悬浮窗口形状");}
            combined=0; // SetWindowRgn transfers ownership to Windows on success.
        }
        finally{if(combined!=0)Native.DeleteObject(combined);}
    }
}
