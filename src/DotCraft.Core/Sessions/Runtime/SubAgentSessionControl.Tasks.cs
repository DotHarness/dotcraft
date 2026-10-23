namespace DotCraft.Sessions;

public static partial class SubAgentSessionControl
{
    internal static async Task<BackgroundTask?> FindAgentTaskAsync(
        ISessionService sessionService,
        BackgroundTaskCaller caller,
        string taskId,
        CancellationToken ct)
    {
        AgentPath callerPath;
        AgentPath targetPath;
        try
        {
            callerPath = GetCurrentAgentPath(caller.Thread);
            targetPath = callerPath.Resolve(taskId);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
        if (targetPath == callerPath || !targetPath.IsSameOrDescendantOf(callerPath))
            return null;

        ResolvedAgentTarget resolved;
        try
        {
            resolved = await ResolveAgentTargetAsync(
                sessionService,
                caller.Thread,
                caller.RootThreadId,
                targetPath.Value,
                requireOpen: false,
                ct).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }

        var child = resolved.Thread;
        RunningChildren.TryGetValue(child.Id, out var running);
        var (status, kindStatus) = ResolveTaskStatus(child, resolved.Edge, running is { Completion.IsCompleted: false });
        var latest = child.Turns.LastOrDefault();
        return new BackgroundTask
        {
            TaskId = resolved.Path.Value,
            Kind = BackgroundTaskKinds.Agent,
            OwnerThreadId = resolved.Edge?.ParentThreadId ?? child.Source.SubAgent?.ParentThreadId ?? caller.Thread.Id,
            TurnId = resolved.Edge?.ParentTurnId,
            Label = NormalizeOptional(resolved.Edge?.AgentNickname) ?? NormalizeOptional(child.DisplayName) ?? resolved.Path.Value,
            Status = status,
            KindStatus = kindStatus,
            LastAgentMessage = await FindLastAgentMessageAsync(sessionService, child, ct).ConfigureAwait(false),
            StartedAt = child.CreatedAt,
            EndedAt = status == BackgroundTaskStatus.Running ? null : latest?.CompletedAt,
            Latch = running?.Latch
        };
    }

    internal static async Task<IReadOnlyList<BackgroundTask>> ListAgentTasksAsync(
        ISessionService sessionService,
        BackgroundTaskCaller caller,
        CancellationToken ct)
    {
        var tasks = new List<BackgroundTask>();
        foreach (var edge in await sessionService.ListSubAgentChildrenAsync(caller.Thread.Id, includeClosed: true, ct).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(edge.AgentPath))
                continue;
            if (await FindAgentTaskAsync(sessionService, caller, edge.AgentPath, ct).ConfigureAwait(false) is { } task)
                tasks.Add(task);
        }
        return tasks;
    }

    internal static async Task StopAgentTaskAsync(ISessionService sessionService, BackgroundTask task, CancellationToken ct)
    {
        var caller = await sessionService.GetThreadAsync(task.OwnerThreadId, ct).ConfigureAwait(false);
        var child = (await ResolveAgentTargetAsync(
            sessionService,
            caller,
            caller.Source.SubAgent?.RootThreadId ?? caller.Id,
            task.TaskId,
            requireOpen: false,
            ct).ConfigureAwait(false)).Thread;
        if (!RunningChildren.TryGetValue(child.Id, out var running))
        {
            await CancelActiveChildTurnAsync(sessionService, child.Id, child, ct).ConfigureAwait(false);
            return;
        }
        try
        {
            await running.Cancellation.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        try
        {
            await running.Completion.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
        }
    }

    internal static bool InterruptRunningChild(string childThreadId)
    {
        if (!RunningChildren.TryGetValue(childThreadId, out var running))
            return false;
        try
        {
            _ = running.Cancellation.CancelAsync();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static async Task<string?> FindLastAgentMessageAsync(
        ISessionService sessionService,
        SessionThread thread,
        CancellationToken ct)
    {
        foreach (var turn in Enumerable.Reverse(thread.Turns))
        {
            if (LastAgentText(turn) is { } resident)
                return resident;
        }

        if (thread.OmittedTurnCount == 0)
            return null;
        await foreach (var turn in sessionService.ReadThreadTurnsAsync(
                           thread.Id, ThreadHistorySortDirection.Descending, ct: ct).ConfigureAwait(false))
        {
            if (LastAgentText(turn) is { } text)
                return text;
        }

        return null;

        static string? LastAgentText(SessionTurn turn) =>
            turn.Items.LastOrDefault(static item => !string.IsNullOrWhiteSpace(item.AsAgentMessage?.Text))
                ?.AsAgentMessage!.Text;
    }

    private static (string Status, string KindStatus) ResolveTaskStatus(SessionThread child, ThreadSpawnEdge? edge, bool running)
    {
        if (string.Equals(edge?.Status, ThreadSpawnEdgeStatus.Closed, StringComparison.Ordinal))
            return (BackgroundTaskStatus.Stopped, ThreadSpawnEdgeStatus.Closed);
        var active = GetActiveTurn(child);
        if (active != null || running)
            return (BackgroundTaskStatus.Running, active == null ? BackgroundTaskStatus.Running : ToAgentStatus(active.Status));
        var latest = child.Turns.LastOrDefault();
        if (latest == null)
            return (BackgroundTaskStatus.Completed, "idle");
        var status = latest.Status switch
        {
            TurnStatus.Failed => BackgroundTaskStatus.Failed,
            TurnStatus.Cancelled => BackgroundTaskStatus.Stopped,
            _ => BackgroundTaskStatus.Completed
        };
        return (status, ToAgentStatus(latest.Status));
    }
}

internal sealed class SubAgentTaskKind(ISessionService sessionService) : IBackgroundTaskKind
{
    public string Kind => BackgroundTaskKinds.Agent;

    public Task<BackgroundTask?> FindAsync(BackgroundTaskCaller caller, string taskId, CancellationToken ct) =>
        SubAgentSessionControl.FindAgentTaskAsync(sessionService, caller, taskId, ct);

    public Task<IReadOnlyList<BackgroundTask>> ListAsync(BackgroundTaskCaller caller, CancellationToken ct) =>
        SubAgentSessionControl.ListAgentTasksAsync(sessionService, caller, ct);

    public Task StopAsync(BackgroundTask task, CancellationToken ct) =>
        SubAgentSessionControl.StopAgentTaskAsync(sessionService, task, ct);
}
