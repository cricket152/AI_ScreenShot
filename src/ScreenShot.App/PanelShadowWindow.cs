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
    private nint memory,bitmap,old,bits;
    private int bufferWidth,bufferHeight;
    private bool unavailable;
    internal int FallbackCount {get;private set;}
    internal byte ConstantAlpha {get;private set;}=255;
    internal int MaskRenderCount {get;private set;}
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
    internal void Update(IReadOnlyList<PanelGeometry.Shape> shapes,PanelAppearance appearance,double scale,bool highContrast,bool prepare=false)
    {
        if(disposed||unavailable)return;
        try{UpdateCore(shapes,appearance,scale,highContrast,prepare);}
        catch(Win32Exception){FallbackCount++;unavailable=true;Hide();}
    }
    private void UpdateCore(IReadOnlyList<PanelGeometry.Shape> shapes,PanelAppearance appearance,double scale,bool highContrast,bool prepare)
    {
        if(disposed)return;
        if(!appearance.ShowShadow||appearance.ShadowSpread<=0||appearance.ShadowOpacity<=0||highContrast||(!prepare&&!Native.IsWindowVisible(panel))||shapes.Count==0){Hide();return;}
        if(!Native.GetWindowRect(panel,out var bounds))return;
        double spread=appearance.ShadowSpread*scale;
        int margin=(int)Math.Ceiling(spread*2)+2;
        int left=shapes.Min(shape=>shape.Left)-margin,top=shapes.Min(shape=>shape.Top)-margin;
        int width=shapes.Max(shape=>shape.Right)-left+margin,height=shapes.Max(shape=>shape.Bottom)-top+margin;
        // Cache the full mask during a shared fade; only SourceConstantAlpha changes.
        double opacity=Math.Clamp(shapes.Max(shape=>shape.Opacity),0,1);
        byte alpha=(byte)Math.Round(opacity*255);
        var maskGeometry=shapes.Select(shape=>shape with{Opacity=opacity>0?Math.Round(shape.Opacity/opacity,5):1}).ToArray();
        bool render=previousSpread!=spread||previousOpacity!=appearance.ShadowOpacity||!previous.SequenceEqual(maskGeometry);
        if(render)
        {
            long start=System.Diagnostics.Stopwatch.GetTimestamp();
            var maskShapes=maskGeometry.Select(shape=>new ShadowShape(shape.Left-left,shape.Top-top,shape.Right-shape.Left,shape.Bottom-shape.Top,shape.Diameter/2d,shape.Opacity)).ToArray();
            var pixels=PanelShadowMask.Render(width,height,maskShapes,spread,appearance.ShadowOpacity);
            LastMaskTicks=System.Diagnostics.Stopwatch.GetTimestamp()-start;
            Upload(pixels,width,height,bounds.Left+left,bounds.Top+top,alpha);MaskRenderCount++;
            LastPixels=pixels;LastRenderTicks=System.Diagnostics.Stopwatch.GetTimestamp()-start;
            previous=maskGeometry;previousSpread=spread;previousOpacity=appearance.ShadowOpacity;
        }
        else if(alpha!=ConstantAlpha)Upload(null,width,height,bounds.Left+left,bounds.Top+top,alpha);
        if(!Native.GetWindowRect(Handle,out var current)||current.Left!=bounds.Left+left||current.Top!=bounds.Top+top||current.Right-current.Left!=width||current.Bottom-current.Top!=height||(!prepare&&!Native.IsWindowVisible(Handle)))
            if(!Native.SetWindowPos(Handle,panel,bounds.Left+left,bounds.Top+top,width,height,0x10u|(prepare?0u:0x40u)))throw new Win32Exception(Marshal.GetLastWin32Error(),"无法定位面板阴影");
    }
    private void Upload(byte[]? pixels,int width,int height,int x,int y,byte alpha)
    {
        nint screen=Native.GetDC(0);
        try
        {
            if(memory==0||bufferWidth!=width||bufferHeight!=height)
            {
                ReleaseBuffer();memory=Native.CreateCompatibleDC(screen);
                var info=new Native.BitmapInfo{Size=(uint)Marshal.SizeOf<Native.BitmapInfo>(),Width=width,Height=-height,Planes=1,BitCount=32};
                bitmap=Native.CreateDIBSection(screen,ref info,0,out bits,0,0);
                if(memory==0||bitmap==0||bits==0){int error=Marshal.GetLastWin32Error();ReleaseBuffer();throw new Win32Exception(error,"无法创建阴影缓冲区");}
                old=Native.SelectObject(memory,bitmap);bufferWidth=width;bufferHeight=height;
            }
            if(pixels!=null)Marshal.Copy(pixels,0,bits,pixels.Length);
            var destination=new Native.Point{X=x,Y=y};var size=new Native.Size{Width=width,Height=height};var origin=new Native.Point();
            var blend=new Native.BlendFunction{ConstantAlpha=alpha,AlphaFormat=1};
            bool uploaded=pixels==null?Native.UpdateLayeredWindowOpacity(Handle,screen,0,0,memory,ref origin,0,ref blend,2):Native.UpdateLayeredWindow(Handle,screen,ref destination,ref size,memory,ref origin,0,ref blend,2);
            if(!uploaded)throw new Win32Exception(Marshal.GetLastWin32Error(),"无法呈现面板阴影");
            ConstantAlpha=alpha;
        }
        finally{if(screen!=0)Native.ReleaseDC(0,screen);}
    }
    private void ReleaseBuffer()
    {if(old!=0)Native.SelectObject(memory,old);if(bitmap!=0)Native.DeleteObject(bitmap);if(memory!=0)Native.DeleteDC(memory);old=bitmap=memory=bits=0;bufferWidth=bufferHeight=0;}
    public void Dispose(){if(disposed)return;disposed=true;ReleaseBuffer();Native.DestroyWindow(Handle);LastPixels=[];}
}
