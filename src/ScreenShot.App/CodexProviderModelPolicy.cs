using System.Text.Json;

namespace ScreenShot.App;

internal static class CodexProviderModelPolicy
{
    internal static string Validate(JsonElement configuration,JsonElement catalog)
    {
        if(configuration.ValueKind!=JsonValueKind.Object||catalog.ValueKind!=JsonValueKind.Object||
            !catalog.TryGetProperty("data",out var data)||data.ValueKind!=JsonValueKind.Array)throw new CodexTransportException();
        string? configured=String(configuration,"model"),provider=String(configuration,"model_provider");
        // A custom provider's model names need not appear in the built-in OpenAI catalog.
        if(!string.IsNullOrEmpty(provider)&&provider!="openai")return "自定义 provider（图片能力由该 provider 决定）";
        JsonElement? selected=null;
        foreach(var model in data.EnumerateArray())
        {
            if(model.ValueKind!=JsonValueKind.Object)continue;
            bool matches=string.IsNullOrEmpty(configured)?model.TryGetProperty("isDefault",out var current)&&current.ValueKind==JsonValueKind.True:
                String(model,"model")==configured||String(model,"id")==configured;
            if(matches){selected=model;break;}
        }
        if(selected is not JsonElement entry)
            throw new InvalidOperationException("当前 Codex CLI 未提供所配置的模型；请使用桌面内置 CLI 或升级 CLI，不会自动更换模型");
        if(!entry.TryGetProperty("inputModalities",out var modalities)||modalities.ValueKind!=JsonValueKind.Array||
            !modalities.EnumerateArray().Any(value=>value.ValueKind==JsonValueKind.String&&value.GetString()=="image"))
            throw new InvalidOperationException("当前模型未声明支持图片输入，请检查 Codex 模型或 CLI 版本");
        string? effort=String(configuration,"model_reasoning_effort");
        if(!string.IsNullOrEmpty(effort)&&entry.TryGetProperty("supportedReasoningEfforts",out var efforts)&&efforts.ValueKind==JsonValueKind.Array&&
            !efforts.EnumerateArray().Any(value=>value.ValueKind==JsonValueKind.Object&&String(value,"reasoningEffort")==effort))
            throw new InvalidOperationException("当前模型不支持所配置的 reasoning effort，请检查 CLI 模型设置");
        return String(entry,"model")??"已配置模型";
    }
    private static string? String(JsonElement value,string name)=>value.TryGetProperty(name,out var property)&&property.ValueKind==JsonValueKind.String?property.GetString():null;
}
