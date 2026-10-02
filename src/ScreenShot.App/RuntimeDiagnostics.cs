using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ScreenShot.App;

internal static class RuntimeDiagnostics
{
    public static async Task<string> VerifyClipboardBackupAsync()
    {
        int checks=0;
        void Check(bool condition,string description){if(!condition)throw new InvalidOperationException(description);checks++;}
        byte[] png=await FixtureAsync();
        using var original=new InMemoryRandomAccessStream();
        using(var writer=new DataWriter(original.GetOutputStreamAt(0))){writer.WriteBytes(png);await writer.StoreAsync();}
        original.Seek(0);var reference=RandomAccessStreamReference.CreateFromStream(original);
        var source=new Windows.ApplicationModel.DataTransfer.DataPackage();
        source.SetText("generated text");source.SetRtf(@"{\rtf1 generated}");
        string html=Windows.ApplicationModel.DataTransfer.HtmlFormatHelper.CreateHtmlFormat("<b>generated</b><img src=\"fixture.png\">");
        source.SetHtmlFormat(html);source.ResourceMap["fixture.png"]=reference;source.SetBitmap(reference);
        source.SetWebLink(new Uri("https://example.invalid/generated"));source.SetApplicationLink(new Uri("aiscreenshot-test:generated"));
        source.SetData("AIScreenShot.Generated.String","custom generated data");
        source.SetData("AIScreenShot.Generated.Stream",original);
        source.RequestedOperation=Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        using var backup=await ClipboardBackup.ReadAsync(source.GetView(),CancellationToken.None);
        original.Dispose(); // The clone must remain readable after the old owner releases its streams.
        var view=backup.Content.GetView();
        Check(backup.SkippedFormats==0&&source.GetView().AvailableFormats.All(view.Contains),$"Generated formats were not preserved (source={source.GetView().AvailableFormats.Count}, backup={view.AvailableFormats.Count}, skipped={backup.SkippedFormats})");
        Check(await view.GetTextAsync()=="generated text","Text backup");
        Check(await view.GetRtfAsync()==@"{\rtf1 generated}","RTF backup");
        Check(await view.GetHtmlFormatAsync()==html,"HTML backup");
        Check((string)await view.GetDataAsync("AIScreenShot.Generated.String")=="custom generated data","Custom string backup");
        Check((await view.GetWebLinkAsync()).Host=="example.invalid","WebLink backup");
        Check((await view.GetApplicationLinkAsync()).Scheme=="aiscreenshot-test","ApplicationLink backup");
        Check(view.RequestedOperation==Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move,"Requested operation preservation");
        async Task<byte[]> ReadAsync(IRandomAccessStreamReference streamReference)
        {
            using var stream=await streamReference.OpenReadAsync();
            using var reader=new DataReader(stream);await reader.LoadAsync((uint)stream.Size);
            byte[] bytes=new byte[(int)stream.Size];reader.ReadBytes(bytes);return bytes;
        }
        Check((await ReadAsync(await view.GetBitmapAsync())).SequenceEqual(png),"Independent bitmap backup");
        var resources=await view.GetResourceMapAsync();
        Check((await ReadAsync(resources["fixture.png"])).SequenceEqual(png),"Independent HTML resource backup");
        var custom=(IRandomAccessStream)await view.GetDataAsync("AIScreenShot.Generated.Stream");
        Check((await ReadAsync(RandomAccessStreamReference.CreateFromStream(custom))).SequenceEqual(png),"Independent custom stream backup");
        using var empty=await ClipboardBackup.ReadAsync(new Windows.ApplicationModel.DataTransfer.DataPackage().GetView(),CancellationToken.None);
        Check(empty.Content.GetView().AvailableFormats.Count==0,"Empty clipboard snapshot");
        return $"PASS: {checks} in-memory WinRT clipboard backup assertions\nPASS: text, RTF, HTML/resources, bitmap, links, custom string/stream, requested operation, empty data\nPASS: copied streams survive original owner release\nSystem clipboard was not read or modified; no Codex message was sent.\n";
    }
    public static string VerifyOverlay()
    {
        const int width=37,height=29;
        var bounds=new ScreenShot.Core.PixelRect(-120,-60,width,height);
        byte[] pixels=new byte[width*height*4];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int offset=(y*width+x)*4;
            pixels[offset]=(byte)(x*5);pixels[offset+1]=(byte)(y*7);pixels[offset+2]=(byte)((x+y)*3);pixels[offset+3]=255;
        }
        var capture=new DesktopCapture(bounds,pixels);
        using var process=System.Diagnostics.Process.GetCurrentProcess();
        uint before=Native.GetGuiResources(process.Handle,0);
        int checks=0;
        for(int iteration=0;iteration<3;iteration++)
        {
            using var surface=new OverlaySurface(capture);
            ScreenShot.Core.PixelRect?[] regions=[null,bounds,new(-115,-53,13,11),new(-140,-80,25,26),ScreenShot.Core.PixelRect.FromPoints(-91,-36,-111,-55),new(-84,-32,1,1)];
            foreach(var region in regions)
            {
                surface.Compose(region,false);
                var clipped=region?.Clamp(bounds);
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    bool bright=clipped is {} r&&x>=r.X-bounds.X&&x<r.X-bounds.X+r.Width&&y>=r.Y-bounds.Y&&y<r.Y-bounds.Y+r.Height;
                    int offset=(y*width+x)*4,divisor=bright?1:2;
                    uint expected=(uint)((pixels[offset+2]/divisor)|((pixels[offset+1]/divisor)<<8)|((pixels[offset]/divisor)<<16));
                    if(Native.GetPixel(surface.Dc,x,y)!=expected)throw new InvalidOperationException($"Overlay pixel alignment failed at {x},{y}");
                    checks++;
                }
            }
            surface.Compose(new(-115,-53,13,11));
            if(Native.GetPixel(surface.Dc,5,7)!=0x00D77800)throw new InvalidOperationException("Overlay selection border failed");
        }
        uint after=Native.GetGuiResources(process.Handle,0);
        if(before!=after)throw new InvalidOperationException($"Overlay GDI resource leak: {before} -> {after}");
        int incrementalChecks=0;
        using(var incremental=new OverlaySurface(capture))
        using(var reference=new OverlaySurface(capture))
        {
            incremental.Compose(null);
            var random=new Random(42);
            for(int i=0;i<160;i++)
            {
                ScreenShot.Core.PixelRect? region=i%17==0?null:new(bounds.X+random.Next(-10,width+5),bounds.Y+random.Next(-10,height+5),random.Next(1,width+10),random.Next(1,height+10));
                bool border=i%3!=0;
                var changes=incremental.Changes(region,border);
                reference.Compose(region,border);
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    if(Native.GetPixel(incremental.Dc,x,y)!=Native.GetPixel(reference.Dc,x,y)&&!changes.Any(r=>x+bounds.X>=r.X&&x+bounds.X<r.X+r.Width&&y+bounds.Y>=r.Y&&y+bounds.Y<r.Y+r.Height))
                        throw new InvalidOperationException("Overlay dirty regions omitted a changed pixel");
                }
                incremental.UpdateSelection(region,border);
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    if(Native.GetPixel(incremental.Dc,x,y)!=Native.GetPixel(reference.Dc,x,y))throw new InvalidOperationException($"Incremental overlay mismatch at {x},{y}, step {i}");
                    incrementalChecks++;
                }
                if(incremental.Changes(region,border).Count!=0)throw new InvalidOperationException("Unchanged selection requests repaint");
            }
        }
        for(int i=0;i<3;i++)
        {
            var probe=new SelectionOverlay(capture,_=>{});probe.Cancel();probe.Cancel();
        }
        return $"PASS: {checks} GDI pixel assertions (top-down alignment, negative origin, clipping, reverse selection, edge pixel)\nPASS: {incrementalChecks} incremental/reference pixel comparisons and dirty-region coverage\nPASS: selection border and GDI resource release ({before} -> {after})\nPASS: repeated overlay window lifecycle and idempotent cancellation\n"+BenchmarkOverlay();
    }

    private static string BenchmarkOverlay()
    {
        const int width=3840,height=2160,frames=80;
        var capture=new DesktopCapture(new(0,0,width,height),new byte[width*height*4]);
        using var surface=new OverlaySurface(capture);
        var region=new ScreenShot.Core.PixelRect(300,300,2200,1400);
        surface.Compose(region);Native.GdiFlush();
        var next=new ScreenShot.Core.PixelRect(300,300,2201,1401);
        long dirtyArea=surface.Changes(next).Sum(r=>(long)r.Width*r.Height);
        if(dirtyArea>width*(long)height/100)throw new InvalidOperationException("Small drag repaints more than 1% of the 4K desktop");
        var timer=System.Diagnostics.Stopwatch.StartNew();
        for(int i=0;i<frames;i++){surface.Compose(i%2==0?next:region);Native.GdiFlush();}
        double full=timer.Elapsed.TotalMilliseconds/frames;
        surface.Compose(region);Native.GdiFlush();timer.Restart();
        for(int i=0;i<frames;i++){surface.UpdateSelection(i%2==0?next:region);Native.GdiFlush();}
        double incremental=timer.Elapsed.TotalMilliseconds/frames;
        return $"4K synthetic composition benchmark ({frames} frames, GdiFlush): full={full:F3} ms/frame, incremental={incremental:F3} ms/frame; small-drag dirty area={dirtyArea}/{width*(long)height} pixels ({dirtyArea*100d/(width*(long)height):F3}%). Not a desktop frame-rate measurement.\n";
    }
    public static async Task<byte[]> RenderAsync(Microsoft.UI.Xaml.UIElement content)
    {
        var bitmap=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();await bitmap.RenderAsync(content);
        var buffer=await bitmap.GetPixelsAsync();byte[] pixels=new byte[buffer.Length];
        using(var reader=DataReader.FromBuffer(buffer))reader.ReadBytes(pixels);
        using var stream=new InMemoryRandomAccessStream();var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels);await encoder.FlushAsync();
        using var encoded=new DataReader(stream.GetInputStreamAt(0));await encoded.LoadAsync((uint)stream.Size);byte[] result=new byte[(int)stream.Size];encoded.ReadBytes(result);return result;
    }
    public static async Task<byte[]> FixtureAsync()
    {
        const int width=320,height=200;var pixels=new byte[width*height*4];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int offset=(y*width+x)*4;pixels[offset]=(byte)(x*255/width);pixels[offset+1]=(byte)(y*255/height);pixels[offset+2]=180;pixels[offset+3]=255;
        }
        using var stream=new InMemoryRandomAccessStream();var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,width,height,96,96,pixels);await encoder.FlushAsync();
        using var reader=new DataReader(stream.GetInputStreamAt(0));await reader.LoadAsync((uint)stream.Size);byte[] png=new byte[(int)stream.Size];reader.ReadBytes(png);return png;
    }
}
