using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ScreenShot.App;

internal enum CodexObservationState { Waiting, Submitted, Completed, ForeignConversation }
internal sealed record CodexObservedUser(string RuntimeId,string Fingerprint,bool MatchesPrompt);
internal sealed record CodexObservedChat(string RuntimeId,string Fingerprint);
internal sealed record CodexConversationSnapshot(string AreaId,IReadOnlyList<CodexObservedUser> Users,CodexObservedChat? SelectedChat,
    bool ComposerCleared,bool HasSend,bool HasStop,bool HasReplyFooter,string? Reply,IReadOnlyList<CodexObservedChat>? SidebarChats=null,
    string? ProgressText=null,string? WorkStatus=null);

internal sealed class CodexObservationPolicy(CodexConversationSnapshot before)
{
    private CodexConversationSnapshot? bound;
    internal bool Submitted=>bound!=null;
    internal string AreaId=>(bound??before).AreaId;
    internal string? UserId=>bound?.Users[^1].RuntimeId;
    internal CodexObservedChat? SelectedChat=>(bound??before).SelectedChat;
    internal bool SameSelectedChat(CodexObservedChat? selected)=>SelectedChat is {} known&&selected?.RuntimeId==known.RuntimeId;
    internal bool ClearlyChangedChat(CodexObservedChat? selected)=>SelectedChat is {} known&&selected is {} current
        &&current.RuntimeId!=known.RuntimeId&&current.Fingerprint!=known.Fingerprint;
    private bool SelectedExistingChat(CodexObservedChat? selected)=>selected is {} current&&before.SidebarChats?.Any(chat=>chat.RuntimeId==current.RuntimeId)==true;
    private bool UniqueNewChatAfterRebuild(CodexConversationSnapshot current)=>bound?.SelectedChat is {} known&&current.SelectedChat is {} selected
        &&selected.Fingerprint==known.Fingerprint&&before.SidebarChats!=null&&current.SidebarChats!=null
        &&!before.SidebarChats.Any(chat=>chat.Fingerprint==known.Fingerprint)
        &&current.SidebarChats.Count(chat=>chat.Fingerprint==known.Fingerprint)==1;

    internal CodexObservationState Observe(CodexConversationSnapshot current)
    {
        if(current.Users.Count==0)return ClearlyChangedChat(current.SelectedChat)&&SelectedExistingChat(current.SelectedChat)
            ?CodexObservationState.ForeignConversation:CodexObservationState.Waiting;
        if(bound==null)
        {
            if(before.Users.Count==0&&SelectedExistingChat(current.SelectedChat))
                return current.SelectedChat?.RuntimeId==before.SelectedChat?.RuntimeId
                    ?CodexObservationState.Waiting:CodexObservationState.ForeignConversation;
            if(current.Users.Count>before.Users.Count+1&&HasPrefix(current.Users,before.Users)
                &&current.Users[before.Users.Count].MatchesPrompt)return CodexObservationState.ForeignConversation;
            bool newMessage=current.Users.Count==before.Users.Count+1&&HasPrefix(current.Users,before.Users)
                &&current.Users[^1].MatchesPrompt&&!before.Users.Any(u=>u.RuntimeId==current.Users[^1].RuntimeId);
            // A new empty chat may acquire its sidebar row only when the first message is submitted.
            bool submissionAcknowledged=current.ComposerCleared||current.HasStop||(current.HasReplyFooter&&current.HasSend);
            if(newMessage&&submissionAcknowledged&&(before.Users.Count==0||!ClearlyChangedChat(current.SelectedChat)))bound=current;
            else return ClearlyChangedChat(current.SelectedChat)&&before.Users.Count>0
                ?CodexObservationState.ForeignConversation:CodexObservationState.Waiting;
        }
        else
        {
            bool sameUser=current.Users[^1].RuntimeId==bound.Users[^1].RuntimeId;
            bool sameHistory=current.Users.Count==bound.Users.Count&&HasPrefix(current.Users,bound.Users);
            if(current.Users.Count>bound.Users.Count&&HasPrefix(current.Users,bound.Users))return CodexObservationState.ForeignConversation;
            if(sameUser&&sameHistory)
            {
                // A title or sidebar row may be created after submission; the existing user node remains the stronger identity.
                bound=current with {SelectedChat=current.SelectedChat??bound.SelectedChat};
            }
            else if(sameHistory&&(SameSelectedChat(current.SelectedChat)||UniqueNewChatAfterRebuild(current)))
            {
                // Rebind after a transient UIA subtree rebuild only while the selected chat identity is unchanged.
                bound=current;
            }
            else return !sameHistory&&ClearlyChangedChat(current.SelectedChat)?CodexObservationState.ForeignConversation:CodexObservationState.Waiting;
        }
        return current.HasReplyFooter&&(current.HasSend||current.ComposerCleared)&&!current.HasStop&&!string.IsNullOrWhiteSpace(current.Reply)
            ?CodexObservationState.Completed:CodexObservationState.Submitted;
    }

    private static bool HasPrefix(IReadOnlyList<CodexObservedUser> users,IReadOnlyList<CodexObservedUser> prefix)
        =>users.Count>=prefix.Count&&Enumerable.Range(0,prefix.Count).All(i=>users[i].Fingerprint==prefix[i].Fingerprint);
    internal static string Fingerprint(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Compact(text))));
    private static string Compact(string text)=>Regex.Replace(text,"\\s+","");
    internal static CodexObservedUser User(string id,IReadOnlyList<string> parts,string prompt,bool includesMetadata=true)
    {
        string full=string.Concat(parts),expected=Compact(prompt);
        string text=full;
        // A timestamp is UI metadata only when it follows other text; retain literal time prompts when they already match.
        if(includesMetadata&&Compact(full)!=expected&&parts.Count>1&&Regex.IsMatch(parts[^1],"^\\d{1,2}:\\d{2}(?::\\d{2})?$"))text=string.Concat(parts.Take(parts.Count-1));
        return new(id,Fingerprint(text),Compact(text)==expected);
    }
}
