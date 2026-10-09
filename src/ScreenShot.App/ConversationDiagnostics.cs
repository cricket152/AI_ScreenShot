using Microsoft.UI.Xaml;
using ScreenShot.Core;

namespace ScreenShot.App;

public partial class App
{
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
