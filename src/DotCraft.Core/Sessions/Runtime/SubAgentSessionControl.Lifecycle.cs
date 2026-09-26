using DotCraft.Agents;
using DotCraft.Hooks;

namespace DotCraft.Sessions;

public static partial class SubAgentSessionControl
{
    public static async Task<SubAgentControlResult> CloseAgentAsync(
        SubAgentSessionContext context,
        string target,
        CancellationToken ct)
    {
        var resolved = await ResolveAgentTargetAsync(
            context.SessionService,
            context.ParentThread,
            context.RootThreadId,
            target,
            requireOpen: false,
            ct);
        if (resolved.Path.IsRoot)
            throw new InvalidOperationException("CloseAgent target cannot be '/root'.");

        var senderPath = GetCurrentAgentPath(context.ParentThread);
        if (string.Equals(senderPath.Value, resolved.Path.Value, StringComparison.Ordinal))
            throw new InvalidOperationException("CloseAgent target cannot be the current agent.");

        var wasAlreadyClosed = string.Equals(resolved.Edge?.Status, ThreadSpawnEdgeStatus.Closed, StringComparison.Ordinal);
        var hadRunningObserver = RunningChildren.ContainsKey(resolved.ThreadId);
        var result = await CloseAgentAsync(context.SessionService, resolved.ThreadId, ct);
        result.AgentPath = resolved.Path.Value;
        result.TaskName = resolved.Edge?.TaskName;
        if (!hadRunningObserver && !wasAlreadyClosed)
        {
            var child = await context.SessionService.GetThreadAsync(resolved.ThreadId, ct);
            await RunLifecycleHookAsync(
                context.LifecycleHook,
                HookEvent.SubagentStop,
                child,
                ThreadSpawnEdgeStatus.Closed,
                message: null,
                ct);
        }

        return result;
    }

    public static async Task<SubAgentControlResult> CloseAgentAsync(
        ISessionService sessionService,
        string childThreadId,
        CancellationToken ct)
    {
        var child = await sessionService.GetThreadAsync(childThreadId, ct);
        var parentThreadId = child.Source.SubAgent?.ParentThreadId ?? child.ChannelContext;
        child = await CancelActiveChildTurnForCloseAsync(sessionService, childThreadId, child, ct);
        MarkNativeProgressCompletedForClose(child);

        if (!string.IsNullOrWhiteSpace(parentThreadId))
            await sessionService.SetThreadSpawnEdgeStatusAsync(parentThreadId!, childThreadId, ThreadSpawnEdgeStatus.Closed, ct);

        if (sessionService is ISubAgentThreadLifecycleService lifecycle)
            await lifecycle.ArchiveSubAgentTreeForCloseAsync(childThreadId, ct);

        return new SubAgentControlResult
        {
            ChildThreadId = childThreadId,
            AgentPath = child.Source.SubAgent?.AgentPath,
            TaskName = child.Source.SubAgent?.TaskName,
            Status = ThreadSpawnEdgeStatus.Closed,
            AgentNickname = child.Source.SubAgent?.AgentNickname,
            AgentRole = child.Source.SubAgent?.AgentRole,
            ProfileName = child.Source.SubAgent?.ProfileName,
            RuntimeType = child.Source.SubAgent?.RuntimeType,
            SupportsSendInput = child.Source.SubAgent?.SupportsSendInput ?? true,
            SupportsResume = child.Source.SubAgent?.SupportsResume ?? true,
            SupportsSendMessage = child.Source.SubAgent?.SupportsSendMessage ?? true,
            SupportsFollowupTask = child.Source.SubAgent?.SupportsFollowupTask ?? true,
            SupportsClose = child.Source.SubAgent?.SupportsClose ?? true
        };
    }

    private static void MarkNativeProgressCompletedForClose(SessionThread child)
    {
        var source = child.Source.SubAgent;
        if (source == null)
            return;

        var runtimeType = NormalizeOptional(source.RuntimeType) ?? NativeSubAgentRuntime.RuntimeTypeName;
        if (!string.Equals(runtimeType, NativeSubAgentRuntime.RuntimeTypeName, StringComparison.OrdinalIgnoreCase))
            return;

        var label = NormalizeOptional(source.AgentNickname)
            ?? NormalizeOptional(source.TaskName)
            ?? NormalizeOptional(child.DisplayName)
            ?? child.Id;
        var task = NormalizeOptional(source.TaskName) ?? label;
        var bridgeKey = SubAgentManager.NormalizeLabel(label, task);
        var progress = SubAgentProgressBridge.GetOrCreate(bridgeKey);
        progress.CurrentTool = null;
        progress.CurrentToolDisplay = null;
        progress.LastTool = "closed";
        progress.LastToolDisplay = $"Closed {bridgeKey}";
        progress.IsCompleted = true;
    }

    private static async Task<SessionThread> CancelActiveChildTurnForCloseAsync(
        ISessionService sessionService,
        string childThreadId,
        SessionThread child,
        CancellationToken ct)
    {
        if (RunningChildren.TryRemove(childThreadId, out var running))
        {
            try
            {
                await running.Cancellation.CancelAsync();
                await running.Completion.WaitAsync(CloseAgentCancellationWait, ct);
            }
            catch (TimeoutException)
            {
                // Best-effort: fall through and explicitly cancel any active turn snapshot below.
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // The running task may surface cancellation directly instead of returning a cancelled result.
            }
            finally
            {
                running.Cancellation.Dispose();
            }

            child = await sessionService.GetThreadAsync(childThreadId, ct);
        }

        var activeTurn = child.Turns.LastOrDefault(t =>
            t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput);
        if (activeTurn == null)
            return child;

        if (sessionService is ISubAgentSyntheticTurnService syntheticTurns
            && !string.Equals(
                child.Source.SubAgent?.RuntimeType,
                NativeSubAgentRuntime.RuntimeTypeName,
                StringComparison.OrdinalIgnoreCase))
        {
            await syntheticTurns.CancelSubAgentSyntheticTurnAsync(
                childThreadId,
                activeTurn.Id,
                "Subagent was cancelled.",
                CancellationToken.None);
        }
        else
        {
            await sessionService.CancelTurnAsync(childThreadId, activeTurn.Id, ct);
        }

        return await sessionService.GetThreadAsync(childThreadId, ct);
    }

    public static async Task<SubAgentControlResult> ResumeAgentAsync(
        ISessionService sessionService,
        string childThreadId,
        CancellationToken ct)
    {
        var child = await sessionService.ResumeThreadAsync(childThreadId, ct);
        var parentThreadId = child.Source.SubAgent?.ParentThreadId ?? child.ChannelContext;
        if (!string.IsNullOrWhiteSpace(parentThreadId))
            await sessionService.SetThreadSpawnEdgeStatusAsync(parentThreadId!, childThreadId, ThreadSpawnEdgeStatus.Open, ct);

        return new SubAgentControlResult
        {
            ChildThreadId = childThreadId,
            Status = ThreadSpawnEdgeStatus.Open,
            AgentNickname = child.Source.SubAgent?.AgentNickname,
            AgentRole = child.Source.SubAgent?.AgentRole,
            ProfileName = child.Source.SubAgent?.ProfileName,
            RuntimeType = child.Source.SubAgent?.RuntimeType,
            SupportsSendInput = child.Source.SubAgent?.SupportsSendInput ?? true,
            SupportsResume = child.Source.SubAgent?.SupportsResume ?? true,
            SupportsClose = child.Source.SubAgent?.SupportsClose ?? true
        };
    }

    private static async Task EnforceResidencyLimitAsync(
        SubAgentSessionContext context,
        int maxConcurrentSubAgents,
        CancellationToken ct)
    {
        var cap = Math.Max(1, maxConcurrentSubAgents);
        while (true)
        {
            // Keep resident open edges strictly below the cap so the pending spawn fits.
            var openEdges = await CollectOpenSubtreeEdgesAsync(context.SessionService, context.RootThreadId, ct);
            if (openEdges.Count < cap)
                return;

            var evictable = openEdges
                .Where(edge => !RunningChildren.ContainsKey(edge.ChildThreadId))
                .OrderBy(edge => edge.UpdatedAt)
                .FirstOrDefault();
            if (evictable == null)
                throw new InvalidOperationException(
                    $"Subagent concurrency limit reached: up to {cap} agents can be active at once. "
                    + "Close a running agent with CloseAgent before spawning another.");

            await CloseAgentAsync(context.SessionService, evictable.ChildThreadId, ct);
        }
    }

    /// <summary>
    /// Collects every open (non-closed) spawn edge in the root thread's subtree,
    /// breadth-first, so residency can be measured across nested SubAgents.
    /// </summary>
    private static async Task<List<ThreadSpawnEdge>> CollectOpenSubtreeEdgesAsync(
        ISessionService sessionService,
        string rootThreadId,
        CancellationToken ct)
    {
        var openEdges = new List<ThreadSpawnEdge>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(rootThreadId);
        while (queue.Count > 0)
        {
            var parentThreadId = queue.Dequeue();
            if (!visited.Add(parentThreadId))
                continue;

            var edges = await sessionService.ListSubAgentChildrenAsync(parentThreadId, includeClosed: false, ct);
            foreach (var edge in edges)
            {
                openEdges.Add(edge);
                queue.Enqueue(edge.ChildThreadId);
            }
        }

        return openEdges;
    }

    private static async Task ThrowIfDuplicateSiblingPathAsync(
        ISessionService sessionService,
        string parentThreadId,
        string taskName,
        AgentPath agentPath,
        CancellationToken ct)
    {
        var siblings = await sessionService.ListSubAgentChildrenAsync(parentThreadId, includeClosed: true, ct);
        if (siblings.Any(edge =>
                string.Equals(edge.TaskName, taskName, StringComparison.Ordinal)
                || string.Equals(edge.AgentPath, agentPath.Value, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Subagent taskName '{taskName}' already exists under this parent.");
        }
    }
}
