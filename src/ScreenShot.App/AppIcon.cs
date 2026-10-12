using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ScreenShot.App;

internal static class AppIcon
{
    internal static nint LoadTrayIcon()
    {
        var icon=Native.LoadImage(0,Path.Combine(AppContext.BaseDirectory,"Assets","AppIcon.ico"),1,
            Native.GetSystemMetrics(49),Native.GetSystemMetrics(50),0x10);
        if(icon==0)throw new Win32Exception(Marshal.GetLastWin32Error(),"无法加载应用图标");
        return icon;
    }
}
