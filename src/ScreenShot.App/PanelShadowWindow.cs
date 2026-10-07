using System.ComponentModel;
using System.Runtime.InteropServices;
using ScreenShot.Core;

namespace ScreenShot.App;

internal sealed class PanelShadowWindow : IDisposable
{
    private static readonly Native.WindowProc Procedure=(window,message,w,l)=>message==0x84?-1:message==0x21?3:Native.DefWindowProc(window,message,w,l);
    private readonly nint panel;
    internal nint Handle {get;}
    private PanelGeometry.Shape[] previous=[];
    private double previousSpread=-1,previousOpacity=-1;
    private bool disposed;
    internal long LastRenderTicks {get;private set;}
    internal long LastMaskTicks {get;private set;}
    internal byte[] LastPixels {get;private set;}=[];

    internal PanelShadowWindow(nint panel)
    {
        this.panel=panel;
        var type=new Native.WindowClass{Size=(uint)Marshal.SizeOf<Native.WindowClass>(),Proc=Procedure,Instance=Native.GetModuleHandle(null),ClassName="AIScreenShot.PanelShadow"};
        Native.RegisterClassEx(ref type);
        Handle=Native.CreateWindowEx(0x080800a0,type.ClassName,"",0x80000000,0,0,1,1,0,0,type.Instance,0);
        if(Handle==0)throw new Win32Exception(Marshal.GetLastWin32Error(),"无法创建面板阴影");
    }
    internal void Hide(){if(!disposed)Native.ShowWindow(Handle,0);}
    internal void Update(IReadOnlyList<PanelGeometry.Shape> shapes,PanelAppearance appearance,double scale,bool highContrast)
    {
        if(disposed)return;
        if(!appearance.ShowShadow||appearance.ShadowSpread<=0||appearance.ShadowOpacity<=0||highContrast||!Native.IsWindowVisible(panel)||shapes.Count==0){Hide();return;}
        if(!Native.GetWindowRect(panel,out var bounds))return;
        double spread=appearance.ShadowSpread*scale;
        int margin=(int)Math.Ceiling(spread*2)+2;
        int left=shapes.Min(shape=>shape.Left)-margin,top=shapes.Min(shape=>shape.Top)-margin;
        int width=shapes.Max(shape=>shape.Right)-left+margin,height=shapes.Max(shape=>shape.Bottom)-top+margin;
        bool render=previousSpread!=spread||previousOpacity!=appearance.ShadowOpacity||!previous.SequenceEqual(shapes);
        if(render)
        {
            long start=System.Diagnostics.Stopwatch.GetTimestamp();
            var maskShapes=shapes.Select(shape=>new ShadowShape(shape.Left-left,shape.Top-top,shape.Right-shape.Left,shape.Bottom-shape.Top,shape.Diameter/2d,shape.Opacity)).ToArray();
            var pixels=PanelShadowMask.Render(width,height,maskShapes,spread,appearance.ShadowOpacity);
            LastMaskTicks=System.Diagnostics.Stopwatch.GetTimestamp()-start;
            Upload(pixels,width,height,bounds.Left+left,bounds.Top+top);
            LastPixels=pixels;LastRenderTicks=System.Diagnostics.Stopwatch.GetTimestamp()-start;
            previous=shapes.ToArray();previousSpread=spread;previousOpacity=appearance.ShadowOpacity;
        }
        if(!Native.SetWindowPos(Handle,panel,bounds.Left+left,bounds.Top+top,width,height,0x10|0x40))throw new Win32Exception(Marshal.GetLastWin32Error(),"无法定位面板阴影");
    }
    private void Upload(byte[] pixels,int width,int height,int x,int y)
    {
        nint screen=Native.GetDC(0),memory=0,bitmap=0,old=0;
        try
        {
            memory=Native.CreateCompatibleDC(screen);
            var info=new Native.BitmapInfo{Size=(uint)Marshal.SizeOf<Native.BitmapInfo>(),Width=width,Height=-height,Planes=1,BitCount=32};
            bitmap=Native.CreateDIBSection(screen,ref info,0,out var bits,0,0);
            if(memory==0||bitmap==0||bits==0)throw new Win32Exception(Marshal.GetLastWin32Error(),"无法创建阴影缓冲区");
            Marshal.Copy(pixels,0,bits,pixels.Length);old=Native.SelectObject(memory,bitmap);
            var destination=new Native.Point{X=x,Y=y};var size=new Native.Size{Width=width,Height=height};var origin=new Native.Point();
            var blend=new Native.BlendFunction{ConstantAlpha=255,AlphaFormat=1};
            if(!Native.UpdateLayeredWindow(Handle,screen,ref destination,ref size,memory,ref origin,0,ref blend,2))throw new Win32Exception(Marshal.GetLastWin32Error(),"无法呈现面板阴影");
        }
        finally{if(old!=0)Native.SelectObject(memory,old);if(bitmap!=0)Native.DeleteObject(bitmap);if(memory!=0)Native.DeleteDC(memory);if(screen!=0)Native.ReleaseDC(0,screen);}
    }
    public void Dispose(){if(disposed)return;disposed=true;Native.DestroyWindow(Handle);LastPixels=[];}
}
