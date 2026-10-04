using System.Text;
using System.Text.Json;

namespace ScreenShot.App;

internal sealed class CodexProviderEvents(string threadId,string turnId)
{
    private enum MessagePhase { Unspecified, Commentary, FinalAnswer, Other }
    private sealed class Message
    {
        internal readonly StringBuilder Text=new();
        internal MessagePhase Phase;
        internal bool Completed;
    }

    private readonly Dictionary<string,Message> messages=new(StringComparer.Ordinal);
    private readonly List<string> messageOrder=[];
    private CodexReplyUpdate? previous;
    private string? lastFailure;
    internal bool IsComplete { get; private set; }

    internal CodexReplyUpdate? Read(JsonElement notification)
    {
        if(IsComplete||notification.ValueKind!=JsonValueKind.Object)return null;
        string? method=String(notification,"method");
        if(!notification.TryGetProperty("params",out var parameters)||parameters.ValueKind!=JsonValueKind.Object||
            String(parameters,"threadId")!=threadId)return null;
        if(method=="turn/completed")
        {
            if(!parameters.TryGetProperty("turn",out var turn)||turn.ValueKind!=JsonValueKind.Object||String(turn,"id")!=turnId)return null;
            return Finish(turn);
        }
        if(String(parameters,"turnId")!=turnId)return null;
        switch(method)
        {
            case "error":
                if(parameters.TryGetProperty("error",out var error))lastFailure=CodexProviderFailure.Describe(error);
                return Thinking(parameters.TryGetProperty("willRetry",out var retry)&&retry.ValueKind==JsonValueKind.True?"Codex 正在重试连接":"Codex 正在结束本次处理");
            case "item/agentMessage/delta":
                string? id=String(parameters,"itemId"),delta=String(parameters,"delta");
                if(string.IsNullOrEmpty(id)||string.IsNullOrEmpty(delta))return null;
                var message=GetMessage(id);
                if(message.Completed)return null;
                message.Text.Append(delta);
                return Thinking("Codex 正在回复");
            case "item/started":
            case "item/completed":
                if(!parameters.TryGetProperty("item",out var item)||item.ValueKind!=JsonValueKind.Object)return null;
                string? type=String(item,"type");
                if(type=="agentMessage")
                {
                    if(!ReadMessage(item,method=="item/completed"))return null;
                    return Thinking("Codex 正在回复");
                }
                if(type is "commandExecution" or "fileChange" or "mcpToolCall" or "dynamicToolCall" or "webSearch")
                    return Thinking(method=="item/started"?"Codex 正在处理":"Codex 正在思考");
                return null;
            default:
                return null;
        }
    }

    private CodexReplyUpdate Finish(JsonElement turn)
    {
        // The terminal snapshot can repair missing deltas and supplies complete text for each item.
        if(turn.TryGetProperty("items",out var items)&&items.ValueKind==JsonValueKind.Array)
            foreach(var item in items.EnumerateArray())
                if(item.ValueKind==JsonValueKind.Object&&String(item,"type")=="agentMessage")ReadMessage(item,true,true);
        IsComplete=true;
        string? status=String(turn,"status");
        if(status=="completed")
        {
            string text=messages.Values.Any(message=>message.Phase==MessagePhase.FinalAnswer)?
                Text(MessagePhase.FinalAnswer):Text(MessagePhase.Unspecified);
            return new(CodexReplyPhase.Completed,text,string.IsNullOrWhiteSpace(text)?"Codex 已完成，未返回回复正文":"Codex 已完成回复");
        }
        string visible=VisibleText();
        string reason=status switch
        {
            "failed"=>turn.TryGetProperty("error",out var failure)&&failure.ValueKind==JsonValueKind.Object&&
                failure.TryGetProperty("codexErrorInfo",out var category)&&category.ValueKind!=JsonValueKind.Null?
                CodexProviderFailure.Describe(failure):lastFailure??CodexProviderFailure.Describe(default),
            "interrupted"=>"Codex 已停止，已保留收到的内容",
            _=>"Codex 返回了无法识别的结束状态，已保留收到的内容"
        };
        return new(CodexReplyPhase.Unavailable,string.IsNullOrWhiteSpace(visible)?null:visible,reason);
    }

    private bool ReadMessage(JsonElement item,bool completed,bool terminal=false)
    {
        string? id=String(item,"id");
        if(string.IsNullOrEmpty(id))return false;
        var message=GetMessage(id);
        if(message.Completed&&!terminal)return false;
        if(item.TryGetProperty("phase",out var phase)&&phase.ValueKind==JsonValueKind.String)
            message.Phase=phase.GetString() switch
            {
                "commentary"=>MessagePhase.Commentary,
                "final_answer"=>MessagePhase.FinalAnswer,
                _=>MessagePhase.Other
            };
        if(item.TryGetProperty("text",out var text)&&text.ValueKind==JsonValueKind.String)
        {
            // Full item text replaces streamed fragments; appending it would duplicate the answer.
            message.Text.Clear();message.Text.Append(text.GetString());
        }
        if(completed)message.Completed=true;
        return true;
    }

    private Message GetMessage(string id)
    {
        if(messages.TryGetValue(id,out var message))return message;
        message=new();messages.Add(id,message);messageOrder.Add(id);return message;
    }

    private CodexReplyUpdate? Thinking(string status)
    {
        string text=VisibleText();
        var update=new CodexReplyUpdate(CodexReplyPhase.Thinking,string.IsNullOrWhiteSpace(text)?null:text,status);
        if(update==previous)return null;
        previous=update;return update;
    }

    private string VisibleText()=>string.Join("\n\n",messageOrder.Select(id=>messages[id])
        .Where(message=>message.Phase!=MessagePhase.Other)
        .Select(message=>message.Text.ToString()).Where(text=>!string.IsNullOrWhiteSpace(text)));

    private string Text(MessagePhase phase)=>string.Join("\n\n",messageOrder.Select(id=>messages[id])
        .Where(message=>message.Phase==phase)
        .Select(message=>message.Text.ToString()).Where(text=>!string.IsNullOrWhiteSpace(text)));

    private static string? String(JsonElement value,string property)=>value.TryGetProperty(property,out var found)&&found.ValueKind==JsonValueKind.String?found.GetString():null;
}
