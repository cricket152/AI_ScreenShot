using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using ScreenShot.Core;

namespace ScreenShot.App;

public partial class App
{
    private async Task<string> VerifyStorageAsync(string output)
    {
        string storage=Path.Combine(output,"storage-fixtures",Guid.NewGuid().ToString("N"));
        var original=Animations.Appearance;string savedPrompt=Settings.DefaultPrompt;
        SettingsWindow? window=null;string? openedFolder=null;
        static void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        async Task<ContentDialog> Confirmation()
        {
            var timer=System.Diagnostics.Stopwatch.StartNew();
            while(window!.StorageConfirmation?.IsLoaded!=true)
            {if(timer.Elapsed>TimeSpan.FromSeconds(5))throw new TimeoutException("Storage confirmation did not load");await Task.Delay(20);}
            return window.StorageConfirmation;
        }
        void Answer(ContentDialog dialog,bool clean)
        {
            var button=FloatingPanelWindow.FindDialogButton(dialog,clean?"PrimaryButton":"CloseButton")??throw new InvalidOperationException("Storage confirmation action is absent");
            Invoke(button);
        }
        int checks=0;void Check(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);checks++;}
        try
        {
            byte[] png=await RuntimeDiagnostics.FixtureAsync();using var store=new ConversationStore(storage);
            var chat=new Conversation{IsOpen=false};store.SaveDraftImage(chat,png);string draft=chat.DraftImage!;
            string history=store.SaveImage(png);chat.Messages.Add(new(){Image=history,State="Thinking"});
            string obsolete=store.SaveImage(png);chat.Images.Add(obsolete);store.Save(chat);string orphan=store.SaveImage(png);
            File.WriteAllBytes(Path.Combine(ScreenshotStorage.Folder(storage),"user-image.png"),png);
            window=new SettingsWindow(this,(_,_)=>true,storage,path=>{openedFolder=path;return Task.CompletedTask;});window.Activate();
            await window.StorageTask.WaitAsync(TimeSpan.FromSeconds(5));await Task.Delay(100);
            var root=(FrameworkElement)window.Content;root.UpdateLayout();var navigation=(NavigationView)((Border)root).Child;
            var scroller=((Grid)navigation.Content).Children.OfType<ScrollViewer>().Single();
            scroller.ChangeView(null,scroller.ScrollableHeight,null,true);await Task.Delay(80);
            Check(window.StorageSummary.StartsWith("5 张截图")&&window.StorageResult.Contains("2 张")&&window.StorageCleanButton.IsEnabled,"Storage card did not display generated usage or cleanup availability");
            Check(AutomationProperties.GetName(window.StorageCleanButton)=="清理未使用截图"&&AutomationProperties.GetHelpText(window.StorageCleanButton).Contains("保留"),"Storage action lacks an accessible description of preserved images");
            window.DefaultPromptEditor.Text="generated unsaved storage prompt";
            Invoke(window.StorageOpenButton);await window.StorageTask;
            Check(openedFolder==ScreenshotStorage.Folder(storage)&&Settings.DefaultPrompt==savedPrompt,"Open folder used the wrong directory or saved unrelated settings");
            foreach(var theme in new[]{AppThemeMode.Light,AppThemeMode.Dark})
            {
                ApplyAppearance(original with{Theme=theme});await Task.Delay(100);root.UpdateLayout();
                Check(window.StorageOpenButton.IsLoaded&&window.StorageCleanButton.IsLoaded,"Storage controls were unloaded in a theme");
                await File.WriteAllBytesAsync(Path.Combine(output,"storage-"+theme.ToString().ToLowerInvariant()+".png"),await RuntimeDiagnostics.RenderAsync(root));
            }
            window.AppWindow.Resize(new(520,760));await Task.Delay(100);root.UpdateLayout();
            scroller.ChangeView(null,scroller.ScrollableHeight,null,true);await Task.Delay(80);
            Check(root.ActualWidth<=520&&window.StorageOpenButton.ActualWidth>0&&window.StorageCleanButton.ActualWidth<=root.ActualWidth,"Storage actions failed narrow window layout");
            await File.WriteAllBytesAsync(Path.Combine(output,"storage-narrow.png"),await RuntimeDiagnostics.RenderAsync(root));
            Invoke(window.StorageCleanButton);var cancel=await Confirmation();
            Check(cancel.DefaultButton==ContentDialogButton.Close&&cancel.Content.ToString()!.Contains("2 张")&&!window.StorageCleanButton.IsEnabled,"Confirmation does not default to Cancel or prevent repeated cleanup");
            Answer(cancel,false);await window.StorageTask;
            Check(store.ReadImage(obsolete).Length==png.Length&&store.ReadImage(orphan).Length==png.Length,"Cancel deleted generated files");
            Invoke(window.StorageCleanButton);var confirm=await Confirmation();Answer(confirm,true);await window.StorageTask;
            Check(store.ReadImage(obsolete).Length==0&&store.ReadImage(orphan).Length==0&&store.ReadImage(draft).Length==png.Length&&store.ReadImage(history).Length==png.Length,"Native cleanup removed live images or retained unused files");
            Check(window.StorageResult.Contains("已清理 2 张")&&window.StorageSummary.StartsWith("3 张截图")&&!window.StorageCleanButton.IsEnabled,"Cleanup receipt or refreshed usage is incorrect");
            Check(File.Exists(Path.Combine(ScreenshotStorage.Folder(storage),"user-image.png"))&&Settings.DefaultPrompt==savedPrompt&&window.DefaultPromptEditor.Text=="generated unsaved storage prompt","Cleanup touched unrelated files or unsaved settings");
            navigation.SelectedItem=navigation.MenuItems.OfType<NavigationViewItem>().Single(item=>Equals(item.Tag,"appearance"));window.BorderWidthSlider.Value=2.5;
            navigation.SelectedItem=navigation.MenuItems.OfType<NavigationViewItem>().Single(item=>Equals(item.Tag,"general"));await window.StorageTask;
            Check(window.AppearanceDraft.BorderThickness==2.5&&window.StorageSummary.StartsWith("3 张截图"),"Storage navigation lost an appearance draft or usage");
            store.SaveImage(png);navigation.SelectedItem=navigation.MenuItems.OfType<NavigationViewItem>().Single(item=>Equals(item.Tag,"about"));
            navigation.SelectedItem=navigation.MenuItems.OfType<NavigationViewItem>().Single(item=>Equals(item.Tag,"general"));await window.StorageTask;
            Invoke(window.StorageCleanButton);await Confirmation();var closing=window.StorageTask;window.Close();window=null;await closing.WaitAsync(TimeSpan.FromSeconds(5));
            Check(ScreenshotStorage.Prepare(storage).Info.UnusedCount==1,"Closing settings while confirmation was open performed cleanup");
            string result=$"PASS: {checks} native local storage assertions; generated usage/path, injected folder opener, Light/Dark and narrow layout, accessible labels, default Cancel, duplicate-operation guard, actual native cancel/clean, live history/draft preservation, unmanaged files, refreshed count/bytes, unsaved setting isolation, navigation refresh and closing confirmation without deletion.\nGenerated fixtures and dedicated SQLite only; no user history/desktop/clipboard, real folder launch, settings save or model request.\n";
            await File.WriteAllTextAsync(Path.Combine(output,"storage-test.txt"),result);return result;
        }
        finally{window?.Close();ApplyAppearance(original);}
    }
}
