using System.Runtime.InteropServices;

namespace ScreenShot.App;

internal static class Native
{
    public delegate nint WindowProc(nint hwnd,uint message,nint wparam,nint lparam);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] public struct WindowClass { public uint Size,Style; public WindowProc Proc; public int ClassExtra,WindowExtra; public nint Instance,Icon,Cursor,Background; public string? MenuName; public string ClassName; public nint SmallIcon; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Size { public int Width,Height; }
    [StructLayout(LayoutKind.Sequential)] public struct Margins { public int Left,Right,Top,Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct BlurBehind { public uint Flags; public int Enabled; public nint Region; public int Transition; }
    [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(nint window,ref Margins margins);
    [DllImport("dwmapi.dll")] public static extern int DwmEnableBlurBehindWindow(nint window,ref BlurBehind blur);
    [StructLayout(LayoutKind.Sequential,Pack=1)] public struct BlendFunction { public byte Operation,Flags,ConstantAlpha,AlphaFormat; }
    [DllImport("user32.dll",SetLastError=true)] public static extern bool UpdateLayeredWindow(nint window,nint screen,ref Point destination,ref Size size,nint source,ref Point origin,uint colorKey,ref BlendFunction blend,uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct WindowStyles { public uint Old,New; }
    [StructLayout(LayoutKind.Sequential)] public struct BitmapInfo { public uint Size; public int Width,Height; public ushort Planes,BitCount; public uint Compression,SizeImage; public int XPels,YPels; public uint Used,Important; }
    [StructLayout(LayoutKind.Sequential)] public struct Paint { public nint Dc; public int Erase; public Rect Bounds; public int Restore,Update; [MarshalAs(UnmanagedType.ByValArray,SizeConst=32)] public byte[] Reserved; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] public struct NotifyIcon { public uint Size; public nint Window; public uint Id,Flags,Callback; public nint Icon; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Tip; public uint State,StateMask; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Info; public uint Version; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)] public string Title; public uint InfoFlags; public Guid Guid; public nint BalloonIcon; }
    [StructLayout(LayoutKind.Sequential)] public struct MonitorInfo { public uint Size; public Rect Monitor,Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] public struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] public struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Key; }
    [StructLayout(LayoutKind.Sequential)] public struct MouseInput { public int X,Y; public uint Data,Flags,Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] public struct KeyInput { public ushort Key,Scan; public uint Flags,Time; public nuint Extra; }
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern ushort RegisterClassEx(ref WindowClass wc);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern nint CreateWindowEx(uint ex,string cls,string title,uint style,int x,int y,int width,int height,nint parent,nint menu,nint instance,nint param);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] public static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")] public static extern nint DefWindowProc(nint h,uint m,nint w,nint l);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(nint h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint h,int command);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint h);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint h,nint after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(nint h);
    [DllImport("user32.dll")] public static extern uint GetGuiResources(nint process,uint flags);
    [DllImport("user32.dll")] public static extern nint GetDC(nint h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(nint h,nint dc);
    [DllImport("gdi32.dll")] public static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] public static extern nint CreateCompatibleBitmap(nint dc,int width,int height);
    [DllImport("gdi32.dll",SetLastError=true)] public static extern nint CreateDIBSection(nint dc,ref BitmapInfo info,uint usage,out nint bits,nint section,uint offset);
    [DllImport("gdi32.dll")] public static extern uint GetPixel(nint dc,int x,int y);
    [DllImport("gdi32.dll")] public static extern nint CreateSolidBrush(uint color);
    [DllImport("user32.dll")] public static extern int FillRect(nint dc,ref Rect bounds,nint brush);
    [DllImport("gdi32.dll")] public static extern bool GdiFlush();
    [DllImport("gdi32.dll")] public static extern nint SelectObject(nint dc,nint obj);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(nint dest,int x,int y,int width,int height,nint source,int sx,int sy,uint rop);
    [DllImport("gdi32.dll")] public static extern int GetDIBits(nint dc,nint bitmap,uint start,uint lines,byte[] bits,ref BitmapInfo info,uint usage);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] public static extern nint CreatePen(int style,int width,uint color);
    [DllImport("gdi32.dll")] public static extern nint GetStockObject(int index);
    [DllImport("gdi32.dll")] public static extern bool Rectangle(nint dc,int left,int top,int right,int bottom);
    [DllImport("user32.dll")] public static extern nint BeginPaint(nint h,out Paint paint);
    [DllImport("user32.dll")] public static extern bool EndPaint(nint h,ref Paint paint);
    [DllImport("user32.dll")] public static extern bool InvalidateRect(nint h,nint rect,bool erase);
    [DllImport("user32.dll")] public static extern bool InvalidateRect(nint h,ref Rect rect,bool erase);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern nint SetCapture(nint h);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern nint LoadCursor(nint instance,nint name);
    [DllImport("user32.dll")] public static extern nint SetCursor(nint cursor);
    [DllImport("user32.dll")] public static extern nint LoadIcon(nint instance,nint name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern nint LoadImage(nint instance,string name,uint type,int width,int height,uint flags);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern nint SendMessage(nint window,uint message,nint wparam,nint lparam);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] public static extern uint ExtractIconEx(string file,int index,out nint large,out nint small,uint count);
    [DllImport("user32.dll",SetLastError=true)] public static extern bool RegisterHotKey(nint h,int id,uint mods,uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint h,int id);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] public static extern bool Shell_NotifyIcon(uint message,ref NotifyIcon data);
    [DllImport("user32.dll")] public static extern nint CreatePopupMenu();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern bool AppendMenu(nint menu,uint flags,nuint id,string text);
    [DllImport("user32.dll")] public static extern int TrackPopupMenu(nint menu,uint flags,int x,int y,int reserved,nint window,nint rect);
    [DllImport("user32.dll")] public static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] public static extern nint MonitorFromPoint(Point point,uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(nint monitor,ref MonitorInfo info);
    [DllImport("user32.dll")] public static extern uint SendInput(uint count,Input[] inputs,int size);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] public static extern nint GetClipboardOwner();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint window,out Rect rect);
    [DllImport("user32.dll")] public static extern bool GetClientRect(nint window,out Rect rect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(nint window,ref Point point);
    [DllImport("user32.dll")] public static extern int GetWindowRgn(nint window,nint region);
    [DllImport("gdi32.dll")] public static extern nint CreateRectRgn(int left,int top,int right,int bottom);
    public delegate nint SubclassProc(nint window,uint message,nint w,nint l,nuint id,nuint data);
    public delegate bool EnumWindowProc(nint window,nint data);
    [DllImport("comctl32.dll",SetLastError=true)] public static extern bool SetWindowSubclass(nint window,SubclassProc procedure,nuint id,nuint data);
    [DllImport("comctl32.dll")] public static extern bool RemoveWindowSubclass(nint window,SubclassProc procedure,nuint id);
    [DllImport("comctl32.dll")] public static extern nint DefSubclassProc(nint window,uint message,nint w,nint l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(nint parent,EnumWindowProc procedure,nint data);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint window,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] public static extern nint SetWindowLongPtr(nint window,int index,nint value);
    [DllImport("dwmapi.dll")] public static extern int DwmFlush();
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(nint window,uint attribute,ref uint value,uint size);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern bool UnregisterClass(string name,nint instance);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] public static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    public static void Paste()
    {
        Input Key(ushort key,bool up)=>new(){Type=1,Data=new(){Key=new(){Key=key,Flags=up?2u:0u}}};
        Input[] keys=[Key(0x11,false),Key(0x56,false),Key(0x56,true),Key(0x11,true)];
        if(SendInput((uint)keys.Length,keys,Marshal.SizeOf<Input>())!=keys.Length) throw new InvalidOperationException("无法向目标窗口粘贴，请检查权限与焦点");
    }
}
