using System.Diagnostics;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using ScreenShot.Core;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Dispatching;

namespace ScreenShot.App;

internal sealed class CodexTarget(DispatcherQueue dispatcher) : ISendTarget
{
    private static readonly Regex NewChat=new("^(?:在 (.+) 中开始新聊天|Start (?:a )?new chat in (.+))$",RegexOptions.IgnoreCase);
    private static Window Locate(UIA3Automation automation)
    {
        foreach(var process in Process.GetProcessesByName("ChatGPT").Concat(Process.GetProcessesByName("Codex")))
        {
            try
            {
                nint handle=process.MainWindowHandle;if(handle==0)continue;
                var window=automation.FromHandle(handle).AsWindow();
                if(window.FindAllDescendants(cf=>cf.ByControlType(ControlType.Button)).Any(button=>NewChat.IsMatch(button.Name)))return window;
            }
            catch(Exception e) when(e is ArgumentException or InvalidOperationException){ }
        }
        throw new InvalidOperationException("未找到暴露工作区的 Codex 窗口。请打开 Codex 并展开项目列表。");
    }
    public Task<IReadOnlyList<Workspace>> WorkspacesAsync()=>Task.Run<IReadOnlyList<Workspace>>(()=>
    {
        using var automation=new UIA3Automation();var window=Locate(automation);
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
    private static bool InDraftOrSidebar(AutomationElement element)
    {
        for(var node=element;node!=null;node=node.Parent)
            if(node.ClassName.Contains("ProseMirror",StringComparison.Ordinal)||node.ClassName.Contains("sidebar",StringComparison.Ordinal))return true;
        return false;
    }
    private static int MessageCount(Window window,string prompt)
    {
        return window.FindAllDescendants().Count(e=>
            e.ControlType is not (ControlType.Button or ControlType.Edit)
            &&CodexAutomation.NormalizeText(e.Name)==CodexAutomation.NormalizeText(prompt)&&!InDraftOrSidebar(e)
            &&!e.FindAllDescendants().Any(n=>n.ClassName.Contains("ProseMirror",StringComparison.Ordinal)));
    }
    public async Task<SendResult> SendAsync(SendRequest request,CancellationToken cancellationToken)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);timeout.CancelAfter(TimeSpan.FromSeconds(90));
        try{return await SendCoreAsync(request,timeout.Token).WaitAsync(timeout.Token);}
        catch(OperationCanceledException){return new(SendState.Unknown,"Codex 自动化超时，发送结果未知；请检查 Codex，不会自动重发");}
    }
    private Task<SendResult> SendCoreAsync(SendRequest request,CancellationToken cancellationToken)=>Task.Run(async()=>
    {
        if(request.Workspace==null)return new SendResult(SendState.Failed,"请先从 Codex 下拉菜单选择工作区");
        bool submitted=false;uint ownedSequence=0;ClipboardBackup? previous=null;string stage="查找窗口";
        try
        {
            using var automation=new UIA3Automation();
            var window=await CodexAutomation.WaitAsync(()=>Locate(automation),cancellationToken,TimeSpan.FromSeconds(15));
            var candidates=await CodexAutomation.WaitAsync(()=>window.FindAllDescendants(cf=>cf.ByControlType(ControlType.Button)).Where(b=>
            {var m=NewChat.Match(b.Name);return m.Success&&(m.Groups[1].Value==request.Workspace.Name||m.Groups[2].Value==request.Workspace.Name);}).ToArray(),cancellationToken,TimeSpan.FromSeconds(15));
            if(candidates.Length!=1)throw new InvalidOperationException("工作区不存在或重名，请刷新并重新选择");
            stage="新建聊天";cancellationToken.ThrowIfCancellationRequested();window.SetForeground();InvokeOnce(candidates[0]);
            // Navigation is asynchronous. Wait for a workspace marker in the composer area.
            stage="确认工作区";await Wait(()=>
            {
                var candidate=Composer(window);if(candidate==null)return null;
                var panel=candidate.Parent;
                for(int depth=0;depth<8&&panel!=null;depth++,panel=panel.Parent)
                {
                    var nodes=panel.FindAllDescendants();
                    if(nodes.Any(n=>NewChat.IsMatch(n.Name)))break; // Sidebar/root is not evidence.
                    if(nodes.Any(n=>n.Name==request.Workspace.Name))return candidate;
                }
                return null;
            },cancellationToken,20);
            stage="备份原剪贴板";await OnUi(async()=>
            {
                uint before=Native.GetClipboardSequenceNumber();previous=await ClipboardBackup.ReadAsync(Clipboard.GetContent(),cancellationToken);
                if(before!=Native.GetClipboardSequenceNumber())throw new InvalidOperationException("剪贴板在读取期间发生变化，请重试");
                cancellationToken.ThrowIfCancellationRequested();stage="写入图片剪贴板";
                ClipboardService.Image(request.Png,contentSet:()=>ownedSequence=Native.GetClipboardSequenceNumber());
            });
            stage="粘贴图片";
            await FocusComposer(window,cancellationToken);cancellationToken.ThrowIfCancellationRequested();Native.Paste();
            stage="等待图片附件";
            await Wait(()=>window.FindAllDescendants().FirstOrDefault(e=>e.Name.Contains("screenshot",StringComparison.OrdinalIgnoreCase)||Regex.IsMatch(e.Name,"^(Remove|移除|删除).*(attachment|附件|图片)",RegexOptions.IgnoreCase)),cancellationToken,30);
            stage="准备提示词剪贴板";await OnUi(()=>{cancellationToken.ThrowIfCancellationRequested();if(Native.GetClipboardSequenceNumber()!=ownedSequence)throw new InvalidOperationException("剪贴板已被修改，停止发送以避免覆盖你的内容");ClipboardService.Text(request.EffectivePrompt,()=>ownedSequence=Native.GetClipboardSequenceNumber());});
            stage="粘贴提示词";
            await FocusComposer(window,cancellationToken);cancellationToken.ThrowIfCancellationRequested();Native.Paste();
            stage="确认提示词";
            await Wait(()=>
            {
                var editor=Composer(window);if(editor==null)return null;
                return CodexAutomation.NormalizeText(editor.Patterns.Text.Pattern.DocumentRange.GetText(-1))==CodexAutomation.NormalizeText(request.EffectivePrompt)?editor:null;
            },cancellationToken);
            int beforeMessages=0;
            await Wait(()=>{beforeMessages=MessageCount(window,request.EffectivePrompt);return window;},cancellationToken);
            stage="等待发送按钮";
            var send=await Wait(()=>window.FindAllDescendants(cf=>cf.ByControlType(ControlType.Button)).FirstOrDefault(e=>Regex.IsMatch(e.Name,"^(Send|Send message|发送|发送消息)$",RegexOptions.IgnoreCase)&&e.IsEnabled),cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if(Native.GetForegroundWindow()!=window.Properties.NativeWindowHandle.Value)throw new InvalidOperationException("Codex 焦点已改变，已停止发送");
            stage="提交消息";submitted=true;InvokeOnce(send);
            stage="确认消息提交";
            await Wait(()=>
            {
                var editor=Composer(window);if(editor==null)return null;
                bool cleared=string.IsNullOrWhiteSpace(editor.Patterns.Text.Pattern.DocumentRange.GetText(-1));
                return cleared&&MessageCount(window,request.EffectivePrompt)>beforeMessages?window:null;
            },cancellationToken,30);
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
            if(previous!=null)
            {
                try{await OnUi(()=>{if(ownedSequence!=0&&Native.GetClipboardSequenceNumber()==ownedSequence)ClipboardService.Restore(previous);else previous.Dispose();});}
                catch(Exception){ /* Clipboard owner may disappear; never override the send outcome. */ }
            }
        }
    },cancellationToken);
    private static async Task FocusComposer(Window window,CancellationToken token)
    {
        await Wait(()=>
        {
            var editor=Composer(window);if(editor==null)return null;
            editor.Focus();return editor;
        },token);
        if(Native.GetForegroundWindow()!=window.Properties.NativeWindowHandle.Value)throw new InvalidOperationException("Codex 未获得焦点，已停止发送");
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
