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
        SessionThread? childThread = null;
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
                    TaskName = $"workflow_{active.State.RunId}_{operationId}",
                    AgentNickname = label,
                    AgentRole = options["agentType"]?.GetValue<string>(),
                    RoleConfigs = roleConfigs,
                    InvocationModelOverride = invocationModelOverride,
                    RuntimeConfig = _runtimeConfig,
                    ForkTurns = "none",
                    MaxDepth = 1,
                    MaxConcurrentSubAgents = active.State.Limits.MaxConcurrency,
                    Purpose = "dynamicWorkflow",
                    SilentCompletion = true,
                    ChildStarted = async (child, ct) =>
                    {
                        await JournalAsync(active, "agent.started", new JsonObject
                        {
                            ["operationId"] = operationId,
                            ["childThreadId"] = child.Id,
                            ["phase"] = phase,
                            ["label"] = label
                        }, ct).ConfigureAwait(false);
                    },
                    StartupFailed = async (child, _) =>
                    {
                        await CleanupWorktreeSafelyAsync(active, child).ConfigureAwait(false);
                        childThread = null;
                    },
                    ChildCreated = async (child, ct) =>
                    {
                        childThreadId = child.Id;
                        childThread = child;
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
            if (childThread != null) await CleanupWorktreeSafelyAsync(active, childThread).ConfigureAwait(false);
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
        "xhigh" => "ExtraHigh",
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
        var task = $"Workflow task metadata:\n{header.ToJsonString()}\n\nTask:\n{prompt}";
        return schema == null
            ? task
            : task + "\n\nWhen done, call SubmitWorkflowResult exactly once with a result that matches `schema`. That call is your final answer; do not reply in prose instead.";
    }

    private async Task CleanupWorktreeSafelyAsync(ActiveRun active, SessionThread child)
    {
        if (child.Worktree == null) return;
        using var cleanup = new CancellationTokenSource(CompletionWait);
        var task = CleanupWorktreeAsync(active, child, cleanup.Token);
        try { await task.WaitAsync(cleanup.Token).ConfigureAwait(false); }
        catch (Exception ex)
        {
            active.CleanupTasks.Add(task);
            logger?.LogWarning(ex, "Workflow worktree retained after cleanup failed for child {ChildThreadId}.", child.Id);
        }
    }

    private async Task CleanupWorktreeAsync(ActiveRun active, SessionThread child, CancellationToken cancellationToken)
    {
        if (child.Turns.Any(turn => turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput))
            return;
        var session = _sessionService!;
        var status = await session.GetWorktreeStatusAsync(child.Id, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var retained = !status.Exists || !status.IsGitWorktree || status.HasUncommittedChanges || status.HasCommitsAheadOfBase;
        await JournalAsync(active, "agent.worktree", JsonSerializer.SerializeToNode(new
        {
            childThreadId = child.Id,
            status.Path,
            status.BranchName,
            retained,
            status.HasUncommittedChanges,
            status.HasCommitsAheadOfBase
        }), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
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

}
