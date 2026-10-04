using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace ScreenShot.App;

internal sealed class CodexConversation
{
    private static readonly Regex UserRole=new("^(?:You\\b|用户|你).{0,30}[:：]$",RegexOptions.IgnoreCase);
    private static readonly Regex AssistantRole=new("^(?:Codex\\b|ChatGPT\\b|Assistant\\b|助手).{0,30}[:：]$",RegexOptions.IgnoreCase);
    private static readonly Regex CopyReply=new("^(?:Copy|Copy response|复制|复制回复)$",RegexOptions.IgnoreCase);
    private static readonly Regex SendButton=new("^(?:Send|Send message|发送|发送消息)$",RegexOptions.IgnoreCase);
    private static readonly Regex StopButton=new("^(?:Stop|Stop generating|Stop generation|停止|停止生成|停止响应)$",RegexOptions.IgnoreCase);
    private static readonly Regex CopyUser=new("^(?:Copy message|复制消息)$",RegexOptions.IgnoreCase);
    private static readonly Regex WorkspaceNewChat=new("^(?:在 (.+) 中开始新聊天|Start (?:a )?new chat in (.+))$",RegexOptions.IgnoreCase);
    private static readonly Regex PublicActivity=new("^(?:正在思考|正在处理|正在运行|正在读取|正在查看|正在重新连接|已运行|已读取|已查看|Thinking\\b|Processing\\b|Running\\b|Reading\\b|Reconnecting\\b|Ran\\b|Read\\b|Viewed\\b)",RegexOptions.IgnoreCase);
    private readonly nint windowHandle;
    private readonly int processId;
    private readonly string prompt;
    private readonly CodexObservationPolicy policy;
    private readonly string scopeKey;
    private readonly string composerId;
    private string? unavailableReason;
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint handle);
    private CodexConversation(Window window,string prompt,AutomationElement scope,AutomationElement composer,CodexConversationSnapshot before)
    {
        windowHandle=window.Properties.NativeWindowHandle.Value;processId=window.Properties.ProcessId.Value;
        this.prompt=prompt;policy=new(before);scopeKey=CodexScopePolicy.ScopeKey(scope.ClassName)!;composerId=Id(composer);
    }
    private static string Id(AutomationElement element)=>string.Join('.',element.Properties.RuntimeId.Value);
    private static bool IsArea(AutomationElement element)=>CodexScopePolicy.IsScroll(element.ClassName);
    private static bool IsComposer(AutomationElement element)=>element.ControlType==ControlType.Edit&&element.ClassName.Contains("ProseMirror",StringComparison.Ordinal);
    private static bool IsRole(AutomationElement element,Regex role)=>element.ControlType==ControlType.Text&&element.ClassName.Split(' ',StringSplitOptions.RemoveEmptyEntries).Contains("sr-only",StringComparer.Ordinal)&&role.IsMatch(element.Name);
    private static bool IsMessageRole(AutomationElement element)=>IsRole(element,UserRole)||IsRole(element,AssistantRole);
    private static AutomationElement? FindMessageScroll(AutomationElement scope,IReadOnlyList<AutomationElement> nodes)
        =>CodexScopePolicy.DiscoverScroll(scope,nodes,n=>n.ClassName,IsMessageRole,n=>n.Parent,Id,n=>n.ControlType is ControlType.Document or ControlType.Window);
    private static AutomationElement[] ReadMessageNodes(AutomationElement area)
    {
        var walker=area.Automation.TreeWalkerFactory.GetRawViewWalker();
        List<AutomationElement> nodes=[];
        void ReadChildren(AutomationElement parent,int depth)
        {
            if(depth>80)throw new InvalidOperationException("Codex 消息结构暂时无法完整读取");
            for(var child=walker.GetFirstChild(parent);child!=null;child=walker.GetNextSibling(child))
            {
                if(nodes.Count>=20000)throw new InvalidOperationException("Codex 消息结构暂时无法完整读取");
                nodes.Add(child);ReadChildren(child,depth+1);
            }
        }
        // Chromium's flattened tree can omit a user role even though RawView exposes it in this isolated scroll.
        ReadChildren(area,0);return nodes.ToArray();
    }

    internal static CodexConversation Prepare(Window window,string prompt)
    {
        var nodes=window.FindAllDescendants();
        var editors=nodes.Where(n=>IsComposer(n)&&ReadDraft(n) is {} draft&&CodexAutomation.NormalizeText(draft)==CodexAutomation.NormalizeText(prompt)).ToArray();
        if(editors.Length!=1)throw new InvalidOperationException("Codex 提交前无法确定当前聊天输入框，已停止发送");
        var scope=FindScope(editors[0]);
        if(scope==null)throw new InvalidOperationException("Codex 提交前无法确定当前聊天输入区域，已停止发送");
        return new(window,prompt,scope,editors[0],Read(scope,prompt,SelectedChat(nodes),SidebarChats(nodes)));
    }
    private static AutomationElement? FindScope(AutomationElement composer)=>CodexScopePolicy.FindScope(composer,n=>n.Parent,n=>n.ClassName,n=>n.ControlType is ControlType.Document or ControlType.Window);
    internal static string? ReadDraft(AutomationElement editor)
    {
        bool hasValue=editor.Patterns.Value.IsSupported,hasText=!hasValue&&editor.Patterns.Text.IsSupported;
        string? PatternDraft()=>CodexScopePolicy.DraftText(hasValue,hasValue?editor.Patterns.Value.Pattern.Value:null,hasText,hasText?editor.Patterns.Text.Pattern.DocumentRange.GetText(-1):null);
        string? draft=PatternDraft();
        if(string.IsNullOrWhiteSpace(draft))return draft;
        var walker=editor.Automation.TreeWalkerFactory.GetRawViewWalker();
        IReadOnlyList<AutomationElement> RawChildren(AutomationElement node)
        {
            List<AutomationElement> children=[];
            for(var child=walker.GetFirstChild(node);child!=null;child=walker.GetNextSibling(child))children.Add(child);
            return children;
        }
        // Chromium includes the placeholder in Value/Text. Only its isolated raw branch proves an empty draft.
        string? normalized=CodexScopePolicy.DraftWithoutPlaceholder(draft,editor.Name,editor,RawChildren,n=>n.ClassName,
            n=>n.Properties.IsControlElement.TryGetValue(out bool control)&&n.Properties.IsContentElement.TryGetValue(out bool content)&&!control&&!content,
            n=>n.ControlType==ControlType.Group,n=>n.ControlType==ControlType.Text,n=>n.Name);
        return normalized==string.Empty?CodexScopePolicy.ConfirmPlaceholderDraft(draft,PatternDraft()):normalized;
    }
    internal static bool CanPrepareNewChat(Window window)=>ReadReadyComposer(window,null)!=null;
    internal static AutomationElement? ReadyComposer(Window window,string workspace)=>ReadReadyComposer(window,workspace);
    private static bool IsDraftAttachment(AutomationElement node)=>node.ControlType==ControlType.Button&&CodexScopePolicy.IsDraftAttachmentName(node.Name);
    internal static AutomationElement? DraftAttachment(Window window,string? workspace=null)
    {
        var editors=window.FindAllDescendants().Where(IsComposer).ToArray();
        if(editors.Length!=1)return null;
        var scope=FindScope(editors[0]);if(scope==null)return null;
        var scopeNodes=scope.FindAllDescendants();
        if(!ValidateScope(scopeNodes)||scopeNodes.Any(IsMessageRole))return null;
        if(FindMessageScroll(scope,scopeNodes) is {} area&&ReadMessageNodes(area).Any(IsMessageRole))return null;
        if(workspace!=null&&!HasWorkspace(scopeNodes,workspace))return null;
        // This is still the empty new-chat scope. Attachments may be siblings of the editor body.
        return CodexScopePolicy.FindDraftAttachment(scopeNodes,IsDraftAttachment);
    }
    private static bool HasWorkspace(IReadOnlyList<AutomationElement> nodes,string workspace)=>CodexScopePolicy.HasWorkspace(nodes,workspace,n=>n.ControlType==ControlType.Button
        &&n.Parent is {} parent&&CodexScopePolicy.IsProjectSelector(n.ClassName,parent.ClassName),n=>n.Name);
    private static AutomationElement? ReadReadyComposer(Window window,string? workspace)
    {
        return CodexScopePolicy.NewChatComposer(window.FindAllDescendants(),IsComposer,FindScope,scope=>scope.FindAllDescendants(),(scopeNodes,editor)=>
        {
            var scope=FindScope(editor);if(scope==null)return false;
            if(!ValidateScope(scopeNodes)||scopeNodes.Any(IsMessageRole))return false;
            if(FindMessageScroll(scope,scopeNodes) is {} area&&ReadMessageNodes(area).Any(IsMessageRole))return false;
            bool attachments=scopeNodes.Any(IsDraftAttachment);
            return CodexScopePolicy.CanStartNewChat(scopeNodes.Count(IsComposer),scopeNodes.Count(n=>IsRole(n,UserRole)),ReadDraft(editor),attachments)
                &&(workspace==null||HasWorkspace(scopeNodes,workspace));
        });
    }
    private static bool ValidateScope(IReadOnlyList<AutomationElement> nodes)=>CodexScopePolicy.ValidateScope(nodes,n=>n.ClassName,IsComposer,n=>n.ControlType==ControlType.Button&&WorkspaceNewChat.IsMatch(n.Name));

    internal Task<bool> ConfirmSubmissionAsync(CancellationToken token)=>Task.Run(async()=>
    {
        using var automation=new UIA3Automation();
        while(true)
        {
            token.ThrowIfCancellationRequested();
            var state=Observe(automation);
            if(state is CodexObservationState.Submitted or CodexObservationState.Completed)return true;
            if(state==CodexObservationState.ForeignConversation)return false;
            await Task.Delay(500,token);
        }
    },token);

    internal Task ObserveAsync(Action<CodexReplyUpdate> onUpdate,CancellationToken cancellationToken)=>Task.Run(async()=>
    {
        try
        {
            using var automation=new UIA3Automation();
            var stream=new CodexReplyStreamPolicy();
            while(true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var state=Observe(automation,out CodexConversationSnapshot? snapshot);
                if(state==CodexObservationState.ForeignConversation)
                {
                    Unavailable(unavailableReason??"已发送到 Codex；当前聊天已切换或出现新的用户消息，请在 Codex 查看回复");return;
                }
                if(snapshot!=null&&stream.Observe(state,snapshot.ProgressText,snapshot.WorkStatus,snapshot.Reply) is {} update)
                {
                    onUpdate(update);
                    if(update.Phase==CodexReplyPhase.Completed)return;
                }
                else if(snapshot==null)stream.Observe(state,null,null,null);
                await Task.Delay(500,cancellationToken);
            }
        }
        catch(OperationCanceledException){Unavailable("已发送到 Codex；已停止读取回复，请在 Codex 查看处理结果");}
        catch(Exception){Unavailable("已发送到 Codex；当前界面无法可靠读取回复，请在 Codex 查看处理结果");}
        void Unavailable(string message)=>onUpdate(new(CodexReplyPhase.Unavailable,message));
    });

    private CodexObservationState Observe(UIA3Automation automation)=>Observe(automation,out _);
    private CodexObservationState Observe(UIA3Automation automation,out CodexConversationSnapshot? snapshot)
    {
        snapshot=null;
        if(!IsWindow(windowHandle)){unavailableReason="Codex 窗口已关闭，请重新打开 Codex 查看已提交消息";return CodexObservationState.ForeignConversation;}
        try
        {
            var window=automation.FromHandle(windowHandle).AsWindow();
            if(window.Properties.ProcessId.Value!=processId){unavailableReason="Codex 窗口已关闭，请重新打开 Codex 查看已提交消息";return CodexObservationState.ForeignConversation;}
            var nodes=window.FindAllDescendants();
            var selected=SelectedChat(nodes);
            var scopes=CodexScopePolicy.DiscoverScopes(nodes,scopeKey,n=>n.ClassName,IsComposer,FindScope,Id);
            var scope=scopes.FirstOrDefault(n=>Id(n)==policy.AreaId);
            if(scope==null&&policy.UserId is {} userId)
            {
                var anchored=scopes.Where(n=>n.FindAllDescendants().Any(e=>IsRole(e,UserRole)&&Id(e)==userId)).ToArray();
                if(anchored.Length==1)scope=anchored[0];
            }
            if(scope==null)
            {
                var composerScopes=scopes.Where(n=>n.FindAllDescendants().Any(e=>IsComposer(e)&&Id(e)==composerId)).ToArray();
                if(composerScopes.Length==1)scope=composerScopes[0];
            }
            if(scope==null&&scopes.Length==1&&selected!=null)scope=scopes[0];
            if(scope==null)return CodexObservationState.Waiting;
            snapshot=Read(scope,prompt,selected,SidebarChats(nodes));
            var state=policy.Observe(snapshot);
            return state;
        }
        catch(Exception error) when(error is COMException or InvalidOperationException or ArgumentException or NotSupportedException||CodexAutomation.IsUnavailable(error))
        {
            // UIA can expose an incomplete tree while the app rebuilds it. Keep observing; never replay the send action.
            return CodexObservationState.Waiting;
        }
    }

    private static CodexObservedChat? SelectedChat(AutomationElement[] nodes)
    {
        var selected=nodes.Where(n=>n.ControlType==ControlType.Button
            &&n.ClassName.Contains("data-[app-action-sidebar-thread-selected=true]",StringComparison.Ordinal)
            &&n.ClassName.Split(' ',StringSplitOptions.RemoveEmptyEntries).Contains("bg-primary-ghost-hover",StringComparer.Ordinal)).ToArray();
        return selected.Length==1?new(Id(selected[0]),CodexObservationPolicy.Fingerprint(selected[0].Name)):null;
    }
    private static CodexObservedChat[] SidebarChats(AutomationElement[] nodes)=>nodes.Where(n=>n.ControlType==ControlType.Button
        &&n.ClassName.Contains("data-[app-action-sidebar-thread-selected=true]",StringComparison.Ordinal))
        .Select(n=>new CodexObservedChat(Id(n),CodexObservationPolicy.Fingerprint(n.Name))).ToArray();

    private static CodexConversationSnapshot Read(AutomationElement scope,string prompt,CodexObservedChat? selected,CodexObservedChat[]? sidebarChats=null)
    {
        var scopeNodes=scope.FindAllDescendants();
        if(!ValidateScope(scopeNodes))throw new InvalidOperationException("Codex 当前输入区域未隔离侧栏或无法确定唯一输入框，已停止读取以避免关联错误聊天");
        var editors=scopeNodes.Where(IsComposer).ToArray();
        if(editors.Length!=1)throw new InvalidOperationException("Codex 当前输入区域无法确定唯一输入框，请等待界面完成更新");
        var composerState=ReadComposer(scope,editors[0]);
        var area=FindMessageScroll(scope,scopeNodes);
        var excluded=composerState.Container.FindAllDescendants().Prepend(composerState.Container).Select(Id).ToHashSet(StringComparer.Ordinal);
        if(area!=null&&excluded.Contains(Id(area)))excluded=editors[0].FindAllDescendants().Prepend(editors[0]).Select(Id).ToHashSet(StringComparer.Ordinal);
        var nodes=area==null?[]:ReadMessageNodes(area).Where(n=>!excluded.Contains(Id(n))).ToArray();
        List<CodexObservedUser> users=[];
        for(int i=0;i<nodes.Length;i++)
        {
            if(!IsRole(nodes[i],UserRole))continue;
            int end=Array.FindIndex(nodes,i+1,n=>IsRole(n,AssistantRole)||IsRole(n,UserRole));
            if(end<0)end=nodes.Length;
            int copyUser=Array.FindIndex(nodes,i+1,end-i-1,n=>n.ControlType==ControlType.Button&&CopyUser.IsMatch(n.Name));
            if(copyUser>=0)end=copyUser;
            var following=nodes[(i+1)..end];
            var body=CodexScopePolicy.UserBodyBounds(nodes[i],following,n=>n.Parent,n=>n.ClassName,Id);
            var content=body is {} userRange?following[userRange.Start..(userRange.Start+userRange.Count)]:following;
            users.Add(CodexObservationPolicy.User(Id(nodes[i]),ReadParts(content),prompt,includesMetadata:body==null));
        }
        // A code block can also expose a Copy button. The response footer is the last Copy control in a contents wrapper.
        var bounds=CodexScopePolicy.ReplyBounds(nodes,n=>IsRole(n,UserRole),n=>IsRole(n,AssistantRole),n=>
            n.ControlType==ControlType.Button&&CopyReply.IsMatch(n.Name)
            &&n.Parent is {} parent&&parent.ControlType==ControlType.Group
            &&parent.ClassName.Split(' ',StringSplitOptions.RemoveEmptyEntries).Contains("contents",StringComparer.Ordinal));
        string? reply=bounds is {} range?ReadText(nodes[range.Start..(range.Start+range.Count)]):null;
        var progressBounds=CodexScopePolicy.ProgressBounds(nodes,n=>IsRole(n,UserRole),n=>IsRole(n,AssistantRole));
        var progressNodes=progressBounds is {} progress?nodes[progress.Start..(progress.Start+progress.Count)]:[];
        string? progressText=progressNodes.Length>0?ReadText(progressNodes):null;
        // Only visible public activity labels are shown; collapsed history and private reasoning are not read.
        var activities=progressNodes.Where(IsPublicActivity).Select(n=>n.Name.Trim()).ToArray();
        string workStatus=activities.LastOrDefault() is {} activity?activity.Length<=100?activity:activity[..100]+"…"
            :composerState.HasStop?"正在思考 / 正在处理":"正在读取回复";
        return new(Id(scope),users,selected,composerState.Cleared,composerState.HasSend,composerState.HasStop,bounds!=null,reply,sidebarChats,
            progressText,workStatus);
    }
    private static ComposerObservation ReadComposer(AutomationElement scope,AutomationElement editor)
    {
        string? draft=ReadDraft(editor);
        for(var branch=editor.Parent;branch!=null;branch=branch.Parent)
        {
            var buttons=branch.FindAllDescendants(cf=>cf.ByControlType(ControlType.Button)).Where(n=>n.ClassName.Contains("token-button-composer",StringComparison.Ordinal)).ToArray();
            bool send=buttons.Any(n=>SendButton.IsMatch(n.Name)),stop=buttons.Any(n=>StopButton.IsMatch(n.Name));
            if(send||stop)return new(branch,draft!=null&&string.IsNullOrWhiteSpace(draft),send,stop);
            if(Id(branch)==Id(scope))break;
        }
        return new(editor.Parent??editor,draft!=null&&string.IsNullOrWhiteSpace(draft),false,false);
    }
    private sealed record ComposerObservation(AutomationElement Container,bool Cleared,bool HasSend,bool HasStop);

    private static string ReadText(AutomationElement[] nodes)=>string.Join(Environment.NewLine,ReadParts(nodes));
    private static bool IsPublicActivity(AutomationElement node)=>PublicActivity.IsMatch(node.Name)&&!HasHiddenRoleAncestor(node)
        &&node.Properties.IsOffscreen.TryGetValue(out bool offscreen)&&!offscreen
        &&(node.ControlType==ControlType.Button||node.ControlType==ControlType.Text&&node.Parent is {} parent
            &&parent.ClassName.Split(' ',StringSplitOptions.RemoveEmptyEntries).Contains("text-secondary",StringComparer.Ordinal));
    private static bool HasHiddenRoleAncestor(AutomationElement node)
    {
        for(var ancestor=node;ancestor!=null;ancestor=ancestor.Parent)
        {
            if(IsArea(ancestor))break;
            if(ancestor.ClassName.Split(' ',StringSplitOptions.RemoveEmptyEntries).Contains("sr-only",StringComparer.Ordinal))return true;
        }
        return false;
    }
    private static string[] ReadParts(AutomationElement[] nodes)
    {
        List<string> lines=[];
        foreach(var element in nodes)
        {
            if(element.ControlType==ControlType.Button&&IsPublicActivity(element))
            {
                lines.Add(element.Name.Trim());continue;
            }
            if(element.ControlType is not (ControlType.Text or ControlType.ListItem or ControlType.Hyperlink))continue;
            if(element.ClassName.Contains("sr-only",StringComparison.Ordinal)||string.IsNullOrWhiteSpace(element.Name))continue;
            if(element.ControlType!=ControlType.Text&&element.FindAllDescendants().Any(n=>
                n.ControlType is ControlType.Text or ControlType.ListItem or ControlType.Hyperlink&&!string.IsNullOrWhiteSpace(n.Name)))continue;
            bool excluded=false;
            for(var parent=element.Parent;parent!=null;parent=parent.Parent)
            {
                if(IsArea(parent))break;
                if(parent.ControlType is ControlType.Button or ControlType.Edit or ControlType.StatusBar
                    ||parent.ClassName.Split(' ',StringSplitOptions.RemoveEmptyEntries).Contains("sr-only",StringComparer.Ordinal)){excluded=true;break;}
            }
            if(!excluded)lines.Add(element.Name.Trim());
        }
        return lines.ToArray();
    }
}
