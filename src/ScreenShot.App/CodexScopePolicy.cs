namespace ScreenShot.App;

internal static class CodexScopePolicy
{
    private static readonly string[] MainScopes=["_MainContentFrame_","_MainContentViewport_","_MainContentSurface_"];
    internal static string? ScopeKey(string className)=>MainScopes.FirstOrDefault(prefix=>Tokens(className).Any(token=>token.StartsWith(prefix,StringComparison.Ordinal)));
    internal static bool IsScroll(string className)=>Tokens(className).Contains("group/thread-scroll-layout",StringComparer.Ordinal);
    internal static bool IsSidebar(string className)=>Tokens(className).Any(token=>token=="sidebar-item"||token.StartsWith("group/sidebar",StringComparison.Ordinal)||token.StartsWith("_LeftPanel_",StringComparison.Ordinal));
    private static string[] Tokens(string className)=>className.Split(' ',StringSplitOptions.RemoveEmptyEntries);

    internal static T? FindScope<T>(T composer,Func<T,T?> parent,Func<T,string> className,Func<T,bool> boundary) where T:class
    {
        T? frame=null,fallback=null;
        for(var node=parent(composer);node!=null;node=parent(node))
        {
            if(boundary(node))break;
            if(IsSidebar(className(node)))return null;
            string? key=ScopeKey(className(node));
            if(key==MainScopes[0]&&frame==null)frame=node;
            if(key!=null&&fallback==null)fallback=node;
        }
        return frame??fallback;
    }

    internal static T[] DiscoverScopes<T>(IReadOnlyList<T> nodes,string scopeKey,Func<T,string> className,Func<T,bool> composer,Func<T,T?> findScope,Func<T,string> runtimeId) where T:class
    {
        List<T> scopes=[];
        HashSet<string> identities=new(StringComparer.Ordinal);
        void Add(T scope)
        {
            if(ScopeKey(className(scope))==scopeKey&&identities.Add(runtimeId(scope)))scopes.Add(scope);
        }
        foreach(var node in nodes)
        {
            if(ScopeKey(className(node))==scopeKey)Add(node);
            // UIA's flattened descendants may omit layout ancestors that the editor's Parent chain still exposes.
            if(composer(node)&&findScope(node) is {} scope)Add(scope);
        }
        return scopes.ToArray();
    }

    internal static T? FindScroll<T>(IReadOnlyList<T> descendants,Func<T,string> className) where T:class
    {
        var areas=descendants.Where(node=>IsScroll(className(node))).ToArray();
        return areas.Length switch
        {
            0=>null,
            1=>areas[0],
            _=>throw new InvalidOperationException("Codex 当前输入区域包含多个消息栏目，已停止读取以避免关联错误聊天")
        };
    }
    internal static T? DiscoverScroll<T>(T scope,IReadOnlyList<T> descendants,Func<T,string> className,Func<T,bool> role,
        Func<T,T?> parent,Func<T,string> runtimeId,Func<T,bool> boundary) where T:class
    {
        Dictionary<string,T> areas=new(StringComparer.Ordinal);
        foreach(var seed in descendants.Where(node=>IsScroll(className(node))||role(node)))
        {
            List<T> path=[];
            for(var node=seed;node!=null;node=parent(node))
            {
                if(runtimeId(node)==runtimeId(scope))
                {
                    foreach(var area in path)areas.TryAdd(runtimeId(area),area);
                    break;
                }
                if(boundary(node)||IsSidebar(className(node)))break;
                if(IsScroll(className(node)))path.Add(node);
            }
        }
        return FindScroll(areas.Values.ToArray(),className);
    }

    internal static (int Start,int Count)? ProgressBounds<T>(IReadOnlyList<T> nodes,Func<T,bool> user,Func<T,bool> assistant)
    {
        int lastUser=-1;
        for(int i=0;i<nodes.Count;i++)if(user(nodes[i]))lastUser=i;
        if(lastUser<0)return null;
        for(int i=lastUser+1;i<nodes.Count;i++)if(assistant(nodes[i]))return(i+1,nodes.Count-i-1);
        return null;
    }
    internal static bool ValidateScope<T>(IReadOnlyList<T> descendants,Func<T,string> className,Func<T,bool> composer,Func<T,bool> newChatButton)
        =>descendants.Count(composer)==1&&!descendants.Any(node=>IsSidebar(className(node)))&&!descendants.Any(newChatButton);

    internal static T? NewChatComposer<T>(IReadOnlyList<T> windowNodes,Func<T,bool> composer,Func<T,T?> findScope,Func<T,IReadOnlyList<T>> descendants,Func<IReadOnlyList<T>,T,bool> ready) where T:class
    {
        var editors=windowNodes.Where(composer).ToArray();
        if(editors.Length!=1)return null;
        var scope=findScope(editors[0]);if(scope==null)return null;
        return ready(descendants(scope),editors[0])?editors[0]:null;
    }
    internal static bool IsProjectSelector(string className,string parentClass)
        =>Tokens(className).Any(token=>token.StartsWith("_ComposerFooterDropdown_",StringComparison.Ordinal))
        &&Tokens(parentClass).Any(token=>token.StartsWith("_ActiveProjectSelectorTrigger_",StringComparison.Ordinal));
    internal static bool HasWorkspace<T>(IReadOnlyList<T> scopeNodes,string workspace,Func<T,bool> selector,Func<T,string> name)
    {
        var selectors=scopeNodes.Where(selector).ToArray();
        const string prefix="切换项目：";
        if(selectors.Length!=1)return false;
        string selectedName=name(selectors[0]);
        return selectedName.StartsWith(prefix,StringComparison.Ordinal)
            &&string.Equals(selectedName[prefix.Length..],workspace,StringComparison.Ordinal);
    }
    internal static bool IsDraftAttachmentName(string name)=>System.Text.RegularExpressions.Regex.IsMatch(name,"^(?:Remove|移除|删除).*(?:attachment|image|screenshot|附件|图片)",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    internal static T? FindDraftAttachment<T>(IReadOnlyList<T> composerNodes,Func<T,bool> attachment) where T:class
    {
        var attachments=composerNodes.Where(attachment).ToArray();
        return attachments.Length==1?attachments[0]:null;
    }

    internal static (int Start,int Count)? UserBodyBounds<T>(T role,IReadOnlyList<T> following,Func<T,T?> parent,
        Func<T,string> className,Func<T,string> runtimeId) where T:class
    {
        var bodies=following.Select((node,index)=>(Node:node,Index:index))
            .Where(item=>Tokens(className(item.Node)).Contains("bg-user-message",StringComparer.Ordinal)).ToArray();
        if(bodies.Length==0)return null;
        if(bodies.Length!=1||parent(role) is not {} container||parent(bodies[0].Node) is not {} bodyParent
            ||runtimeId(bodyParent)!=runtimeId(container))
            throw new InvalidOperationException("Codex 用户消息正文无法唯一关联，已停止读取以避免确认错误消息");
        string bodyId=runtimeId(bodies[0].Node),containerId=runtimeId(container);
        bool InsideBody(T node)
        {
            int depth=0;
            for(var ancestor=parent(node);ancestor!=null;ancestor=parent(ancestor))
            {
                if(++depth>80)throw new InvalidOperationException("Codex 用户消息结构暂时无法完整读取");
                string id=runtimeId(ancestor);
                if(id==bodyId)return true;
                if(id==containerId||IsScroll(className(ancestor))||IsSidebar(className(ancestor)))break;
            }
            return false;
        }
        int start=bodies[0].Index+1,end=start;
        while(end<following.Count&&InsideBody(following[end]))end++;
        if(following.Skip(end).Any(InsideBody))throw new InvalidOperationException("Codex 用户消息结构暂时无法完整读取");
        // The RawView body is contiguous. Sibling timestamps and turn activity are UI metadata, not prompt text.
        return(start,end-start);
    }

    internal static (int Start,int Count)? ReplyBounds<T>(IReadOnlyList<T> nodes,Func<T,bool> user,Func<T,bool> assistant,Func<T,bool> footer)
    {
        int lastUser=-1,lastAssistant=-1,lastFooter=-1;
        for(int i=0;i<nodes.Count;i++)
        {
            if(user(nodes[i]))lastUser=i;
            if(assistant(nodes[i])){lastAssistant=i;lastFooter=-1;}
            if(footer(nodes[i])&&lastAssistant>=0)lastFooter=i;
        }
        return lastUser>=0&&lastAssistant>lastUser&&lastFooter>lastAssistant?(lastAssistant+1,lastFooter-lastAssistant-1):null;
    }
    internal static string? DraftText(bool hasValue,string? value,bool hasText,string? text)=>hasValue?value:hasText?text:null;
    internal static string? DraftWithoutPlaceholder<T>(string? draft,string label,T editor,Func<T,IReadOnlyList<T>> children,Func<T,string> className,Func<T,bool> nonContentPlaceholder,Func<T,bool> group,Func<T,bool> text,Func<T,string> name) where T:class
    {
        if(string.IsNullOrWhiteSpace(draft)||CodexAutomation.NormalizeText(draft)!=CodexAutomation.NormalizeText(label))return draft;
        var roots=children(editor);
        if(roots.Count!=1||!group(roots[0])||!Tokens(className(roots[0])).Contains("placeholder",StringComparer.Ordinal)||!nonContentPlaceholder(roots[0]))return draft;
        List<T> pending=[roots[0]];
        int labels=0,breaks=0;
        while(pending.Count>0)
        {
            var node=pending[^1];pending.RemoveAt(pending.Count-1);
            if(text(node))
            {
                if(Tokens(className(node)).Contains("ProseMirror-trailingBreak",StringComparer.Ordinal))
                {
                    if(!string.IsNullOrWhiteSpace(name(node)))return draft;
                    breaks++;
                }
                else
                {
                    if(CodexAutomation.NormalizeText(name(node))!=CodexAutomation.NormalizeText(label))return draft;
                    labels++;
                }
                if(children(node).Count!=0)return draft;
            }
            else if(group(node))pending.AddRange(children(node));
            else return draft;
        }
        return labels==1&&breaks==1?string.Empty:draft;
    }
    internal static string ConfirmPlaceholderDraft(string draft,string? reread)=>string.Equals(draft,reread,StringComparison.Ordinal)?string.Empty:string.IsNullOrWhiteSpace(reread)?draft:reread;
    internal static bool CanStartNewChat(int composerCount,int userCount,string? draft,bool attachments)
        =>composerCount==1&&userCount==0&&draft!=null&&string.IsNullOrWhiteSpace(draft)&&!attachments;
}
