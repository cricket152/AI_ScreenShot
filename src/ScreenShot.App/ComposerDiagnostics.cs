using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ScreenShot.Core;
using Windows.System;

namespace ScreenShot.App;

public partial class App
{
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window,out uint process);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from,uint to,bool attach);
    private async Task<string> VerifyComposerAsync(string output)
    {
        var previous=Panel;var appearance=Animations.Appearance;var foreground=Native.GetForegroundWindow();
        FloatingPanelWindow? panel=null;
        string storage=Path.Combine(output,"composer-fixtures",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(storage);
        var requests=new List<(string Target,SendRequest Request)>();
        var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<SendResult> Send(string target,SendRequest request,CancellationToken token)
        {
            if(request.IsTitleRequest)return new(SendState.Failed,"generated title skipped");
            requests.Add((target,request));
            if(request.Prompt=="busy fixture")await gate.Task.WaitAsync(token);
            return new(target=="Codex"?SendState.Unknown:SendState.Sent,"generated receipt");
        }
        static IEnumerable<T> Find<T>(DependencyObject node) where T:DependencyObject
        {
            if(node is T match)yield return match;
            for(int index=0;index<VisualTreeHelper.GetChildrenCount(node);index++)
                foreach(var child in Find<T>(VisualTreeHelper.GetChild(node,index)))yield return child;
        }
        TextBox Prompt()=>panel!.ComposerPromptEditor;
        async Task Focus(UIElement target)
        {
            panel!.Activate();
            uint currentThread=GetCurrentThreadId(),foregroundThread=GetWindowThreadProcessId(Native.GetForegroundWindow(),out _);
            bool attached=foregroundThread!=0&&foregroundThread!=currentThread&&AttachThreadInput(currentThread,foregroundThread,true);
            try{Native.SetForegroundWindow(panel.Handle);}
            finally{if(attached)AttachThreadInput(currentThread,foregroundThread,false);}
            await Task.Delay(60);
            if(target is Control control&&!control.Focus(FocusState.Programmatic))
            {
                await File.WriteAllTextAsync(Path.Combine(output,"composer-focus.txt"),$"Type={control.GetType().Name}; loaded={control.IsLoaded}; enabled={control.IsEnabled}; visible={control.Visibility}; size={control.ActualWidth:0}x{control.ActualHeight:0}; composerCollapsed={panel.IsComposerCollapsed}; panelVisible={Native.IsWindowVisible(panel.Handle)}; busy={panel.IsBusy}\n");
                throw new InvalidOperationException("Cannot focus generated composer control");
            }
            if(Native.GetForegroundWindow()!=panel.Handle)
            {
                await File.WriteAllTextAsync(Path.Combine(output,"composer-focus.txt"),$"foreground=0x{Native.GetForegroundWindow():X}; testHWND=0x{panel.Handle:X}; visible={Native.IsWindowVisible(panel.Handle)}; attached={attached}\n");
                throw new InvalidOperationException("Generated composer does not own foreground; keyboard injection skipped");
            }
        }
        async Task Press(VirtualKeyModifiers modifiers,int repeats=1)
        {
            if(Native.GetForegroundWindow()!=panel!.Handle)throw new InvalidOperationException("Composer lost foreground; keyboard injection skipped");
            var keys=new List<Native.Input>();
            void Key(VirtualKey key,bool up)=>keys.Add(new(){Type=1,Data=new(){Key=new(){Key=(ushort)key,Flags=up?2u:0u}}});
            if(modifiers.HasFlag(VirtualKeyModifiers.Control))Key(VirtualKey.Control,false);
            if(modifiers.HasFlag(VirtualKeyModifiers.Shift))Key(VirtualKey.Shift,false);
            for(int index=0;index<repeats;index++){Key(VirtualKey.Enter,false);Key(VirtualKey.Enter,true);}
            if(modifiers.HasFlag(VirtualKeyModifiers.Shift))Key(VirtualKey.Shift,true);
            if(modifiers.HasFlag(VirtualKeyModifiers.Control))Key(VirtualKey.Control,true);
            var inputs=keys.ToArray();
            if(Native.SendInput((uint)inputs.Length,inputs,Marshal.SizeOf<Native.Input>())!=inputs.Length)throw new InvalidOperationException("Generated composer keyboard input failed");
            await Task.Delay(120);
        }
        async Task CompleteSend()
        {if(panel!.ComposerSendTask==null)throw new InvalidOperationException("Ctrl+Enter did not start a composer send");await panel.ComposerSendTask.WaitAsync(TimeSpan.FromSeconds(5));}
        int checks=0;void Check(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);checks++;}
        try
        {
            ApplyAppearance(appearance with{EnableAnimations=false});
            panel=new FloatingPanelWindow(this,
                sendCodex:(request,_,token)=>Send("Codex",request,token),
                sendAgent:(request,_,token)=>Send("Agent",request,token),conversationDirectory:storage);Panel=panel;panel.Open();
            panel.SetWorkspaceForDiagnostic(new("fixture",storage));panel.ComposerModeSelector.SelectedIndex=1;
            await Task.Delay(180);
            panel.PromptText="text fixture";await Focus(Prompt());await Press(VirtualKeyModifiers.None);
            Check(requests.Count==0&&panel.PromptText.Contains('\r'),"Enter sent instead of inserting a native newline");
            panel.PromptText="text fixture";
            Check(panel.CanSendFromComposer&&panel.IsExpanded&&!panel.IsComposerCollapsed,"Generated composer is not ready for keyboard submission");
            await Press(VirtualKeyModifiers.Control);await CompleteSend();
            Check(requests.Count==1&&requests[0].Target=="Agent"&&requests[0].Request.Prompt=="text fixture"&&requests[0].Request.Png.Length==0,"Text-only Ctrl+Enter changed prompt or destination");
            byte[] image=await RuntimeDiagnostics.FixtureAsync();await panel.PresentAsync(image,new(0,0,320,200));
            var root=(FrameworkElement)panel.Content;root.UpdateLayout();
            Check(!Find<Image>(panel.ComposerSurface).Any()&&!Find<TextBlock>(panel.ComposerSurface).Any(text=>text.Text.Contains("截取屏幕")||text.Text.Contains("当前截图")),"Removed composer screenshot controls are still in the visual tree");
            Check(Find<TextBox>(panel.ComposerSurface).Count(box=>AutomationProperties.GetName(box)=="对话或截图提示词")==1&&Find<ComboBox>(panel.ComposerSurface).Count()==1&&panel.ComposerSendButton.IsLoaded,"Compact composer lost prompt, mode or Send");
            Check(panel.ComposerSurface.ActualHeight<=112,"Screenshot removal did not reclaim the preview row's height");
            Check(AutomationProperties.GetAcceleratorKey(panel.ComposerSendButton)=="Ctrl+Enter"&&AutomationProperties.GetHelpText(Prompt()).Contains("Ctrl+Enter"),"Send shortcut is not discoverable or has incorrect modifiers");
            await Focus(Prompt());await Press(VirtualKeyModifiers.Control);await CompleteSend();
            Check(requests.Count==2&&requests[^1].Target=="Agent"&&requests[^1].Request.Png.SequenceEqual(image)&&requests[^1].Request.EffectivePrompt==Settings.DefaultPrompt,"Blank prompt shortcut lost screenshot or default prompt");
            await Focus(panel.ComposerModeSelector);await Press(VirtualKeyModifiers.Control);
            Check(requests.Count==2,"Ctrl+Enter bypassed Sent duplicate protection");
            panel.ComposerModeSelector.SelectedIndex=0;await Focus(Prompt());await Press(VirtualKeyModifiers.Control);await CompleteSend();
            Check(requests.Count==3&&requests[^1].Target=="Codex"&&requests[^1].Request.Png.SequenceEqual(image),"Ctrl+Enter ignored the selected Codex mode");
            await Focus(panel.ComposerModeSelector);await Press(VirtualKeyModifiers.Control);
            Check(requests.Count==3,"Ctrl+Enter retried an Unknown submission");
            panel.NewConversation();panel.ComposerModeSelector.SelectedIndex=1;panel.PromptText="busy fixture";
            await Focus(Prompt());await Press(VirtualKeyModifiers.Control,repeats:2);
            Check(requests.Count==4&&panel.IsBusy&&!panel.IsComposerCollapsed,"Repeated Ctrl+Enter submitted duplicate busy requests or collapsed the composer");
            gate.TrySetResult();await CompleteSend();
            panel.NewConversation();panel.ComposerModeSelector.SelectedIndex=1;panel.PromptText="scope fixture";
            await Focus(panel.FullScreenButton);await Press(VirtualKeyModifiers.Control);
            Check(requests.Count==4,"Ctrl+Enter escaped the composer scope");
            panel.ExitFullScreen();
            await Focus(Prompt());await Press(VirtualKeyModifiers.Control);await CompleteSend();
            Check(requests.Count==5&&requests[^1].Request.Prompt=="scope fixture"&&requests[^1].Request.Png.SequenceEqual(image),"Shortcut targeted an inactive tab or discarded its screenshot");
            await File.WriteAllBytesAsync(Path.Combine(output,"composer-compact.png"),await RuntimeDiagnostics.RenderAsync(root));
            string result=$"PASS: {checks} compact composer assertions; removed screenshot row, preserved prompt/mode/Send, native Enter newline and scoped Ctrl+Enter for text/image/default prompt, selected Agent/Codex, current tab, Sent/Unknown locks and repeated busy input.\nOnly generated fixtures and injected senders; keyboard injection is restricted to this foreground test HWND; no user content, clipboard, desktop screenshot or real model request.\n";
            await File.WriteAllTextAsync(Path.Combine(output,"composer-test.txt"),result);return result;
        }
        finally{gate.TrySetResult();panel?.Close();Panel=previous;ApplyAppearance(appearance);if(foreground!=0)Native.SetForegroundWindow(foreground);}
    }
}
