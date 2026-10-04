using Microsoft.UI.Xaml;
using ScreenShot.Core;
using Windows.Storage.Pickers;

namespace ScreenShot.App;

internal static class CodexDirectoryPicker
{
    internal static async Task<bool> PickAsync(Window window,Settings settings)
    {
        var picker=new FolderPicker();picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(window));
        var folder=await picker.PickSingleFolderAsync();
        if(folder==null)return false;
        Select(settings,folder.Path);return true;
    }
    internal static void Select(Settings settings,string directory)
    {
        string path=Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if(!Directory.Exists(path))throw new InvalidOperationException("所选工作目录不存在");
        settings.CodexDirectories??=[];
        settings.CodexDirectories.RemoveAll(item=>string.Equals(item,path,StringComparison.OrdinalIgnoreCase));
        settings.CodexDirectories.Insert(0,path);
        if(settings.CodexDirectories.Count>12)settings.CodexDirectories.RemoveRange(12,settings.CodexDirectories.Count-12);
        settings.CodexDirectory=path;settings.Workspace=DisplayName(path);settings.Save();
    }
    internal static string DisplayName(string directory)
    {
        string name=Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        return string.IsNullOrEmpty(name)?directory:name;
    }
}
