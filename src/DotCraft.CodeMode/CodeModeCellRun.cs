using System.Text.Json.Nodes;
using DotCraft.Tools;
using DotCraft.Utilities;

namespace DotCraft.CodeMode;

internal sealed record CodeModeOutputItem(string? Text, string? MimeType = null, string? Data = null);

internal sealed class CodeModeNestedCall(string jsName, string callId)
{
    public string JsName { get; } = jsName;
    public string CallId { get; } = callId;
    public volatile string Status = "running";
}

internal sealed record CodeModeCellOutcome(
    string Status,
    string? Error,
    string? Stack,
    JsonObject? StoreWrites);

internal sealed class CodeModeCellRun(
    CodeModeExecDependencies dependencies,
    ToolInvocationContext context,
    CodeModeCellSession session,
    PausableDeadline deadline,
    CancellationToken turnToken) : IDisposable
{
    private readonly CodeModeLimits _limits = dependencies.Host.Limits;
    private readonly CancellationTokenSource _nested = CancellationTokenSource.CreateLinkedTokenSource(turnToken);
    private readonly SemaphoreSlim _concurrency = new(dependencies.Host.Limits.MaxConcurrentNestedCalls);
    private readonly List<Task> _dispatches = [];
    private readonly List<CodeModeNestedCall> _calls = [];
    private int _callCount;
    private bool _drained;

    public List<CodeModeOutputItem> Output { get; } = [];

    public IReadOnlyList<CodeModeNestedCall> Calls
    {
        get
        {
            lock (_calls)
                return _calls.ToArray();
        }
    }

    public async Task<CodeModeCellOutcome> RunAsync()
    {
        var waitToken = deadline.Token;
        CancellationTokenSource? grace = null;
        string? stopError = null;
        var timedOut = false;
        try
        {
            while (true)
            {
                CodeModeCellEvent next;
                try
                {
                    next = await session.Events.ReadAsync(waitToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (grace is null)
                {
                    timedOut = !turnToken.IsCancellationRequested;
                    grace = await StopAsync(timedOut ? CodeModeProtocol.TimeoutReason : "cancelled").ConfigureAwait(false);
                    waitToken = grace.Token;
                    continue;
                }
                catch (OperationCanceledException)
                {
                    await session.KillWorkerAsync("a stopped script did not end").ConfigureAwait(false);
                    return Finish(timedOut, new CodeModeCellOutcome(CodeModeProtocol.Failed, stopError ?? "The script did not stop after it was cancelled.", null, null));
                }

                switch (next)
                {
                    case CodeModeCellEvent.WorkerFailed failed:
                        return Finish(timedOut, new CodeModeCellOutcome(CodeModeProtocol.Failed, $"The script stopped because {failed.Cause}.", null, null));
                    case CodeModeCellEvent.StopRequested stop when grace is null:
                        stopError = $"The script was stopped because {stop.Reason}.";
                        grace = await StopAsync(stop.Reason).ConfigureAwait(false);
                        waitToken = grace.Token;
                        break;
                    case CodeModeCellEvent.Frame { Value: var frame }:
                        switch (frame.Type)
                        {
                            case CodeModeProtocol.CallRequest when grace is null:
                                if (!await StartCallAsync(frame.Payload as JsonObject).ConfigureAwait(false))
                                {
                                    stopError = $"The script exceeded its limit of {_limits.MaxNestedCalls} tool calls.";
                                    grace = await StopAsync("limit").ConfigureAwait(false);
                                    waitToken = grace.Token;
                                }
                                break;
                            case CodeModeProtocol.CellOutput:
                                if (!TryAddOutput(frame.Payload as JsonObject))
                                    await session.ReportProtocolViolationAsync("invalid cell.output").ConfigureAwait(false);
                                break;
                            case CodeModeProtocol.CellDone:
                                var outcome = ParseOutcome(frame.Payload as JsonObject);
                                if (stopError is not null && !timedOut)
                                    outcome = new CodeModeCellOutcome(CodeModeProtocol.Failed, stopError, null, null);
                                return Finish(timedOut, outcome);
                        }
                        break;
                }
            }
        }
        finally
        {
            grace?.Dispose();
            await _nested.CancelAsync().ConfigureAwait(false);
            Task[] pending;
            lock (_dispatches)
                pending = _dispatches.ToArray();
            try
            {
                await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
            catch
            {
            }
            _drained = pending.All(static task => task.IsCompleted);
            foreach (var call in Calls.Where(static call => call.Status == "running"))
                call.Status = "cancelled";
        }
    }

    public void Dispose()
    {
        if (!_drained)
            return;
        _nested.Dispose();
        _concurrency.Dispose();
    }

    private CodeModeCellOutcome Finish(bool timedOut, CodeModeCellOutcome outcome)
    {
        turnToken.ThrowIfCancellationRequested();
        return timedOut
            ? outcome with { Status = CodeModeProtocol.TimedOut, StoreWrites = null }
            : outcome;
    }

    private async Task<CancellationTokenSource> StopAsync(string reason)
    {
        await session.SendAsync(CodeModeProtocol.CellCancel, new JsonObject { ["reason"] = reason }).ConfigureAwait(false);
        await _nested.CancelAsync().ConfigureAwait(false);
        return new CancellationTokenSource(_limits.CancelGrace);
    }

    private async Task<bool> StartCallAsync(JsonObject? payload)
    {
        var requestId = payload?["requestId"]?.GetValue<string>();
        var name = payload?["tool"]?.GetValue<string>();
        if (requestId is null || name is null || payload!["arguments"] is not JsonObject arguments)
        {
            await session.ReportProtocolViolationAsync("invalid call.request").ConfigureAwait(false);
            return true;
        }
        if (++_callCount > _limits.MaxNestedCalls)
            return false;
        if (!dependencies.Surface.TryResolve(name, out var tool))
        {
            await session.SendAsync(CodeModeProtocol.CallResult, new JsonObject
            {
                ["requestId"] = requestId,
                ["error"] = $"{ToolErrorCodes.NotFound}: tools.{name} is not a callable tool."
            }).ConfigureAwait(false);
            return true;
        }

        var call = new CodeModeNestedCall(tool.JsName, "exec-" + Guid.NewGuid().ToString("N"));
        lock (_calls)
            _calls.Add(call);
        var dispatch = Task.Run(() => DispatchAsync(call, tool, (JsonObject)arguments.DeepClone(), requestId));
        lock (_dispatches)
            _dispatches.Add(dispatch);
        return true;
    }

    private async Task DispatchAsync(
        CodeModeNestedCall call,
        CodeModeNestedTool tool,
        JsonObject arguments,
        string requestId)
    {
        try
        {
            await _concurrency.WaitAsync(_nested.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            call.Status = "cancelled";
            return;
        }

        try
        {
            ToolExecutionResult result;
            using (CodeModeApprovalScope.Enter(deadline))
            {
                result = await dependencies.Dispatcher.DispatchAsync(
                    dependencies.Snapshot,
                    tool.Registration.Definition.Name,
                    arguments,
                    new ToolInvocationRequest(
                        context.ThreadId,
                        context.TurnId,
                        call.CallId,
                        ToolInvocationAudience.Model,
                        new ToolInvocationOrigin(ToolInvocationOrigin.CodeModeKind, context.CallId),
                        context.WorkspacePath),
                    _nested.Token).ConfigureAwait(false);
            }

            call.Status = result.Success
                ? "completed"
                : result.Error?.Code == ToolErrorCodes.Cancelled ? "cancelled" : "failed";
            if (_nested.IsCancellationRequested)
                return;
            var (value, error) = CodeModeResults.ToScriptValue(tool.Registration, result, _limits.MaxNestedResultBytes);
            await session.SendAsync(CodeModeProtocol.CallResult, error is null
                ? new JsonObject { ["requestId"] = requestId, ["value"] = value }
                : new JsonObject { ["requestId"] = requestId, ["error"] = error }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            call.Status = "failed";
            await session.SendAsync(CodeModeProtocol.CallResult, new JsonObject
            {
                ["requestId"] = requestId,
                ["error"] = $"{ToolErrorCodes.ExecutionFailed}: {ex.Message}"
            }).ConfigureAwait(false);
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private bool TryAddOutput(JsonObject? payload)
    {
        switch (payload?["type"]?.GetValue<string>())
        {
            case "text" when payload["text"]?.GetValue<string>() is { } text:
                Output.Add(new CodeModeOutputItem(text));
                return true;
            case "image" when payload["mimeType"]?.GetValue<string>() is { } mimeType
                              && payload["data"]?.GetValue<string>() is { } data:
                Output.Add(new CodeModeOutputItem(null, mimeType, data));
                return true;
            default:
                return false;
        }
    }

    private static CodeModeCellOutcome ParseOutcome(JsonObject? payload)
    {
        var status = payload?["outcome"]?.GetValue<string>() is CodeModeProtocol.Completed or CodeModeProtocol.TimedOut
            ? payload!["outcome"]!.GetValue<string>()
            : CodeModeProtocol.Failed;
        return new CodeModeCellOutcome(
            status,
            payload?["error"]?.GetValue<string>(),
            payload?["stack"]?.GetValue<string>(),
            status == CodeModeProtocol.Completed ? payload?["store"] as JsonObject : null);
    }
}
