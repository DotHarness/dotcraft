using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using Contract = DotCraft.Protocol.AppServer;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.AppServer;

internal sealed class AppServerThreadWireProjector(
    ISessionService sessionService,
    AppServerConnection connection,
    IAppConfigMonitor? appConfigMonitor,
    PlanStore? planStore,
    IReadOnlyList<IThreadOriginPresentationProvider>? originPresentationProviders,
    string? workspaceDataPath)
{
    public async Task<SessionWireThread> ProjectAsync(
        SessionThread thread,
        bool includeTurns,
        bool filterToolExecutions,
        CancellationToken ct)
    {
        var wire = string.IsNullOrWhiteSpace(workspaceDataPath)
            ? thread.ToWire(includeTurns)
            : thread.ToWire(workspaceDataPath, includeTurns);
        if (filterToolExecutions)
            wire = FilterToolExecutionItemsForConnection(wire);
        return await EnrichAsync(
            WithRuntimeSnapshot(WithContextUsage(wire, thread.Id), thread),
            thread,
            ct);
    }

    public async Task<SessionWireThread> ProjectHeaderAsync(ThreadHistorySnapshot snapshot, CancellationToken ct)
    {
        var thread = snapshot.Thread;
        var wire = thread.ToWire(includeTurns: false) with
        {
            Runtime = snapshot.PersistedRuntime.ToWireRuntimeState()
        };
        wire = await WithPlanAsync(WithContextUsage(wire, thread.Id), thread.Id, ct);
        wire = wire with { ReadState = await sessionService.GetThreadReadStateAsync(thread, ct) };
        return await EnrichAsync(wire, thread, ct);
    }

    public async Task<IReadOnlyDictionary<string, Contract.ThreadReadState>?> GetReadStatesAsync(
        IReadOnlyList<ThreadSummary> page,
        CancellationToken ct)
    {
        var states = await sessionService.GetThreadReadStatesAsync(page, ct);
        return states?.ToDictionary(
            static entry => entry.Key,
            static entry => AppServerContractMapper.ToContract(entry.Value),
            StringComparer.Ordinal);
    }

    public async Task<SessionWireThread> EnrichAsync(
        SessionWireThread wire,
        SessionThread thread,
        CancellationToken ct)
    {
        return await HydrateThreadGoalAsync(
            WithOriginPresentation(wire),
            ct).ConfigureAwait(false);
    }

    public SessionWireThread EnrichForNotification(SessionWireThread wire) => WithOriginPresentation(wire);

    public Task<ThreadGoalSnapshot?> TryGetGoalSnapshotAsync(string threadId, CancellationToken ct) =>
        TryReadGoalSnapshotAsync(() => sessionService.GetStoredThreadGoalAsync(threadId, ct));

    private async Task<ThreadGoalSnapshot?> TryReadGoalSnapshotAsync(Func<Task<ThreadGoal?>> readGoal)
    {
        if (!GoalsCapabilityEnabled())
            return null;

        try
        {
            var goal = await readGoal();
            return goal is null ? null : ThreadGoalSnapshot.FromGoal(goal);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    public async Task EnrichSummaryAsync(ThreadSummary summary, CancellationToken ct)
    {
        summary.Goal = await TryReadGoalSnapshotAsync(() => sessionService.GetStoredThreadGoalAsync(summary.Id, ct));
        summary.OriginPresentation = ResolveOriginPresentation(
            summary.Id,
            summary.WorkspacePath,
            summary.OriginChannel,
            summary.ChannelContext);
    }

    public bool GoalsCapabilityEnabled()
    {
        var config = appConfigMonitor?.Current ?? new AppConfig();
        return config.Goals.Enabled;
    }

    public SessionWireThread FilterToolExecutionItemsForConnection(SessionWireThread wire)
    {
        if (connection.SupportsToolExecutionLifecycle || wire.Turns is null)
            return wire;

        foreach (var turn in wire.Turns)
            turn.Items?.RemoveAll(item => item.Type == ItemType.ToolExecution);
        return wire;
    }

    public IReadOnlyList<(string TurnId, SessionWireItem Item)> ProjectHistoryItems(
        IReadOnlyList<ThreadHistoryItem> entries) =>
        entries
            .Where(entry => connection.SupportsToolExecutionLifecycle || entry.Item.Type != ItemType.ToolExecution)
            .Select(entry => (entry.TurnId, entry.Item.ToWire()))
            .ToList();

    public SessionWireThread WithContextUsage(SessionWireThread wire, string threadId)
    {
        var snapshot = sessionService.TryGetContextUsageSnapshot(threadId);
        return snapshot is null ? wire : wire with { ContextUsage = snapshot };
    }

    public SessionWireThread WithRuntimeSnapshot(SessionWireThread wire, SessionThread thread) =>
        wire with { Runtime = sessionService.GetThreadRuntimeSnapshot(thread).ToWireRuntimeState() };

    public async Task<SessionWireThread> WithPlanAsync(
        SessionWireThread wire,
        string threadId,
        CancellationToken ct)
    {
        if (planStore == null)
            return wire;

        var plan = await planStore.LoadStructuredPlanAsync(threadId);
        ct.ThrowIfCancellationRequested();
        return wire with { Plan = plan == null ? null : SessionWireMapper.ToWire(plan) };
    }

    public SessionWireThread WithOriginPresentation(SessionWireThread wire)
    {
        var presentation = ResolveOriginPresentation(
            wire.Id,
            wire.WorkspacePath,
            wire.OriginChannel,
            wire.ChannelContext);
        return presentation is null ? wire : wire with { OriginPresentation = presentation };
    }

    private ThreadOriginPresentationSnapshot? ResolveOriginPresentation(
        string threadId,
        string workspacePath,
        string originChannel,
        string? channelContext)
    {
        if (originPresentationProviders is null || originPresentationProviders.Count == 0)
            return null;

        var context = new ThreadOriginPresentationContext(
            threadId,
            workspacePath,
            originChannel,
            channelContext);
        foreach (var provider in originPresentationProviders)
        {
            var presentation = provider.Resolve(context);
            if (presentation is not null)
                return presentation;
        }

        return null;
    }

    public async Task<SessionWireThread> HydrateThreadGoalAsync(SessionWireThread wire, CancellationToken ct)
    {
        var goal = await TryGetGoalSnapshotAsync(wire.Id, ct);
        return goal is null ? wire : wire with { Goal = goal };
    }
}
