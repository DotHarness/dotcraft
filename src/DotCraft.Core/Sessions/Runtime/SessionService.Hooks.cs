using DotCraft.Agents;
using DotCraft.Context.Compaction;
using DotCraft.Hooks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ContextUsageSnapshot = DotCraft.Sessions.Wire.ContextUsageSnapshot;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private static readonly TimeSpan TeardownHookTimeout = TimeSpan.FromSeconds(5);

    private void EnsureHookRewakeHandler()
    {
        if (hookRunner == null)
            return;

        hookRunner.RewakeHandler ??= EnqueueHookRewakeAsync;
        hookRunner.McpExecutor ??= new SessionHookMcpExecutor(
            ResolveActiveMcpManagerForHook,
            ResolveEnabledPluginIds);
    }

    private DotCraft.Mcp.McpClientManager? ResolveActiveMcpManagerForHook(string? threadId)
    {
        if (!string.IsNullOrWhiteSpace(threadId)
            && _runtimeRegistry.TryGetRuntime(threadId, out var runtime)
            && runtime.McpManager is not null)
        {
            return runtime.McpManager;
        }

        return agentFactory.RuntimeContext.McpClientManager;
    }

    private async Task RunInterruptHookAsync(
        SessionThread thread,
        string turnId,
        string? workspacePath,
        string? triggerKind)
    {
        if (hookRunner == null)
            return;

        EnsureHookRewakeHandler();
        try
        {
            using var timeoutCts = new CancellationTokenSource(TeardownHookTimeout);
            var isSubagent = IsSubAgentThread(thread);
            var parentThreadId = isSubagent
                ? thread.Source.SubAgent?.ParentThreadId ?? thread.ChannelContext
                : null;
            var input = new HookInput
            {
                SessionId = thread.Id,
                TurnId = turnId,
                Cwd = workspacePath,
                StopHookActive = string.Equals(triggerKind, "hook", StringComparison.Ordinal),
                IsSubagent = isSubagent,
                ParentThreadId = parentThreadId
            };
            await hookRunner.RunAsync(HookEvent.Interrupt, input, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Interrupt hook failed for thread {ThreadId}", thread.Id);
        }
    }

    private async Task RunSessionEndHookAsync(SessionThread thread)
    {
        if (hookRunner == null)
            return;

        EnsureHookRewakeHandler();
        try
        {
            using var timeoutCts = new CancellationTokenSource(TeardownHookTimeout);
            var isSubagent = IsSubAgentThread(thread);
            var parentThreadId = isSubagent
                ? thread.Source.SubAgent?.ParentThreadId ?? thread.ChannelContext
                : null;
            var input = new HookInput
            {
                SessionId = thread.Id,
                Cwd = thread.WorkspacePath,
                IsSubagent = isSubagent,
                ParentThreadId = parentThreadId
            };
            await hookRunner.RunAsync(HookEvent.SessionEnd, input, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "SessionEnd hook failed for thread {ThreadId}", thread.Id);
        }
    }

    private async Task<HookResult> RunPromptLifecycleHookAsync(
        HookEvent evt,
        string threadId,
        string turnId,
        string? workspacePath,
        string prompt,
        bool stopHookActive,
        CancellationToken ct)
    {
        if (hookRunner == null)
            return new HookResult();

        try
        {
            var hookInput = new HookInput
            {
                SessionId = threadId,
                TurnId = turnId,
                Cwd = workspacePath,
                Prompt = prompt,
                StopHookActive = stopHookActive
            };
            return await hookRunner.RunAsync(evt, hookInput, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "{EventName} hook failed for thread {ThreadId}", evt, threadId);
            return new HookResult();
        }
    }

    private async Task<HookResult> RunSessionStartHookOnceAsync(
        string threadId,
        string turnId,
        string? workspacePath,
        bool stopHookActive,
        CancellationToken ct)
    {
        if (hookRunner == null)
            return new HookResult();

        if (!_sessionStartHookThreads.TryAdd(threadId, 0))
            return new HookResult();

        try
        {
            var hookInput = new HookInput
            {
                SessionId = threadId,
                TurnId = turnId,
                Cwd = workspacePath,
                StopHookActive = stopHookActive
            };
            return await hookRunner.RunAsync(HookEvent.SessionStart, hookInput, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "SessionStart hook failed for thread {ThreadId}", threadId);
            return new HookResult();
        }
    }

    private async Task<HookResult> RunLifecycleHookAsync(
        HookEvent evt,
        string threadId,
        string? turnId,
        string? workspacePath,
        IReadOnlyDictionary<string, object?>? context,
        CancellationToken ct)
    {
        if (hookRunner == null)
            return new HookResult();

        try
        {
            var hookInput = new HookInput
            {
                SessionId = threadId,
                TurnId = turnId,
                Cwd = workspacePath,
                ToolName = evt.ToString(),
                ToolArgs = context,
                StopHookActive = string.Equals(TurnTriggerScope.Current?.Kind, "hook", StringComparison.Ordinal)
            };
            return await hookRunner.RunAsync(evt, hookInput, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "{EventName} hook failed for thread {ThreadId}", evt, threadId);
            return new HookResult();
        }
    }

    private async Task<HookResult> RunCompactionHookAsync(
        HookEvent evt,
        SessionThread thread,
        string? turnId,
        string trigger,
        CompactionThreshold thresholdBefore,
        CompactionThreshold? thresholdAfter,
        ContextUsageSnapshot? contextUsage,
        string? outcome,
        CancellationToken ct)
    {
        var context = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["trigger"] = trigger,
            ["compactionTrigger"] = trigger,
            ["compaction_trigger"] = trigger,
            ["outcome"] = outcome,
            ["tokensBefore"] = thresholdBefore.Tokens,
            ["tokens_before"] = thresholdBefore.Tokens,
            ["percentLeftBefore"] = thresholdBefore.PercentLeft,
            ["percent_left_before"] = thresholdBefore.PercentLeft,
            ["tokensAfter"] = thresholdAfter?.Tokens,
            ["tokens_after"] = thresholdAfter?.Tokens,
            ["percentLeftAfter"] = thresholdAfter?.PercentLeft,
            ["percent_left_after"] = thresholdAfter?.PercentLeft,
            ["contextUsage"] = contextUsage,
            ["context_usage"] = contextUsage
        };

        return await RunLifecycleHookAsync(
            evt,
            thread.Id,
            turnId,
            thread.WorkspacePath,
            context,
            ct).ConfigureAwait(false);
    }

    public async Task RunSubAgentLifecycleHookAsync(
        SubAgentLifecycleHookRequest request,
        CancellationToken ct)
    {
        var child = request.ChildThread;
        var source = child.Source.SubAgent;
        var turnId = child.Turns.LastOrDefault()?.Id ?? source?.ParentTurnId;
        var context = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["childThreadId"] = child.Id,
            ["child_thread_id"] = child.Id,
            ["parentThreadId"] = source?.ParentThreadId ?? child.ChannelContext,
            ["parent_thread_id"] = source?.ParentThreadId ?? child.ChannelContext,
            ["parentTurnId"] = source?.ParentTurnId,
            ["parent_turn_id"] = source?.ParentTurnId,
            ["rootThreadId"] = source?.RootThreadId,
            ["root_thread_id"] = source?.RootThreadId,
            ["agentPath"] = source?.AgentPath,
            ["agent_path"] = source?.AgentPath,
            ["taskName"] = source?.TaskName,
            ["task_name"] = source?.TaskName,
            ["agentNickname"] = source?.AgentNickname,
            ["agent_nickname"] = source?.AgentNickname,
            ["agentRole"] = source?.AgentRole,
            ["agent_role"] = source?.AgentRole,
            ["depth"] = source?.Depth,
            ["status"] = request.Status,
            ["message"] = request.Message
        };

        var parentThreadId = source?.ParentThreadId ?? child.ChannelContext;
        if (hookRunner == null)
            return;

        EnsureHookRewakeHandler();
        try
        {
            var hookInput = new HookInput
            {
                SessionId = child.Id,
                TurnId = turnId,
                Cwd = child.WorkspacePath,
                ToolName = request.Event.ToString(),
                ToolArgs = context,
                StopHookActive = string.Equals(TurnTriggerScope.Current?.Kind, "hook", StringComparison.Ordinal),
                IsSubagent = true,
                ParentThreadId = parentThreadId
            };
            await hookRunner.RunAsync(request.Event, hookInput, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "{EventName} hook failed for thread {ThreadId}", request.Event, child.Id);
        }
    }

    private static string BuildHookBlockedMessage(string action, HookResult result)
    {
        var reason = string.IsNullOrWhiteSpace(result.BlockReason)
            ? "no reason given"
            : result.BlockReason;
        return $"{action} blocked by hook: {reason}";
    }

    private static string? ExtractHookContext(HookResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.AdditionalContext))
            return result.AdditionalContext;
        return string.IsNullOrWhiteSpace(result.Output) ? null : result.Output;
    }

    private static string? BuildPromptHookReminder(HookResult promptHook, HookResult prePromptHook)
    {
        var feedback = new List<StreamingToolHookFeedback>();
        if (ExtractHookContext(promptHook) is { } promptContext)
            feedback.Add(new(nameof(HookEvent.UserPromptSubmit), promptContext.Trim(), false));
        if (ExtractHookContext(prePromptHook) is { } prePromptContext)
            feedback.Add(new(nameof(HookEvent.PrePrompt), prePromptContext.Trim(), false));
        return feedback.Count == 0 ? null : StreamingFunctionInvokingChatClient.BuildHookFeedbackReminder(feedback);
    }

    private async Task EnqueueHookRewakeAsync(HookRewakeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ThreadId) || string.IsNullOrWhiteSpace(request.Prompt))
            return;

        try
        {
            var label = string.IsNullOrWhiteSpace(request.Summary) ? "Hook feedback" : request.Summary;
            using var triggerScope = TurnTriggerScope.Set(new TurnTriggerInfo
            {
                Kind = "hook",
                Label = label,
                RefId = request.HookKey
            });
            await EnqueueTurnInputAsync(
                request.ThreadId,
                [new TextContent(request.Prompt)],
                sender: null,
                ct,
                inputSnapshot: null).ConfigureAwait(false);
            await TryStartNextQueuedTurnAsync(request.ThreadId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to enqueue hook rewake for thread {ThreadId}", request.ThreadId);
        }
    }
}
