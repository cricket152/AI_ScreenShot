using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace ScreenShot.App;

internal sealed class CodexRpcException(int errorCode) : Exception($"Codex provider 请求失败（错误代码 {errorCode}）")
{
    internal int ErrorCode { get; } = errorCode;
}

internal sealed class CodexTransportException() : IOException("Codex provider 连接已中断");

// Every client owns its transport. A cancelled request is removed, never replayed.
internal sealed class CodexRpcClient : IAsyncDisposable
{
    private readonly TextReader input;
    private readonly TextWriter output;
    private readonly bool leaveOpen;
    private readonly Process? process;
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken lifetimeToken;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = new();
    private readonly Channel<JsonElement> notifications = Channel.CreateUnbounded<JsonElement>(
        new UnboundedChannelOptions { SingleWriter = true, AllowSynchronousContinuations = false });
    private readonly Task readPump;
    private readonly Task stderrPump;
    private Exception? failure;
    private long nextId;
    private int disposed;

    internal ChannelReader<JsonElement> Notifications => notifications.Reader;

    internal CodexRpcClient(TextReader input, TextWriter output, bool leaveOpen = false)
        : this(input, output, leaveOpen, null) { }

    private CodexRpcClient(TextReader input, TextWriter output, bool leaveOpen, Process? process)
    {
        this.input = input;
        this.output = output;
        this.leaveOpen = leaveOpen;
        this.process = process;
        lifetimeToken = lifetime.Token;
        readPump = ReadAsync();
        stderrPump = process is null ? Task.CompletedTask : DrainStderrAsync(process.StandardError);
    }

    internal static CodexRpcClient Start(string executable, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--stdio");
        var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start()) throw new CodexTransportException();
            return new CodexRpcClient(process.StandardOutput, process.StandardInput, false, process);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { }
            process.Dispose();
            throw new CodexTransportException();
        }
    }

    internal async Task<JsonElement> RequestAsync(string method, object parameters, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfUnavailable();
        var id = Interlocked.Increment(ref nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pending.TryAdd(id, completion)) throw new CodexTransportException();
        using var registration = cancellationToken.Register(() =>
        {
            if (pending.TryRemove(id, out var request)) request.TrySetCanceled(cancellationToken);
        });
        try
        {
            ThrowIfUnavailable();
            await WriteWithDeadlineAsync(new { id, method, @params = parameters }, cancellationToken).ConfigureAwait(false);
            return await completion.Task.ConfigureAwait(false);
        }
        finally { pending.TryRemove(id, out _); }
    }

    internal Task NotifyAsync(string method, object parameters, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        return WriteWithDeadlineAsync(new { method, @params = parameters }, cancellationToken);
    }

    private async Task WriteWithDeadlineAsync(object frame, CancellationToken cancellationToken)
    {
        var writing = WriteAsync(frame, cancellationToken);
        try { await writing.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Return control to the owner so it can close a stalled transport. The frame
            // continues under the transport lifetime and must never be sent again.
            _ = ObserveWriteCompletionAsync(writing);
            throw;
        }
    }

    private static async Task ObserveWriteCompletionAsync(Task writing)
    {
        try { await writing.ConfigureAwait(false); }
        catch { }
    }

    private async Task WriteAsync(object frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfUnavailable();
        string text;
        try { text = JsonSerializer.Serialize(frame); }
        catch { throw new CodexTransportException(); }
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeToken);
        await writeLock.WaitAsync(waitCancellation.Token).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfUnavailable();
            // Once a frame starts, finish it even if this request is cancelled.
            await output.WriteLineAsync(text.AsMemory(), lifetimeToken).ConfigureAwait(false);
            await output.FlushAsync(lifetimeToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            Fail(new CodexTransportException());
            throw new CodexTransportException();
        }
        finally { writeLock.Release(); }
    }

    private async Task ReadAsync()
    {
        try
        {
            while (await input.ReadLineAsync(lifetimeToken).AsTask().WaitAsync(lifetimeToken).ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var document = JsonDocument.Parse(line);
                var frame = document.RootElement;
                if (frame.ValueKind != JsonValueKind.Object) throw new CodexTransportException();
                if (frame.TryGetProperty("method", out var method))
                {
                    if (method.ValueKind != JsonValueKind.String) throw new CodexTransportException();
                    if (frame.TryGetProperty("id", out var requestId))
                    {
                        if (requestId.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
                            throw new CodexTransportException();
                        await DenyServerRequestAsync(requestId.Clone(), method.GetString()!).ConfigureAwait(false);
                    }
                    else if (!notifications.Writer.TryWrite(frame.Clone())) break;
                    continue;
                }
                if (!frame.TryGetProperty("id", out var responseId)) throw new CodexTransportException();
                long id;
                if (responseId.ValueKind == JsonValueKind.Number)
                {
                    if (!responseId.TryGetInt64(out id)) throw new CodexTransportException();
                }
                else if (responseId.ValueKind == JsonValueKind.String)
                {
                    if (!long.TryParse(responseId.GetString(), out id)) throw new CodexTransportException();
                }
                else throw new CodexTransportException();
                var hasResult = frame.TryGetProperty("result", out var result);
                var hasError = frame.TryGetProperty("error", out var error);
                if (hasResult == hasError) throw new CodexTransportException();
                if (hasError && (error.ValueKind != JsonValueKind.Object ||
                    !error.TryGetProperty("code", out var code) || !code.TryGetInt32(out _)))
                    throw new CodexTransportException();
                // Late responses to cancelled requests are consumed without replaying them.
                if (!pending.TryRemove(id, out var completion)) continue;
                if (hasError) completion.TrySetException(new CodexRpcException(error.GetProperty("code").GetInt32()));
                else completion.TrySetResult(result.Clone());
            }
        }
        catch { }
        finally { Fail(new CodexTransportException()); }
    }

    private Task DenyServerRequestAsync(JsonElement id, string method)
    {
        object? result = method switch
        {
            "item/commandExecution/requestApproval" or "item/fileChange/requestApproval" => new { decision = "cancel" },
            "execCommandApproval" or "applyPatchApproval" => new { decision = "abort" },
            "item/permissions/requestApproval" => new { permissions = new { }, scope = "turn" },
            "item/tool/call" => new { success = false, contentItems = Array.Empty<object>() },
            "item/tool/requestUserInput" => new { answers = new { } },
            "mcpServer/elicitation/request" => new { action = "cancel" },
            _ => null
        };
        return result is null
            ? WriteAsync(new { id, error = new { code = -32601, message = "Unsupported server request" } }, lifetimeToken)
            : WriteAsync(new { id, result }, lifetimeToken);
    }

    private async Task DrainStderrAsync(TextReader stderr)
    {
        var buffer = new char[2048];
        try
        {
            while (await stderr.ReadAsync(buffer.AsMemory(), lifetimeToken).ConfigureAwait(false) > 0)
                Array.Clear(buffer);
        }
        catch { }
        finally { Array.Clear(buffer); }
    }

    private void ThrowIfUnavailable()
    {
        if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(CodexRpcClient));
        if (Volatile.Read(ref failure) is { } error) throw error;
    }

    private void Fail(Exception error)
    {
        if (Interlocked.CompareExchange(ref failure, error, null) is not null) return;
        foreach (var pair in pending)
            if (pending.TryRemove(pair.Key, out var completion)) completion.TrySetException(error);
        notifications.Writer.TryComplete(error);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Fail(new CodexTransportException());
        lifetime.Cancel();
        if (process is not null)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { }
        }
        if (!leaveOpen)
        {
            try { input.Dispose(); } catch { }
            try { output.Dispose(); } catch { }
        }
        try { await Task.WhenAll(readPump, stderrPump).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch { }
        process?.Dispose();
        var writeStopped = false;
        try { writeStopped = await writeLock.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch { }
        if (readPump.IsCompleted && stderrPump.IsCompleted && writeStopped)
        {
            lifetime.Dispose();
            writeLock.Dispose();
        }
        else if (writeStopped) writeLock.Release();
    }
}
