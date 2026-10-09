using ScreenShot.Core;

namespace ScreenShot.App;

internal static class StartupDiagnostics
{
    internal static string Stage {get;private set;}="application";
    internal static int ErrorCount {get;private set;}
    internal static void Record(string stage,Exception? error=null)
    {
        Stage=stage;
        if(error!=null)ErrorCount++;
        try
        {
            string folder=Path.Combine(Settings.DirectoryPath,"diagnostics");Directory.CreateDirectory(folder);
            // Never log exception messages, settings, prompts, replies or database contents.
            string detail=error==null?stage:stage+" "+error.GetType().FullName+" 0x"+error.HResult.ToString("X8")+"\n"+error.StackTrace;
            File.AppendAllText(Path.Combine(folder,"startup.log"),DateTime.UtcNow.ToString("O")+" "+detail+"\n");
        }
        catch(Exception failure) when(failure is IOException or UnauthorizedAccessException){ }
    }
}

public partial class App
{
    private async void RunStartupTest()
    {
        string output=Path.Combine(AppContext.BaseDirectory,"diagnostics");Directory.CreateDirectory(output);
        string storage=Path.Combine(Path.GetTempPath(),"AIScreenShot-startup-"+Guid.NewGuid().ToString("N"));
        try
        {
            var first=new Conversation{Client="Agent",Draft="fixture draft",Order=0};
            var second=new Conversation{Client="Agent",Title="fixture history",Order=1,Messages=[new(){Role="assistant",Client="Agent",Text="fixture reply",State="Sent"}]};
            using(var store=new ConversationStore(storage)){store.Save(first);store.Save(second);store.Select(first.Id);}
            Conversation? closedFixture=null;
            if(Environment.GetCommandLineArgs().Contains("--conversation-history-test"))
            {
                using var fixtureStore=new ConversationStore(storage);
                string image=fixtureStore.SaveImage(await RuntimeDiagnostics.FixtureAsync());
                closedFixture=new Conversation{Title="closed fixture",IsOpen=false,Order=2,DraftImage=image,Messages=[new(){Text="generated closed history",Image=image,State="Sent"}]};
                fixtureStore.Save(closedFixture);
            }
            if(Ball!=null)throw new InvalidOperationException("Cold startup must restore the panel before creating a ball");
            if(Windows.System.DispatcherQueue.GetForCurrentThread()==null)throw new InvalidOperationException("System composition queue was not initialized");
            Panel=new FloatingPanelWindow(this,conversationDirectory:storage);
            if(Panel.OpenConversationCount!=2||Panel.CurrentConversationId!=first.Id||Panel.PromptText!=first.Draft)throw new InvalidOperationException("Cold startup did not restore saved tabs, selection and draft");
            ShowFloatingBall();Panel.Open();await Task.Delay(400);
            if(!Native.IsWindowVisible(Ball!.Handle)||!Native.IsWindowVisible(Panel.Handle))throw new InvalidOperationException("Restored floating windows are not visible");
            Panel.SelectConversationForDiagnostic(second.Id);
            if(Panel.ResponseText!="fixture reply")throw new InvalidOperationException("Restored history is unavailable");
            string appearanceChecks=Environment.GetCommandLineArgs().Contains("--conversation-appearance-test")?await RuntimeDiagnostics.VerifyConversationAppearanceAsync(Panel,output):"";
            if(closedFixture!=null)appearanceChecks+=await RuntimeDiagnostics.VerifyConversationHistoryAsync(Panel,storage,closedFixture,output);
            if(StartupDiagnostics.ErrorCount!=0)throw new InvalidOperationException("Unhandled exception during cold startup");
            Panel.Close();Panel=null;
            await File.WriteAllTextAsync(Path.Combine(output,"startup-test.txt"),"PASS: cold startup creates transparent restored panel before ball, restores saved tabs/selection/draft/history and opens both native windows\n"+appearanceChecks+"No user database, screenshots, clipboard or model requests were used.\n");
        }
        catch(Exception error)
        {
            StartupDiagnostics.Record("startup-test",error);
            await File.WriteAllTextAsync(Path.Combine(output,"startup-test.txt"),$"FAIL: {error.GetType().Name}; HRESULT: 0x{error.HResult:X8}\n");Environment.ExitCode=1;
        }
        finally
        {
            Panel?.Close();Panel=null;
            if(Directory.Exists(storage))Directory.Delete(storage,true);
            Shutdown();
        }
    }
}
