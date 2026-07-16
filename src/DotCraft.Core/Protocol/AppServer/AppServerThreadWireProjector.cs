using System.Text.Json.Nodes;
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
    IReadOnlyList<IThreadOriginPresentationProvider>? originPresentationProviders,
    IReadOnlyList<string>? builtInPluginSourceRoots,
    IThreadToolSnapshotService? toolSnapshots,
    IThreadMcpRuntimeService? mcpRuntime)
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
        CancellationToken ct)
    {
        wire = await WithMcpAppAvailabilityAsync(wire, thread, ct).ConfigureAwait(false);
        return await HydrateThreadGoalAsync(
            WithOriginPresentation(WithAppBindingAttribution(wire, thread.Id, thread.WorkspacePath)),
            ct).ConfigureAwait(false);
    }

    public SessionWireThread EnrichForNotification(SessionWireThread wire) =>
        WithOriginPresentation(WithAppBindingAttribution(wire, wire.Id, wire.WorkspacePath));

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
        summary.OriginPresentation = ResolveOriginPresentation(
            summary.Id,
            summary.WorkspacePath,
            summary.OriginChannel,
            summary.ChannelContext);
        if (appBindingService == null)
            return;

        if (!catalogByWorkspace.TryGetValue(summary.WorkspacePath, out var catalog))
        {
            catalog = TryGetAppCatalog(summary.WorkspacePath);
            catalogByWorkspace[summary.WorkspacePath] = catalog;
        }

        if (catalog is null)
            return;

        var appBindings = MapBindingSummaries(
            catalog,
            appBindingService.ListThreadBindings(Path.Combine(summary.WorkspacePath, ".craft"), summary.Id));
        if (appBindings.Count > 0)
            summary.AppBindings = appBindings;
        summary.OriginApp = ResolveOriginApp(catalog, summary.OriginChannel, summary.ChannelContext);
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

    private async Task<SessionWireThread> WithMcpAppAvailabilityAsync(
        SessionWireThread wire,
        SessionThread thread,
        CancellationToken cancellationToken)
    {
        if (!connection.SupportsMcpApps || wire.Turns is null || wire.Turns.Count == 0)
            return wire;

        var context = await McpAppEligibilityResolver.CreateContextAsync(
            thread.Id,
            toolSnapshots,
            mcpRuntime,
            cancellationToken).ConfigureAwait(false);
        if (context is null)
            return wire;

        var turnsById = thread.Turns.ToDictionary(turn => turn.Id, StringComparer.Ordinal);
        foreach (var wireTurn in wire.Turns)
        {
            if (wireTurn.Items is null || !turnsById.TryGetValue(wireTurn.Id, out var turn))
                continue;
            var itemsById = turn.Items.ToDictionary(item => item.Id, StringComparer.Ordinal);
            for (var index = 0; index < wireTurn.Items.Count; index++)
            {
                var wireItem = wireTurn.Items[index];
                if (!itemsById.TryGetValue(wireItem.Id, out var item))
                    continue;
                var eligibility = McpAppEligibilityResolver.Resolve(turn.Id, item, context);
                wireTurn.Items[index] = wireItem with
                {
                    McpApp = eligibility is null ? null : new McpAppViewHintWire { Available = true }
                };
            }
        }

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
        => wire;

    public SessionWireThread WithOriginPresentation(SessionWireThread wire)
    {
        var presentation = ResolveOriginPresentation(
            wire.Id,
            wire.WorkspacePath,
            wire.OriginChannel,
            wire.ChannelContext);
        return presentation is null ? wire : wire with { OriginPresentation = presentation };
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
        var appBindings = MapBindingSummaries(
            catalog,
            appBindingService.ListThreadBindings(Path.Combine(workspacePath, ".craft"), threadId));
        var originApp = ResolveOriginApp(catalog, wire.OriginChannel, wire.ChannelContext);
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
        return AppBindingCatalog.Discover(
            appConfigMonitor?.Current ?? workspaceConfig.LoadCurrentMergedConfig(),
            workspacePath,
            craftPath,
            skillsLoader,
            builtInPluginSourceRoots);
    }

    private ThreadOriginPresentationWire? ResolveOriginPresentation(
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

    public void RevokeAppBindingsForDeletedThread(SessionThread thread)
    {
        if (appBindingService == null)
            return;

        var craftPath = Path.Combine(thread.WorkspacePath, ".craft");
        if (!Directory.Exists(craftPath))
            return;

        _ = appBindingService.RevokeThreadBindings(craftPath, thread.Id, "threadDeleted");
    }

    public IReadOnlyList<AppBindingWire> RevokeSocialAppBindingsForArchivedThread(SessionThread thread)
    {
        if (appBindingService == null)
            return [];

        var craftPath = Path.Combine(thread.WorkspacePath, ".craft");
        if (!Directory.Exists(craftPath))
            return [];

        return appBindingService.ListThreadBindings(craftPath, thread.Id)
            .Where(binding => binding.SocialTarget != null && binding.State != AppBindingStates.Revoked)
            .Select(binding => appBindingService.RevokeBinding(craftPath, thread.Id, binding.BindingId, "threadArchived"))
            .ToArray();
    }

    private static List<ThreadAppBindingSummaryWire> MapBindingSummaries(
        AppCatalogSnapshot catalog,
        IReadOnlyList<AppBindingWire> bindings) => bindings.Select(binding =>
    {
        var app = catalog.Entries.FirstOrDefault(entry =>
            string.Equals(entry.Descriptor.AppId, binding.AppId, StringComparison.Ordinal));
        return new ThreadAppBindingSummaryWire
        {
            ThreadId = binding.ThreadId,
            BindingId = binding.BindingId,
            AppId = binding.AppId,
            DisplayName = app?.Descriptor.DisplayName ?? binding.AppId,
            Icon = app?.Descriptor.Icon,
            State = binding.State,
            ConnectionState = AppConnectionStates.Connected,
            BindingKind = binding.SocialTarget == null ? "app" : "socialChannel",
            SocialTarget = binding.SocialTarget,
            AuthorityRevision = binding.AuthorityRevision,
            ApprovedCapabilityRevision = binding.ApprovedCapabilityRevision,
            CandidateCapabilityRevision = binding.CandidateCapabilityRevision,
            ApprovedTools = binding.ApprovedTools,
            PendingChanges = binding.PendingChanges,
            FailureReason = binding.FailureReason
        };
    }).ToList();

    private static ThreadOriginAppWire? ResolveOriginApp(
        AppCatalogSnapshot catalog,
        string? originChannel,
        string? channelContext)
    {
        _ = channelContext;
        if (string.IsNullOrWhiteSpace(originChannel))
            return null;
        var descriptor = catalog.Entries.Select(entry => entry.Descriptor).FirstOrDefault(app =>
            string.Equals(app.OriginChannel, originChannel, StringComparison.OrdinalIgnoreCase));
        return descriptor == null
            ? null
            : new ThreadOriginAppWire
            {
                AppId = descriptor.AppId,
                DisplayName = descriptor.DisplayName,
                Icon = descriptor.Icon
            };
    }

    public async Task<SessionWireThread> HydrateThreadGoalAsync(SessionWireThread wire, CancellationToken ct)
    {
        var goal = await TryGetGoalSnapshotAsync(wire.Id, ct);
        return goal is null ? wire : wire with { Goal = goal };
    }
}
