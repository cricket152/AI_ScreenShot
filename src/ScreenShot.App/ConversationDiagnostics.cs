using Microsoft.UI.Xaml;
using ScreenShot.Core;

namespace ScreenShot.App;

public partial class App
{
    private async Task<string> VerifyCaptureRoutingAsync(string output)
    {
        string storage=Path.Combine(Path.GetTempPath(),"AIScreenShot-capture-routing-"+Guid.NewGuid().ToString("N"));
        var previous=Panel;
        FloatingPanelWindow? panel=null;
        var requests=new List<(string Target,SendRequest Request)>();
        var pending=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool retryFailed=true;
        Workspace Destination(string name)
        {
            string path=Path.Combine(storage,"workspaces",name);Directory.CreateDirectory(path);return new(name,path);
        }
        async Task<SendResult> Send(string target,SendRequest request,Action<CodexReplyUpdate> update,CancellationToken token)
        {
            if(request.IsTitleRequest){update(new(CodexReplyPhase.Completed,"生成的测试标题"));return new(SendState.Sent,"title");}
            requests.Add((target,request));
            if(request.Prompt=="retry fixture"&&retryFailed){retryFailed=false;return new(SendState.Failed,"generated failure");}
            if(request.Workspace?.Name=="third"&&target=="Codex")return new(SendState.Unknown,"generated unknown receipt");
            update(new(CodexReplyPhase.Submitted));
            if(request.Prompt=="slow fixture")await pending.Task.WaitAsync(token);
            update(new(CodexReplyPhase.Completed,"生成的截图测试回复"));return new(SendState.Sent,"completed");
        }
        Task<SendResult> Codex(SendRequest request,Action<CodexReplyUpdate> update,CancellationToken token)=>Send("Codex",request,update,token);
        Task<SendResult> Agent(SendRequest request,Action<AgentReplyUpdate> update,CancellationToken token)=>Send("Agent",request,value=>update(new(value.Phase switch{CodexReplyPhase.Submitted=>AgentReplyPhase.Submitted,CodexReplyPhase.Completed=>AgentReplyPhase.Completed,_=>AgentReplyPhase.Thinking},value.Text,value.Status)),token);
        async Task Invoke()
        {
            if(!panel!.CanSendFromComposer)throw new InvalidOperationException("Screenshot composer is unexpectedly disabled");
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(panel.ComposerSendButton).GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            await Task.Delay(30);await panel.ComposerSendTask!.WaitAsync(TimeSpan.FromSeconds(5));
        }
        void VerifyImage(byte[] expected)
        {
            if(!panel!.CurrentImage.SequenceEqual(expected)||!panel.HasScreenshotPreview)throw new InvalidOperationException("Selected conversation lost the latest capture or its thumbnail");
        }
        try
        {
            Directory.CreateDirectory(storage);
            byte[] firstImage=await RuntimeDiagnostics.FixtureAsync(),secondImage=await RuntimeDiagnostics.FixtureAsync(70);
            panel=new FloatingPanelWindow(this,sendCodex:Codex,sendAgent:Agent,conversationDirectory:storage);Panel=panel;panel.Open();
            string first=panel.CurrentConversationId;panel.SetWorkspaceForDiagnostic(Destination("first"));
            panel.NewConversation();string second=panel.CurrentConversationId;
            panel.SetWorkspaceForDiagnostic(Destination("second"));panel.ComposerModeSelector.SelectedIndex=1;panel.PromptText="existing draft fixture";
            panel.SelectConversationForDiagnostic(first);await panel.PresentAsync(firstImage,new(0,0,320,200));
            panel.SelectConversationForDiagnostic(second);VerifyImage(firstImage);
            if(panel.PromptText!="existing draft fixture"||!panel.CanSendToCodex||!panel.CanSendToAgent)throw new InvalidOperationException("Capture adoption changed another tab's draft or disabled a target");
            await Invoke();
            if(requests.Count!=1||requests[0].Target!="Agent"||requests[0].Request.Workspace!=Destination("second")||!requests[0].Request.Png.SequenceEqual(firstImage))throw new InvalidOperationException("Existing tab sent to the capture origin instead of its own workspace");
            panel.SelectConversationForDiagnostic(first);panel.ComposerModeSelector.SelectedIndex=0;await Invoke();
            if(requests[^1].Target!="Codex"||requests[^1].Request.Workspace!=Destination("first")||!requests[^1].Request.Png.SequenceEqual(firstImage))throw new InvalidOperationException("Original tab lost its capture or Codex destination");
            int fileCount=Directory.GetFiles(Path.Combine(storage,"screenshots")).Length;
            panel.SelectConversationForDiagnostic(second);panel.SelectConversationForDiagnostic(first);
            if(panel.CanSendToCodex||!panel.CanSendToAgent||Directory.GetFiles(Path.Combine(storage,"screenshots")).Length!=fileCount)throw new InvalidOperationException("Switching back unlocked a submitted capture or wrote duplicate draft images");
            panel.NewConversation();string third=panel.CurrentConversationId;panel.SetWorkspaceForDiagnostic(Destination("third"));VerifyImage(firstImage);await Invoke();
            if(requests[^1].Request.Workspace!=Destination("third")||!requests[^1].Request.Png.SequenceEqual(firstImage)||panel.CanSendToCodex)throw new InvalidOperationException("Post-capture new tab cannot send or lost Unknown protection");
            panel.CloseCurrentConversation();panel.SelectConversationForDiagnostic(third);
            if(panel.CanSendToCodex)throw new InvalidOperationException("Tab switching unlocked an Unknown capture submission");
            panel.ComposerModeSelector.SelectedIndex=1;panel.PromptText="retry fixture";await Invoke();
            if(!panel.CanSendFromComposer)throw new InvalidOperationException("Failed image send cannot retry");
            await Invoke();if(panel.CanSendFromComposer)throw new InvalidOperationException("Sent image retry became duplicate-send eligible");

            panel.NewConversation();string fourth=panel.CurrentConversationId;panel.SetWorkspaceForDiagnostic(Destination("fourth"));panel.PromptText="slow fixture";
            var slowSend=panel.SendAgentAsync();await Task.Delay(60);
            if(!panel.IsBusy||slowSend.IsCompleted)throw new InvalidOperationException("Generated pending send did not start");
            SendRequest snapshot=requests[^1].Request;
            panel.SelectConversationForDiagnostic(first);await panel.PresentAsync(secondImage,new(0,0,320,200));
            panel.SelectConversationForDiagnostic(fourth);VerifyImage(firstImage);
            if(!panel.IsBusy||panel.CanSendFromComposer)throw new InvalidOperationException("Adopting another capture mutated an active conversation's request");
            panel.NewConversation();VerifyImage(secondImage);panel.PromptText="new while busy fixture";await Invoke();
            if(!requests[^1].Request.Png.SequenceEqual(secondImage)||!snapshot.Png.SequenceEqual(firstImage))throw new InvalidOperationException("New tab did not receive the latest image or mutated the pending snapshot");
            pending.TrySetResult();await slowSend.WaitAsync(TimeSpan.FromSeconds(5));
            panel.SelectConversationForDiagnostic(fourth);VerifyImage(secondImage);
            if(!panel.CanSendFromComposer)throw new InvalidOperationException("Completed worker locked a newer screenshot");
            panel.PromptText="after busy fixture";await Invoke();
            if(!requests[^1].Request.Png.SequenceEqual(secondImage))throw new InvalidOperationException("Completed worker sent its old image instead of the new capture");
            using(var store=new ConversationStore(storage))
            {
                var history=store.Load().Single(chat=>chat.Id==fourth);
                var user=history.Messages.Where(message=>message.Role=="user").ToArray();
                if(user.Length!=2||!store.ReadImage(user[0].Image).SequenceEqual(firstImage)||!store.ReadImage(user[1].Image).SequenceEqual(secondImage)||!store.ReadImage(history.DraftImage).SequenceEqual(secondImage))throw new InvalidOperationException("Capture routing replaced historical message images or saved a mismatched draft");
            }
            await panel.PresentAsync(firstImage,new(0,0,320,200),()=>false);panel.NewConversation();VerifyImage(secondImage);
            panel.SelectConversationForDiagnostic(second);panel.PromptText="decode target draft fixture";panel.SelectConversationForDiagnostic(first);
            var decoded=panel.PresentAsync(firstImage,new(0,0,320,200));panel.SelectConversationForDiagnostic(second);await decoded;VerifyImage(firstImage);
            if(panel.PromptText!="decode target draft fixture")throw new InvalidOperationException("Switching during capture decoding erased the destination's draft");
            var stale=panel.PresentAsync(firstImage,new(0,0,320,200));var latest=panel.PresentAsync(secondImage,new(0,0,320,200));await Task.WhenAll(stale,latest);VerifyImage(secondImage);
            await File.WriteAllBytesAsync(Path.Combine(output,"capture-routing.png"),await RuntimeDiagnostics.RenderAsync((FrameworkElement)panel.Content));
            panel.Close();panel=new FloatingPanelWindow(this,sendCodex:Codex,sendAgent:Agent,conversationDirectory:storage);Panel=panel;panel.Open();await Task.Delay(100);VerifyImage(secondImage);
            panel.Close();panel=new FloatingPanelWindow(this,sendCodex:Codex,sendAgent:Agent);Panel=panel;
            await panel.PresentAsync(firstImage,new(0,0,320,200));panel.NewConversation();VerifyImage(firstImage);
            if(!panel.CanSendFromComposer)throw new InvalidOperationException("In-memory conversation adoption requires available storage");
            string result="PASS: latest screenshot and native thumbnail follow existing/new/reopened conversation tabs with their own workspaces, modes and prompt drafts; native Send Invoke delivers exact image and destination\nPASS: per-tab/target Sent and Unknown locks survive switching; Failed remains retryable; active workers keep snapshots, later adopt the newer capture and retain historical images\nPASS: canceled/stale decodes, switching during decode, persisted draft restoration and storage-free new tabs retain correct capture state\nNo user history, clipboard, desktop capture or real model request was used.\n";
            await File.WriteAllTextAsync(Path.Combine(output,"capture-routing-test.txt"),result);return result;
        }
        finally
        {
            pending.TrySetResult();panel?.Close();Panel=previous;
            if(Directory.Exists(storage))Directory.Delete(storage,true);
        }
    }

    private async Task<string> VerifyConcurrentConversationsAsync()
    {
        Panel?.Close();
        var replies=new Dictionary<string,TaskCompletionSource>();
        var started=new Dictionary<string,SendRequest>();
        var cancelled=new HashSet<string>();
        int titles=0;
        async Task<SendResult> Send(SendRequest request,Action<AgentReplyUpdate> update,CancellationToken token)
        {
            if(request.IsTitleRequest){titles++;await Task.Delay(100,token);update(new(AgentReplyPhase.Completed,"测试对话标题"));return new(SendState.Sent,"title");}
            started.Add(request.Prompt,request);var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);replies.Add(request.Prompt,completion);
            update(new(AgentReplyPhase.Submitted));update(new(AgentReplyPhase.Thinking,"partial "+request.Prompt));
            try{await completion.Task.WaitAsync(token);}
            catch(OperationCanceledException){cancelled.Add(request.Prompt);update(new(AgentReplyPhase.Unavailable,"partial "+request.Prompt,"cancelled"));return new(SendState.Unknown,"cancelled");}
            update(new(AgentReplyPhase.Completed,"reply "+request.Prompt,ModelName:"fixture-model"));return new(SendState.Sent,"completed");
        }
        string storage=Path.Combine(Path.GetTempPath(),"AIScreenShot-ui-conversations-"+Guid.NewGuid().ToString("N"));
        var panel=new FloatingPanelWindow(this,sendAgent:Send,conversationDirectory:storage);Panel=panel;panel.Open();await Task.Delay(80);
        panel.ComposerModeSelector.SelectedIndex=1;
        panel.SetWorkspaceForDiagnostic(new("original",AppContext.BaseDirectory));panel.PromptText="first";await Task.Delay(60);
        string first=panel.CurrentConversationId;var firstSend=panel.SendAgentAsync();await Task.Delay(60);
        panel.NewConversation();string second=panel.CurrentConversationId;
        if(panel.OpenConversationCount!=2||panel.CurrentWorkspace?.Name!="original"||panel.IsBusy||!string.IsNullOrEmpty(panel.PromptText)||panel.MessageCount!=0)throw new InvalidOperationException("New tab did not inherit only workspace and isolate runtime/draft/history");
        panel.SetWorkspaceForDiagnostic(new("changed",Path.GetTempPath()));panel.PromptText="second";await Task.Delay(60);
        var secondSend=panel.SendAgentAsync();await Task.Delay(60);
        if(started.Count!=2||firstSend.IsCompleted||secondSend.IsCompleted)throw new InvalidOperationException("Conversation workers did not run concurrently");
        panel.StopWaiting();await secondSend;
        if(!cancelled.SetEquals(["second"])||firstSend.IsCompleted)throw new InvalidOperationException("Stopping one tab interrupted another worker");
        panel.SelectConversationForDiagnostic(first);
        if(!panel.IsBusy||panel.CurrentWorkspace?.Name!="original"||panel.PromptText!="first")throw new InvalidOperationException("Switching tabs changed workspace or stopped the hidden worker");
        replies["first"].TrySetResult();await firstSend;
        if(panel.ResponseText!="reply first"||panel.ResponseModelName!="fixture-model")throw new InvalidOperationException("Hidden worker failed to render on its own tab");
        panel.RenameCurrentConversationForDiagnostic("手动测试标题");await Task.Delay(150);
        if(titles!=1||panel.CurrentConversationTitle!="手动测试标题")throw new InvalidOperationException("Asynchronous AI title overwrote a manual rename");
        panel.PromptText="follow-up";await Task.Delay(60);var follow=panel.SendAgentAsync();await Task.Delay(60);
        var history=started["follow-up"].Conversation!.History;
        if(history.Count!=2||history[0].Text!="first"||history[1].Text!="reply first"||history.Any(m=>m.Text.Contains("second")))throw new InvalidOperationException("Agent conversation context leaked across tabs");
        panel.CloseCurrentConversation();
        if(follow.IsCompleted||panel.OpenConversationCount!=1||panel.CurrentConversationId!=second)throw new InvalidOperationException("Closing a tab destroyed its live worker or another tab");
        replies["follow-up"].TrySetResult();await follow;panel.SelectConversationForDiagnostic(first);
        if(panel.ResponseText!="reply follow-up"||panel.OpenConversationCount!=2)throw new InvalidOperationException("Closed conversation could not reopen with background reply intact");
        var root=(FrameworkElement)panel.Content;await Task.Delay(120);
        await File.WriteAllBytesAsync(Path.Combine(AppContext.BaseDirectory,"diagnostics","floating-tabs.png"),await RuntimeDiagnostics.RenderAsync(root));
        // Move the actual ball through the same native ApplyBounds callback used by dragging.
        if(Ball!=null)
        {
            panel.Open();Native.GetWindowRect(panel.Handle,out var before);var ball=Ball.GetBounds();
            Ball.MoveForDiagnostic(ball.Left,ball.Top-20);await Task.Delay(60);Native.GetWindowRect(panel.Handle,out var after);
            if(after.Top==before.Top)throw new InvalidOperationException("Ball drag did not move its companion panes");
            var toolbar=panel.ToolbarSurface.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
            double scale=Native.GetDpiForWindow(panel.Handle)/96d;
            var aligned=Ball.GetBounds();
            if(Math.Abs((aligned.Top+aligned.Bottom)/2d-(after.Top+(toolbar.Y+panel.ToolbarSurface.ActualHeight/2)*scale))>1)throw new InvalidOperationException("Ball and toolbar centers are not aligned after dragging");
        }
        panel.Close();
        var restored=new FloatingPanelWindow(this,sendAgent:Send,conversationDirectory:storage);Panel=restored;restored.Open();await Task.Delay(100);
        if(restored.OpenConversationCount!=2||restored.CurrentConversationId!=first||restored.CurrentConversationTitle!="手动测试标题"||restored.PromptText!="follow-up"||restored.ResponseText!="reply follow-up"||restored.CurrentWorkspace?.Name!="original"||restored.ComposerModeSelector.SelectedIndex!=1||restored.ResponseModelName!="fixture-model")throw new InvalidOperationException("SQLite restart did not restore native tab selection, workspace, draft, mode, model and messages");
        restored.SelectConversationForDiagnostic(second);
        if(restored.ResponseText!="partial second"||restored.CurrentWorkspace?.Name!="changed"||restored.CanSendFromComposer)throw new InvalidOperationException("Unknown send state or isolated workspace was lost on restart");
        string appearanceChecks=await RuntimeDiagnostics.VerifyConversationAppearanceAsync(restored,Path.Combine(AppContext.BaseDirectory,"diagnostics"));
        restored.Close();
        Directory.Delete(storage,true);
        return "PASS: native compact tabs, per-tab drafts/workspaces, concurrent workers, isolated cancellation, closed-tab background completion/reopen, Agent scoped history and ball-follow movement with toolbar center alignment\nPASS: SQLite restores open tabs/selection/draft/mode/model/history/workspace and blocks duplicate Unknown sends across panel restart\n"+appearanceChecks;
    }
}
