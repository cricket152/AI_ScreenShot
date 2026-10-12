using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using ScreenShot.Core;

namespace ScreenShot.App;

public partial class App
{
    private async Task<string> VerifyAnimationsAsync(string output)
    {
        var previous=Panel;var original=Animations.Appearance;
        FloatingPanelWindow? panel=null;SettingsWindow? settings=null;
        var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var submitted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<AgentReplyUpdate>? stream=null;
        async Task<SendResult> Send(SendRequest request,Action<AgentReplyUpdate> update,CancellationToken token)
        {
            if(request.IsTitleRequest)return new(SendState.Failed,"generated title skipped");
            update(new(AgentReplyPhase.Submitted));stream=update;submitted.TrySetResult();
            await gate.Task.WaitAsync(token);update(new(AgentReplyPhase.Completed,"生成的最终回复"));return new(SendState.Sent,"generated completion");
        }
        static void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        int checks=0;void Check(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);checks++;}
        try
        {
            string storage=Path.Combine(output,"animation-fixtures",Guid.NewGuid().ToString("N"));
            ApplyAppearance(original with{EnableAnimations=true,Motion=new()});
            panel=new FloatingPanelWindow(this,sendAgent:Send,conversationDirectory:storage);Panel=panel;panel.Open();await Task.Delay(400);
            var root=(FrameworkElement)panel.Content;var probe=panel.ComposerSendButton;
            ApplyAppearance(original with{EnableAnimations=true,ShowShadow=true,ShadowSpread=12,ShadowOpacity=0.25,Motion=new(){Style=AnimationStyle.Minimal,Speed=0.5}});
            bool systemMotion=Animations.Profile(AnimationArea.Panel).Enabled;
            double toolbarHeight=panel.ToolbarSurface.ActualHeight,messageHeight=panel.MessageSurface.ActualHeight;
            panel.Collapse();
            Check(!systemMotion||Native.IsWindowVisible(panel.Handle)&&panel.ToolbarSurface.Height>0&&panel.MessageSurface.Height>0,"Minimal collapse removed layout before fading");
            await Task.Delay(90);
            if(systemMotion)
            {
                Check(panel.IsTransitioning&&Native.IsWindowVisible(panel.Handle)&&Math.Abs(panel.ToolbarSurface.ActualHeight-toolbarHeight)<1&&Math.Abs(panel.MessageSurface.ActualHeight-messageHeight)<1,"Minimal fade changed pane geometry or hid HWND early");
                Check(panel.MessageSurface.Opacity>0&&panel.MessageSurface.Opacity<1&&panel.ShadowWindow.ConstantAlpha>0&&panel.ShadowWindow.ConstantAlpha<255,"Minimal close did not fade body and shadow");
                Check(Math.Abs(panel.ShadowWindow.ConstantAlpha-panel.PaneShapes.Max(shape=>shape.Opacity)*255)<=1,"Shadow and body opacity diverged during exit");
            }
            await Task.Delay(450);
            Check(!Native.IsWindowVisible(panel.Handle)&&!Native.IsWindowVisible(panel.ShadowWindow.Handle),"Minimal fade completion left either HWND visible");
            panel.Open();int maskCount=panel.ShadowWindow.MaskRenderCount;
            Check(!systemMotion||panel.ShadowWindow.ConstantAlpha==0&&panel.PaneShapes.Count==2&&panel.ShadowWindow.LastPixels.Any(value=>value>0),"Opening did not prepare the full shadow mask at zero opacity");
            await Task.Delay(70);
            if(systemMotion)
            {
                Check(panel.MessageSurface.Opacity>0&&panel.MessageSurface.Opacity<1&&Native.IsWindowVisible(panel.ShadowWindow.Handle)&&panel.ShadowWindow.ConstantAlpha>0,"Opening showed body without its shadow");
                Check(Math.Abs(panel.ShadowWindow.ConstantAlpha-panel.PaneShapes.Max(shape=>shape.Opacity)*255)<=1,"Shadow and body opacity diverged during entrance");
            }
            await Task.Delay(450);
            Check(panel.ShadowWindow.MaskRenderCount-maskCount<=1,"A shared opacity fade rebuilt the shadow blur each frame");
            Check(panel.ShadowWindow.FallbackCount==0,"Shadow synchronization triggered a native fallback");
            panel.ToggleMessage();await Task.Delay(70);panel.ToggleMessage();await Task.Delay(450);
            Check(panel.MessageSurface.Visibility==Visibility.Visible&&panel.MessageSurface.Opacity==1&&Native.IsWindowVisible(panel.Handle),"Minimal pane reversal committed stale visibility");
            foreach(var style in Enum.GetValues<AnimationStyle>())
            {
                ApplyAppearance(original with{EnableAnimations=true,Motion=new(){Style=style,Speed=2}});
                int before=panel.MotionScope.StartedCount;
                panel.MotionScope.Enter(probe,AnimationArea.Messages);
                bool enabled=Animations.Profile(AnimationArea.Messages).Enabled;
                Check(panel.MotionScope.StartedCount==before+(enabled?1:0),"Preset failed to honor effective Windows animation preference");
                var completionDeadline=Stopwatch.StartNew();
                while(panel.MotionScope.ActiveCount>0&&completionDeadline.Elapsed<TimeSpan.FromSeconds(3))await Task.Delay(30);
                await File.AppendAllTextAsync(Path.Combine(output,"animation-timing.txt"),$"{style}: active={panel.MotionScope.ActiveCount}, wait={completionDeadline.Elapsed.TotalMilliseconds:0}ms, fallback={panel.MotionScope.FallbackCount}\n");
                Check(panel.MotionScope.ActiveCount==0,"Composition batch failed to finish");
                var visual=ElementCompositionPreview.GetElementVisual(probe);
                Check(visual.Opacity==1&&visual.Scale==Vector3.One,"Completed animation left opacity/scale behind");
            }
            ApplyAppearance(original with{EnableAnimations=true,Motion=new(){Speed=0.5}});
            for(int i=0;i<10;i++){panel.ToggleToolbarSize();await Task.Delay(12);}
            ApplyAppearance(Animations.Appearance with{Motion=new(){Style=AnimationStyle.Off}});
            Check(!panel.IsTransitioning&&!panel.IsComposerTransitioning&&panel.MotionScope.ActiveCount==0,"Off did not settle all pending animation kinds");
            Check(panel.ToolbarSurface.Opacity==1&&panel.MessageSurface.Opacity==1,"Interrupted panes retained opacity");
            ApplyAppearance(original with{EnableAnimations=true,Motion=new(){Toolbar=false,Panel=false,Tabs=false,Messages=false}});
            int disabledCount=panel.MotionScope.StartedCount;
            panel.ToggleToolbarSize();panel.ToggleMessage();panel.ToggleMessage();panel.NewConversation();
            Check(!panel.IsTransitioning&&panel.MotionScope.StartedCount==disabledCount,"Local switches did not make their areas immediate");
            ApplyAppearance(original with{EnableAnimations=true,Motion=new(){Style=AnimationStyle.Elastic}});
            string first=panel.CurrentConversationId;panel.PromptText="第一个标签草稿";var firstTab=panel.SelectedTab;
            panel.NewConversation();string second=panel.CurrentConversationId;panel.PromptText="第二个标签草稿";
            for(int i=0;i<8;i++){panel.SelectConversationForDiagnostic(first);panel.SelectConversationForDiagnostic(second);}
            panel.SelectConversationForDiagnostic(first);Check(ReferenceEquals(firstTab,panel.SelectedTab)&&panel.PromptText=="第一个标签草稿","Switching rebuilt tabs or lost isolated drafts");
            panel.ReorderCurrentForDiagnostic(1);panel.CloseCurrentConversation();await Task.Delay(300);
            Check(panel.RenderedTabCount==panel.OpenConversationCount,"Closed tab retained an invisible layout slot");
            Check(panel.CurrentConversationId==second&&panel.PromptText=="第二个标签草稿","Closing/reordering changed another conversation");
            using(var stored=new ConversationStore(storage))
            {
                var records=stored.Load();Check(records.Single(r=>r.Id==first).IsOpen==false&&records.Single(r=>r.Id==second).Order<records.Single(r=>r.Id==first).Order,"Reordered/closed session was not persisted");
            }
            panel.NewConversation();panel.CloseCurrentConversation();panel.NewConversation();await Task.Delay(300);
            using(var stored=new ConversationStore(storage))
            {Check(panel.RenderedConversationIds.SequenceEqual(stored.Load().Where(r=>r.IsOpen).OrderBy(r=>r.Order).Select(r=>r.Id)),"Rapid close/create left visual tabs in a different order from session state");}
            panel.SelectConversationForDiagnostic(second);
            ApplyAppearance(original with{EnableAnimations=true,Motion=new()});panel.ComposerModeSelector.SelectedIndex=1;
            var send=panel.SendAgentAsync();await submitted.Task.WaitAsync(TimeSpan.FromSeconds(5));await Task.Delay(100);
            int streamCount=panel.MotionScope.StartedCount;
            var elapsed=Stopwatch.StartNew();
            for(int i=0;i<40;i++)stream!(new(AgentReplyPhase.Thinking,$"生成的流式片段 {i}"));
            elapsed.Stop();
            Check(panel.MotionScope.StartedCount==streamCount&&panel.IsBusy,"Stream snapshots replayed container entrance or stopped work");
            panel.NewConversation();string background=panel.CurrentConversationId;
            gate.TrySetResult();await send.WaitAsync(TimeSpan.FromSeconds(5));panel.SelectConversationForDiagnostic(second);
            Check(!panel.IsBusy&&panel.ResponseText=="生成的最终回复","Background reply failed after animation and tab switching");
            panel.SelectConversationForDiagnostic(background);
            panel.ReorderCurrentForDiagnostic(-1);
            using(var stored=new ConversationStore(storage))
            {var records=stored.Load();Check(records.Select(r=>r.Order).Distinct().Count()==records.Count,"Reordering open tabs collided with closed history slots");}
            byte[] image=await RuntimeDiagnostics.FixtureAsync();await panel.PresentAsync(image,new(0,0,320,200));
            byte[] replacement=await RuntimeDiagnostics.FixtureAsync(70);await panel.PresentAsync(replacement,new(0,0,320,200));
            Check(panel.CurrentImage.SequenceEqual(replacement),"Capture replacement lost the current screenshot after removing its composer preview");
            for(int i=0;i<6;i++){panel.Collapse();panel.Open();Invoke(panel.ComposerToggleButton);await Task.Delay(15);}
            ApplyAppearance(original with{EnableAnimations=false});
            Check(Native.IsWindowVisible(panel.Handle)&&!panel.IsTransitioning&&!panel.IsComposerTransitioning&&panel.MotionScope.ActiveCount==0,"Rapid reverse and disabling left a hidden or running pane");
            var titlePoint=panel.MessageDragTitle.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(2,2));
            Check(panel.MoveAtForDiagnostic(titlePoint,12,8)&&panel.PaneShapes.Count>0,"Movement lost pane hit geometry");
            settings=new SettingsWindow(this,(_,_)=>true);settings.Activate();await Task.Delay(150);
            var navigation=(NavigationView)((Border)settings.Content).Child;
            navigation.SelectedItem=navigation.MenuItems.OfType<NavigationViewItem>().Single(item=>Equals(item.Tag,"motion"));await Task.Delay(100);
            Check(((TextBlock)((StackPanel)((Grid)navigation.Content).Children[0]).Children[0]).Text=="动效设置","Standalone motion navigation failed to update the right heading");
            settings.MotionStyleSelector.SelectedIndex=(int)AnimationStyle.Lively;settings.MotionSpeedSlider.Value=150;settings.ToolbarMotionToggle.IsOn=false;
            Check(Animations.Appearance.Motion==settings.AppearanceDraft.Motion&&Animations.Appearance.Motion.Speed==1.5&&!Animations.Appearance.Motion.Toolbar,"Settings draft failed to preview motion immediately");
            settings.BorderWidthSlider.Value=Settings.PanelAppearance.BorderThickness==3?0.5:3;
            var motionSave=settings.AppearanceToSave(onlyMotion:true);var appearanceSave=settings.AppearanceToSave(onlyMotion:false);
            Check(motionSave==Settings.PanelAppearance with{EnableAnimations=settings.AppearanceDraft.EnableAnimations,Motion=settings.AppearanceDraft.Motion},"Saving motion also persisted an unrelated appearance draft");
            Check(appearanceSave.Motion==Settings.PanelAppearance.Motion&&appearanceSave.EnableAnimations==Settings.PanelAppearance.EnableAnimations&&appearanceSave.BorderThickness==settings.AppearanceDraft.BorderThickness,"Saving appearance also persisted an unrelated motion draft");
            string motionFile=Path.Combine(storage,"generated-motion-settings.json");
            await File.WriteAllTextAsync(motionFile,System.Text.Json.JsonSerializer.Serialize(new Settings{PanelAppearance=motionSave}));
            Check(System.Text.Json.JsonSerializer.Deserialize<Settings>(await File.ReadAllTextAsync(motionFile))!.PanelAppearance==motionSave.Normalize(),"Standalone motion save candidate failed generated JSON persistence");
            navigation.SelectedItem=navigation.MenuItems.OfType<NavigationViewItem>().First();
            Check(settings.AppearanceDraft.Motion.Style==AnimationStyle.Lively,"Navigation discarded an unsaved motion draft");
            settings.Close();settings=null;Check(Animations.Appearance.Motion==Settings.PanelAppearance.Motion,"Closing settings failed to restore saved motion");
            Check(panel.MotionScope.FallbackCount==0,"Unexpected Composition fallback in native motion diagnostics");
            ApplyAppearance(original with{EnableAnimations=true,Motion=new()});
            probe=panel.ComposerSendButton;
            using(var failed=Animations.CreateScope(()=>throw new InvalidOperationException("injected animation failure")))
            {
                failed.Enter(probe,AnimationArea.Messages);
                if(Animations.Profile(AnimationArea.Messages).Enabled)
                    Check(failed.FallbackCount==1&&failed.ActiveCount==0&&ElementCompositionPreview.GetElementVisual(probe).Opacity==1&&ElementCompositionPreview.GetElementVisual(probe).Scale==Vector3.One,"Animation failure failed to restore its final visible state");
            }
            int pendingCount=panel.MotionScope.StartedCount;
            panel.MotionScope.Enter(probe,AnimationArea.Messages);panel.Close();
            Check(panel.MotionScope.StartedCount==pendingCount+(Animations.Profile(AnimationArea.Messages).Enabled?1:0),"Close test did not start an enabled animation");
            Check(panel.MotionScope.IsDisposed&&panel.MotionScope.ActiveCount==0,"Window close retained active Composition resources");
            string report=$"PASS: {checks} native motion assertions; minimal close retains layout through fade, full shadow prepared before entrance, synchronized body/shadow opacity and cached blur, pane reversal, standalone motion heading, presets, speed, local/global opt-out, cancellation, native springs, tab reuse/reorder/close persistence, isolated drafts, 40 stream snapshots without repeated entrance, background completion, generated capture replacement, rapid pane/composer reverse, move geometry, settings preview/navigation/close rollback, injected animation failure fallback and disposal.\n40 generated stream callbacks: {elapsed.Elapsed.TotalMilliseconds:0.0}ms (not a frame-rate benchmark).\nSystem animation preference: {new Windows.UI.ViewManagement.UISettings().AnimationsEnabled}. Mixed-DPI/multi-monitor physical gestures require desktop acceptance.\nGenerated fixtures only; no user content, clipboard or real model request.\n";
            await File.WriteAllTextAsync(Path.Combine(output,"animation-test.txt"),report);return report;
        }
        finally{gate.TrySetResult();settings?.Close();panel?.Close();Panel=previous;ApplyAppearance(original);}
    }
}
