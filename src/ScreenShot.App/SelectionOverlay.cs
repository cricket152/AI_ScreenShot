using System.Runtime.InteropServices;
using ScreenShot.Core;

namespace ScreenShot.App;

internal sealed class SelectionOverlay
{
    private const string ClassName="AIScreenShot.Selection";
    private readonly Native.WindowProc procedure;
    private readonly DesktopCapture capture;
    private readonly OverlaySurface surface;
    private readonly Action<PixelRect?> selected;
    private readonly Action selectionStarted;
    private readonly Action cancelled;
    private nint window;
    private bool dragging,finished;
    private Native.Point start,end;
    private PixelRect? selection;
    internal nint Handle=>window;

    public SelectionOverlay(DesktopCapture capture,Action<PixelRect?> selected,Action? selectionStarted=null,Action? cancelled=null)
    {
        this.capture=capture;this.selected=selected;this.selectionStarted=selectionStarted??(()=>{});this.cancelled=cancelled??(()=>selected(null));procedure=Process;
        surface=new OverlaySurface(capture);
        bool registered=false;
        try
        {
            surface.Compose(null);
            var wc=new Native.WindowClass{Size=(uint)Marshal.SizeOf<Native.WindowClass>(),Proc=procedure,Instance=Native.GetModuleHandle(null),Cursor=Native.LoadCursor(0,32515),ClassName=ClassName};
            registered=Native.RegisterClassEx(ref wc)!=0;
            if(!registered)throw new InvalidOperationException("无法注册截图遮罩");
            var b=capture.Bounds;
            window=Native.CreateWindowEx(0x00000088,ClassName,"框选截图 · Esc 取消",0x80000000,b.X,b.Y,b.Width,b.Height,0,0,wc.Instance,0);
            if(window==0)throw new InvalidOperationException("无法创建截图遮罩");
            Native.ShowWindow(window,5);Native.SetForegroundWindow(window);
        }
        catch
        {
            if(window!=0)Native.DestroyWindow(window);
            if(registered)Native.UnregisterClass(ClassName,Native.GetModuleHandle(null));
            surface.Dispose();throw;
        }
    }

    public void Cancel()
    {
        if(finished)return;
        Close();cancelled();
    }

    public void Close()
    {
        if(finished)return;
        finished=true;dragging=false;
        Native.ReleaseCapture();Native.DestroyWindow(window);
        Native.UnregisterClass(ClassName,Native.GetModuleHandle(null));surface.Dispose();
    }

    public void SetVisible(bool visible)
    {
        if(!finished)Native.ShowWindow(window,visible?4:0);
    }

    private void UpdateFrame(nint hwnd)
    {
        // Mouse messages only record the newest selection and its dirty regions.
        // WM_PAINT coalesces intermediate positions before doing any pixel copies.
        foreach(var dirty in surface.Changes(selection))
        {
            var rect=new Native.Rect{Left=dirty.X-capture.Bounds.X,Top=dirty.Y-capture.Bounds.Y,Right=dirty.X-capture.Bounds.X+dirty.Width,Bottom=dirty.Y-capture.Bounds.Y+dirty.Height};
            Native.InvalidateRect(hwnd,ref rect,false);
        }
    }

    private nint Process(nint hwnd,uint message,nint w,nint l)
    {
        switch(message)
        {
            case 0x14:return 1; // WM_ERASEBKGND: the composed frame covers the client area.
            case 0x20:
                Native.SetCursor(Native.LoadCursor(0,32515));return 1;
            case 0x100:
                if(w==27)Cancel();return 0;
            case 0x201:
                selectionStarted();Native.GetCursorPos(out start);end=start;selection=null;dragging=true;
                Native.SetCapture(hwnd);UpdateFrame(hwnd);return 0;
            case 0x200:
                if(dragging)
                {
                    Native.GetCursorPos(out end);
                    selection=PixelRect.FromPoints(start.X,start.Y,end.X,end.Y).Clamp(capture.Bounds);UpdateFrame(hwnd);
                }
                return 0;
            case 0x202:
                if(dragging)
                {
                    Native.GetCursorPos(out end);dragging=false;Native.ReleaseCapture();
                    var region=PixelRect.FromPoints(start.X,start.Y,end.X,end.Y).Clamp(capture.Bounds);
                    selection=region.Width>0&&region.Height>0?region:null;UpdateFrame(hwnd);
                    if(selection!=null)selected(selection);
                }
                return 0;
            case 0x215: // Losing capture aborts this drag, without confirming a partial rectangle.
                if(dragging){dragging=false;selection=null;UpdateFrame(hwnd);}return 0;
            case 0x205:
            case 0x10:
            case 0x7E: // Display topology changed; the frozen desktop no longer matches it.
                Cancel();return 0;
            case 0x2E0:return 0; // Keep the virtual desktop's physical bounds across mixed-DPI monitors.
            case 0x0F:
                var dc=Native.BeginPaint(hwnd,out var paint);
                try{surface.UpdateSelection(selection);surface.Paint(dc,paint.Bounds);}
                finally{Native.EndPaint(hwnd,ref paint);}
                return 0;
        }
        return Native.DefWindowProc(hwnd,message,w,l);
    }
}
