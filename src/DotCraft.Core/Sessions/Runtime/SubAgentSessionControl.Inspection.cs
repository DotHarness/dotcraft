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
