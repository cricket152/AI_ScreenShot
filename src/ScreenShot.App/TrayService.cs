using System.Runtime.InteropServices;
using ScreenShot.Core;

namespace ScreenShot.App;

internal sealed class TrayService : IDisposable
{
    private readonly Native.WindowProc procedure;
    private readonly nint window;
    private Native.NotifyIcon icon;
    private readonly Action capture,settings,exit;
    private readonly uint taskbarCreated;
    public TrayService(Settings configuration,Action capture,Action settings,Action exit)
    {
        this.capture=capture;this.settings=settings;this.exit=exit;procedure=Process;
        var wc=new Native.WindowClass{Size=(uint)Marshal.SizeOf<Native.WindowClass>(),Proc=procedure,Instance=Native.GetModuleHandle(null),ClassName="AIScreenShot.Tray"};
        Native.RegisterClassEx(ref wc);window=Native.CreateWindowEx(0,wc.ClassName,"AI ScreenShot",0,0,0,0,0,0,0,wc.Instance,0);
        if(window==0)throw new InvalidOperationException("无法创建托盘消息窗口");
        taskbarCreated=Native.RegisterWindowMessage("TaskbarCreated");
        icon=new(){Size=(uint)Marshal.SizeOf<Native.NotifyIcon>(),Window=window,Id=1,Flags=7,Callback=0x8001,Icon=Native.LoadIcon(0,32512),Tip="AI ScreenShot · Ctrl+Alt+S",Info="",Title=""};
        Native.Shell_NotifyIcon(0,ref icon);
        if(!ChangeHotkey(configuration.HotkeyModifiers,configuration.HotkeyKey)) Notify("快捷键冲突","截图快捷键已被占用，请从托盘设置修改。");
    }
    public bool ChangeHotkey(uint modifiers,uint key)
    {Native.UnregisterHotKey(window,1);return Native.RegisterHotKey(window,1,modifiers|0x4000,key);}
    public void Notify(string title,string text){icon.Flags=0x10;icon.Title=title;icon.Info=text;icon.InfoFlags=1;Native.Shell_NotifyIcon(1,ref icon);icon.Flags=7;}
    private nint Process(nint hwnd,uint message,nint w,nint l)
    {
        if(message==taskbarCreated && taskbarCreated!=0)Native.Shell_NotifyIcon(0,ref icon);
        if(message==0x312){capture();return 0;}
        if(message==0x8001)
        {
            if(l==0x203)capture();
            if(l==0x205)
            {
                nint menu=Native.CreatePopupMenu();Native.AppendMenu(menu,0,1,"截图");Native.AppendMenu(menu,0,2,"设置");Native.AppendMenu(menu,0x800,0,"");Native.AppendMenu(menu,0,3,"退出");
                Native.GetCursorPos(out var point);Native.SetForegroundWindow(hwnd);
                int selected=Native.TrackPopupMenu(menu,0x100|0x2,point.X,point.Y,0,hwnd,0);Native.DestroyMenu(menu);
                if(selected==1)capture();if(selected==2)settings();if(selected==3)exit();
            }
            return 0;
        }
        return Native.DefWindowProc(hwnd,message,w,l);
    }
    public void Dispose(){Native.UnregisterHotKey(window,1);Native.Shell_NotifyIcon(2,ref icon);Native.DestroyWindow(window);}
}
