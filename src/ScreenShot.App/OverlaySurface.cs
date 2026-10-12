using System.ComponentModel;
using System.Runtime.InteropServices;
using ScreenShot.Core;

namespace ScreenShot.App;

// All three DIBs use the same top-down, physical-pixel layout. Painting only
// copies an already composed frame; no partially dimmed frame reaches the screen.
internal sealed class OverlaySurface : IDisposable
{
    private readonly BitmapSurface original;
    private readonly BitmapSurface dimmed;
    private readonly BitmapSurface frame;
    private readonly PixelRect bounds;
    private PixelRect? composedSelection;
    private bool composedBorder;
    private nint borderPen;
    public nint Dc=>frame.Dc;

    public OverlaySurface(DesktopCapture capture)
    {
        bounds=capture.Bounds;
        original=new BitmapSurface(bounds.Width,bounds.Height,capture.Pixels);
        try
        {
            byte[] darkPixels=(byte[])capture.Pixels.Clone();
            for(int i=0;i<darkPixels.Length;i+=4)
            {
                darkPixels[i]/=2;darkPixels[i+1]/=2;darkPixels[i+2]/=2;
            }
            dimmed=new BitmapSurface(bounds.Width,bounds.Height,darkPixels);
            try{frame=new BitmapSurface(bounds.Width,bounds.Height);}
            catch{dimmed.Dispose();throw;}
            borderPen=Native.CreatePen(0,1,0x00D77800);
            if(borderPen==0){frame.Dispose();dimmed.Dispose();throw new Win32Exception(Marshal.GetLastWin32Error());}
        }
        catch{original.Dispose();throw;}
    }

    public void Compose(PixelRect? selection,bool drawBorder=true)
    {
        Copy(frame.Dc,0,0,bounds.Width,bounds.Height,dimmed.Dc,0,0);
        composedSelection=Normalize(selection);composedBorder=drawBorder;
        if(composedSelection is not {} region)return;
        CopyRegion(region,original.Dc);
        if(drawBorder)DrawBorder(region);
    }

    public List<PixelRect> Changes(PixelRect? selection,bool drawBorder=true)
    {
        var next=Normalize(selection);
        var changes=new List<PixelRect>(16);
        if(next==composedSelection&&drawBorder==composedBorder)return changes;
        AddDifference(changes,composedSelection,next);
        AddDifference(changes,next,composedSelection);
        if(composedBorder)AddBorder(changes,composedSelection);
        if(drawBorder)AddBorder(changes,next);
        return changes;
    }

    // Preserve the unchanged interior. Only changed strips and the old/new
    // one-pixel outlines need to be restored in the retained back buffer.
    public void UpdateSelection(PixelRect? selection,bool drawBorder=true)
    {
        var next=Normalize(selection);
        var changes=Changes(next,drawBorder);
        foreach(var dirty in changes)
        {
            CopyRegion(dirty,dimmed.Dc);
            if(next is {} region)
            {
                var bright=Intersect(dirty,region);
                if(bright.Width>0&&bright.Height>0)CopyRegion(bright,original.Dc);
            }
        }
        if(changes.Count>0&&drawBorder&&next is {} border)DrawBorder(border);
        composedSelection=next;composedBorder=drawBorder;
    }

    private PixelRect? Normalize(PixelRect? selection)
    {
        if(selection is not {} value)return null;
        var clipped=value.Clamp(bounds);
        return clipped.Width>0&&clipped.Height>0?clipped:null;
    }

    private void CopyRegion(PixelRect region,nint source)
    {
        int x=region.X-bounds.X,y=region.Y-bounds.Y;
        Copy(frame.Dc,x,y,region.Width,region.Height,source,x,y);
    }

    private static PixelRect Intersect(PixelRect a,PixelRect b)
    {
        int left=Math.Max(a.X,b.X),top=Math.Max(a.Y,b.Y);
        return new(left,top,Math.Max(0,Math.Min(a.X+a.Width,b.X+b.Width)-left),Math.Max(0,Math.Min(a.Y+a.Height,b.Y+b.Height)-top));
    }

    private static void AddDifference(List<PixelRect> changes,PixelRect? source,PixelRect? excluded)
    {
        if(source is not {} a)return;
        if(excluded is not {} b){changes.Add(a);return;}
        var overlap=Intersect(a,b);
        if(overlap.Width==0||overlap.Height==0){changes.Add(a);return;}
        if(overlap.Y>a.Y)changes.Add(new(a.X,a.Y,a.Width,overlap.Y-a.Y));
        if(overlap.Y+overlap.Height<a.Y+a.Height)changes.Add(new(a.X,overlap.Y+overlap.Height,a.Width,a.Y+a.Height-overlap.Y-overlap.Height));
        if(overlap.X>a.X)changes.Add(new(a.X,overlap.Y,overlap.X-a.X,overlap.Height));
        if(overlap.X+overlap.Width<a.X+a.Width)changes.Add(new(overlap.X+overlap.Width,overlap.Y,a.X+a.Width-overlap.X-overlap.Width,overlap.Height));
    }

    private static void AddBorder(List<PixelRect> changes,PixelRect? selection)
    {
        if(selection is not {} r)return;
        changes.Add(new(r.X,r.Y,r.Width,1));
        if(r.Height>1)changes.Add(new(r.X,r.Y+r.Height-1,r.Width,1));
        if(r.Height>2)
        {
            changes.Add(new(r.X,r.Y+1,1,r.Height-2));
            if(r.Width>1)changes.Add(new(r.X+r.Width-1,r.Y+1,1,r.Height-2));
        }
    }

    private void DrawBorder(PixelRect region)
    {
        int x=region.X-bounds.X,y=region.Y-bounds.Y;
        nint oldPen=Native.SelectObject(frame.Dc,borderPen),oldBrush=Native.SelectObject(frame.Dc,Native.GetStockObject(5));
        try{Native.Rectangle(frame.Dc,x,y,x+region.Width,y+region.Height);}
        finally{Native.SelectObject(frame.Dc,oldPen);Native.SelectObject(frame.Dc,oldBrush);}
    }

    public void Paint(nint target,Native.Rect invalid)
    {
        Copy(target,invalid.Left,invalid.Top,invalid.Right-invalid.Left,invalid.Bottom-invalid.Top,frame.Dc,invalid.Left,invalid.Top);
    }

    private static void Copy(nint target,int x,int y,int width,int height,nint source,int sourceX,int sourceY)
    {
        if(width>0&&height>0&&!Native.BitBlt(target,x,y,width,height,source,sourceX,sourceY,0x00CC0020))
            throw new InvalidOperationException("截图遮罩像素绘制失败");
    }

    public void Dispose(){if(borderPen!=0){Native.DeleteObject(borderPen);borderPen=0;}frame.Dispose();dimmed.Dispose();original.Dispose();}

    private sealed class BitmapSurface : IDisposable
    {
        public nint Dc {get;private set;}
        private nint bitmap,previous;
        public BitmapSurface(int width,int height,byte[]? pixels=null)
        {
            try
            {
                Dc=Native.CreateCompatibleDC(0);
                var info=new Native.BitmapInfo{Size=(uint)Marshal.SizeOf<Native.BitmapInfo>(),Width=width,Height=-height,Planes=1,BitCount=32};
                bitmap=Native.CreateDIBSection(Dc,ref info,0,out var bits,0,0);
                if(Dc==0||bitmap==0||bits==0)throw new InvalidOperationException("无法创建截图遮罩缓冲区");
                previous=Native.SelectObject(Dc,bitmap);
                if(previous==0||previous==-1)throw new InvalidOperationException("无法选择截图遮罩缓冲区");
                if(pixels!=null)Marshal.Copy(pixels,0,bits,pixels.Length);
            }
            catch{Dispose();throw;}
        }
        public void Dispose()
        {
            if(Dc!=0&&previous!=0&&previous!=-1)Native.SelectObject(Dc,previous);
            if(bitmap!=0){Native.DeleteObject(bitmap);bitmap=0;}
            if(Dc!=0){Native.DeleteDC(Dc);Dc=0;}
        }
    }
}
