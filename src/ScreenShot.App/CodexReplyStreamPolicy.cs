namespace ScreenShot.App;

internal enum CodexReplyPhase { Submitted, Thinking, Completed, Unavailable }
// Text is the current public text snapshot, not an append-only fragment.
internal sealed record CodexReplyUpdate(CodexReplyPhase Phase,string? Text=null,string? Status=null);

internal sealed class CodexReplyStreamPolicy
{
    private string? completionText;
    private int stableCompletion;
    private CodexReplyUpdate? previous;

    internal CodexReplyUpdate? Observe(CodexObservationState state,string? text,string? status,string? reply)
    {
        if(state is not (CodexObservationState.Submitted or CodexObservationState.Completed))
        {
            completionText=null;stableCompletion=0;
            return null;
        }
        if(state==CodexObservationState.Completed&&!string.IsNullOrWhiteSpace(reply))
        {
            stableCompletion=reply==completionText?stableCompletion+1:1;completionText=reply;
            if(stableCompletion>=2)return new(CodexReplyPhase.Completed,reply,"Codex 已完成回复");
        }
        else {completionText=null;stableCompletion=0;}
        var update=new CodexReplyUpdate(CodexReplyPhase.Thinking,string.IsNullOrWhiteSpace(text)?null:text,
            string.IsNullOrWhiteSpace(status)?"正在思考 / 正在处理":status);
        if(update==previous)return null;
        previous=update;
        return update;
    }
}
