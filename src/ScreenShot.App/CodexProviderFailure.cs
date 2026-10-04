using System.Text.Json;

namespace ScreenShot.App;

internal static class CodexProviderFailure
{
    internal static string Describe(JsonElement error)
    {
        // Free-form provider details can contain prompts, images, paths or credentials.
        // Only protocol-defined categories and valid numeric HTTP statuses are displayable.
        if(error.ValueKind!=JsonValueKind.Object||
            !error.TryGetProperty("codexErrorInfo",out var info))return Unknown();

        if(info.ValueKind==JsonValueKind.String)
            return info.GetString() switch
            {
                "contextWindowExceeded"=>Format("contextWindowExceeded","输入超出模型上下文容量"),
                "sessionBudgetExceeded"=>Format("sessionBudgetExceeded","当前会话预算已耗尽"),
                "usageLimitExceeded"=>Format("usageLimitExceeded","账户使用额度已达上限"),
                "serverOverloaded"=>Format("serverOverloaded","Codex 服务暂时繁忙"),
                "cyberPolicy"=>Format("cyberPolicy","请求被 Codex 服务策略阻止"),
                "internalServerError"=>Format("internalServerError","Codex 服务发生内部错误"),
                "unauthorized"=>Format("unauthorized","登录或访问授权失效，请检查 Codex CLI 登录"),
                "badRequest"=>Format("badRequest","请求参数或模型配置不受支持"),
                "threadRollbackFailed"=>Format("threadRollbackFailed","Codex 会话回退失败"),
                "sandboxError"=>Format("sandboxError","沙箱初始化或执行失败"),
                "other"=>Format("other","Codex 返回了未分类错误"),
                _=>Unknown()
            };

        if(info.ValueKind!=JsonValueKind.Object)return Unknown();
        using var properties=info.EnumerateObject();
        if(!properties.MoveNext())return Unknown();
        var variant=properties.Current;
        if(properties.MoveNext()||variant.Value.ValueKind!=JsonValueKind.Object)return Unknown();
        return variant.Name switch
        {
            "httpConnectionFailed"=>HttpFailure("httpConnectionFailed","无法连接 Codex 服务",variant.Value),
            "responseStreamConnectionFailed"=>HttpFailure("responseStreamConnectionFailed","无法建立 Codex 回复流连接",variant.Value),
            "responseStreamDisconnected"=>HttpFailure("responseStreamDisconnected","Codex 回复流连接中断",variant.Value),
            "responseTooManyFailedAttempts"=>HttpFailure("responseTooManyFailedAttempts","Codex 回复连接重试达到上限",variant.Value),
            "activeTurnNotSteerable"=>Format("activeTurnNotSteerable","当前 Codex 回合无法接受追加请求"),
            _=>Unknown()
        };
    }

    private static string HttpFailure(string category,string description,JsonElement details)
    {
        int? status=null;
        if(details.TryGetProperty("httpStatusCode",out var code)&&code.ValueKind==JsonValueKind.Number&&
            code.TryGetInt32(out int value)&&value is >=100 and <=599)status=value;
        description=status switch
        {
            401 or 403=>"登录或访问权限被拒绝，请检查 Codex CLI 登录和模型权限",
            429=>"Codex 请求受到限流或使用额度限制",
            400 or 422=>"Codex 请求参数或模型配置不受支持",
            _=>description
        };
        return Format(category,description,status);
    }

    private static string Unknown()=>Format("genericUnknown","Codex 未返回可识别的错误类别");

    private static string Format(string category,string description,int? httpStatus=null)=>
        $"Codex 回复失败：{description}（{category}{(httpStatus is int status?$"，HTTP {status}":"")}）；已收到的内容已保留";
}
