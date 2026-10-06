using System.Text.Json;
using ScreenShot.Core;

internal static class PromptTests
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool condition,string message)
        {
            if(!condition)throw new Exception(message);
            checks++;
        }

        var settings=new Settings();
        Check(settings.DefaultPrompt==SendRequest.BuiltInDefaultPrompt,"New settings retain the built-in default prompt");
        var legacy=JsonSerializer.Deserialize<Settings>("{\"Browser\":\"Chrome\",\"Workspace\":\"existing\"}")!;
        Check(legacy.DefaultPrompt==SendRequest.BuiltInDefaultPrompt&&legacy.Browser=="Chrome"&&legacy.Workspace=="existing","Legacy settings without a default prompt remain compatible");
        foreach(string? configured in new string?[]{null,""," \r\n\t "})
        {
            var restored=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new {DefaultPrompt=configured}))!;
            Check(restored.DefaultPrompt==SendRequest.BuiltInDefaultPrompt,"Null or whitespace defaults fall back when settings are restored");
            Check(new SendRequest([1],"  ",null,configured).EffectivePrompt==SendRequest.BuiltInDefaultPrompt,"Requests tolerate a null or whitespace default");
        }

        const string customized="  ## 分析要求\n保留公式与代码：`value`  ";
        settings.DefaultPrompt=customized;
        var roundTrip=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!;
        Check(roundTrip.DefaultPrompt==customized,"Configured markdown and surrounding whitespace survive settings serialization");
        Check(new SendRequest([1],"\t",null,roundTrip.DefaultPrompt).EffectivePrompt==customized,"Empty user input uses the configured default verbatim");
        const string manual="  用户手动提示\n**按原文发送**  ";
        Check(new SendRequest([1],manual,null,customized).EffectivePrompt==manual,"Manual prompts override the default without normalization");
        Check(new SendRequest([1],"",null).EffectivePrompt==SendRequest.BuiltInDefaultPrompt,"Legacy three-argument requests retain fallback behavior");

        var captured=new SendRequest([1],"",null,settings.DefaultPrompt);
        settings.DefaultPrompt="后续配置";
        Check(captured.EffectivePrompt==customized,"A request keeps the configured prompt captured at submission");
        settings.DefaultPrompt=" \t ";
        Check(settings.DefaultPrompt==SendRequest.BuiltInDefaultPrompt,"Clearing the setting restores the built-in default");

        foreach(var request in new[]{captured,new SendRequest([1],manual,null,customized),new SendRequest([1],"",null," ")})
        {
            var begin=JsonSerializer.SerializeToElement(BridgeProtocol.Encode(request,"prompt-test").First());
            Check(begin.GetProperty("prompt").GetString()==request.EffectivePrompt,"ChatGPT bridge receives the same effective prompt as Codex");
        }
        return checks;
    }
}
