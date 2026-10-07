using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ScreenShot.App;

// Transparent host framing and input hit testing; no HRGN or visual clipping.
internal sealed class PanelInputTransparency : IDisposable
{
    private readonly nint handle;
    private readonly PanelGeometry geometry;
    private readonly Native.SubclassProc procedure;
    private readonly HashSet<nint> attached=[];
    private bool disposed;

    internal PanelInputTransparency(nint handle,PanelGeometry geometry)
    {
        this.handle=handle;this.geometry=geometry;procedure=Process;Attach(handle);
        Native.SetWindowLongPtr(handle,-16,Native.GetWindowLongPtr(handle,-16)&~0x00c40000);
        Native.SetWindowLongPtr(handle,-20,Native.GetWindowLongPtr(handle,-20)&~0x00020300);
        if(!Native.SetWindowPos(handle,0,0,0,0,0,0x01|0x02|0x04|0x10|0x20))throw new Win32Exception(Marshal.GetLastWin32Error(),"无法更新透明宿主的客户区");
    }

    internal void Update()
    {
        if(disposed)return;
        Native.EnumChildWindows(handle,(child,_)=>{Attach(child);return true;},0);
    }
    private void Attach(nint window)
    {
        if(attached.Contains(window))return;
        if(!Native.SetWindowSubclass(window,procedure,1,0))throw new Win32Exception(Marshal.GetLastWin32Error(),"无法设置透明区域的输入行为");
        attached.Add(window);
    }
    private nint Process(nint window,uint message,nint w,nint l,nuint id,nuint data)
    {
        // WinUI can restore WS_DLGFRAME after presenter/backdrop changes. Keep the
        // entire rectangular HWND client area transparent throughout its lifetime.
        if(window==handle&&message==0x7c&&l!=0)
        {
            var styles=Marshal.PtrToStructure<Native.WindowStyles>(l);
            if(w==-16)styles.New&=~0x00c40000u;
            else if(w==-20)styles.New&=~0x00020300u;
            Marshal.StructureToPtr(styles,l,false);
            return 0;
        }
        if(window==handle&&message==0x83)return 0;
        if(window==handle&&message==0x14&&TransparentPanelBackdrop.ClearBackground(window,w))return 1;
        if(window==handle&&message==0x31e)TransparentPanelBackdrop.EnableAlpha(window);
        if(message==0x84&&Native.GetWindowRect(handle,out var bounds))
        {
            int x=(short)(l.ToInt64()&0xffff),y=(short)((l.ToInt64()>>16)&0xffff);
            if(!geometry.Contains(x-bounds.Left,y-bounds.Top))return -1;
        }
        if(message==0x82){Native.RemoveWindowSubclass(window,procedure,1);attached.Remove(window);}
        return Native.DefSubclassProc(window,message,w,l);
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        foreach(var window in attached)Native.RemoveWindowSubclass(window,procedure,1);
        attached.Clear();
    }
}
