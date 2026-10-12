using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ScreenShot.Core;

namespace ScreenShot.App;

internal sealed partial class SettingsWindow
{
    private readonly string localStorageDirectory;
    private readonly Func<string,Task> openLocalFolder;
    private readonly CancellationTokenSource storageLifetime=new();
    private readonly TextBlock storageSummary=Body("正在统计截图…"),storageLocation=Caption("");
    private readonly TextBlock storageResult=Caption("");
    private readonly ProgressRing storageProgress=new(){Width=18,Height=18,IsActive=false,Visibility=Visibility.Collapsed};
    private Button openStorageButton=null!,cleanStorageButton=null!;
    private bool storageBusy,storageClosed;
    private ScreenshotCleanupPlan? storagePlan;
    internal Task StorageTask {get;private set;}=Task.CompletedTask;
    internal Button StorageOpenButton=>openStorageButton;
    internal Button StorageCleanButton=>cleanStorageButton;
    internal string StorageSummary=>storageSummary.Text;
    internal string StorageResult=>storageResult.Text;
    internal ContentDialog? StorageConfirmation {get;private set;}

    private Border BuildLocalStorageCard()
    {
        storageLocation.Text=ScreenshotStorage.Folder(localStorageDirectory);storageLocation.IsTextSelectionEnabled=true;
        AutomationProperties.SetName(storageSummary,"本地截图占用");AutomationProperties.SetLiveSetting(storageResult,Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        openStorageButton=ActionButton("打开文件夹","\uE8B7");cleanStorageButton=ActionButton("清理未使用截图","\uE74D");cleanStorageButton.IsEnabled=false;
        AutomationProperties.SetHelpText(cleanStorageButton,"保留对话、草稿和正在发送的任务使用的截图；点击后确认清理范围。");
        openStorageButton.Click+=(_,_)=>StorageTask=OpenStorageAsync();
        cleanStorageButton.Click+=(_,_)=>StorageTask=CleanLocalStorageAsync();
        var actions=new StackPanel{Spacing=UiDesign.Number("SS.Spacing.S")};actions.Children.Add(openStorageButton);actions.Children.Add(cleanStorageButton);actions.Children.Add(storageProgress);
        var detail=new StackPanel{Spacing=UiDesign.Number("SS.Spacing.XS")};detail.Children.Add(storageSummary);detail.Children.Add(storageLocation);detail.Children.Add(storageResult);
        return SettingRow("本地存储","保留对话和草稿中仍在使用的截图。清理无需保存设置。",actions,detail);
    }
    private void SetStorageBusy(bool value)
    {
        storageBusy=value;openStorageButton.IsEnabled=!value;
        cleanStorageButton.IsEnabled=!value&&storagePlan?.Info is{CanClean:true,UnusedCount:>0};
        storageProgress.IsActive=value;storageProgress.Visibility=value?Visibility.Visible:Visibility.Collapsed;
    }
    private void ShowStoragePlan(ScreenshotCleanupPlan plan)
    {
        storagePlan=plan;var info=plan.Info;
        storageSummary.Text=$"{info.FileCount} 张截图 · {StorageSize(info.TotalBytes)}";
        storageResult.Text=info.Notice??(info.UnusedCount>0?$"可清理 {info.UnusedCount} 张 · {StorageSize(info.UnusedBytes)}":"没有可清理的未使用截图。");
    }
    internal static string StorageSize(long bytes)=>bytes<1024?$"{bytes} B":bytes<1024*1024?$"{bytes/1024d:0.#} KB":bytes<1024L*1024*1024?$"{bytes/(1024d*1024):0.#} MB":$"{bytes/(1024d*1024*1024):0.#} GB";
    private async Task RefreshLocalStorageAsync()
    {
        if(storageClosed||storageBusy)return;SetStorageBusy(true);var token=storageLifetime.Token;
        try{var plan=await Task.Run(()=>ScreenshotStorage.Prepare(localStorageDirectory,token),token);if(!storageClosed)ShowStoragePlan(plan);}
        catch(OperationCanceledException) when(token.IsCancellationRequested){ }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException)
        {if(!storageClosed){storagePlan=null;storageSummary.Text="无法统计截图占用";storageResult.Text="请检查本地存储目录及文件权限。";}}
        finally{if(!storageClosed)SetStorageBusy(false);}
    }
    private async Task OpenStorageAsync()
    {
        if(storageClosed||storageBusy)return;SetStorageBusy(true);
        try{await openLocalFolder(ScreenshotStorage.Folder(localStorageDirectory));}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {if(!storageClosed)storageResult.Text="无法打开截图文件夹，请检查目录及文件权限。";}
        finally{if(!storageClosed)SetStorageBusy(false);}
    }
    private static Task OpenLocalFolderAsync(string folder)=>Task.Run(()=>
    {
        Directory.CreateDirectory(folder);
        using var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder){UseShellExecute=true});
    });
    private async Task CleanLocalStorageAsync()
    {
        if(storageClosed||storageBusy)return;SetStorageBusy(true);var token=storageLifetime.Token;
        try
        {
            var plan=await Task.Run(()=>ScreenshotStorage.Prepare(localStorageDirectory,token),token);if(storageClosed)return;ShowStoragePlan(plan);
            if(!plan.Info.CanClean||plan.Info.UnusedCount==0)return;
            var dialog=new ContentDialog{XamlRoot=((FrameworkElement)Content).XamlRoot,Title="清理未使用截图？",
                Content=$"将清理 {plan.Info.UnusedCount} 张未使用截图，预计释放 {StorageSize(plan.Info.UnusedBytes)}。\n\n保留对话、草稿和正在发送的任务使用的截图。清理后无法恢复。",
                PrimaryButtonText="清理",CloseButtonText="取消",DefaultButton=ContentDialogButton.Close};
            app.Themes.ThemeDialog(dialog,(FrameworkElement)Content);StorageConfirmation=dialog;
            var answer=await dialog.ShowAsync();StorageConfirmation=null;
            if(storageClosed||answer!=ContentDialogResult.Primary)return;
            var result=await Task.Run(()=>ScreenshotStorage.Clean(plan,token),token);
            var latest=await Task.Run(()=>ScreenshotStorage.Prepare(localStorageDirectory,token),token);if(storageClosed)return;ShowStoragePlan(latest);
            storageResult.Text=$"已清理 {result.DeletedCount} 张，释放 {StorageSize(result.FreedBytes)}。"+
                (result.SkippedCount>0?$" {result.SkippedCount} 张已被使用或发生变化，已跳过。":"")+
                (result.FailedCount>0?$" {result.FailedCount} 张无法删除，请检查文件权限。":"");
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){ }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {if(!storageClosed)storageResult.Text="无法安全清理截图，请检查文件权限及对话索引；已保留未清理的文件。";}
        finally{StorageConfirmation=null;if(!storageClosed)SetStorageBusy(false);}
    }
}
