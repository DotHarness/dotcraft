using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
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
        try
        {
            await UpdateStateAsync(active, state => state with { StartedAt = DateTimeOffset.UtcNow }, token).ConfigureAwait(false);
            var startInfo = CreateWorkerStartInfo(workspacePath);
            active.Worker = processFactory.Start(startInfo);
            var process = active.Worker.Process;
            await using var connection = new WorkflowProtocolConnection(
                process.StandardOutput.BaseStream,
                process.StandardInput.BaseStream,
                active.State.Limits.MaxFrameBytes);
            var stderrTask = DrainStderrAsync(active, process, token);
            var rssTask = MonitorRssAsync(active, process, token);
            await connection.WriteAsync(active.State.RunId, active.State.AttemptId, "initialize", new JsonObject
            {
                ["script"] = script,
                ["scriptHash"] = active.State.ScriptHash,
                ["args"] = active.State.Args?.DeepClone(),
                ["cwd"] = workspacePath,
                ["limits"] = JsonSerializer.SerializeToNode(active.State.Limits, WorkflowProtocolConnection.JsonOptions),
                ["budget"] = BuildBudget(active)
            }, token).ConfigureAwait(false);
            using var workerCancellationRegistration = active.Cancellation.Token.Register(() =>
            {
                _ = connection.WriteAsync(
                    active.State.RunId,
                    active.State.AttemptId,
                    "cancel",
                    null,
                    CancellationToken.None);
            });

            var terminalSeen = false;
            while (!token.IsCancellationRequested)
            {
                var frame = await connection.ReadAsync(token).ConfigureAwait(false)
                    ?? throw new WorkflowProtocolException("worker_eof", "Workflow worker exited without a terminal frame.");
                ValidateIdentity(active.State, frame);
                if (terminalSeen) throw new WorkflowProtocolException("duplicate_terminal", "Worker sent a message after its terminal frame.");
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
                            ?? throw new WorkflowProtocolException("agent_request_invalid", "Agent request payload must be an object.");
                        var task = HandleAgentRequestAsync(active, connection, request, token);
                        agentTasks.Add(task);
                        break;
                    case "complete":
                        terminalSeen = true;
                        await Task.WhenAll(agentTasks).ConfigureAwait(false);
                        var result = CanonicalJson.Normalize(frame.Payload?["result"]);
                        await CompleteAsync(active, DynamicWorkflowStatuses.Succeeded, result, null, notify: true).ConfigureAwait(false);
                        break;
                    case "failed":
                        terminalSeen = true;
                        await Task.WhenAll(agentTasks).ConfigureAwait(false);
                        var message = frame.Payload?["message"]?.GetValue<string>() ?? "Workflow worker failed.";
                        await CompleteAsync(active, DynamicWorkflowStatuses.Failed, null, message, notify: true).ConfigureAwait(false);
                        break;
                    default:
                        throw new WorkflowProtocolException("protocol_message_invalid", $"Unexpected worker message '{frame.Type}'.");
                }
                if (terminalSeen) break;
            }

            if (!terminalSeen) token.ThrowIfCancellationRequested();
            try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (TimeoutException) { }
            timeout.Cancel();
            try { await Task.WhenAll(stderrTask, rssTask).ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        catch (OperationCanceledException)
        {
            timeout.Cancel();
            await DrainCancelledAttemptAsync(active, agentTasks).ConfigureAwait(false);
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
            await DrainCancelledAttemptAsync(active, agentTasks).ConfigureAwait(false);
            logger?.LogError(ex, "Dynamic workflow {RunId} failed.", active.State.RunId);
            await CompleteAsync(active, DynamicWorkflowStatuses.Failed, null, ex.Message, notify: true).ConfigureAwait(false);
        }
        finally
        {
            if (active.Worker != null) await active.Worker.DisposeAsync().ConfigureAwait(false);
            _active.TryRemove(active.State.RunId, out _);
            active.Capacity.Dispose();
            active.Gate.Dispose();
            active.JournalGate.Dispose();
            active.Cancellation.Dispose();
        }
    }

    private static async Task DrainCancelledAttemptAsync(ActiveRun active, IReadOnlyCollection<Task> agentTasks)
    {
        if (active.Worker is { Process: { HasExited: false } } worker)
        {
            try
            {
                await worker.Process.WaitForExitAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
            }
            catch (TimeoutException) { }
        }
        if (agentTasks.Count == 0) return;
        try
        {
            await Task.WhenAll(agentTasks)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException) { }
        catch { }
    }

    private async Task HandleAgentRequestAsync(
        ActiveRun active,
        WorkflowProtocolConnection connection,
        JsonObject request,
        CancellationToken cancellationToken)
    {
        string? operationId = null;
        try
        {
            operationId = request["operationId"]?.GetValue<string>()
                ?? throw new WorkflowProtocolException("agent_request_invalid", "Agent operation id is required.");
            var result = await ExecuteAgentAsync(active, operationId, request, cancellationToken).ConfigureAwait(false);
            await connection.WriteAsync(active.State.RunId, active.State.AttemptId, "agent.result", new JsonObject
            {
                ["operationId"] = operationId,
                ["result"] = result
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is WorkflowProtocolException or WorkflowRunFatalException)
        {
            active.CancellationStatus = DynamicWorkflowStatuses.Failed;
            active.CancellationError = ex.Message;
            active.Cancellation.Cancel();
            throw;
        }
        catch (Exception ex)
        {
            await JournalAsync(active, "agent.failed", new JsonObject { ["operationId"] = operationId, ["error"] = ex.Message }, cancellationToken).ConfigureAwait(false);
            await connection.WriteAsync(active.State.RunId, active.State.AttemptId, "agent.result", new JsonObject
            {
                ["operationId"] = operationId,
                ["result"] = null
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<JsonNode?> ExecuteAgentAsync(
        ActiveRun active,
        string operationId,
        JsonObject request,
        CancellationToken cancellationToken)
    {
        var session = _sessionService ?? throw new InvalidOperationException("Session service has not been bound.");
        if (Interlocked.Increment(ref active.AgentCalls) > active.State.Limits.MaxAgentCalls)
            throw new WorkflowRunFatalException("Workflow Agent-call limit reached.");
        var options = request["options"] as JsonObject ?? new JsonObject();
        var input = request["input"];
        var fingerprint = ComputeAgentFingerprint(active.State.Args, input, options);
        var label = options["label"]?.GetValue<string>() ?? operationId;
        var phase = options["phase"]?.GetValue<string>() ?? active.CurrentPhase;
        DynamicWorkflowReplayCall? replayed = null;
        lock (active.ReplayGate)
        {
            if (!active.ReplayDiverged && active.ReplayCursor < active.ReplayCalls.Count)
            {
                var cached = active.ReplayCalls[active.ReplayCursor++];
                if (cached.Completed && string.Equals(cached.Fingerprint, fingerprint, StringComparison.Ordinal))
                    replayed = cached;
                else
                    active.ReplayDiverged = true;
            }
            else if (!active.ReplayDiverged)
            {
                active.ReplayDiverged = true;
            }
        }
        phase = replayed?.Phase ?? phase;
        label = replayed?.Label ?? label;
        await JournalAsync(active, "agent.requested", new JsonObject
        {
            ["operationId"] = operationId,
            ["label"] = label,
            ["phase"] = phase,
            ["model"] = options["model"]?.DeepClone(),
            ["effort"] = options["effort"]?.DeepClone(),
            ["isolation"] = options["isolation"]?.DeepClone(),
            ["fingerprint"] = fingerprint
        }, cancellationToken).ConfigureAwait(false);
        if (replayed != null)
        {
            await UpdateUsageAsync(active, replayed.InputTokens, replayed.OutputTokens, cancellationToken).ConfigureAwait(false);
            await JournalAsync(active, "agent.replayed", new JsonObject
            {
                ["operationId"] = operationId,
                ["fingerprint"] = fingerprint,
                ["sourceRunId"] = active.State.ResumedFromRunId,
                ["phase"] = phase,
                ["label"] = label,
                ["childThreadId"] = replayed.ChildThreadId,
                ["inputTokens"] = replayed.InputTokens,
                ["outputTokens"] = replayed.OutputTokens,
                ["result"] = replayed.Result?.DeepClone()
            }, cancellationToken).ConfigureAwait(false);
            return replayed.Result?.DeepClone();
        }
        if (active.State.TokenBudget is { } budget && active.State.InputTokens + active.State.OutputTokens >= budget)
            throw new InvalidOperationException("Workflow token budget is exhausted.");
        await active.Capacity.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? childThreadId = null;
        try
        {
            var prompt = input is JsonValue scalar && scalar.TryGetValue<string>(out var text)
                ? text
                : input?["prompt"]?.GetValue<string>() ?? input?.ToJsonString() ?? string.Empty;
            if (input is JsonObject inputObject && inputObject["context"] != null)
                prompt += $"\n\nContext:\n{inputObject["context"]!.ToJsonString()}";
            var schema = options["schema"]?.DeepClone();
            var isolation = options["isolation"]?.GetValue<string>();
            var parent = await session.GetThreadAsync(active.State.ParentThreadId, cancellationToken).ConfigureAwait(false);
            var invocationModelOverride = BuildInvocationModelOverride(options);
            var childPrompt = BuildChildPrompt(active.State, operationId, label, phase, prompt, schema);
            var result = await SubAgentSessionControl.SpawnAgentAsync(
                new SubAgentSessionContext
                {
                    SessionService = session,
                    ParentThread = parent,
                    ParentTurnId = active.State.ParentTurnId,
                    RootThreadId = parent.Id,
                    Depth = 0
                },
                new SubAgentSpawnOptions
                {
                    AgentPrompt = childPrompt,
                    TaskName = $"workflow_{operationId}",
                    AgentNickname = label,
                    AgentRole = options["agentType"]?.GetValue<string>(),
                    RoleConfigs = roleConfigs,
                    InvocationModelOverride = invocationModelOverride,
                    RuntimeConfig = _runtimeConfig,
                    ForkTurns = "none",
                    MaxDepth = 1,
                    MaxConcurrentSubAgents = active.State.Limits.MaxConcurrency,
                    Purpose = "dynamicWorkflow",
                    ChildCreated = async (child, ct) =>
                    {
                        childThreadId = child.Id;
                        await JournalAsync(active, "agent.started", new JsonObject
                        {
                            ["operationId"] = operationId,
                            ["childThreadId"] = child.Id,
                            ["phase"] = phase,
                            ["label"] = label
                        }, ct).ConfigureAwait(false);
                        if (schema != null) structuredResults.Bind(child.Id, schema, active.State.Limits.MaxResultBytes);
                        if (string.Equals(isolation, "worktree", StringComparison.OrdinalIgnoreCase))
                        {
                            await session.HandoffThreadWorktreeAsync(new WorktreeHandoffOptions
                            {
                                ThreadId = child.Id,
                                Mode = WorktreeHandoffModes.Worktree,
                                BranchName = $"dotcraft/workflow/{active.State.RunId}/{operationId}",
                                CopyDirtyChanges = true
                            }, ct).ConfigureAwait(false);
                        }
                    }
                },
                waitForCompletion: true,
                coordinator: null,
                cancellationToken).ConfigureAwait(false);

            var child = await session.GetThreadAsync(result.ChildThreadId, cancellationToken).ConfigureAwait(false);
            childThreadId = child.Id;
            var turn = child.Turns.LastOrDefault();
            var usage = turn?.TokenUsage;
            await UpdateUsageAsync(active, usage?.InputTokens ?? 0, usage?.OutputTokens ?? 0, cancellationToken).ConfigureAwait(false);
            JsonNode? value = null;
            if (schema != null)
            {
                if (structuredResults.TryGetResult(child.Id, out var submitted)) value = submitted;
            }
            else if (string.Equals(result.Status, "completed", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(result.Message))
            {
                value = JsonValue.Create(result.Message);
            }
            await CleanupWorktreeAsync(active, child, cancellationToken).ConfigureAwait(false);
            await JournalAsync(active, "agent.completed", new JsonObject
            {
                ["operationId"] = operationId,
                ["childThreadId"] = child.Id,
                ["status"] = result.Status,
                ["inputTokens"] = usage?.InputTokens ?? 0,
                ["outputTokens"] = usage?.OutputTokens ?? 0,
                ["effectiveModel"] = child.Configuration?.Model,
                ["effectiveEffort"] = child.Configuration?.Reasoning?.Effort.ToString(),
                ["fingerprint"] = fingerprint,
                ["result"] = value?.DeepClone()
            }, cancellationToken).ConfigureAwait(false);
            return value;
        }
        finally
        {
            if (childThreadId != null) structuredResults.Remove(childThreadId);
            active.Capacity.Release();
        }
    }

    private static string ComputeAgentFingerprint(JsonNode? rootArgs, JsonNode? input, JsonObject options)
    {
        var normalized = CanonicalJson.Normalize(new JsonObject
        {
            ["args"] = rootArgs?.DeepClone(),
            ["input"] = input?.DeepClone(),
            ["options"] = options.DeepClone()
        });
        var bytes = Encoding.UTF8.GetBytes(normalized?.ToJsonString() ?? "null");
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static SubAgentInvocationModelOverride? BuildInvocationModelOverride(JsonObject options)
    {
        var model = options["model"]?.GetValue<string>()?.Trim();
        var effortText = options["effort"]?.GetValue<string>();
        ModelReasoningEffort? effort = null;
        if (!string.IsNullOrWhiteSpace(effortText)
            && Enum.TryParse<ModelReasoningEffort>(NormalizeEffort(effortText), true, out var parsedEffort))
            effort = parsedEffort;
        if (string.IsNullOrWhiteSpace(model) && effort == null) return null;
        return new SubAgentInvocationModelOverride
        {
            Model = string.IsNullOrWhiteSpace(model) ? null : model,
            Effort = effort
        };
    }

    private static string NormalizeEffort(string value) => value.ToLowerInvariant() switch
    {
        "xhigh" or "max" => "ExtraHigh",
        _ => value
    };

    private static string BuildChildPrompt(
        DynamicWorkflowRun run,
        string operationId,
        string label,
        string? phase,
        string prompt,
        JsonNode? schema)
    {
        var header = new JsonObject
        {
            ["runId"] = run.RunId,
            ["operationId"] = operationId,
            ["label"] = label,
            ["phase"] = phase,
            ["schema"] = schema?.DeepClone()
        };
        return $"Workflow task metadata:\n{header.ToJsonString()}\n\nTask:\n{prompt}";
    }

    private async Task CleanupWorktreeAsync(ActiveRun active, SessionThread child, CancellationToken cancellationToken)
    {
        if (child.Worktree == null) return;
        var session = _sessionService!;
        var status = await session.GetWorktreeStatusAsync(child.Id, cancellationToken).ConfigureAwait(false);
        var retained = status.HasUncommittedChanges || status.HasCommitsAheadOfBase;
        await JournalAsync(active, "agent.worktree", JsonSerializer.SerializeToNode(new
        {
            childThreadId = child.Id,
            status.Path,
            status.BranchName,
            retained,
            status.HasUncommittedChanges,
            status.HasCommitsAheadOfBase
        }), cancellationToken).ConfigureAwait(false);
        if (!retained)
        {
            await session.RemoveManagedWorktreeAsync(new WorktreeRemoveOptions
            {
                ThreadId = child.Id,
                Path = status.Path,
                BranchName = status.BranchName,
                DeleteBranch = true
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CompleteAsync(ActiveRun active, string status, JsonNode? result, string? error, bool notify)
    {
        await UpdateStateAsync(active, state => state with
        {
            Status = status,
            Result = result?.DeepClone(),
            Error = error,
            CompletedAt = DateTimeOffset.UtcNow,
            NotificationStatus = notify ? "pending" : "notApplicable"
        }, CancellationToken.None).ConfigureAwait(false);
        await JournalAsync(active, $"run.{status}", error == null ? null : new JsonObject { ["error"] = error }, CancellationToken.None).ConfigureAwait(false);
        if (notify) await NotifyParentAsync(active).ConfigureAwait(false);
    }

    private async Task NotifyParentAsync(ActiveRun active)
    {
        if (active.State.NotificationStatus == "queued") return;
        var session = _sessionService!;
        var parent = await session.GetThreadAsync(active.State.ParentThreadId, CancellationToken.None).ConfigureAwait(false);
        var alreadyDelivered = parent.QueuedInputs.Any(input =>
                string.Equals(input.TriggerKind, "workflow", StringComparison.Ordinal)
                && string.Equals(input.TriggerRefId, active.State.RunId, StringComparison.Ordinal))
            || parent.Turns.SelectMany(static turn => turn.Items).Any(item =>
                item.AsUserMessage is { } message
                && string.Equals(message.TriggerKind, "workflow", StringComparison.Ordinal)
                && string.Equals(message.TriggerRefId, active.State.RunId, StringComparison.Ordinal));
        if (alreadyDelivered)
        {
            await UpdateStateAsync(active, state => state with { NotificationStatus = "queued" }, CancellationToken.None).ConfigureAwait(false);
            return;
        }
        var content = active.State.Status == DynamicWorkflowStatuses.Succeeded
            ? $"Dynamic workflow '{active.State.Name}' ({active.State.RunId}) completed. Result: {active.State.Result?.ToJsonString() ?? "null"}"
            : $"Dynamic workflow '{active.State.Name}' ({active.State.RunId}) ended with status '{active.State.Status}': {active.State.Error}";
        using var trigger = TurnTriggerScope.Set(new TurnTriggerInfo
        {
            Kind = "workflow",
            Label = active.State.Name,
            RefId = active.State.RunId
        });
        var queued = await session.EnqueueTurnInputAsync(active.State.ParentThreadId, [new TextContent(content)], ct: CancellationToken.None).ConfigureAwait(false);
        await UpdateStateAsync(active, state => state with
        {
            NotificationStatus = "queued",
            NotificationInputId = queued.Id
        }, CancellationToken.None).ConfigureAwait(false);
        await session.TryStartNextQueuedTurnAsync(active.State.ParentThreadId, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task UpdateUsageAsync(ActiveRun active, long inputTokens, long outputTokens, CancellationToken cancellationToken) =>
        await UpdateStateAsync(active, state => state with
        {
            InputTokens = state.InputTokens + inputTokens,
            OutputTokens = state.OutputTokens + outputTokens
        }, cancellationToken).ConfigureAwait(false);

    private async Task UpdateStateAsync(
        ActiveRun active,
        Func<DynamicWorkflowRun, DynamicWorkflowRun> update,
        CancellationToken cancellationToken)
    {
        await active.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            active.State = update(active.State);
            await store.WriteStateAsync(active.State, cancellationToken).ConfigureAwait(false);
            PublishChanged(active.State, "state");
        }
        finally { active.Gate.Release(); }
    }

    private async Task JournalAsync(ActiveRun active, string type, JsonNode? payload, CancellationToken cancellationToken)
    {
        await active.JournalGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await store.AppendJournalAsync(active.State.RunId, type, payload, cancellationToken).ConfigureAwait(false);
            if (type is "workflow.phase" or "agent.requested" or "agent.started" or "agent.completed" or "agent.failed" or "agent.replayed"
                || type.StartsWith("run.", StringComparison.Ordinal))
                PublishChanged(active.State, "progress");
        }
        finally { active.JournalGate.Release(); }
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

    private static void ValidateIdentity(DynamicWorkflowRun run, WorkflowProtocolFrame frame)
    {
        if (!string.Equals(run.RunId, frame.RunId, StringComparison.Ordinal)
            || !string.Equals(run.AttemptId, frame.AttemptId, StringComparison.Ordinal))
            throw new WorkflowProtocolException("protocol_identity_mismatch", "Worker frame belongs to another run or attempt.");
    }

    private static JsonNode? BoundPayload(JsonNode? payload, int maxBytes)
    {
        var json = payload?.ToJsonString() ?? "null";
        if (Encoding.UTF8.GetByteCount(json) <= maxBytes) return payload?.DeepClone();
        return new JsonObject { ["truncated"] = true, ["preview"] = json[..Math.Min(json.Length, 1024)] };
    }

    private async Task DrainStderrAsync(ActiveRun active, Process process, CancellationToken cancellationToken)
    {
        var total = 0;
        while (!cancellationToken.IsCancellationRequested && await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            var bytes = Encoding.UTF8.GetByteCount(line);
            if (total + bytes > active.State.Limits.MaxStderrBytes) continue;
            total += bytes;
            await JournalAsync(active, "worker.stderr", new JsonObject { ["text"] = line }, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task MonitorRssAsync(ActiveRun active, Process process, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (process.HasExited) return;
            process.Refresh();
            if (process.WorkingSet64 <= active.State.Limits.MaxWorkerRssBytes) continue;
            active.CancellationStatus = DynamicWorkflowStatuses.Failed;
            active.CancellationError = "Workflow worker exceeded its RSS limit.";
            active.Cancellation.Cancel();
            return;
        }
    }

    private sealed class WorkflowRunFatalException(string message) : Exception(message);
}
