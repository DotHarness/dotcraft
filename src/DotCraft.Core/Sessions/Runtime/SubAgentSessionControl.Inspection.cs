using DotCraft.Agents;
using DotCraft.Configuration;

namespace DotCraft.Sessions;

public static partial class SubAgentSessionControl
{
    public static async Task<SubAgentListResult> ListAgentsAsync(
        SubAgentSessionContext context,
        string? pathPrefix,
        CancellationToken ct)
    {
        var prefix = string.IsNullOrWhiteSpace(pathPrefix)
            ? (AgentPath?)null
            : GetCurrentAgentPath(context.ParentThread).Resolve(pathPrefix);
        var root = await context.SessionService.GetThreadAsync(context.RootThreadId, ct);
        var items = new List<SubAgentListItem>
        {
            new()
            {
                AgentPath = AgentPath.Root,
                Status = ResolveThreadStatus(root, null),
                DisplayName = root.DisplayName,
                LastTaskMessage = ExtractLastTaskMessage(root)
            }
        };

        await AddAgentListChildrenAsync(context.SessionService, context.RootThreadId, items, includeClosed: false, ct);
        var filtered = items
            .Where(item => prefix == null || AgentPath.Parse(item.AgentPath).IsSameOrDescendantOf(prefix.Value))
            .OrderBy(item => item.AgentPath == AgentPath.Root ? 0 : 1)
            .ThenBy(item => item.AgentPath, StringComparer.Ordinal)
            .ToArray();

        return new SubAgentListResult { Data = filtered };
    }

    public static async Task<SubAgentWaitResult> WaitAgentAsync(
        SubAgentSessionContext context,
        int? timeoutMs,
        CancellationToken ct,
        SubAgentWaitAgentTimeoutOptions? timeoutOptions = null)
    {
        var effectiveTimeoutMs = (timeoutOptions ?? SubAgentWaitAgentTimeoutOptions.Defaults)
            .ResolveTimeoutMs(timeoutMs);

        var currentPath = GetCurrentAgentPath(context.ParentThread);
        var communicationRuntime = SubAgentCommunicationRuntime.For(context.SessionService);
        using var subscription = communicationRuntime.Subscribe(
            context.RootThreadId,
            currentPath.Value,
            out var waitTask);
        var pending = await context.SessionService.ListPendingSubAgentMailboxAsync(
            context.RootThreadId,
            currentPath.Value,
            ct);
        if (pending.Count > 0)
            return new SubAgentWaitResult { Status = "changed", TimedOut = false };

        try
        {
            await waitTask.WaitAsync(TimeSpan.FromMilliseconds(effectiveTimeoutMs), ct);
            return new SubAgentWaitResult { Status = "changed", TimedOut = false };
        }
        catch (TimeoutException)
        {
            return new SubAgentWaitResult { Status = "timeout", TimedOut = true };
        }
    }

    public static async Task<SubAgentControlResult> WaitAgentAsync(
        ISessionService sessionService,
        string childThreadId,
        int? timeoutSeconds,
        CancellationToken ct)
    {
        SubAgentRunResult result;
        if (RunningChildren.TryGetValue(childThreadId, out var running))
        {
            try
            {
                var waitTask = running.Completion;
                if (timeoutSeconds is > 0)
                    waitTask = waitTask.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds.Value), ct);
                result = await waitTask.WaitAsync(ct);
            }
            catch (TimeoutException)
            {
                result = new SubAgentRunResult
                {
                    ThreadId = childThreadId,
                    Status = "timeout",
                    Message = "Wait timed out."
                };
            }
        }
        else
        {
            var loadedThread = await sessionService.GetThreadAsync(childThreadId, ct);
            var lastTurn = loadedThread.Turns.LastOrDefault();
            result = new SubAgentRunResult
            {
                ThreadId = childThreadId,
                Status = lastTurn?.Status.ToString().ToLowerInvariant() ?? "idle",
                Message = ExtractFinalAgentText(lastTurn)
            };
        }

        var thread = await sessionService.GetThreadAsync(childThreadId, ct);
        var source = thread.Source.SubAgent;
        return new SubAgentControlResult
        {
            ChildThreadId = childThreadId,
            AgentPath = source?.AgentPath,
            TaskName = source?.TaskName,
            Status = result.Status,
            Message = result.Message,
            AgentNickname = source?.AgentNickname,
            AgentRole = source?.AgentRole,
            ProfileName = source?.ProfileName,
            RuntimeType = source?.RuntimeType,
            SupportsSendInput = source?.SupportsSendInput ?? true,
            SupportsResume = source?.SupportsResume ?? true,
            SupportsSendMessage = source?.SupportsSendMessage ?? true,
            SupportsFollowupTask = source?.SupportsFollowupTask ?? true,
            SupportsClose = source?.SupportsClose ?? true
        };
    }

    private static AgentPath GetCurrentAgentPath(SessionThread thread)
    {
        var source = thread.Source.SubAgent;
        if (source == null)
            return AgentPath.RootPath;
        if (string.IsNullOrWhiteSpace(source.AgentPath))
            throw new InvalidOperationException($"Subagent thread '{thread.Id}' has no agentPath and cannot use path controls.");

        return AgentPath.Parse(source.AgentPath);
    }

    private static async Task<ResolvedAgentTarget> ResolveAgentTargetAsync(
        ISessionService sessionService,
        SessionThread currentThread,
        string rootThreadId,
        string target,
        bool requireOpen,
        CancellationToken ct)
    {
        var currentPath = GetCurrentAgentPath(currentThread);
        var targetPath = currentPath.Resolve(target);
        if (targetPath.IsRoot)
        {
            var root = await sessionService.GetThreadAsync(rootThreadId, ct);
            return new ResolvedAgentTarget(root.Id, targetPath, root, null);
        }

        var queue = new Queue<string>();
        queue.Enqueue(rootThreadId);
        while (queue.Count > 0)
        {
            var parentThreadId = queue.Dequeue();
            var edges = await sessionService.ListSubAgentChildrenAsync(parentThreadId, includeClosed: true, ct);
            foreach (var edge in edges
                         .Where(edge => !string.IsNullOrWhiteSpace(edge.AgentPath))
                         .OrderBy(edge => edge.AgentPath, StringComparer.Ordinal))
            {
                var edgePath = AgentPath.Parse(edge.AgentPath!);
                if (string.Equals(edgePath.Value, targetPath.Value, StringComparison.Ordinal))
                {
                    if (requireOpen && string.Equals(edge.Status, ThreadSpawnEdgeStatus.Closed, StringComparison.Ordinal))
                        throw new InvalidOperationException($"Subagent '{targetPath.Value}' is closed.");

                    var child = await sessionService.GetThreadAsync(edge.ChildThreadId, ct);
                    return new ResolvedAgentTarget(child.Id, edgePath, child, edge);
                }

                if (targetPath.IsSameOrDescendantOf(edgePath))
                    queue.Enqueue(edge.ChildThreadId);
            }
        }

        throw new KeyNotFoundException($"Subagent path '{targetPath.Value}' was not found.");
    }

    private static async Task AddAgentListChildrenAsync(
        ISessionService sessionService,
        string parentThreadId,
        List<SubAgentListItem> items,
        bool includeClosed,
        CancellationToken ct)
    {
        var edges = await sessionService.ListSubAgentChildrenAsync(parentThreadId, includeClosed, ct);
        foreach (var edge in edges
                     .Where(edge => !string.IsNullOrWhiteSpace(edge.AgentPath))
                     .OrderBy(edge => edge.AgentPath, StringComparer.Ordinal))
        {
            var child = await sessionService.GetThreadAsync(edge.ChildThreadId, ct);
            items.Add(new SubAgentListItem
            {
                AgentPath = edge.AgentPath!,
                Status = ResolveThreadStatus(child, edge),
                DisplayName = child.DisplayName ?? edge.AgentNickname ?? edge.TaskName,
                LastTaskMessage = ExtractLastTaskMessage(child)
            });
            await AddAgentListChildrenAsync(sessionService, edge.ChildThreadId, items, includeClosed, ct);
        }
    }

    private static string ResolveThreadStatus(SessionThread thread, ThreadSpawnEdge? edge)
    {
        if (string.Equals(edge?.Status, ThreadSpawnEdgeStatus.Closed, StringComparison.Ordinal))
            return ThreadSpawnEdgeStatus.Closed;

        var active = thread.Turns.LastOrDefault(turn =>
            turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput);
        if (active != null)
            return ToAgentStatus(active.Status);

        var latest = thread.Turns.LastOrDefault();
        return latest == null ? "idle" : ToAgentStatus(latest.Status);
    }

    private static string ToAgentStatus(TurnStatus status) => status.ToString().ToLowerInvariant();

    private static bool HasActiveTurn(SessionThread thread) =>
        thread.Turns.Any(turn => turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput);

    private static SessionTurn? GetActiveTurn(SessionThread thread) =>
        thread.Turns.LastOrDefault(turn => turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput);

    private static bool IsNativeRuntime(SessionThread thread) =>
        string.Equals(
            thread.Source.SubAgent?.RuntimeType ?? NativeSubAgentRuntime.RuntimeTypeName,
            NativeSubAgentRuntime.RuntimeTypeName,
            StringComparison.OrdinalIgnoreCase);
}
