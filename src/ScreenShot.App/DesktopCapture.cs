using System.Runtime.InteropServices;
using ScreenShot.Core;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ScreenShot.App;

internal sealed class DesktopCapture
{
    public PixelRect Bounds { get; }
    public byte[] Pixels { get; }
    public Native.BitmapInfo Info;
    internal DesktopCapture(PixelRect bounds,byte[] pixels)
    {
        if(bounds.Width<=0||bounds.Height<=0||pixels.Length!=checked(bounds.Width*bounds.Height*4))throw new ArgumentException("屏幕像素尺寸不匹配");
        Bounds=bounds;Pixels=pixels;
        Info=new(){Size=(uint)Marshal.SizeOf<Native.BitmapInfo>(),Width=bounds.Width,Height=-bounds.Height,Planes=1,BitCount=32};
    }
    public DesktopCapture()
    {
        Bounds=new(Native.GetSystemMetrics(76),Native.GetSystemMetrics(77),Native.GetSystemMetrics(78),Native.GetSystemMetrics(79));
        Pixels=new byte[checked(Bounds.Width*Bounds.Height*4)];
        Info=new(){Size=(uint)Marshal.SizeOf<Native.BitmapInfo>(),Width=Bounds.Width,Height=-Bounds.Height,Planes=1,BitCount=32};
        nint screen=Native.GetDC(0),dc=0,bitmap=0,old=0;
        try
        {
            if(screen==0)throw new InvalidOperationException("无法获取屏幕设备上下文");
            dc=Native.CreateCompatibleDC(screen);bitmap=Native.CreateCompatibleBitmap(screen,Bounds.Width,Bounds.Height);
            if(dc==0||bitmap==0)throw new InvalidOperationException("无法创建屏幕抓取资源");
            old=Native.SelectObject(dc,bitmap);
            if(!Native.BitBlt(dc,0,0,Bounds.Width,Bounds.Height,screen,Bounds.X,Bounds.Y,0x40CC0020)) throw new InvalidOperationException("屏幕抓取失败");
            Native.SelectObject(dc,old);
            if(Native.GetDIBits(screen,bitmap,0,(uint)Bounds.Height,Pixels,ref Info,0)==0) throw new InvalidOperationException("屏幕像素读取失败");
        }
        finally
        {
            if(dc!=0&&old!=0)Native.SelectObject(dc,old);
            if(bitmap!=0)Native.DeleteObject(bitmap);
            if(dc!=0)Native.DeleteDC(dc);
            if(screen!=0)Native.ReleaseDC(0,screen);
        }
        for(int i=3;i<Pixels.Length;i+=4) Pixels[i]=255;
    }
    public async Task<byte[]> CropAsync(PixelRect rect)
    {
        rect=rect.Clamp(Bounds);
        if(rect.Width==0 || rect.Height==0) throw new ArgumentException("截图区域为空");
        byte[] crop=new byte[checked(rect.Width*rect.Height*4)];
        for(int row=0;row<rect.Height;row++) System.Buffer.BlockCopy(Pixels,((rect.Y-Bounds.Y+row)*Bounds.Width+rect.X-Bounds.X)*4,crop,row*rect.Width*4,rect.Width*4);
        using var stream=new InMemoryRandomAccessStream();
        var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,(uint)rect.Width,(uint)rect.Height,96,96,crop);
        await encoder.FlushAsync(); stream.Seek(0);
        using var reader=new DataReader(stream.GetInputStreamAt(0));await reader.LoadAsync((uint)stream.Size);
        byte[] png=new byte[(int)stream.Size];reader.ReadBytes(png);return png;
    }
}
