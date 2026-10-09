using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ScreenShot.Core;

internal static class AgentTests
{
    internal static async Task<int> RunAsync()
    {
        int checks=0;
        void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
        var settings=new AgentSettings{ModelName="test-vision",BaseUrl="https://example.invalid/custom/v1/"};
        Check(settings.Endpoint().AbsoluteUri=="https://example.invalid/custom/v1/chat/completions","Versioned API path is retained");
        settings.BaseUrl="https://example.invalid/v1/chat/completions/";
        Check(settings.Endpoint("models").AbsoluteUri=="https://example.invalid/v1/models","Full completion endpoint is normalized");
        foreach(string url in new[]{"file:///tmp/key","https://user:pass@example.invalid/v1","https://example.invalid/v1?key=value","https://example.invalid/#fragment","http://example.invalid/v1"})
        {
            settings.BaseUrl=url;
            try{_=settings.Endpoint();Check(false,"Unsafe endpoints must fail");}catch(InvalidOperationException){Check(true,"Unsafe endpoints fail locally");}
        }
        settings.BaseUrl="http://127.0.0.1:32123/v1";settings.Validate();Check(true,"Local HTTP is allowed");
        settings.TimeoutSeconds=0;
        try{settings.Validate();Check(false,"Invalid timeout must fail");}catch(InvalidOperationException){Check(true,"Invalid timeout is rejected");}
        settings.TimeoutSeconds=180;
        const string testKey="generated-test-key";settings.ApiKey=testKey;
        string persisted=JsonSerializer.Serialize(settings);
        Check(!persisted.Contains(testKey)&&persisted.Contains("apikey_protected"),"Serialized config contains only encrypted API key");
        Check(JsonSerializer.Deserialize<AgentSettings>(persisted)!.ApiKey==testKey,"Current-user DPAPI key round trip");
        var restored=JsonSerializer.Deserialize<Settings>("{\"Browser\":\"Chrome\",\"Workspace\":\"keep\",\"Agent\":null}")!;
        Check(restored.Agent!=null&&restored.Workspace=="keep"&&!JsonSerializer.Serialize(restored).Contains("Browser"),"Legacy config drops browser and preserves unrelated values");

        var input=new SendRequest([137,80,78,71],"",null,"默认中文提示词");
        var updates=new List<AgentReplyUpdate>();int requests=0;
        int textRequests=0;
        using(var client=new AgentClient(()=>settings,new Handler(async(message,token)=>
        {
            textRequests++;
            using var document=JsonDocument.Parse(await message.Content!.ReadAsStringAsync(token));
            var content=document.RootElement.GetProperty("messages")[1].GetProperty("content");
            Check(content.GetArrayLength()==1&&content[0].GetProperty("type").GetString()=="text"&&content[0].GetProperty("text").GetString()=="  你好\n继续对话  ","Agent text-only payload preserves the prompt and omits every image block");
            return Response("{\"choices\":[{\"message\":{\"content\":\"你好\"}}]}");
        })))
        {
            Check((await client.SendWithReplyAsync(new([],"  你好\n继续对话  ",null),updates.Add,CancellationToken.None)).State==SendState.Sent&&updates[^1].Text=="你好","Agent text-only request completes through the normal reply path");
            Check((await client.SendAsync(new([]," \r\n\t",null,"截图默认提示词"),CancellationToken.None)).State==SendState.Failed&&textRequests==1,"Empty text-only input fails locally without an HTTP request or default prompt");
        }
        updates.Clear();
        using(var client=new AgentClient(()=>settings,new Handler(async(message,token)=>
        {
            using var document=JsonDocument.Parse(await message.Content!.ReadAsStringAsync(token));
            var history=document.RootElement.GetProperty("messages");
            Check(history.GetArrayLength()==4&&history[1].GetProperty("role").GetString()=="user"&&history[2].GetProperty("role").GetString()=="assistant","Agent sends structured history only from the provided conversation");
            Check(history[1].GetProperty("content")[1].GetProperty("type").GetString()=="image_url"&&history[3].GetProperty("content").GetArrayLength()==1,"Previous screenshots remain usable when the next turn contains only text");
            return Response("{\"choices\":[{\"message\":{\"content\":\"history reply\"}}]}");
        })))
        {
            var request=new SendRequest([],"follow-up",null){Conversation=new(){History=[new("user","isolated question",[1,2]),new("assistant","isolated reply")]}};
            Check((await client.SendAsync(request,CancellationToken.None)).State==SendState.Sent,"Agent history completes through the existing transport");
        }
        using(var client=new AgentClient(()=>settings,new Handler(async(message,token)=>
        {
            requests++;
            Check(message.Method==HttpMethod.Post&&message.RequestUri!.AbsolutePath=="/v1/chat/completions","Single POST uses compatible endpoint");
            Check(message.Headers.Authorization?.Scheme=="Bearer"&&message.Headers.Authorization.Parameter==testKey,"Bearer authentication is used");
            using var json=JsonDocument.Parse(await message.Content!.ReadAsStringAsync(token));var root=json.RootElement;
            Check(root.GetProperty("model").GetString()==settings.ModelName&&root.GetProperty("stream").GetBoolean(),"Model and streaming options are sent");
            var parts=root.GetProperty("messages")[1].GetProperty("content");
            Check(parts[0].GetProperty("text").GetString()==input.EffectivePrompt,"Default prompt is sent verbatim");
            string url=parts[1].GetProperty("image_url").GetProperty("url").GetString()!;
            Check(Convert.FromBase64String(url["data:image/png;base64,".Length..]).SequenceEqual(input.Png),"Screenshot is transported losslessly");
            return Response("data: {\"model\":\"reported-vision-model\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\"}}]}\n\ndata: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"## 分析\\n\"}}]}\n\ndata: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"正文\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n","text/event-stream");
        })))
        {
            var result=await client.SendWithReplyAsync(input,updates.Add,CancellationToken.None);
            Check(result.State==SendState.Sent&&requests==1,"SSE completes without retries");
            Check(updates[0].Phase==AgentReplyPhase.Submitted&&updates[^1]==new AgentReplyUpdate(AgentReplyPhase.Completed,"## 分析\n正文",ModelName:"reported-vision-model"),"Streaming reply is accumulated and finalized");
        }
        Check(updates[0].ModelName=="test-vision"&&updates.Skip(1).All(update=>update.ModelName=="reported-vision-model"),"Response model metadata supersedes the captured request model in streaming updates");
        foreach(bool stream in new[]{true,false})
        {
            settings.Stream=stream;
            using var client=new AgentClient(()=>settings,new Handler((_,_)=>Task.FromResult(Response("{\"choices\":[{\"message\":{\"content\":\"JSON 回复\"},\"finish_reason\":\"stop\"}]}"))));
            updates.Clear();var result=await client.SendWithReplyAsync(input,updates.Add,CancellationToken.None);
            Check(result.State==SendState.Sent&&updates[^1].Text=="JSON 回复"&&updates[^1].ModelName=="test-vision","JSON without model metadata keeps the request model");
        }
        using(var client=new AgentClient(()=>settings,new Handler((_,_)=>Task.FromResult(Response("{\"model\":\"actual-json-model\",\"choices\":[{\"message\":{\"content\":\"JSON 模型回复\"}}]}")))))
        {
            updates.Clear();var result=await client.SendWithReplyAsync(input,updates.Add,CancellationToken.None);
            Check(result.State==SendState.Sent&&updates[^1].ModelName=="actual-json-model","Complete JSON response reports its actual model");
        }
        foreach(int code in new[]{400,401,403,404,413,415,422,429,302,500,503})
        {
            int attempts=0;
            using var client=new AgentClient(()=>settings,new Handler((_,_)=>{attempts++;return Task.FromResult(new HttpResponseMessage((HttpStatusCode)code){Content=new StringContent("sensitive upstream payload")});}));
            var result=await client.SendAsync(input,CancellationToken.None);
            Check(result.State==(code is 302 or 500 or 503?SendState.Unknown:SendState.Failed)&&attempts==1,"Explicit rejection differs from uncertain upstream failure, no retry");
            Check(!result.Message.Contains("sensitive"),"Upstream errors are never echoed");
        }
        foreach(string body in new[]{"data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n","data: invalid-json\n\n","data: {\"error\":{\"message\":\"private\"}}\n\n","data: [DONE]\n\n"})
        {
            using var client=new AgentClient(()=>settings,new Handler((_,_)=>Task.FromResult(Response(body,"text/event-stream"))));
            updates.Clear();var result=await client.SendWithReplyAsync(input,updates.Add,CancellationToken.None);
            Check(result.State==SendState.Unknown&&updates[^1].Phase==AgentReplyPhase.Unavailable,"Truncated, malformed, error and empty streams remain unknown");
            if(body.Contains("partial"))Check(updates[^1].Text=="partial","Partial reply is retained on disconnect");
        }
        using(var client=new AgentClient(()=>settings,new Handler((_,_)=>throw new HttpRequestException("private connection error"))))
        {var result=await client.SendAsync(input,CancellationToken.None);Check(result.State==SendState.Unknown&&!result.Message.Contains("private"),"Transport failure remains unknown and redacted");}
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel();int attempts=0;
            using var client=new AgentClient(()=>settings,new Handler((_,_)=>{attempts++;return Task.FromResult(Response("{}"));}));
            var result=await client.SendAsync(input,cancelled.Token);Check(result.State==SendState.Failed&&attempts==0,"Pre-cancelled send never reaches transport");
        }
        using(var cancel=new CancellationTokenSource())
        using(var client=new AgentClient(()=>settings,new Handler(async(_,token)=>{cancel.Cancel();await Task.Delay(Timeout.Infinite,token);return Response("{}");})))
        {Check((await client.SendAsync(input,cancel.Token)).State==SendState.Unknown,"Cancellation during transmission cannot resend");}
        using(var client=new AgentClient(()=>settings,new Handler((message,_)=>
        {
            Check(message.Method==HttpMethod.Get&&message.RequestUri!.AbsolutePath=="/v1/models","Connection check sends no screenshot or prompt");
            return Task.FromResult(Response("{\"data\":[{\"id\":\"test-vision\"}]}"));
        })))Check((await client.CheckAsync(settings,CancellationToken.None)).Contains("检查通过"),"Connection check confirms configured model");
        settings.ModelName="";
        using(var client=new AgentClient(()=>settings,new Handler((_,_)=>throw new Exception("Must not send"))))
        {Check((await client.SendAsync(input,CancellationToken.None)).State==SendState.Failed,"Missing model fails before transport");}
        settings.ModelName="test-vision";settings.ApiKey="";

        // Real loopback HTTP validates framing, response headers and UTF-8 SSE through HttpClient.
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        try
        {
            settings.BaseUrl="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/v1";settings.Stream=true;
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var server=Task.Run(async()=>
            {
                using var peer=await listener.AcceptTcpClientAsync(deadline.Token);using var network=peer.GetStream();
                var header=new List<byte>();var octet=new byte[1];
                while(await network.ReadAsync(octet,deadline.Token)>0)
                {header.Add(octet[0]);if(header.Count>=4&&header.TakeLast(4).SequenceEqual(new byte[]{13,10,13,10}))break;}
                string headers=Encoding.ASCII.GetString(header.ToArray());
                int length=int.Parse(headers.Split("\r\n").Single(line=>line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase)).Split(':')[1]);
                var body=new byte[length];await network.ReadExactlyAsync(body,deadline.Token);
                using var json=JsonDocument.Parse(body);
                Check(json.RootElement.GetProperty("messages")[1].GetProperty("content")[0].GetProperty("text").GetString()==input.EffectivePrompt,"Real HTTP carries the prompt");
                byte[] content=Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\"真实 HTTP 测试回复\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n");
                await network.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nContent-Length: "+content.Length+"\r\nConnection: close\r\n\r\n"),deadline.Token);
                await network.WriteAsync(content,deadline.Token);
            },deadline.Token);
            using var client=new AgentClient(()=>settings);updates.Clear();
            Check((await client.SendWithReplyAsync(input,updates.Add,deadline.Token)).State==SendState.Sent&&updates[^1].Text=="真实 HTTP 测试回复","Real loopback SSE round trip passes");
            await server;
        }
        finally{listener.Stop();}
        return checks;
    }
    private static HttpResponseMessage Response(string body,string type="application/json")=>new(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,type)};
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>send(request,token);}
}
