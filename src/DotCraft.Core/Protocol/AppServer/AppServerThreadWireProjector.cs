using DotCraft.AppBinding;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Skills;

namespace DotCraft.Protocol.AppServer;

internal sealed class AppServerThreadWireProjector(
    ISessionService sessionService,
    AppServerConnection connection,
    IAppConfigMonitor? appConfigMonitor,
    WorkspaceConfigEditor workspaceConfig,
    SkillsLoader? skillsLoader,
    PlanStore? planStore,
    AppBindingService? appBindingService,
    IReadOnlyList<string>? builtInPluginSourceRoots)
{
    public async Task<SessionWireThread> ProjectAsync(
        SessionThread thread,
        bool includeTurns,
        bool filterToolExecutions,
        CancellationToken ct)
    {
        var wire = thread.ToWire(includeTurns);
        if (filterToolExecutions)
            wire = FilterToolExecutionItemsForConnection(wire);
        return await EnrichAsync(
            WithRuntimeSnapshot(WithContextUsage(wire, thread.Id), thread),
            thread,
            ct);
    }

    public async Task<SessionWireThread> EnrichAsync(
        SessionWireThread wire,
        SessionThread thread,
        CancellationToken ct) =>
        await HydrateThreadGoalAsync(WithAppBindingAttribution(wire, thread.Id, thread.WorkspacePath), ct);

    public SessionWireThread EnrichForNotification(SessionWireThread wire) =>
        WithAppBindingAttribution(wire, wire.Id, wire.WorkspacePath);

    public async Task<ThreadGoalWire?> TryGetGoalSnapshotAsync(string threadId, CancellationToken ct)
    {
        if (!GoalsCapabilityEnabled())
            return null;

        try
        {
            var goal = await sessionService.GetThreadGoalAsync(threadId, ct);
            return goal is null ? null : ThreadGoalWire.FromGoal(goal);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    public async Task EnrichSummaryAsync(
        ThreadSummary summary,
        Dictionary<string, AppCatalogSnapshot?> catalogByWorkspace,
        CancellationToken ct)
    {
        summary.Goal = await TryGetGoalSnapshotAsync(summary.Id, ct);
        if (appBindingService == null)
            return;

        if (!catalogByWorkspace.TryGetValue(summary.WorkspacePath, out var catalog))
        {
            catalog = TryGetAppCatalog(summary.WorkspacePath);
            catalogByWorkspace[summary.WorkspacePath] = catalog;
        }

        if (catalog is null)
            return;

        var appBindings = appBindingService.ListThreadBindingSummaries(
            catalog,
            Path.Combine(summary.WorkspacePath, ".craft"),
            summary.Id);
        if (appBindings.Count > 0)
            summary.AppBindings = appBindings;
        summary.OriginApp = appBindingService.ResolveOriginApp(catalog, summary.OriginChannel, summary.ChannelContext);
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

    public SessionWireThread WithWidgetState(SessionWireThread wire, string threadId)
    {
        if (wire.Turns is not { Count: > 0 } turns)
            return wire;
        var states = sessionService.GetItemWidgetStates(threadId);
        if (states.Count == 0)
            return wire;

        foreach (var turn in turns)
        {
            if (turn.Items is not { Count: > 0 } items)
                continue;
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].Payload is not DynamicToolCallPayload payload
                    || string.IsNullOrEmpty(payload.CallId)
                    || !states.TryGetValue(payload.CallId, out var json))
                {
                    continue;
                }

                try
                {
                    if (System.Text.Json.Nodes.JsonNode.Parse(json) is { } node)
                        items[i] = items[i] with { Payload = payload with { WidgetState = node } };
                }
                catch (System.Text.Json.JsonException)
                {
                    // Skip a corrupt stored state rather than failing the whole read.
                }
            }
        }

        return wire;
    }

    public SessionWireThread WithAppBindingAttribution(
        SessionWireThread wire,
        string threadId,
        string workspacePath)
    {
        if (appBindingService is null || string.IsNullOrWhiteSpace(threadId))
            return wire;
        var catalog = TryGetAppCatalog(workspacePath);
        if (catalog is null)
            return wire;
        var appBindings = appBindingService.ListThreadBindingSummaries(
            catalog, Path.Combine(workspacePath, ".craft"), threadId);
        var originApp = appBindingService.ResolveOriginApp(catalog, wire.OriginChannel, wire.ChannelContext);
        if (appBindings.Count == 0 && originApp is null)
            return wire;
        return wire with
        {
            AppBindings = appBindings.Count > 0 ? appBindings : wire.AppBindings,
            OriginApp = originApp ?? wire.OriginApp
        };
    }

    public AppCatalogSnapshot? TryGetAppCatalog(string workspacePath)
    {
        if (appBindingService == null || string.IsNullOrWhiteSpace(workspacePath))
            return null;
        var craftPath = Path.Combine(workspacePath, ".craft");
        if (!Directory.Exists(craftPath))
            return null;
        return appBindingService.DiscoverCatalog(
            appConfigMonitor?.Current ?? workspaceConfig.LoadCurrentMergedConfig(),
            workspacePath,
            craftPath,
            skillsLoader,
            builtInPluginSourceRoots);
    }

    public void RevokeAppBindingsForDeletedThread(SessionThread thread)
    {
        if (appBindingService == null)
            return;

        var craftPath = Path.Combine(thread.WorkspacePath, ".craft");
        if (!Directory.Exists(craftPath))
            return;

        var catalog = appBindingService.DiscoverCatalog(
            appConfigMonitor?.Current ?? workspaceConfig.LoadCurrentMergedConfig(),
            thread.WorkspacePath,
            craftPath,
            skillsLoader,
            builtInPluginSourceRoots);
        _ = appBindingService.RevokeBindingsForDeletedThread(catalog, craftPath, thread.Id);
    }

    public ThreadArchiveSocialBindingCleanupResult RevokeSocialAppBindingsForArchivedThread(SessionThread thread)
    {
        if (appBindingService == null)
            return new ThreadArchiveSocialBindingCleanupResult([], []);

        var craftPath = Path.Combine(thread.WorkspacePath, ".craft");
        if (!Directory.Exists(craftPath))
            return new ThreadArchiveSocialBindingCleanupResult([], []);

        var catalog = appBindingService.DiscoverCatalog(
            appConfigMonitor?.Current ?? workspaceConfig.LoadCurrentMergedConfig(),
            thread.WorkspacePath,
            craftPath,
            skillsLoader,
            builtInPluginSourceRoots);
        return appBindingService.RevokeSocialBindingsForArchivedThread(catalog, craftPath, thread.Id);
    }

    public async Task<SessionWireThread> HydrateThreadGoalAsync(SessionWireThread wire, CancellationToken ct)
    {
        var goal = await TryGetGoalSnapshotAsync(wire.Id, ct);
        return goal is null ? wire : wire with { Goal = goal };
    }
}
