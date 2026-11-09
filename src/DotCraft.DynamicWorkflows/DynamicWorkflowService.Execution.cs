using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Scripting;
using DotCraft.Sessions;
using Microsoft.Extensions.Logging;

namespace DotCraft.DynamicWorkflows;

public sealed partial class DynamicWorkflowService
{
    private async Task ExecuteRunAsync(ActiveRun active, string script)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(active.Cancellation.Token);
        timeout.CancelAfter(active.State.Limits.RunTimeout);
        var token = timeout.Token;
        var agentTasks = new List<Task>();
        Task? stderrTask = null;
        Task? rssTask = null;
        ScriptProtocolConnection? connection = null;
        try
        {
            await UpdateStateAsync(active, state => state with { StartedAt = DateTimeOffset.UtcNow }, token).ConfigureAwait(false);
            active.Worker = ScriptWorkerProcess.Start(processFactory, "workflow", workspacePath);
            var process = active.Worker.Process;
            connection = new ScriptProtocolConnection(
                process.StandardOutput.BaseStream,
                process.StandardInput.BaseStream,
                active.State.Limits.MaxFrameBytes);
            stderrTask = ScriptWorkerProcess.DrainStderrAsync(
                process,
                active.State.Limits.MaxStderrBytes,
                (line, ct) => JournalAsync(active, "worker.stderr", new JsonObject { ["text"] = line }, ct),
                token);
            rssTask = ScriptWorkerProcess.MonitorRssAsync(process, active.State.Limits.MaxWorkerRssBytes, () =>
            {
                active.CancellationStatus = DynamicWorkflowStatuses.Failed;
                active.CancellationError = "Workflow worker exceeded its RSS limit.";
                active.Cancellation.Cancel();
            }, token);
            await connection.WriteAsync(WorkerScope(active.State), "initialize", new JsonObject
            {
                ["script"] = script,
                ["scriptHash"] = active.State.ScriptHash,
                ["args"] = active.State.Args?.DeepClone(),
                ["cwd"] = workspacePath,
                ["limits"] = JsonSerializer.SerializeToNode(active.State.Limits, ScriptProtocolConnection.JsonOptions),
                ["budget"] = BuildBudget(active)
            }, token).ConfigureAwait(false);
            var terminalSeen = false;
            while (!token.IsCancellationRequested)
            {
                var frame = await connection.ReadAsync(token).ConfigureAwait(false)
                    ?? throw new ScriptProtocolException("worker_eof", "Workflow worker exited without a terminal frame.");
                ValidateIdentity(active.State, frame);
                switch (frame.Type)
                {
                    case "ready":
                        await JournalAsync(active, "worker.ready", null, token).ConfigureAwait(false);
                        break;
                    case "phase":
                        active.CurrentPhase = frame.Payload?["name"]?.GetValue<string>();
                        await JournalBoundLogAsync(active, "workflow.phase", frame.Payload, token).ConfigureAwait(false);
                        break;
                    case "log":
                        await JournalBoundLogAsync(active, "workflow.log", frame.Payload, token).ConfigureAwait(false);
                        break;
                    case "agent.request":
                        var request = frame.Payload as JsonObject
                            ?? throw new ScriptProtocolException("agent_request_invalid", "Agent request payload must be an object.");
                        var task = HandleAgentRequestAsync(active, connection, request, token);
                        agentTasks.Add(task);
                        break;
                    case "complete":
                        terminalSeen = true;
                        active.WorkerCompleted = true;
                        await Task.WhenAll(agentTasks).WaitAsync(token).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        var result = CanonicalJson.Normalize(frame.Payload?["result"]);
                        await CompleteAsync(active, DynamicWorkflowStatuses.Succeeded, result, null, notify: true).ConfigureAwait(false);
                        break;
                    case "failed":
                        terminalSeen = true;
                        var message = frame.Payload?["message"]?.GetValue<string>() ?? "Workflow worker failed.";
                        timeout.Cancel();
                        await TerminateAttemptAsync(active, agentTasks, stderrTask, rssTask).ConfigureAwait(false);
                        await CompleteAsync(active, DynamicWorkflowStatuses.Failed, null, message, notify: true).ConfigureAwait(false);
                        break;
                    default:
                        throw new ScriptProtocolException("protocol_message_invalid", $"Unexpected worker message '{frame.Type}'.");
                }
                if (terminalSeen) break;
            }

            if (!terminalSeen) token.ThrowIfCancellationRequested();
            try { if (!token.IsCancellationRequested) await process.WaitForExitAsync(CancellationToken.None).WaitAsync(CompletionWait).ConfigureAwait(false); }
            catch (TimeoutException) { }
            timeout.Cancel();
        }
        catch (OperationCanceledException)
        {
            timeout.Cancel();
            await TerminateAttemptAsync(active, agentTasks, stderrTask, rssTask).ConfigureAwait(false);
            var status = active.Cancellation.IsCancellationRequested
                ? active.CancellationStatus
                : DynamicWorkflowStatuses.Failed;
            var error = active.CancellationError ?? (status switch
            {
                DynamicWorkflowStatuses.Interrupted => "The AppServer stopped before this workflow completed.",
                DynamicWorkflowStatuses.Failed => "Workflow exceeded its run deadline.",
                _ => "Workflow was cancelled."
            });
            await CompleteAsync(active, status, null, error, notify: status is DynamicWorkflowStatuses.Failed or DynamicWorkflowStatuses.Stopped).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            timeout.Cancel();
            await TerminateAttemptAsync(active, agentTasks, stderrTask, rssTask).ConfigureAwait(false);
            logger?.LogError(ex, "Dynamic workflow {RunId} failed.", active.State.RunId);
            await CompleteAsync(active, DynamicWorkflowStatuses.Failed, null, ex.Message, notify: true).ConfigureAwait(false);
        }
        finally
        {
            timeout.Cancel();
            try
            {
                if (active.Worker != null) await active.Worker.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { logger?.LogWarning(ex, "Workflow worker cleanup failed for {RunId}.", active.State.RunId); }
            var notification = StartNotificationDelivery(active);
            _active.TryRemove(new KeyValuePair<string, ActiveRun>(active.State.RunId, active));
            TrackRetirement(active, agentTasks.Concat([stderrTask, rssTask, notification]).OfType<Task>().ToArray(), connection);
        }
    }

    private async Task TerminateAttemptAsync(
        ActiveRun active,
        IReadOnlyCollection<Task> agentTasks,
        Task? stderrTask,
        Task? rssTask)
    {
        if (active.Worker != null) await active.Worker.DisposeAsync().ConfigureAwait(false);
        var pendingTasks = agentTasks
            .Concat([stderrTask, rssTask])
            .OfType<Task>()
            .ToArray();
        if (pendingTasks.Length == 0) return;
        try
        {
            await Task.WhenAll(pendingTasks)
                .WaitAsync(CompletionWait)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            logger?.LogWarning("Workflow {RunId} settled with child cleanup still pending.", active.State.RunId);
        }
        catch { }
    }

    private async Task HandleAgentRequestAsync(
        ActiveRun active,
        ScriptProtocolConnection connection,
        JsonObject request,
        CancellationToken cancellationToken)
    {
        string? operationId = null;
        try
        {
            operationId = request["operationId"]?.GetValue<string>()
                ?? throw new ScriptProtocolException("agent_request_invalid", "Agent operation id is required.");
            var result = await ExecuteAgentAsync(active, operationId, request, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await SendAgentResultAsync(active, connection, operationId, result, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException || cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is ScriptProtocolException or WorkflowRunFatalException)
        {
            active.CancellationStatus = DynamicWorkflowStatuses.Failed;
            active.CancellationError = ex.Message;
            active.Cancellation.Cancel();
            throw;
        }
        catch (Exception ex)
        {
            await JournalAsync(active, "agent.failed", new JsonObject { ["operationId"] = operationId, ["error"] = ex.Message }, cancellationToken).ConfigureAwait(false);
            await SendAgentResultAsync(active, connection, operationId!, null, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendAgentResultAsync(
        ActiveRun active, ScriptProtocolConnection connection, string operationId, JsonNode? result, CancellationToken cancellationToken)
    {
        if (active.WorkerCompleted) return;
        try
        {
            await connection.WriteAsync(WorkerScope(active.State), "agent.result",
                new JsonObject { ["operationId"] = operationId, ["result"] = result }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The read loop owns worker EOF and terminal-frame classification.
            logger?.LogDebug(ex, "Worker no longer accepts the result of {OperationId}.", operationId);
        }
    }

    private async Task JournalBoundLogAsync(
        ActiveRun active,
        string type,
        JsonNode? payload,
        CancellationToken cancellationToken)
    {
        var bounded = BoundPayload(payload, active.State.Limits.MaxLogEntryBytes);
        var bytes = Encoding.UTF8.GetByteCount(bounded?.ToJsonString() ?? "null");
        if (Interlocked.Add(ref active.LogBytes, bytes) > active.State.Limits.MaxLogBytes)
            throw new InvalidOperationException("Workflow log output exceeded its cumulative limit.");
        await JournalAsync(active, type, bounded, cancellationToken).ConfigureAwait(false);
    }

    private static JsonObject BuildBudget(ActiveRun active) => new()
    {
        ["maxAgentCalls"] = active.State.Limits.MaxAgentCalls,
        ["maxConcurrency"] = active.State.Limits.MaxConcurrency,
        ["tokenBudget"] = active.State.TokenBudget,
        ["inputTokens"] = active.State.InputTokens,
        ["outputTokens"] = active.State.OutputTokens
    };

    private static string WorkerScope(DynamicWorkflowRun run) => $"{run.RunId}/{run.AttemptId}";

    private static void ValidateIdentity(DynamicWorkflowRun run, ScriptProtocolFrame frame)
    {
        if (!string.Equals(WorkerScope(run), frame.Scope, StringComparison.Ordinal))
            throw new ScriptProtocolException("protocol_identity_mismatch", "Worker frame belongs to another run or attempt.");
    }

    private static JsonNode? BoundPayload(JsonNode? payload, int maxBytes)
    {
        var json = payload?.ToJsonString() ?? "null";
        if (Encoding.UTF8.GetByteCount(json) <= maxBytes) return payload?.DeepClone();
        return new JsonObject { ["truncated"] = true, ["preview"] = json[..Math.Min(json.Length, 1024)] };
    }

    private sealed class WorkflowRunFatalException(string message) : Exception(message);
}
