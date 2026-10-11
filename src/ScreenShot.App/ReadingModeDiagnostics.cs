using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using ScreenShot.Core;

namespace ScreenShot.App;

public partial class App
{
    private async Task<string> VerifyReadingModeAsync(string output)
    {
        string storage=Path.Combine(Path.GetTempPath(),"AIScreenShot-reading-mode-"+Guid.NewGuid().ToString("N"));
        var previous=Panel;FloatingPanelWindow? panel=null;
        var originalAppearance=Settings.PanelAppearance;
        var replyGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string reply=string.Concat(Enumerable.Repeat("生成的全屏流式回复，保留专业符号与文本选择。",70));
        async Task<SendResult> Send(SendRequest request,Action<AgentReplyUpdate> update,CancellationToken token)
        {
            if(request.IsTitleRequest){update(new(AgentReplyPhase.Completed,"生成标题"));return new(SendState.Sent,"generated");}
            update(new(AgentReplyPhase.Submitted));update(new(AgentReplyPhase.Thinking,reply));
            await replyGate.Task.WaitAsync(token);update(new(AgentReplyPhase.Completed,reply));return new(SendState.Sent,"generated");
        }
        void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        static bool SameBounds(Native.Rect a,Native.Rect b)=>a.Left==b.Left&&a.Top==b.Top&&a.Right==b.Right&&a.Bottom==b.Bottom;
        try
        {
            var fixture=new Conversation{Client="Agent",ManualTitle=true,Draft="保留输入草稿",Messages=[new(){Role="assistant",Client="Agent",Text=string.Join("\n\n",Enumerable.Repeat("生成的阅读测试段落。数学符号：\\(\\Sigma=\\{w,e,b\\}\\)。",24)),State="Sent"}]};
            using(var store=new ConversationStore(storage)){store.Save(fixture);store.Select(fixture.Id);}
            panel=new FloatingPanelWindow(this,sendAgent:Send,conversationDirectory:storage);Panel=panel;panel.Open();await Task.Delay(400);
            var root=(FrameworkElement)panel.Content;
            byte[] image=await RuntimeDiagnostics.FixtureAsync();await panel.PresentAsync(image,new(0,0,320,200));panel.PromptText=fixture.Draft;await Task.Delay(250);
            double expandedScroll=panel.MessageViewportHeight;
            Invoke(panel.ComposerToggleButton);await Task.Delay(60);
            if(!panel.IsComposerCollapsed)throw new InvalidOperationException("Composer toggle did not change reading state");
            if(originalAppearance.EnableAnimations&&new Windows.UI.ViewManagement.UISettings().AnimationsEnabled&&panel.IsComposerTransitioning&&
                (panel.ComposerSurface.Opacity>=1||panel.ComposerTranslation<=0))throw new InvalidOperationException("Composer exit has no downward fade");
            // Retarget a running exit, then collapse again without losing its captured draft.
            Invoke(panel.ComposerToggleButton);await Task.Delay(30);Invoke(panel.ComposerToggleButton);await Task.Delay(250);root.UpdateLayout();
            if(panel.ComposerSurface.Visibility!=Visibility.Collapsed||panel.ComposerToggleButton.Visibility!=Visibility.Visible||panel.PromptText!=fixture.Draft||!panel.CurrentImage.SequenceEqual(image))throw new InvalidOperationException("Collapse lost input or left no expansion entry");
            double collapsedScroll=panel.MessageViewportHeight;
            if(collapsedScroll<=expandedScroll+60)throw new InvalidOperationException("Collapsed composer did not release reading space");
            panel.NewConversation();string second=panel.CurrentConversationId;
            if(!panel.IsComposerCollapsed||panel.ComposerSurface.Visibility!=Visibility.Collapsed||!panel.CurrentImage.SequenceEqual(image))throw new InvalidOperationException("New tab lost shared reading state or screenshot");
            Invoke(panel.ComposerToggleButton);await Task.Delay(250);panel.PromptText="第二个草稿";
            panel.SelectConversationForDiagnostic(fixture.Id);
            if(panel.PromptText!=fixture.Draft||panel.ComposerSurface.Visibility!=Visibility.Visible)throw new InvalidOperationException("Restoring input changed another tab's draft");
            if(!Native.GetWindowRect(panel.Handle,out var floating))throw new InvalidOperationException("No floating window bounds");
            var info=new Native.MonitorInfo{Size=(uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MonitorInfo>()};
            if(!Native.GetMonitorInfo(Native.MonitorFromPoint(new Native.Point{X=(floating.Left+floating.Right)/2,Y=(floating.Top+floating.Bottom)/2},2),ref info))throw new InvalidOperationException("No fullscreen monitor");
            Invoke(panel.FullScreenButton);await Task.Delay(250);root.UpdateLayout();
            if(!panel.IsFullScreen||panel.IsToolbarVisible||!panel.IsMessageVisible||!Native.GetWindowRect(panel.Handle,out var full)||!SameBounds(full,info.Monitor))throw new InvalidOperationException("Fullscreen did not fill the current monitor and hide toolbar");
            if(Math.Abs(panel.TabRow.TransformToVisual(root).TransformPoint(new()).Y)>1||Math.Abs(panel.MessageSurface.ActualHeight-root.ActualHeight)>1||panel.PaneShapes is not [{Diameter:0}])throw new InvalidOperationException("Fullscreen tabs are not at the top or message surface does not stretch");
            if(Native.IsWindowVisible(panel.ShadowWindow.Handle)||Ball!=null&&Native.IsWindowVisible(Ball.Handle))throw new InvalidOperationException("Fullscreen retained floating decorations");
            panel.ResizeForDiagnostic(PanelResizePane.Message,PanelResizeEdges.Right,50,0);
            panel.FollowBall(floating,new Native.Rect{Left=0,Top=0,Right=52,Bottom=52});
            if(!Native.GetWindowRect(panel.Handle,out var fixedFull)||!SameBounds(fixedFull,full))throw new InvalidOperationException("Fullscreen was moved by floating resize or ball following");
            foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark})
            {
                root.RequestedTheme=theme;Invoke(panel.ComposerToggleButton);await Task.Delay(250);root.UpdateLayout();
                await File.WriteAllBytesAsync(Path.Combine(output,$"reading-fullscreen-{theme}.png"),await RuntimeDiagnostics.RenderAsync(root));
                if(panel.ComposerSurface.Visibility!=(panel.IsComposerCollapsed?Visibility.Collapsed:Visibility.Visible))throw new InvalidOperationException("Composer visibility disagrees with toggle in fullscreen");
            }
            Invoke(panel.ComposerSendButton);await Task.Delay(120);root.UpdateLayout();
            if(!panel.IsBusy||!panel.CanStopWaiting||panel.ResponseView!.ActualWidth<=floating.Right-floating.Left)throw new InvalidOperationException("Fullscreen streaming reply retained floating width or lost waiting controls");
            var sendTask=panel.ComposerSendTask!;Invoke(panel.ComposerToggleButton);await Task.Delay(250);
            if(!panel.CanStopWaiting||panel.ComposerSurface.Visibility!=Visibility.Collapsed)throw new InvalidOperationException("Hiding input removed the independent stop control");
            panel.SelectConversationForDiagnostic(second);replyGate.TrySetResult();await sendTask.WaitAsync(TimeSpan.FromSeconds(5));panel.SelectConversationForDiagnostic(fixture.Id);
            if(!panel.IsFullScreen||panel.IsBusy||panel.ResponseText!=reply||!panel.IsComposerCollapsed)throw new InvalidOperationException("Background completion changed fullscreen or collapsed input state");
            Invoke(panel.ComposerToggleButton);await Task.Delay(250);
            panel.SelectConversationForDiagnostic(second);panel.NewConversation();
            if(!panel.IsFullScreen||panel.ComposerSurface.Visibility!=Visibility.Visible)throw new InvalidOperationException("Fullscreen did not survive switching or creating tabs");
            EndCaptureSession(hidePanel:!panel.IsFullScreen);panel.HideForOperation();if(Native.IsWindowVisible(panel.Handle))throw new InvalidOperationException("Fullscreen did not hide for capture operation");
            panel.RestoreAfterOperation();await Task.Delay(80);
            if(!panel.IsFullScreen||!Native.IsWindowVisible(panel.Handle)||!Native.GetWindowRect(panel.Handle,out var restoredFull)||!SameBounds(restoredFull,full))throw new InvalidOperationException("Operation restore changed fullscreen geometry");
            // The same production exit method is called by Escape and the fullscreen icon.
            if(!panel.ExitFullScreen())throw new InvalidOperationException("Escape exit did not restore the floating panel");await Task.Delay(80);
            if(panel.IsFullScreen||!panel.IsToolbarVisible||!Native.GetWindowRect(panel.Handle,out var restored)||!SameBounds(restored,floating))throw new InvalidOperationException("Fullscreen exit did not restore original bounds and toolbar");
            panel.ToggleToolbar();await Task.Delay(250);Native.GetWindowRect(panel.Handle,out var toolbarHidden);
            Invoke(panel.FullScreenButton);Invoke(panel.FullScreenButton);await Task.Delay(80);
            if(panel.IsToolbarVisible||!Native.GetWindowRect(panel.Handle,out var restoredHidden)||!SameBounds(restoredHidden,toolbarHidden))throw new InvalidOperationException("Exit overwrote a previously hidden toolbar");
            panel.ApplyAppearance(originalAppearance with {EnableAnimations=false});Invoke(panel.ComposerToggleButton);
            if(panel.IsComposerTransitioning||panel.ComposerSurface.Visibility!=Visibility.Collapsed)throw new InvalidOperationException("Disabled motion did not collapse instantly");
            await panel.PresentAsync(image,new(0,0,320,200));
            if(panel.IsComposerCollapsed||panel.ComposerSurface.Visibility!=Visibility.Visible||!panel.HasScreenshotPreview)throw new InvalidOperationException("New capture did not reveal the preserved input surface");
            Invoke(panel.FullScreenButton);panel.Collapse();await Task.Delay(80);panel.Open();await Task.Delay(80);
            if(panel.IsFullScreen||!Native.IsWindowVisible(panel.Handle))throw new InvalidOperationException("Collapse/reopen left a stuck fullscreen panel");
            panel.SelectConversationForDiagnostic(fixture.Id);
            await File.WriteAllBytesAsync(Path.Combine(output,"reading-floating.png"),await RuntimeDiagnostics.RenderAsync(root));
            return "PASS: native fullscreen/composer icon Invoke, current monitor bounds, top tabs and stretching body, floating state restoration, hidden toolbar preservation, no fullscreen shadow/ball/resize/follow, operation restore, generated Light/Dark previews\nPASS: downward fade, rapid reverse, reclaimed reading space, retained input/image, shared tab reading state, disabled motion, new capture expands input and collapse/reopen\nPASS: full-width streaming reply and background completion preserve fullscreen and input state; stop remains available when input is hidden; injected sender only\n";
        }
        finally
        {
            panel?.Close();Panel=previous;ShowFloatingBallIfWanted();
            if(Directory.Exists(storage))Directory.Delete(storage,true);
        }
    }
}
