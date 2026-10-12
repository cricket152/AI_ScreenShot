using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ScreenShot.App;
using ScreenShot.Core;

internal static class CodexProviderTests
{
    internal static async Task<int> RunAsync()
    {
        int checks=0;
        void Check(bool condition,string message)
        {
            if(!condition)throw new Exception(message);
            checks++;
        }

        CheckFailureDescriptions(Check);
        CheckModelPolicy(Check);
        CheckEvents(Check);
        await CheckRpcAsync(Check);
        await CheckBlockedWritesAsync(Check);
        await CheckPreparationFailuresAsync(Check);
        await CheckProviderAsync(Check);
        await CheckTextOnlyAsync(Check);
        await CheckResumeAsync(Check);
        return checks;
    }

    private static JsonElement Notification(string method,object parameters)
        =>JsonSerializer.SerializeToElement(new {method,@params=parameters});

    private static JsonElement Delta(string itemId,string text,string threadId="thread-current",string turnId="turn-current")
        =>Notification("item/agentMessage/delta",new {threadId,turnId,itemId,delta=text});

    private static JsonElement AgentMessage(string itemId,string text,string? phase=null,string method="item/completed")
        =>Notification(method,new {threadId="thread-current",turnId="turn-current",item=new {id=itemId,type="agentMessage",text,phase}});

    private static JsonElement Terminal(string status,string threadId="thread-current",string turnId="turn-current")
        =>Notification("turn/completed",new {threadId,turn=new {id=turnId,status,error=status=="failed"?new {message="provider failure"}:null}});

    private static void CheckFailureDescriptions(Action<bool,string> check)
    {
        const string privateContent="PRIVATE_PROMPT screenshot=data:image/png;base64,PRIVATE_IMAGE https://private.example/?token=PRIVATE_TOKEN";
        foreach(string category in new[]{"contextWindowExceeded","sessionBudgetExceeded","usageLimitExceeded","serverOverloaded","cyberPolicy","internalServerError","unauthorized","badRequest","threadRollbackFailed","sandboxError","other"})
        {
            string description=CodexProviderFailure.Describe(JsonSerializer.SerializeToElement(new
            {
                codexErrorInfo=category,message=privateContent,additionalDetails=privateContent
            }));
            check(description.Contains(category),"A protocol-defined failure category remains available for diagnosis");
            check(!description.Contains("PRIVATE_")&&!description.Contains("data:image/")&&!description.Contains("https://"),"Failure descriptions never display provider message or additional details");
        }

        foreach(string category in new[]{"httpConnectionFailed","responseStreamConnectionFailed","responseStreamDisconnected","responseTooManyFailedAttempts"})
        {
            string description=CodexProviderFailure.Describe(JsonSerializer.SerializeToElement(new
            {
                codexErrorInfo=new Dictionary<string,object>{{category,new {httpStatusCode=503,message=privateContent,url=privateContent,additionalDetails=privateContent}}},
                message=privateContent,additionalDetails=privateContent
            }));
            check(description.Contains(category)&&description.Contains("HTTP 503"),"A recognized connection failure exposes only its category and valid HTTP status");
            check(!description.Contains("PRIVATE_")&&!description.Contains("data:image/")&&!description.Contains("https://"),"Nested HTTP details cannot leak screenshots, credentials or URLs");
        }

        foreach(int status in new[]{401,403,429,400,422})
        {
            string description=CodexProviderFailure.Describe(JsonSerializer.SerializeToElement(new
            {
                codexErrorInfo=new {httpConnectionFailed=new {httpStatusCode=status}}
            }));
            check(description.Contains($"HTTP {status}")&&description.Contains(status switch {401 or 403=>"登录",429=>"限流",_=>"配置"}),"HTTP authentication, rate limit and invalid configuration failures provide actionable safe guidance");
        }

        foreach(object? invalidStatus in new object?[]{-1,0,99,600,long.MaxValue,"401 PRIVATE_TOKEN",null})
        {
            string description=CodexProviderFailure.Describe(JsonSerializer.SerializeToElement(new
            {
                codexErrorInfo=new {responseStreamDisconnected=new {httpStatusCode=invalidStatus,additionalDetails=privateContent}}
            }));
            check(description.Contains("responseStreamDisconnected")&&!description.Contains("HTTP ")&&!description.Contains("PRIVATE_"),"Malformed or out-of-range HTTP statuses are omitted without echoing untrusted values");
        }

        foreach(JsonElement error in new[]
        {
            JsonSerializer.SerializeToElement(new {message=privateContent}),
            JsonSerializer.SerializeToElement(new {codexErrorInfo=privateContent,message=privateContent}),
            JsonSerializer.SerializeToElement(new {codexErrorInfo=new Dictionary<string,object>{{privateContent,new {message=privateContent}}}}),
            JsonSerializer.SerializeToElement(new {codexErrorInfo=new {httpConnectionFailed=new {httpStatusCode=401},responseStreamDisconnected=new {httpStatusCode=500}}}),
            JsonSerializer.SerializeToElement(new {codexErrorInfo=new {httpConnectionFailed=privateContent}}),
            JsonSerializer.SerializeToElement(new {codexErrorInfo=Array.Empty<string>()}),
            JsonSerializer.SerializeToElement(new {codexErrorInfo=(string?)null}),
            JsonSerializer.SerializeToElement(privateContent),
            JsonSerializer.SerializeToElement(401),
            JsonSerializer.SerializeToElement<object?>(null)
        })
        {
            string description=CodexProviderFailure.Describe(error);
            check(description.Contains("genericUnknown")&&!description.Contains("PRIVATE_")&&!description.Contains("data:image/")&&!description.Contains("https://"),"Unknown or malformed provider errors fail safely without echoing any remote content");
        }
    }

    private static void CheckModelPolicy(Action<bool,string> check)
    {
        JsonElement configuration=JsonSerializer.SerializeToElement(new {model="test-image",model_reasoning_effort="high"});
        JsonElement catalog=JsonSerializer.SerializeToElement(new
        {
            data=new[]
            {
                new {id="default-id",model="different-default",isDefault=true,inputModalities=new[]{"text","image"},supportedReasoningEfforts=new[]{new {reasoningEffort="high"}}},
                new {id="configured-id",model="test-image",isDefault=false,inputModalities=new[]{"text","image"},supportedReasoningEfforts=new[]{new {reasoningEffort="high"}}}
            }
        });
        check(CodexProviderModelPolicy.Validate(configuration,catalog)=="test-image","Configured image-capable models are preserved instead of silently replacing them with the default");
        check(CodexProviderModelPolicy.Validate(JsonSerializer.SerializeToElement(new {model=(string?)null}),catalog)=="different-default","An unset model uses the catalog's image-capable default");
        check(CodexProviderModelPolicy.Validate(JsonSerializer.SerializeToElement(new {model="configured-id"}),catalog)=="test-image","A configured catalog model identity resolves to the supported model");
        check(CodexProviderModelPolicy.Validate(JsonSerializer.SerializeToElement(new {model="custom-only",model_provider="custom-provider"}),catalog).Contains("自定义 provider"),"Custom provider model names are not rejected against the built-in model catalog");

        foreach(JsonElement invalidConfiguration in new[]
        {
            JsonSerializer.SerializeToElement(new {model="unavailable-PRIVATE_MODEL"}),
            JsonSerializer.SerializeToElement(new {model="test-image",model_reasoning_effort="unsupported-PRIVATE_EFFORT"}),
            JsonSerializer.SerializeToElement(new {model="unavailable-PRIVATE_MODEL",model_provider="openai"})
        })
        {
            Exception? rejection=null;
            try{CodexProviderModelPolicy.Validate(invalidConfiguration,catalog);}
            catch(Exception error){rejection=error;}
            check(rejection is InvalidOperationException&&!rejection.ToString().Contains("PRIVATE_"),"Unavailable built-in models and unsupported reasoning efforts are rejected without exposing configuration values");
        }

        foreach(JsonElement invalidCatalog in new[]
        {
            JsonSerializer.SerializeToElement(new {data=new[]{new {model="test-image",isDefault=true,inputModalities=new[]{"text"}}}}),
            JsonSerializer.SerializeToElement(new {data=new[]{new {model="test-image",isDefault=true}}}),
            JsonSerializer.SerializeToElement(new {data=Array.Empty<object>()})
        })
        {
            Exception? rejection=null;
            try{CodexProviderModelPolicy.Validate(configuration,invalidCatalog);}
            catch(Exception error){rejection=error;}
            check(rejection is InvalidOperationException,"Missing image capability or a missing configured model rejects preparation instead of submitting a screenshot");
        }
        Exception? malformed=null;
        try{CodexProviderModelPolicy.Validate(configuration,JsonSerializer.SerializeToElement(new {data="PRIVATE_CATALOG"}));}
        catch(Exception error){malformed=error;}
        check(malformed is CodexTransportException&&!malformed.ToString().Contains("PRIVATE_"),"Malformed model catalogs fail without leaking raw provider data");

        string nonexistent=Path.Combine(Directory.GetCurrentDirectory(),"not-installed-codex-bin-"+Guid.NewGuid().ToString("N"));
        check(CodexProviderExecutable.DesktopCandidate(nonexistent,[])==null,"A missing desktop CLI installation does not create a false executable candidate");
        check(CodexProviderExecutable.DesktopCandidate(nonexistent,["codex.exe",Path.Combine(Directory.GetCurrentDirectory(),"codex.exe")])==null,"Relative and outside-root process paths cannot become desktop CLI candidates");
    }

    private static void CheckEvents(Action<bool,string> check)
    {
        var events=new CodexProviderEvents("thread-current","turn-current");
        check(events.Read(Delta("first","foreign","thread-other"))==null,"Provider discards another thread's output");
        check(events.Read(Delta("first","foreign",turnId:"turn-other"))==null,"Provider discards another turn's output");
        check(events.Read(Notification("item/agentMessage/delta",new {itemId="first",delta="missing identity"}))==null,"Provider discards output without thread and turn identity");
        var first=events.Read(Delta("first","第一"));
        check(first is {Phase:CodexReplyPhase.Thinking,Text:"第一"},"Provider emits a full initial reply snapshot");
        var continued=events.Read(Delta("first","段"));
        check(continued?.Text=="第一段","Provider accumulates message deltas without losing Chinese text");
        var second=events.Read(Delta("second","第二段"));
        check(second?.Text?.Contains("第一段")==true&&second.Text.Contains("第二段"),"Provider keeps distinct agent messages in the reply snapshot");
        var replaced=events.Read(AgentMessage("first","第一段修正"));
        check(replaced?.Text?.Contains("第一段修正")==true&&replaced.Text.Contains("第二段")&&!replaced.Text.Contains("第一段第一段"),"Completed agent items replace accumulated text instead of duplicating it");
        check(!events.IsComplete,"Completed agent items do not finish the turn");
        var final=events.Read(AgentMessage("final","最终答案","final_answer"));
        check(final?.Text?.Contains("最终答案")==true,"Final answer item appears in the visible reply snapshot");
        check(!events.IsComplete,"Final answer text waits for the native turn completion event");
        check(events.Read(Delta("final","duplicate after item completion"))==null,"Deltas after an agent item completion do not append duplicate text");
        check(events.Read(Notification("item/reasoning/textDelta",new {threadId="thread-current",turnId="turn-current",itemId="reason",delta="private reasoning"}))==null,"Reasoning deltas do not enter the visible reply");
        check(events.Read(Notification("item/completed",new {threadId="thread-current",turnId="turn-current",item=new {id="reason",type="reasoning",text="private reasoning"}}))==null,"Reasoning items do not enter the visible reply");
        check(events.Read(Terminal("completed",turnId:"turn-other"))==null&&!events.IsComplete,"Another turn completion cannot finish the observed turn");
        var completed=events.Read(Terminal("completed"));
        check(completed is {Phase:CodexReplyPhase.Completed,Text:"最终答案"}&&events.IsComplete,"Native completion finalizes the authoritative reply");
        check(events.Read(Delta("final","late"))==null,"Late deltas cannot change a completed reply");
        check(events.Read(Terminal("failed"))==null,"Late contradictory completion cannot reopen a completed reply");

        var repaired=new CodexProviderEvents("thread-current","turn-current");
        repaired.Read(Delta("final","不完整"));
        var repairedUpdate=repaired.Read(Notification("turn/completed",new {threadId="thread-current",turn=new {id="turn-current",status="completed",items=new[]{new {id="final",type="agentMessage",text="完整答案",phase="final_answer"}}}}));
        check(repairedUpdate is {Phase:CodexReplyPhase.Completed,Text:"完整答案"},"Terminal item snapshots repair missing deltas with the complete final answer");

        var phases=new CodexProviderEvents("thread-current","turn-current");
        phases.Read(AgentMessage("commentary","处理过程","commentary"));
        phases.Read(AgentMessage("final","最终结果","final_answer"));
        var finalOnly=phases.Read(Terminal("completed"));
        check(finalOnly?.Text=="最终结果","A successful turn displays final answer text without commentary");

        foreach(string status in new[]{"failed","interrupted"})
        {
            var partial=new CodexProviderEvents("thread-current","turn-current");
            partial.Read(Delta("partial","已经收到的正文"));
            var terminal=partial.Read(Terminal(status));
            check(partial.IsComplete&&terminal?.Phase==CodexReplyPhase.Unavailable,"Failed and interrupted turns become terminal without claiming success");
            check(terminal?.Text?.Contains("已经收到的正文")==true,"Failed and interrupted turns preserve received text");
        }

        var diagnosticFailure=new CodexProviderEvents("thread-current","turn-current");
        diagnosticFailure.Read(Delta("partial","保留的部分回复"));
        var failedWithReason=diagnosticFailure.Read(Notification("turn/completed",new
        {
            threadId="thread-current",turn=new {id="turn-current",status="failed",error=new
            {
                codexErrorInfo=new {responseStreamConnectionFailed=new {httpStatusCode=401}},
                message="PRIVATE_PROMPT data:image/png;base64,PRIVATE_IMAGE",additionalDetails="https://private.example/?token=PRIVATE_TOKEN"
            }}
        }));
        check(diagnosticFailure.IsComplete&&failedWithReason is {Phase:CodexReplyPhase.Unavailable,Text:"保留的部分回复"},"A failed terminal event preserves the exact partial reply and ends observation");
        check(failedWithReason?.Status?.Contains("responseStreamConnectionFailed")==true&&failedWithReason.Status.Contains("HTTP 401"),"Failed terminal events surface the safe provider category and HTTP status");
        check(failedWithReason?.Status?.Contains("PRIVATE_")==false&&failedWithReason.Status.Contains("data:image/")==false&&failedWithReason.Status.Contains("https://")==false,"Terminal failure status never displays provider free-form error details");

        var retrying=new CodexProviderEvents("thread-current","turn-current");
        retrying.Read(Delta("partial","重试前的正文"));
        var retry=retrying.Read(Notification("error",new
        {
            threadId="thread-current",turnId="turn-current",willRetry=true,error=new
            {
                codexErrorInfo=new {responseStreamDisconnected=new {httpStatusCode=503}},
                message="PRIVATE_PROMPT data:image/png;base64,PRIVATE_IMAGE",additionalDetails="PRIVATE_TOKEN"
            }
        }));
        check(retry is {Phase:CodexReplyPhase.Thinking,Text:"重试前的正文"}&&!retrying.IsComplete,"Provider-owned retry notifications keep the observed turn active and preserve partial output");
        check(retry?.Status?.Contains("连接")==true&&retry.Status.Contains("PRIVATE_")==false&&!retry.Status.Contains("data:image/"),"Retry progress communicates retrying without echoing remote content");
        retrying.Read(Delta("partial","，恢复后的正文"));
        var recovered=retrying.Read(Terminal("completed"));
        check(recovered is {Phase:CodexReplyPhase.Completed,Text:"重试前的正文，恢复后的正文"}&&retrying.IsComplete,"A provider retry can recover and finish normally without losing received text");

        var reportedError=new CodexProviderEvents("thread-current","turn-current");
        reportedError.Read(Delta("partial","错误之前收到的正文"));
        check(reportedError.Read(Notification("error",new {threadId="thread-other",turnId="turn-current",willRetry=false,error=new {codexErrorInfo="unauthorized"}}))==null,"Another thread's error cannot contaminate the observed reply failure");
        var errorProgress=reportedError.Read(Notification("error",new
        {
            threadId="thread-current",turnId="turn-current",willRetry=false,
            error=new {codexErrorInfo="usageLimitExceeded",message="PRIVATE_PROMPT",additionalDetails="PRIVATE_IMAGE"}
        }));
        check(errorProgress?.Phase==CodexReplyPhase.Thinking&&!reportedError.IsComplete,"A non-retrying error notification still waits for the authoritative turn terminal event");
        var missingTerminalReason=reportedError.Read(Terminal("failed"));
        check(missingTerminalReason is {Phase:CodexReplyPhase.Unavailable,Text:"错误之前收到的正文"}&&missingTerminalReason.Status?.Contains("usageLimitExceeded")==true&&!missingTerminalReason.Status.Contains("PRIVATE_"),"A terminal failure with no category retains the earlier safe error reason and partial text");
    }

    private static async Task CheckRpcAsync(Action<bool,string> check)
    {
        using var input=new ChannelTextReader();
        using var output=new ChannelTextWriter();
        await using(var client=new CodexRpcClient(input,output,leaveOpen:true))
        {
            var first=client.RequestAsync("first",new {value=1},CancellationToken.None);
            var second=client.RequestAsync("second",new {value=2},CancellationToken.None);
            var firstRequest=await output.NextAsync();
            var secondRequest=await output.NextAsync();
            check(firstRequest.GetProperty("method").GetString()=="first"&&secondRequest.GetProperty("method").GetString()=="second","Concurrent RPC writes remain complete JSON lines");
            input.Add(JsonSerializer.Serialize(new {id=secondRequest.GetProperty("id"),result=new {value="second result"}}));
            input.Add(JsonSerializer.Serialize(new {id=firstRequest.GetProperty("id"),result=new {value="first result"}}));
            check((await first.WaitAsync(TimeSpan.FromSeconds(3))).GetProperty("value").GetString()=="first result","Out-of-order RPC responses reach the first matching request");
            check((await second.WaitAsync(TimeSpan.FromSeconds(3))).GetProperty("value").GetString()=="second result","Out-of-order RPC responses reach the second matching request");

            await client.NotifyAsync("initialized",new {},CancellationToken.None);
            var notification=await output.NextAsync();
            check(notification.GetProperty("method").GetString()=="initialized"&&!notification.TryGetProperty("id",out _),"RPC notifications omit request identity");
            input.Add(Notification("turn/started",new {threadId="thread-current",turn=new {id="turn-current"}}).GetRawText());
            var inbound=await client.Notifications.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            check(inbound.GetProperty("method").GetString()=="turn/started","Server notifications are delivered to the event channel");

            input.Add(JsonSerializer.Serialize(new {id="approval-request",method="item/commandExecution/requestApproval",@params=new {threadId="thread-current",turnId="turn-current",command="forbidden command"}}));
            var refusal=await output.NextAsync();
            bool refused=refusal.TryGetProperty("error",out _)||refusal.TryGetProperty("result",out var decision)&&decision.TryGetProperty("decision",out var outcome)&&outcome.GetString() is "decline" or "cancel";
            check(refusal.GetProperty("id").GetString()=="approval-request"&&refused,"Unimplemented server requests are explicitly refused without granting execution");

            input.Add(JsonSerializer.Serialize(new {id=909,method="unknown-server-request",@params=new {value="untrusted"}}));
            var unsupported=await output.NextAsync();
            check(unsupported.GetProperty("id").GetInt32()==909&&unsupported.GetProperty("error").GetProperty("code").GetInt32()==-32601,"Unknown server requests receive an explicit unsupported-method response");

            using var cancellation=new CancellationTokenSource();
            var cancelled=client.RequestAsync("cancelled",new {value="single attempt"},cancellation.Token);
            var cancelledRequest=await output.NextAsync();
            int writesBeforeCancellation=output.Count;
            cancellation.Cancel();
            try{await cancelled.WaitAsync(TimeSpan.FromSeconds(3));check(false,"Cancelled RPC wait must stop");}
            catch(OperationCanceledException){check(output.Count==writesBeforeCancellation,"Cancelling an RPC wait never resends the request");}
            input.Add(JsonSerializer.Serialize(new {id=cancelledRequest.GetProperty("id"),result=new {value="late result"}}));
            var next=client.RequestAsync("after-cancellation",new {},CancellationToken.None);
            var nextRequest=await output.NextAsync();
            input.Add(JsonSerializer.Serialize(new {id=nextRequest.GetProperty("id"),result=new {value="correct"}}));
            check((await next.WaitAsync(TimeSpan.FromSeconds(3))).GetProperty("value").GetString()=="correct","Late response for a cancelled request cannot corrupt a later request");

            var rejected=client.RequestAsync("rejected",new {},CancellationToken.None);
            var rejectedRequest=await output.NextAsync();
            input.Add(JsonSerializer.Serialize(new {id=rejectedRequest.GetProperty("id"),error=new {code=-32001,message="PRIVATE_SCREENSHOT_OR_PROMPT"}}));
            try{await rejected.WaitAsync(TimeSpan.FromSeconds(3));check(false,"RPC server error must propagate");}
            catch(CodexRpcException error){check(error.ErrorCode==-32001&&!error.ToString().Contains("PRIVATE_SCREENSHOT_OR_PROMPT"),"RPC exposes error codes without leaking remote content");}

            var pending=client.RequestAsync("pending-at-eof",new {},CancellationToken.None);
            await output.NextAsync();
            input.Complete();
            Exception? eofError=null;
            try{await pending.WaitAsync(TimeSpan.FromSeconds(3));}
            catch(Exception error){eofError=error;}
            check(eofError!=null&&eofError is not TimeoutException,"EOF fails pending RPC requests without hanging");
        }

        using var malformedInput=new ChannelTextReader();
        using var malformedOutput=new ChannelTextWriter();
        await using(var malformedClient=new CodexRpcClient(malformedInput,malformedOutput,leaveOpen:true))
        {
            var pending=malformedClient.RequestAsync("malformed-response",new {},CancellationToken.None);
            await malformedOutput.NextAsync();
            malformedInput.Add("{PRIVATE_SCREENSHOT_OR_PROMPT:invalid-json");
            Exception? malformedError=null;
            try{await pending.WaitAsync(TimeSpan.FromSeconds(3));}
            catch(Exception error){malformedError=error;}
            check(malformedError!=null&&malformedError is not TimeoutException&&!malformedError.ToString().Contains("PRIVATE_SCREENSHOT_OR_PROMPT"),"Malformed transport messages fail pending requests without exposing raw content");
        }
    }

    private static async Task CheckPreparationFailuresAsync(Action<bool,string> check)
    {
        string directory=Directory.GetCurrentDirectory();
        var request=new SendRequest([1,2,3,4],"PRIVATE_SCREENSHOT_OR_PROMPT",new Workspace("test",directory));
        foreach(var scenario in new (object Configuration,object Catalog,string Reason)[]
        {
            (new {model="unavailable-new-model"},new {data=new[]{new {model="older-cli-default",isDefault=true,inputModalities=new[]{"text","image"}}}},"CLI"),
            (new {model="text-only"},new {data=new[]{new {model="text-only",isDefault=true,inputModalities=new[]{"text"}}}},"图片"),
            (new {model="test-image",model_reasoning_effort="unsupported-effort"},new {data=new[]{new {model="test-image",isDefault=true,inputModalities=new[]{"text","image"},supportedReasoningEfforts=new[]{new {reasoningEffort="high"}}}}},"reasoning effort")
        })
        {
            using var input=new ChannelTextReader();
            using var output=new ChannelTextWriter();
            using var provider=new CodexProviderTarget(new Settings(),(_,_)=>new CodexRpcClient(input,output),_=>"fake.exe");
            var updates=new ConcurrentQueue<CodexReplyUpdate>();
            var running=provider.SendWithReplyAsync(request,updates.Enqueue,CancellationToken.None);
            await AcceptReadinessAsync(input,output,check,directory,scenario.Configuration,scenario.Catalog);
            var result=await running.WaitAsync(TimeSpan.FromSeconds(3));
            check(result.State==SendState.Failed&&result.Message.Contains(scenario.Reason),"Incompatible CLI/model/image configuration provides a preparation failure with actionable guidance");
            check(output.MethodCount("thread/start")==0&&output.MethodCount("turn/start")==0&&!updates.Any(update=>update.Phase==CodexReplyPhase.Submitted),"Preflight model validation prevents creating or submitting an incompatible screenshot turn");
            check(!result.Message.Contains("PRIVATE_")&&input.IsDisposed&&output.IsDisposed,"Failed readiness releases its transport without leaking screenshot or prompt content");
        }

        using(var input=new ChannelTextReader())
        using(var output=new ChannelTextWriter())
        using(var provider=new CodexProviderTarget(new Settings{CodexDirectory=directory},(_,_)=>new CodexRpcClient(input,output),_=>"fake.exe"))
        {
            var running=provider.CheckAsync(CancellationToken.None);
            await AcceptReadinessAsync(input,output,check,directory);
            string result=await running.WaitAsync(TimeSpan.FromSeconds(3));
            check(result.Contains("test-image")&&output.MethodCount("thread/start")==0&&output.MethodCount("turn/start")==0,"Provider connection diagnostics validate model readiness without creating a chat or submitting content");
        }
    }

    private static async Task CheckProviderAsync(Action<bool,string> check)
    {
        string directory=Directory.GetCurrentDirectory();
        var request=new SendRequest([1,2,3,4],"第一行\n第二行 \"quoted\"",new Workspace("test",directory));
        var settings=new Settings();

        using(var input=new ChannelTextReader())
        using(var output=new ChannelTextWriter())
        using(var provider=new CodexProviderTarget(settings,(_,_)=>new CodexRpcClient(input,output),_=>"fake.exe"))
        {
            var updates=new ConcurrentQueue<CodexReplyUpdate>();
            var running=provider.SendWithReplyAsync(request,updates.Enqueue,CancellationToken.None);
            var turn=await AcceptPreparationAsync(input,output,check,directory,rejectName:true);
            Reply(input,turn,new {turn=new {id="turn-current"}});
            input.Add(Delta("final","标题设置失败后的正文").GetRawText());
            input.Add(AgentMessage("final","标题设置失败后的完整答案","final_answer").GetRawText());
            input.Add(Terminal("completed").GetRawText());
            var result=await running.WaitAsync(TimeSpan.FromSeconds(3));
            check(result.State==SendState.Sent&&updates.Last() is {Phase:CodexReplyPhase.Completed,Text:"标题设置失败后的完整答案"},"A rejected optional thread title does not block screenshot submission or the complete reply");
            check(output.MethodCount("thread/start")==1&&output.MethodCount("thread/name/set")==1&&output.MethodCount("turn/start")==1,"A title rejection does not replay naming, thread creation or turn submission");
            check(!result.Message.Contains("PRIVATE_")&&!updates.Any(update=>update.Status?.Contains("PRIVATE_")==true),"Optional title rejection cannot leak its remote error message into the result or progress");
        }

        using(var input=new ChannelTextReader())
        using(var output=new ChannelTextWriter())
        using(var provider=new CodexProviderTarget(settings,(_,_)=>new CodexRpcClient(input,output),_=>"fake.exe"))
        {
            var updates=new ConcurrentQueue<CodexReplyUpdate>();
            var running=provider.SendWithReplyAsync(request,updates.Enqueue,CancellationToken.None);
            var turn=await AcceptPreparationAsync(input,output,check,directory);
            var turnParameters=turn.GetProperty("params");
            var content=turnParameters.GetProperty("input");
            check(turnParameters.GetProperty("threadId").GetString()=="thread-current","Turn submission uses the thread identity returned by the provider");
            check(content[0].GetProperty("text").GetString()==request.Prompt,"Provider sends multiline prompts without shell escaping or truncation");
            check(content[1].GetProperty("type").GetString()=="image"&&content[1].GetProperty("url").GetString()=="data:image/png;base64,"+Convert.ToBase64String(request.Png),"Provider transports PNG in memory as a data URL");
            Reply(input,turn,new {turn=new {id="turn-current"}});
            input.Add(Delta("foreign","wrong",threadId:"thread-other").GetRawText());
            input.Add(Delta("final","流式答案").GetRawText());
            input.Add(AgentMessage("final","完整答案","final_answer").GetRawText());
            input.Add(Terminal("completed").GetRawText());
            var result=await running.WaitAsync(TimeSpan.FromSeconds(3));
            check(result.State==SendState.Sent&&updates.Any(update=>update.Phase==CodexReplyPhase.Submitted),"Acknowledged provider turns become submitted");
            check(updates.Any(update=>update.Phase==CodexReplyPhase.Thinking&&update.Text=="流式答案"),"Provider delivers streamed output to the existing reply callback");
            check(updates.Count(update=>update.Phase==CodexReplyPhase.Completed)==1&&updates.Last().Text=="完整答案","Provider finishes with one complete authoritative reply");
            check(!updates.Any(update=>update.Text?.Contains("wrong")==true)&&output.MethodCount("turn/start")==1,"Foreign output cannot enter the reply and turn submission occurs only once");
            check(input.IsDisposed&&output.IsDisposed,"Terminal provider turns release their independent transport");
        }

        using(var input=new ChannelTextReader())
        using(var output=new ChannelTextWriter())
        using(var provider=new CodexProviderTarget(settings,(_,_)=>new CodexRpcClient(input,output),_=>"fake.exe"))
        {
            var updates=new ConcurrentQueue<CodexReplyUpdate>();
            var running=provider.SendWithReplyAsync(request,updates.Enqueue,CancellationToken.None);
            var turn=await AcceptPreparationAsync(input,output,check,directory);
            Reply(input,turn,new {turn=new {id="turn-current"}});
            input.Add(Delta("partial","失败前收到的正文").GetRawText());
            input.Add(Notification("turn/completed",new
            {
                threadId="thread-current",turn=new {id="turn-current",status="failed",error=new
                {
                    codexErrorInfo=new {httpConnectionFailed=new {httpStatusCode=403}},
                    message="PRIVATE_PROMPT data:image/png;base64,PRIVATE_IMAGE",additionalDetails="https://private.example/?token=PRIVATE_TOKEN"
                }}
            }).GetRawText());
            var result=await running.WaitAsync(TimeSpan.FromSeconds(3));
            var failed=updates.Last();
            check(result.State==SendState.Sent&&failed is {Phase:CodexReplyPhase.Unavailable,Text:"失败前收到的正文"},"Accepted turns can fail their reply without losing the submitted state or partial output");
            check(failed.Status?.Contains("httpConnectionFailed")==true&&failed.Status.Contains("HTTP 403")&&!failed.Status.Contains("PRIVATE_")&&!failed.Status.Contains("data:image/")&&!failed.Status.Contains("https://"),"Provider routes a safe actionable failure reason through the visible reply callback");
            check(output.MethodCount("turn/start")==1&&!updates.Any(update=>update.Phase==CodexReplyPhase.Completed),"Reply failure never resubmits or falsely reports a complete answer");
        }

        using(var input=new ChannelTextReader())
        using(var output=new ChannelTextWriter())
        using(var provider=new CodexProviderTarget(settings,(_,_)=>new CodexRpcClient(input,output),_=>"fake.exe"))
        {
            var updates=new ConcurrentQueue<CodexReplyUpdate>();
            var running=provider.SendWithReplyAsync(request,updates.Enqueue,CancellationToken.None);
            var turn=await AcceptPreparationAsync(input,output,check,directory);
            Reply(input,turn,new {turn=new {id="turn-current"}});
            input.Add(Delta("partial","回复正文").GetRawText());
            input.Add(Notification("error",new
            {
                threadId="thread-current",turnId="turn-current",willRetry=true,error=new
                {
                    codexErrorInfo=new {responseStreamDisconnected=new {httpStatusCode=503}},message="PRIVATE_PROMPT"
                }
            }).GetRawText());
            input.Add(AgentMessage("partial","恢复后的完整正文","final_answer").GetRawText());
            input.Add(Terminal("completed").GetRawText());
            var result=await running.WaitAsync(TimeSpan.FromSeconds(3));
            check(result.State==SendState.Sent&&updates.Any(update=>update.Phase==CodexReplyPhase.Thinking&&update.Status?.Contains("连接")==true),"Provider surfaces its own connection retries as ongoing reply progress");
            check(updates.Last() is {Phase:CodexReplyPhase.Completed,Text:"恢复后的完整正文"}&&!updates.Any(update=>update.Phase==CodexReplyPhase.Unavailable),"Successful provider retry completes once with the authoritative final answer");
            check(output.MethodCount("turn/start")==1,"Native provider retries do not cause a duplicate screenshot submission");
        }

        using(var input=new ChannelTextReader())
        using(var output=new ChannelTextWriter())
        using(var provider=new CodexProviderTarget(settings,(_,_)=>new CodexRpcClient(input,output),_=>"fake.exe"))
        {
            var updates=new ConcurrentQueue<CodexReplyUpdate>();
            var running=provider.SendWithReplyAsync(request,updates.Enqueue,CancellationToken.None);
            var turn=await AcceptPreparationAsync(input,output,check,directory);
            input.Add(JsonSerializer.Serialize(new {id=turn.GetProperty("id"),error=new {code=-32000,message="PRIVATE_SCREENSHOT_OR_PROMPT"}}));
            var result=await running.WaitAsync(TimeSpan.FromSeconds(3));
            check(result.State==SendState.Failed&&!updates.Any(update=>update.Phase==CodexReplyPhase.Submitted),"A rejected turn is failed without claiming submission");
            check(!result.Message.Contains("PRIVATE_SCREENSHOT_OR_PROMPT")&&output.MethodCount("turn/start")==1,"Rejected turns neither leak remote content nor automatically retry");
        }

        using(var input=new ChannelTextReader())
        using(var output=new ChannelTextWriter())
        using(var provider=new CodexProviderTarget(settings,(_,_)=>new CodexRpcClient(input,output),_=>"fake.exe"))
        {
            var updates=new ConcurrentQueue<CodexReplyUpdate>();
            var running=provider.SendWithReplyAsync(request,updates.Enqueue,CancellationToken.None);
            await AcceptPreparationAsync(input,output,check,directory);
            input.Complete();
            var result=await running.WaitAsync(TimeSpan.FromSeconds(3));
            check(result.State==SendState.Unknown&&!result.Retryable&&!updates.Any(update=>update.Phase==CodexReplyPhase.Submitted),"Lost acknowledgement after submission yields Unknown without making a resend safe");
            check(output.MethodCount("turn/start")==1,"Unknown provider submission never creates a second turn");
        }

        using(var input=new ChannelTextReader())
        using(var output=new ChannelTextWriter())
        using(var provider=new CodexProviderTarget(settings,(_,_)=>new CodexRpcClient(input,output),_=>"fake.exe"))
        {
            var updates=new ConcurrentQueue<CodexReplyUpdate>();
            var streamed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var running=provider.SendWithReplyAsync(request,update=>{updates.Enqueue(update);if(update.Text=="已接收正文")streamed.TrySetResult();},CancellationToken.None);
            var turn=await AcceptPreparationAsync(input,output,check,directory);
            Reply(input,turn,new {turn=new {id="turn-current"}});
            input.Add(Delta("partial","已接收正文").GetRawText());
            await streamed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            input.Complete();
            var result=await running.WaitAsync(TimeSpan.FromSeconds(3));
            check(result.State==SendState.Sent&&updates.Last().Phase==CodexReplyPhase.Unavailable,"Transport loss after acknowledgement preserves submitted state with unavailable completion");
            check(updates.Any(update=>update.Text=="已接收正文")&&!updates.Any(update=>update.Phase==CodexReplyPhase.Completed),"Incomplete streams retain received text without falsely claiming a completed answer");
        }

        using(var input=new ChannelTextReader())
        using(var output=new ChannelTextWriter())
        using(var cancellation=new CancellationTokenSource())
        using(var provider=new CodexProviderTarget(settings,(_,_)=>new CodexRpcClient(input,output),_=>"fake.exe"))
        {
            var updates=new ConcurrentQueue<CodexReplyUpdate>();
            var streamed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var running=provider.SendWithReplyAsync(request,update=>
            {updates.Enqueue(update);if(update.Text=="停止前的正文")streamed.TrySetResult();},cancellation.Token);
            var turn=await AcceptPreparationAsync(input,output,check,directory);
            Reply(input,turn,new {turn=new {id="turn-current"}});
            input.Add(Delta("final","停止前的正文").GetRawText());await streamed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            cancellation.Cancel();cancellation.Cancel();
            var interrupt=await output.NextAsync();
            check(interrupt.GetProperty("method").GetString()=="turn/interrupt"&&interrupt.GetProperty("params").GetProperty("threadId").GetString()=="thread-current"&&interrupt.GetProperty("params").GetProperty("turnId").GetString()=="turn-current","Stop interrupts exactly the acknowledged screenshot turn");
            Reply(input,interrupt,new {});
            check(!running.IsCompleted&&!updates.Any(update=>update.Status?.Contains("Codex 已停止")==true),"An interrupt RPC acknowledgement alone is not a stop confirmation");
            input.Add(Terminal("interrupted").GetRawText());
            var result=await running.WaitAsync(TimeSpan.FromSeconds(3));
            check(result.State==SendState.Sent&&updates.Last() is {Phase:CodexReplyPhase.Unavailable,Text:"停止前的正文"}&&updates.Last().Status?.Contains("Codex 已停止")==true,"Confirmed interruption retains submission state and partial reply");
            check(input.IsDisposed&&output.IsDisposed&&output.MethodCount("turn/start")==1&&output.MethodCount("turn/interrupt")==1,"Stop releases its own transport and neither repeats submission nor interruption");
        }

        using(var input=new ChannelTextReader())
        using(var output=new ChannelTextWriter())
        using(var cancellation=new CancellationTokenSource())
        using(var provider=new CodexProviderTarget(settings,(_,_)=>new CodexRpcClient(input,output),_=>"fake.exe"))
        {
            var updates=new ConcurrentQueue<CodexReplyUpdate>();
            var running=provider.SendWithReplyAsync(request,updates.Enqueue,cancellation.Token);
            var turn=await AcceptPreparationAsync(input,output,check,directory);
            cancellation.Cancel();var result=await running.WaitAsync(TimeSpan.FromSeconds(3));
            check(result.State==SendState.Unknown&&output.MethodCount("turn/interrupt")==0,"Stop without an acknowledged turn ID stays Unknown and cannot interrupt an unrelated turn");
            Reply(input,turn,new {turn=new {id="turn-current"}});
            var interrupt=await output.NextAsync();
            check(interrupt.GetProperty("method").GetString()=="turn/interrupt"&&interrupt.GetProperty("params").GetProperty("turnId").GetString()=="turn-current","A late acknowledgement still interrupts the originally submitted turn");
            Reply(input,interrupt,new {});input.Add(Terminal("interrupted").GetRawText());
            await input.DisposedTask.WaitAsync(TimeSpan.FromSeconds(3));
            check(updates.Count==0&&output.MethodCount("turn/start")==1&&output.MethodCount("turn/interrupt")==1,"Late stop confirmation cannot reopen a detached UI or resubmit the image");
        }

        foreach(bool reject in new[]{true,false})
        using(var input=new ChannelTextReader())
        using(var output=new ChannelTextWriter())
        using(var cancellation=new CancellationTokenSource())
        using(var provider=new CodexProviderTarget(settings,(_,_)=>new CodexRpcClient(input,output),_=>"fake.exe",TimeSpan.FromMilliseconds(150)))
        {
            var updates=new ConcurrentQueue<CodexReplyUpdate>();var streamed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var running=provider.SendWithReplyAsync(request,update=>{updates.Enqueue(update);if(update.Text=="保留正文")streamed.TrySetResult();},cancellation.Token);
            var turn=await AcceptPreparationAsync(input,output,check,directory);Reply(input,turn,new {turn=new {id="turn-current"}});
            input.Add(Delta("final","保留正文").GetRawText());await streamed.Task.WaitAsync(TimeSpan.FromSeconds(3));cancellation.Cancel();
            var interrupt=await output.NextAsync();
            if(reject)input.Add(JsonSerializer.Serialize(new {id=interrupt.GetProperty("id"),error=new {code=-32601,message="PRIVATE_CONTENT"}}));
            else Reply(input,interrupt,new {});
            var result=await running.WaitAsync(TimeSpan.FromSeconds(3));var last=updates.Last();
            check(result.State==SendState.Sent&&last is {Phase:CodexReplyPhase.Unavailable,Text:"保留正文"}&&last.Status?.Contains("未确认")==true&&!last.Status.Contains("PRIVATE_CONTENT"),"Rejected or unconfirmed interruption retains text and never claims a successful stop");
            check(input.IsDisposed&&output.MethodCount("turn/interrupt")==1&&output.MethodCount("turn/start")==1,"Unconfirmed stop is bounded and releases the owned client without retries");
        }

        using(var input=new ChannelTextReader())
        using(var output=new ChannelTextWriter())
        using(var cancellation=new CancellationTokenSource())
        using(var provider=new CodexProviderTarget(settings,(_,_)=>new CodexRpcClient(input,output),_=>"fake.exe"))
        {
            var updates=new ConcurrentQueue<CodexReplyUpdate>();
            var running=provider.SendWithReplyAsync(request,update=>{updates.Enqueue(update);if(update.Phase==CodexReplyPhase.Completed)cancellation.Cancel();},cancellation.Token);
            var turn=await AcceptPreparationAsync(input,output,check,directory);Reply(input,turn,new {turn=new {id="turn-current"}});
            input.Add(AgentMessage("final","正常完成","final_answer").GetRawText());input.Add(Terminal("completed").GetRawText());
            check((await running.WaitAsync(TimeSpan.FromSeconds(3))).State==SendState.Sent&&updates.Last().Phase==CodexReplyPhase.Completed&&output.MethodCount("turn/interrupt")==0,"Natural completion racing with stop retains its real terminal state");
        }

        using(var input=new ChannelTextReader())
        using(var output=new ChannelTextWriter())
        using(var cancellation=new CancellationTokenSource())
        using(var provider=new CodexProviderTarget(settings,(_,_)=>new CodexRpcClient(input,output),_=>"fake.exe"))
        {
            var updates=new ConcurrentQueue<CodexReplyUpdate>();
            var running=provider.SendWithReplyAsync(request,updates.Enqueue,cancellation.Token);
            var thread=await AcceptThreadStartAsync(input,output,check,directory);
            input.BeforeReturningLine=line=>
            {
                using var document=JsonDocument.Parse(line);
                if(document.RootElement.TryGetProperty("result",out var result)&&result.TryGetProperty("thread",out _))cancellation.Cancel();
            };
            Reply(input,thread,new {thread=new {id="thread-current"}});
            var result=await running.WaitAsync(TimeSpan.FromSeconds(3));
            await input.DisposedTask.WaitAsync(TimeSpan.FromSeconds(3));
            check(result.State==SendState.Failed&&output.MethodCount("turn/start")==0,"Cancellation when the thread acknowledgement arrives prevents turn submission");
            check(!updates.Any(update=>update.Phase==CodexReplyPhase.Submitted),"Cancelled thread preparation never reports a submitted screenshot");
        }

        int clientsCreated=0;
        using(var cancellation=new CancellationTokenSource())
        using(var provider=new CodexProviderTarget(settings,(_,_)=>{clientsCreated++;throw new Exception("unexpected transport");},_=>"fake.exe"))
        {
            cancellation.Cancel();
            var result=await provider.SendWithReplyAsync(request,_=>{},cancellation.Token);
            check(result.State==SendState.Failed&&clientsCreated==0,"Cancelled preparation cannot create a provider transport or submit a turn");
            var missingDirectory=await provider.SendWithReplyAsync(request with {Workspace=new Workspace("missing")},_=>{},CancellationToken.None);
            check(missingDirectory.State==SendState.Failed&&clientsCreated==0,"Provider requires an explicit valid directory before accessing its transport");
        }
    }

    private static async Task CheckTextOnlyAsync(Action<bool,string> check)
    {
        string directory=Directory.GetCurrentDirectory();
        using var input=new ChannelTextReader();using var output=new ChannelTextWriter();
        int clients=0;
        using var provider=new CodexProviderTarget(new Settings(),(_,_)=>{clients++;return new CodexRpcClient(input,output);},_=>"fake.exe");
        var request=new SendRequest([],"  你好\n继续对话  ",new Workspace("text diagnostic",directory));
        check((await provider.SendWithReplyAsync(request with {Prompt=" \r\n\t"},_=>{},CancellationToken.None)).State==SendState.Failed&&clients==0,"Empty text-only Codex input fails before creating a provider process");
        var updates=new ConcurrentQueue<CodexReplyUpdate>();
        var running=provider.SendWithReplyAsync(request,updates.Enqueue,CancellationToken.None);
        var turn=await AcceptPreparationAsync(input,output,check,directory,configuration:new {model="text-only"},models:new {data=new[]{new {model="text-only",isDefault=true,inputModalities=new[]{"text"}}}});
        var parts=turn.GetProperty("params").GetProperty("input");
        check(parts.GetArrayLength()==1&&parts[0].GetProperty("type").GetString()=="text"&&parts[0].GetProperty("text").GetString()==request.Prompt,"Text-only Codex requests accept text models and submit only the manual prompt");
        Reply(input,turn,new {turn=new {id="turn-current"}});
        input.Add(Delta("text-reply","你好").GetRawText());input.Add(Terminal("completed").GetRawText());
        check((await running.WaitAsync(TimeSpan.FromSeconds(3))).State==SendState.Sent&&updates.Last() is {Phase:CodexReplyPhase.Completed,Text:"你好"},"Text-only Codex replies use the normal streamed response path");
        check(output.MethodCount("turn/start")==1,"A text-only Codex request is submitted once");
    }

    private static async Task CheckResumeAsync(Action<bool,string> check)
    {
        string directory=Path.GetTempPath();var input=new ChannelTextReader();var output=new ChannelTextWriter();
        using var provider=new CodexProviderTarget(new Settings(),(_,_)=>new CodexRpcClient(input,output),_=>"fake.exe");
        string? savedThread=null,savedTurn=null;
        var request=new SendRequest([],"继续当前对话",new("test",directory)){Conversation=new(){ThreadId="saved-thread",SessionChanged=(thread,turn)=>{savedThread=thread;savedTurn=turn;}}};
        var running=provider.SendWithReplyAsync(request,_=>{},CancellationToken.None);
        await AcceptReadinessAsync(input,output,check,directory);
        var resume=await output.NextAsync();
        check(resume.GetProperty("method").GetString()=="thread/resume"&&resume.GetProperty("params").GetProperty("threadId").GetString()=="saved-thread","Codex resumes only the selected conversation's stored native thread");
        Reply(input,resume,new{thread=new{id="saved-thread"}});
        var turn=await output.NextAsync();
        check(turn.GetProperty("method").GetString()=="turn/start"&&turn.GetProperty("params").GetProperty("threadId").GetString()=="saved-thread"&&savedThread=="saved-thread"&&savedTurn==null,"Acknowledged thread ID is persisted before turn submission");
        Reply(input,turn,new{turn=new{id="saved-turn"}});input.Add(Terminal("completed","saved-thread","saved-turn").GetRawText());
        check((await running.WaitAsync(TimeSpan.FromSeconds(3))).State==SendState.Sent&&savedTurn=="saved-turn"&&output.MethodCount("thread/start")==0,"Resumed turns persist their exact IDs without creating another thread");
        var recoveryInput=new ChannelTextReader();var recoveryOutput=new ChannelTextWriter();
        using var recoveryProvider=new CodexProviderTarget(new Settings(),(_,_)=>new CodexRpcClient(recoveryInput,recoveryOutput),_=>"fake.exe");
        var recovering=recoveryProvider.RecoverAsync(new(){ThreadId="saved-thread",TurnId="saved-turn",Workspace=request.Workspace},CancellationToken.None);
        var initialize=await recoveryOutput.NextAsync();Reply(recoveryInput,initialize,new{});await recoveryOutput.NextAsync();
        var read=await recoveryOutput.NextAsync();
        check(read.GetProperty("method").GetString()=="thread/read"&&read.GetProperty("params").GetProperty("includeTurns").GetBoolean(),"Recovery uses only the public thread/read API");
        Reply(recoveryInput,read,new{thread=new{turns=new[]{new{id="saved-turn",status="completed",items=new[]{new{id="answer",type="agentMessage",text="recovered answer",phase="final_answer"}}}}}});
        check(await recovering is {Phase:CodexReplyPhase.Completed,Text:"recovered answer"}&&recoveryOutput.MethodCount("turn/start")==0,"Recovery reconciles a native terminal reply without resubmitting content");
    }
    private static async Task<JsonElement> AcceptPreparationAsync(ChannelTextReader input,ChannelTextWriter output,Action<bool,string> check,string directory,bool rejectName=false,object? configuration=null,object? models=null)
    {
        var thread=await AcceptThreadStartAsync(input,output,check,directory,configuration,models);
        Reply(input,thread,new {thread=new {id="thread-current"}});
        var name=await output.NextAsync();
        check(name.GetProperty("method").GetString()=="thread/name/set"&&name.GetProperty("params").GetProperty("threadId").GetString()=="thread-current","Provider assigns a title only to its own acknowledged thread");
        string title=name.GetProperty("params").GetProperty("name").GetString()??"";
        check(title.StartsWith("AI ScreenShot · ",StringComparison.Ordinal)&&!title.Contains("第一行")&&!title.Contains(directory),"Provider thread titles identify the screenshot tool without storing the prompt or selected directory in the title");
        if(rejectName)input.Add(JsonSerializer.Serialize(new {id=name.GetProperty("id"),error=new {code=-32000,message="PRIVATE_TITLE_ERROR"}}));
        else Reply(input,name,new {});
        var turn=await output.NextAsync();
        check(turn.GetProperty("method").GetString()=="turn/start","Provider submits exactly one turn after preparation");
        return turn;
    }

    private static async Task<JsonElement> AcceptThreadStartAsync(ChannelTextReader input,ChannelTextWriter output,Action<bool,string> check,string directory,object? configuration=null,object? models=null)
    {
        await AcceptReadinessAsync(input,output,check,directory,configuration,models);
        var thread=await output.NextAsync();
        var parameters=thread.GetProperty("params");
        check(thread.GetProperty("method").GetString()=="thread/start"&&parameters.GetProperty("cwd").GetString()==directory,"Provider creates a thread in the explicitly selected working directory");
        check(parameters.GetProperty("sandbox").GetString()=="read-only"&&parameters.GetProperty("approvalPolicy").GetString()=="never"&&!parameters.GetProperty("ephemeral").GetBoolean(),"Screenshot threads use read-only execution and native persisted sessions");
        return thread;
    }

    private static async Task AcceptReadinessAsync(ChannelTextReader input,ChannelTextWriter output,Action<bool,string> check,string directory,object? configuration=null,object? models=null)
    {
        var initialize=await output.NextAsync();
        check(initialize.GetProperty("method").GetString()=="initialize","Provider initializes the independent JSON-RPC transport");
        Reply(input,initialize,new {});
        var initialized=await output.NextAsync();
        check(initialized.GetProperty("method").GetString()=="initialized"&&!initialized.TryGetProperty("id",out _),"Provider completes the initialization handshake with a notification");
        var account=await output.NextAsync();
        check(account.GetProperty("method").GetString()=="account/read","Provider checks authentication before submitting a screenshot");
        Reply(input,account,new {requiresOpenaiAuth=false});
        var config=await output.NextAsync();
        var catalog=await output.NextAsync();
        check(config.GetProperty("method").GetString()=="config/read"&&config.GetProperty("params").GetProperty("cwd").GetString()==directory&&!config.GetProperty("params").GetProperty("includeLayers").GetBoolean(),"Provider checks effective model configuration for the selected directory without reading raw configuration layers");
        check(catalog.GetProperty("method").GetString()=="model/list"&&catalog.GetProperty("params").GetProperty("includeHidden").GetBoolean(),"Provider includes hidden model entries in image capability preparation");
        Reply(input,catalog,models??new {data=new[]{new {model="test-image",isDefault=true,inputModalities=new[]{"text","image"}}}});
        Reply(input,config,new {config=configuration??new {model="test-image",model_provider=(string?)null,model_reasoning_effort=(string?)null}});
    }

    private static async Task CheckBlockedWritesAsync(Action<bool,string> check)
    {
        foreach(bool blockFlush in new[]{false,true})
        {
            using var input=new ChannelTextReader();
            using var output=new BlockingTextWriter(blockFlush);
            using var cancellation=new CancellationTokenSource();
            await using var client=new CodexRpcClient(input,output,leaveOpen:true);
            var request=client.RequestAsync("blocked-write",new {data="in-memory PNG payload"},cancellation.Token);
            try
            {
                await output.EnteredTask.WaitAsync(TimeSpan.FromSeconds(3));
                var frame=await output.NextAsync();
                cancellation.Cancel();
                Exception? error=null;
                try{await request.WaitAsync(TimeSpan.FromSeconds(3));}
                catch(Exception caught){error=caught;}
                check(error is OperationCanceledException,blockFlush?"Request cancellation exits a blocked Flush without waiting for transport completion":"Request cancellation exits a blocked WriteLine without waiting for transport completion");
                check(output.MethodCount("blocked-write")==1&&!output.FinishedTask.IsCompleted,"Cancelling a blocked transport wait never replays or prematurely completes its frame");
                output.Release();
                await output.FinishedTask.WaitAsync(TimeSpan.FromSeconds(3));
                input.Add(JsonSerializer.Serialize(new {id=frame.GetProperty("id"),result=new {value="late cancelled result"}}));
                var subsequent=client.RequestAsync("after-blocked-write",new {},CancellationToken.None);
                var subsequentFrame=await output.NextAsync();
                Reply(input,subsequentFrame,new {value="transport released"});
                check((await subsequent.WaitAsync(TimeSpan.FromSeconds(3))).GetProperty("value").GetString()=="transport released","Released background writes unlock the transport and cancelled responses cannot redirect later requests");
            }
            finally{output.Release();}
        }
    }

    private static void Reply(ChannelTextReader input,JsonElement request,object result)
        =>input.Add(JsonSerializer.Serialize(new {id=request.GetProperty("id"),result}));

    private sealed class ChannelTextReader : TextReader
    {
        private readonly Channel<string> lines=Channel.CreateUnbounded<string>();
        private readonly TaskCompletionSource disposed=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Action<string>? BeforeReturningLine {get;set;}
        internal bool IsDisposed=>disposed.Task.IsCompleted;
        internal Task DisposedTask=>disposed.Task;
        internal void Add(string line)=>lines.Writer.TryWrite(line);
        internal void Complete()=>lines.Writer.TryComplete();
        public override Task<string?> ReadLineAsync()=>ReadLineAsync(CancellationToken.None).AsTask();
        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            try
            {
                string line=await lines.Reader.ReadAsync(cancellationToken);
                BeforeReturningLine?.Invoke(line);
                return line;
            }
            catch(ChannelClosedException){return null;}
        }
        protected override void Dispose(bool disposing){if(disposing){Complete();disposed.TrySetResult();}base.Dispose(disposing);}
    }

    private class ChannelTextWriter : TextWriter
    {
        private readonly Channel<string> lines=Channel.CreateUnbounded<string>();
        private readonly ConcurrentQueue<string> written=new();
        internal bool IsDisposed {get;private set;}
        public override Encoding Encoding=>Encoding.UTF8;
        internal int Count=>written.Count;
        internal int MethodCount(string method)=>written.Count(line=>
        {
            using var document=JsonDocument.Parse(line);
            return document.RootElement.TryGetProperty("method",out var found)&&found.GetString()==method;
        });
        internal async Task<JsonElement> NextAsync()
        {
            using var document=JsonDocument.Parse(await lines.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
            return document.RootElement.Clone();
        }
        public override Task WriteLineAsync(string? value)
        {
            string line=value??"";written.Enqueue(line);lines.Writer.TryWrite(line);return Task.CompletedTask;
        }
        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer,CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested();return WriteLineAsync(buffer.ToString());
        }
        public override Task FlushAsync()=>Task.CompletedTask;
        public override Task FlushAsync(CancellationToken cancellationToken){cancellationToken.ThrowIfCancellationRequested();return Task.CompletedTask;}
        protected override void Dispose(bool disposing){if(disposing){IsDisposed=true;lines.Writer.TryComplete();}base.Dispose(disposing);}
    }

    private sealed class BlockingTextWriter(bool blockFlush) : ChannelTextWriter
    {
        private readonly TaskCompletionSource entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource finished=new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int blocked;
        internal Task EnteredTask=>entered.Task;
        internal Task FinishedTask=>finished.Task;
        internal void Release()=>release.TrySetResult();
        private async Task BlockOnceAsync()
        {
            if(Interlocked.Exchange(ref blocked,1)!=0)return;
            entered.TrySetResult();
            await release.Task;
            finished.TrySetResult();
        }
        public override async Task WriteLineAsync(ReadOnlyMemory<char> buffer,CancellationToken cancellationToken=default)
        {
            await base.WriteLineAsync(buffer,cancellationToken);
            if(!blockFlush)await BlockOnceAsync();
        }
        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await base.FlushAsync(cancellationToken);
            if(blockFlush)await BlockOnceAsync();
        }
    }
}
