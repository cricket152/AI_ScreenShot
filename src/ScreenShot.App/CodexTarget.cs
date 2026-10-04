using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using ScreenShot.Core;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Dispatching;

namespace ScreenShot.App;

internal sealed class CodexTarget(DispatcherQueue dispatcher,Settings? settings=null) : ISendTarget,IDisposable
{
    private readonly CodexProviderTarget? provider=settings==null?null:new(settings);
    internal bool IsProvider=>provider!=null;
    internal Task<string> CheckProviderAsync(CancellationToken token)=>provider?.CheckAsync(token)??Task.FromResult("当前是 UI Automation 诊断模式");
    public void Dispose()=>provider?.Dispose();
    private static readonly Regex NewChat=CodexWindowLocator.NewChat;
    public Task<IReadOnlyList<Workspace>> WorkspacesAsync()=>provider!=null?Task.FromResult(provider.Workspaces()):Task.Run<IReadOnlyList<Workspace>>(async()=>
    {
        using var automation=new UIA3Automation();
        var window=await CodexAutomation.WaitAsync(()=>CodexWindowLocator.Locate(automation),CancellationToken.None,TimeSpan.FromSeconds(15));
        return window.FindAllDescendants(cf=>cf.ByControlType(ControlType.Button)).Select(b=>NewChat.Match(b.Name)).Where(m=>m.Success)
            .Select(m=>m.Groups[1].Success?m.Groups[1].Value:m.Groups[2].Value).GroupBy(n=>n).Where(g=>g.Count()==1).Select(g=>new Workspace(g.Key)).ToArray();
    }).WaitAsync(TimeSpan.FromSeconds(15));
    private Task OnUi(Action action)
    {
        var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if(!dispatcher.TryEnqueue(()=>{try{action();completion.SetResult();}catch(Exception e){completion.SetException(e);}}))completion.SetException(new InvalidOperationException("应用正在退出"));
        return completion.Task;
    }
    private Task OnUi(Func<Task> action)
    {
        var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if(!dispatcher.TryEnqueue(async()=>{try{await action();completion.SetResult();}catch(Exception e){completion.SetException(e);}}))completion.SetException(new InvalidOperationException("应用正在退出"));
        return completion.Task;
    }
    private static AutomationElement? Composer(Window window)=>window.FindAllDescendants(cf=>cf.ByControlType(ControlType.Edit)).FirstOrDefault(e=>e.ClassName.Contains("ProseMirror",StringComparison.Ordinal));
    private static Task<AutomationElement> Wait(Func<AutomationElement?> find,CancellationToken token,int seconds=15)
        =>CodexAutomation.WaitAsync(find,token,TimeSpan.FromSeconds(seconds));
    private static void InvokeOnce(AutomationElement button)
    {
        try{button.AsButton().Invoke();}
        catch(Exception e) when(CodexAutomation.IsUnavailable(e))
        { /* Navigation/submission can destroy the button before Invoke returns. Observe the result; never invoke again. */ }
    }
    public Task<SendResult> SendAsync(SendRequest request,CancellationToken cancellationToken)
        =>provider!=null?provider.SendWithReplyAsync(request,_=>{},cancellationToken):SendSubmissionAsync(request,cancellationToken,null);
    public async Task<SendResult> SendWithReplyAsync(SendRequest request,Action<CodexReplyUpdate> onUpdate,CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onUpdate);
        if(provider!=null)return await provider.SendWithReplyAsync(request,onUpdate,cancellationToken);
        CodexConversation? conversation=null;
        var result=await SendSubmissionAsync(request,cancellationToken,observed=>conversation=observed);
        if(result.State!=SendState.Sent)return result;
        // SendSubmissionAsync has already restored the clipboard before the UI starts waiting for a reply.
        onUpdate(new(CodexReplyPhase.Submitted));
        if(conversation==null)
        {
            onUpdate(new(CodexReplyPhase.Unavailable,"已发送到 Codex；当前界面未提供可确认的对话结构，请在 Codex 查看回复"));
            return result;
        }
        onUpdate(new(CodexReplyPhase.Thinking));
        await conversation.ObserveAsync(onUpdate,cancellationToken);
        return result;
    }
    private async Task<SendResult> SendSubmissionAsync(SendRequest request,CancellationToken cancellationToken,Action<CodexConversation?>? onSubmitted)
    {
        return await SendCoreAsync(request,cancellationToken,onSubmitted);
    }
    private Task<SendResult> SendCoreAsync(SendRequest request,CancellationToken cancellationToken,Action<CodexConversation?>? onSubmitted)=>Task.Run(async()=>
    {
        if(request.Workspace==null)return new SendResult(SendState.Failed,"请先从 Codex 下拉菜单选择工作区");
        bool submitted=false;uint ownedSequence=0;ClipboardBackup? previous=null;string stage="查找窗口";
        nint originalForeground=Native.GetForegroundWindow(),codexHandle=0;
        bool foregroundRestorationCompleted=false;
        using var preparationTimeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        preparationTimeout.CancelAfter(TimeSpan.FromSeconds(90));
        var preparationToken=preparationTimeout.Token;
        try
        {
            using var automation=new UIA3Automation();
            var window=await CodexAutomation.WaitAsync(()=>CodexWindowLocator.Locate(automation,request.Workspace.Name),preparationToken,TimeSpan.FromSeconds(15));
            codexHandle=window.Properties.NativeWindowHandle.Value;
            var candidates=await CodexAutomation.WaitAsync(()=>window.FindAllDescendants(cf=>cf.ByControlType(ControlType.Button)).Where(b=>
            {var m=NewChat.Match(b.Name);return m.Success&&(m.Groups[1].Value==request.Workspace.Name||m.Groups[2].Value==request.Workspace.Name);}).ToArray(),preparationToken,TimeSpan.FromSeconds(15));
            if(candidates.Length!=1)throw new InvalidOperationException("工作区不存在或重名，请刷新并重新选择");
            stage="新建聊天";preparationToken.ThrowIfCancellationRequested();InvokeOnce(candidates[0]);
            // Navigation is asynchronous. Confirm the workspace and empty draft in the same isolated input scope.
            stage="确认工作区";await Wait(()=>CodexConversation.ReadyComposer(window,request.Workspace.Name),preparationToken,20);
            stage="备份原剪贴板";await OnUi(async()=>
            {
                uint before=Native.GetClipboardSequenceNumber();
                previous=await ClipboardAccessPolicy.RetryAsync(()=>ClipboardBackup.ReadAsync(Clipboard.GetContent(),preparationToken),
                    ()=>ClipboardService.RequireSequence(before),preparationToken);
                if(before!=Native.GetClipboardSequenceNumber())throw new InvalidOperationException("剪贴板在读取期间发生变化，请重试");
                preparationToken.ThrowIfCancellationRequested();stage="写入图片剪贴板";
                await ClipboardService.ImageAsync(request.Png,contentSet:()=>ownedSequence=Native.GetClipboardSequenceNumber(),
                    token:preparationToken,expectedSequence:before,flushStarting:()=>stage="固化图片剪贴板");
            });
            stage="粘贴图片";
            await FocusComposer(window,preparationToken,originalForeground,true);preparationToken.ThrowIfCancellationRequested();
            if(Native.GetClipboardSequenceNumber()!=ownedSequence)throw new InvalidOperationException("剪贴板已被修改，停止粘贴以保护你的内容");
            Native.Paste();
            stage="等待图片附件";
            await Wait(()=>CodexConversation.DraftAttachment(window,request.Workspace.Name),preparationToken,30);
            stage="准备提示词剪贴板";await OnUi(async()=>
            {
                preparationToken.ThrowIfCancellationRequested();
                await ClipboardService.TextAsync(request.EffectivePrompt,()=>ownedSequence=Native.GetClipboardSequenceNumber(),
                    token:preparationToken,expectedSequence:ownedSequence,flushStarting:()=>stage="固化提示词剪贴板");
            });
            stage="粘贴提示词";
            await FocusComposer(window,preparationToken,originalForeground,false);preparationToken.ThrowIfCancellationRequested();
            if(Native.GetClipboardSequenceNumber()!=ownedSequence)throw new InvalidOperationException("剪贴板已被修改，停止粘贴以保护你的内容");
            Native.Paste();
            stage="确认提示词";
            await Wait(()=>
            {
                var editor=Composer(window);if(editor==null)return null;
                string? draft=CodexConversation.ReadDraft(editor);
                return draft!=null&&CodexAutomation.NormalizeText(draft)==CodexAutomation.NormalizeText(request.EffectivePrompt)?editor:null;
            },preparationToken);
            stage="记录提交上下文";
            var conversation=await CodexAutomation.WaitAsync(()=>CodexConversation.Prepare(window,request.EffectivePrompt),preparationToken,TimeSpan.FromSeconds(15));
            stage="等待发送按钮";
            var send=await Wait(()=>window.FindAllDescendants(cf=>cf.ByControlType(ControlType.Button)).FirstOrDefault(e=>Regex.IsMatch(e.Name,"^(Send|Send message|发送|发送消息)$",RegexOptions.IgnoreCase)&&e.IsEnabled),preparationToken);
            preparationToken.ThrowIfCancellationRequested();
            if(Native.GetForegroundWindow()!=window.Properties.NativeWindowHandle.Value)throw new InvalidOperationException("Codex 焦点已改变，已停止发送");
            await RestoreClipboardAsync();
            preparationToken.ThrowIfCancellationRequested();
            if(Native.GetForegroundWindow()!=codexHandle)throw new InvalidOperationException("Codex 焦点已改变，已停止发送");
            stage="提交消息";submitted=true;InvokeOnce(send);
            RestoreForeground();
            stage="确认消息提交";
            if(!await conversation.ConfirmSubmissionAsync(cancellationToken))throw new InvalidOperationException("Codex 聊天已关闭或切换，无法继续确认这次提交");
            onSubmitted?.Invoke(conversation);
            return new SendResult(SendState.Sent,"已发送到 Codex · "+request.Workspace.Name);
        }
        catch(Exception e)
        {
            RecordFailure(stage,e,submitted);
            string detail=CodexAutomation.IsUnavailable(e)?"界面控件已更新，请重试":string.IsNullOrWhiteSpace(e.Message)?$"{e.GetType().Name} (0x{e.HResult:X8})":e.Message;
            return new SendResult(submitted?SendState.Unknown:SendState.Failed,submitted?"发送结果未知，请检查 Codex 新聊天；不会自动重发":$"Codex {stage}失败："+detail);
        }
        finally
        {
            RestoreForeground();
            await RestoreClipboardAsync();
        }
        void RestoreForeground()
        {
            // Preserve a deliberate user focus change; restoration happens before read-only confirmation/reply polling.
            if(foregroundRestorationCompleted)return;foregroundRestorationCompleted=true;
            if(originalForeground!=0&&originalForeground!=codexHandle&&Native.IsWindow(originalForeground)&&Native.GetForegroundWindow()==codexHandle)Native.SetForegroundWindow(originalForeground);
        }
        async Task RestoreClipboardAsync()
        {
            var backup=previous;previous=null;if(backup==null)return;
            bool cleanupStarted=false;
            using var restorationTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try{await OnUi(async()=>{cleanupStarted=true;if(ownedSequence!=0&&Native.GetClipboardSequenceNumber()==ownedSequence)await ClipboardService.RestoreAsync(backup,ownedSequence,restorationTimeout.Token);else backup.Dispose();});}
            catch(Exception){if(!cleanupStarted)backup.Dispose(); /* Clipboard owner may disappear; never override the send outcome. */ }
        }
    });
    private static async Task FocusComposer(Window window,CancellationToken token,nint originalForeground,bool activate)
    {
        token.ThrowIfCancellationRequested();
        nint handle=window.Properties.NativeWindowHandle.Value;
        if(activate&&Native.GetForegroundWindow()==originalForeground)window.SetForeground();
        await Wait(()=>
        {
            if(Native.GetForegroundWindow()!=handle)throw new InvalidOperationException("焦点已改变，已停止发送");
            var editor=Composer(window);if(editor==null)return null;
            editor.Focus();return editor;
        },token);
        if(Native.GetForegroundWindow()!=handle)throw new InvalidOperationException("Codex 未获得焦点，已停止发送");
    }
    private static void RecordFailure(string stage,Exception error,bool submitted)
    {
        try
        {
            string directory=Path.Combine(Settings.DirectoryPath,"diagnostics");Directory.CreateDirectory(directory);
            // Do not write exception messages, UI names, prompts, images, or clipboard data.
            File.WriteAllText(Path.Combine(directory,"codex-send.txt"),$"{DateTimeOffset.Now:O}\nStage: {stage}\nSubmitted: {submitted}\nException: {error.GetType().Name}\nHRESULT: 0x{error.HResult:X8}\n");
        }
        catch(Exception){ /* Diagnostics must not change the send outcome. */ }
    }
}
