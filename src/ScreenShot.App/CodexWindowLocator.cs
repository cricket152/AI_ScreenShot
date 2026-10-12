using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace ScreenShot.App;

internal static class CodexWindowLocator
{
    internal static readonly Regex NewChat=new("^(?:在 (.+) 中开始新聊天|Start (?:a )?new chat in (.+))$",RegexOptions.IgnoreCase);
    private delegate bool EnumWindow(nint handle,nint parameter);
    [DllImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindow callback,nint parameter);
    [DllImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle,out uint processId);

    internal static Window? Locate(UIA3Automation automation,string? workspace=null)
    {
        HashSet<uint> processIds=[];
        foreach(var process in Process.GetProcessesByName("ChatGPT").Concat(Process.GetProcessesByName("Codex")))
        {
            using(process)processIds.Add((uint)process.Id);
        }
        List<nint> handles=[];
        if(!EnumWindows((handle,_)=>
        {
            GetWindowThreadProcessId(handle,out uint processId);
            if(processIds.Contains(processId)&&IsWindowVisible(handle))handles.Add(handle);
            return true;
        },0))throw new InvalidOperationException("无法枚举 Codex 窗口，请重试");

        List<Window> windows=[];
        foreach(nint handle in handles)
        {
            try
            {
                var window=automation.FromHandle(handle).AsWindow();
                var actions=window.FindAllDescendants(cf=>cf.ByControlType(ControlType.Button))
                    .Select(button=>NewChat.Match(button.Name)).Where(match=>match.Success).ToArray();
                if(actions.Any(match=>workspace==null||match.Groups[1].Value==workspace||match.Groups[2].Value==workspace))windows.Add(window);
            }
            catch(Exception error) when(CodexAutomation.IsUnavailable(error))
            { /* A top-level window can close or rebuild during enumeration. Reacquire on the next observation. */ }
        }
        // Electron may expose an auxiliary composer as Process.MainWindowHandle. Only workspace actions identify a sending window.
        return CodexWindowPolicy.Select(windows,_=>true);
    }
}
