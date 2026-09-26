using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Hooks;
using DotCraft.Memory;
using DotCraft.Mcp;
using DotCraft.Plugins;
using DotCraft.Security;
using DotCraft.Logging;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Tracing;
using DotCraft.Workspaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using DotCraft.Sessions.Wire;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using McpServerOrigin = DotCraft.Mcp.McpServerOrigin;

namespace DotCraft.Sessions;

/// <summary>
/// Composite dictionary key that uniquely identifies a Turn across all Threads.
/// Turn IDs (e.g. <c>turn_001</c>) are only unique within a Thread; this struct
/// pairs them with the parent Thread ID so they can safely key concurrent dictionaries.
/// </summary>
internal readonly record struct TurnKey(string ThreadId, string TurnId);

internal sealed record GoalTurnSnapshot(
    string GoalId,
    DateTimeOffset StartedAt,
    DateTimeOffset LastAccountedAt,
    TokenUsageInfo AccountedUsage)
{
    public GoalTurnSnapshot WithAccounted(TokenUsageInfo usage, DateTimeOffset accountedAt) =>
        this with { AccountedUsage = usage, LastAccountedAt = accountedAt };
}

internal enum GoalBudgetLimitSteering
{
    Suppress,
    InjectIfNew
}

internal sealed class ThreadMaintenanceState(string kind) : IDisposable
{
    public string Kind { get; } = kind;

    public CancellationTokenSource Cancellation { get; } = new();

    public CancellationToken Token => Cancellation.Token;

    public void Cancel() => Cancellation.Cancel();

    public void Dispose() => Cancellation.Dispose();
}

internal sealed class ThreadMaintenanceRegistration(
    SessionService owner,
    string threadId,
    ThreadMaintenanceState state)
{
    private readonly object _completionLock = new();
    private Task? _completion;

    public string Kind => state.Kind;

    public CancellationToken Token => state.Token;

    public bool IsCancellationRequested => state.Token.IsCancellationRequested;

    public Task CompleteAsync()
    {
        lock (_completionLock)
            return _completion ??= owner.CompleteThreadMaintenanceAsync(threadId, state);
    }
}

/// <summary>
/// Session Core implementation. Manages Thread/Turn/Item lifecycle, orchestrates agent
/// execution, emits the structured event stream, and delegates persistence to SessionPersistenceService.
/// </summary>
public sealed partial class SessionService(
    AgentFactory agentFactory,
    ChatClientAgent? defaultAgent,
    SessionPersistenceService persistence,
    SessionGate sessionGate,
    HookRunner? hookRunner = null,
    TraceCollector? traceCollector = null,
    TokenUsageStore? tokenUsageStore = null,
    TimeSpan? approvalTimeout = null,
    ILogger<SessionService>? logger = null,
    ApprovalStore? approvalStore = null,
    IToolProfileRegistry? toolProfileRegistry = null,
    SessionStreamDebugLogger? sessionStreamDebugLogger = null,
    IBackgroundTerminalService? backgroundTerminalService = null,
    IAppConfigMonitor? appConfigMonitor = null,
    IEnumerable<IThreadPluginToolSourceProvider>? pluginToolSourceProviders = null,
    ThreadToolDispatchPolicyRegistry? toolDispatchPolicyRegistry = null,
    McpAppTransientContextStore? mcpAppTransientContextStore = null,
    IEnumerable<IThreadLifecycleObserver>? threadLifecycleObservers = null,
    IEnumerable<ISubAgentGuidanceProvider>? subAgentGuidanceProviders = null,
    DotCraftPaths? dotCraftPaths = null,
    ILoggerFactory? loggerFactory = null)
    : ISessionService, IThreadAgentRefreshService, IThreadToolDispatchService, IThreadToolSnapshotService, IThreadToolSnapshotChangeSource, IThreadMcpRuntimeService, IThreadForkToolBindingService, INativeSubAgentForkMaterializationService, IToolInvocationRecorder, ISubAgentSyntheticTurnService, ISubAgentThreadLifecycleService, ISubAgentCommunicationRuntimeProvider
{
    private sealed record PreparedContextTokenEstimate(
        IReadOnlyList<ChatMessage> History,
        PromptRequestSnapshot? RequestSnapshot,
        ContextTokenUsageEstimate Estimate);

    private sealed class ThreadLoadGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int ReferenceCount { get; set; }
    }

    private readonly TimeSpan _approvalTimeout = approvalTimeout ?? TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public event EventHandler<EffectiveToolSnapshotChangedEventArgs>? EffectiveToolSnapshotChanged;

    // In-memory state
    private readonly ThreadManager _runtimeRegistry = new();
    private readonly Lock _threadLoadGatesLock = new();
    private readonly Dictionary<string, ThreadLoadGate> _threadLoadGates = new(StringComparer.Ordinal);
    private WorktreeCoordinator? _worktreeCoordinator;
    private SubAgentSessionCoordinator? _subAgentSessionCoordinator;
    private ThreadIndexCoordinator? _threadIndexCoordinator;
    private ThreadCreationCoordinator? _threadCreationCoordinator;
    private ThreadLifecycleCoordinator? _threadLifecycleCoordinator;
    private ThreadRecoveryCoordinator? _threadRecoveryCoordinator;
    private ContributionLifecycleCoordinator? _contributionLifecycleCoordinator;
    private ThreadAccessCoordinator? _threadAccessCoordinator;
    private ThreadConfigurationCoordinator? _threadConfigurationCoordinator;
    private TurnControlCoordinator? _turnControlCoordinator;
    private ThreadGoalCoordinator? _threadGoalCoordinator;
    private ThreadQueueCoordinator? _threadQueueCoordinator;
    private MaintenanceCoordinator? _maintenanceCoordinator;
    private static readonly AsyncLocal<bool> SuppressGoalBroadcastContext = new();
    private readonly IAppConfigMonitor? _appConfigMonitor = appConfigMonitor;
    private readonly bool _hasExplicitDefaultAgent = defaultAgent != null;
    private ChatClientAgent? _defaultAgent = defaultAgent;
    private string DataPath => persistence.DataPath;
    private readonly ConcurrentDictionary<string, byte> _sessionStartHookThreads = new(StringComparer.Ordinal);

    /// <summary>The turns that reported a start to <see cref="ITurnLifecycleContributor"/>, so an end is reported exactly once and only for a turn the contributors have seen.</summary>
    private readonly ConcurrentDictionary<TurnKey, byte> _turnLifecycleStarted = new();
    private readonly SubAgentCommunicationRuntime _subAgentCommunicationRuntime = new();
    private readonly SessionApprovalScopeRegistry _sessionApprovalScopes = new();
    private volatile bool _forcePerThreadAgents;
    private readonly IReadOnlyList<IThreadLifecycleObserver> _threadLifecycleObservers =
        threadLifecycleObservers?.ToArray() ?? [];
    private readonly IReadOnlyList<ISubAgentGuidanceProvider> _subAgentGuidanceProviders =
        subAgentGuidanceProviders?.ToArray() ?? [];

    internal int ThreadLoadGateCount
    {
        get
        {
            lock (_threadLoadGatesLock)
                return _threadLoadGates.Count;
        }
    }

    SubAgentCommunicationRuntime ISubAgentCommunicationRuntimeProvider.CommunicationRuntime =>
        _subAgentCommunicationRuntime;

    bool IThreadForkToolBindingService.TryForkThreadToolBindings(
        string parentThreadId,
        string childThreadId)
    {
        var inherited = false;
        foreach (var source in agentFactory.ToolSources.OfType<IThreadForkToolBindingSource>())
            inherited |= source.TryForkThreadBinding(parentThreadId, childThreadId);
        return inherited;
    }

    async Task<bool> INativeSubAgentForkMaterializationService.MaterializeNativeSubAgentForkAsync(
        SessionThread parentThread,
        SessionThread childThread,
        IReadOnlyList<ChatMessage> parentModelHistory,
        CancellationToken ct)
    {
        if (childThread.Turns.Count == 0)
            return false;

        var forkHistory = BuildNativeSubAgentForkHistory(
            ResolveParentForkSource(parentThread.Id, parentModelHistory));
        if (forkHistory.Count == 0)
            return false;

        NativeSubAgentGuidance.Reconcile(
            childThread,
            forkHistory,
            ResolveThreadContextCarrier(childThread),
            _subAgentGuidanceProviders);

        AgentInstructionContextPages.TryForkStablePages(parentThread.Id, childThread.Id);

        await PersistThreadWithMaterializationAsync(childThread, ct);
        InheritWorldStateBaseline(parentThread.Id, childThread.Id, forkHistory);
        if (!childThread.Ephemeral)
        {
            var coveredThroughTurnId = childThread.Turns[^1].Id;
            var tokens = MessageTokenEstimator.Estimate(forkHistory);
            await persistence.AppendCompactionCheckpointAsync(
                childThread.Id,
                coveredThroughTurnId,
                forkHistory,
                trigger: "subagent_fork",
                mode: "partial",
                tokensBefore: tokens,
                tokensAfter: tokens,
                ct);

            var childWindow = GetOrCreateResponsesContextWindow(childThread.Id);
            var childIdentity = ThreadConversationIdentity.Create(
                childThread,
                childThread.Turns[^1],
                childWindow.CurrentWindowId,
                ProviderRequestKind.Turn);
            var childProviderState = new ProviderConversationState(childIdentity);
            using var childResponsesScope = ProviderRequestContextScope.Push(
                new ProviderRequestContext(childIdentity, ConversationState: childProviderState));
            await TryReplaceResponsesProviderHistoryAsync(
                childThread,
                forkHistory,
                ProviderHistoryReasons.Fork,
                ct);
        }

        return true;
    }

    /// <summary>
    /// Prefers the parent's last sampled request over its loaded model history. The snapshot is the
    /// exact message list the parent sent for the request that spawned this child, so inherited
    /// leading items keep the byte shape the provider already cached. The loaded history is a
    /// fallback: it lags by the in-flight turn and omits request-time context rendering.
    /// </summary>
    private IReadOnlyList<ChatMessage> ResolveParentForkSource(
        string parentThreadId,
        IReadOnlyList<ChatMessage> fallback)
    {
        var snapshot = TryGetLastPromptRequestSnapshot(parentThreadId);
        return snapshot is { Messages.Count: > 0 } ? snapshot.Messages : fallback;
    }

    /// <summary>
    /// Copies the leading stable items of a parent request and drops its transient work. Assistant
    /// commentary, reasoning, and tool traffic belong to the parent's own loop, so the child keeps
    /// only the ordered system, developer, and user items it can legitimately inherit.
    /// </summary>
    internal static List<ChatMessage> BuildNativeSubAgentForkHistory(
        IReadOnlyList<ChatMessage> parentModelHistory)
    {
        var developerRole = new ChatRole("developer");
        var result = new List<ChatMessage>(parentModelHistory.Count);
        foreach (var message in parentModelHistory)
        {
            if (message.Role == ChatRole.System
                || message.Role == developerRole
                || message.Role == ChatRole.User)
            {
                result.Add(message.Clone());
            }
        }

        return result;
    }

    private WorktreeCoordinator Worktrees => _worktreeCoordinator ??= new WorktreeCoordinator(this);

    private SubAgentSessionCoordinator SubAgents => _subAgentSessionCoordinator ??= new SubAgentSessionCoordinator(this);

    private ThreadIndexCoordinator ThreadIndex => _threadIndexCoordinator ??= new ThreadIndexCoordinator(this);

    private ThreadCreationCoordinator ThreadCreation => _threadCreationCoordinator ??= new ThreadCreationCoordinator(this);

    private ThreadLifecycleCoordinator ThreadLifecycle => _threadLifecycleCoordinator ??= new ThreadLifecycleCoordinator(this);

    private ThreadRecoveryCoordinator ThreadRecovery => _threadRecoveryCoordinator ??= new ThreadRecoveryCoordinator(this);

    private ContributionLifecycleCoordinator ContributionLifecycle =>
        _contributionLifecycleCoordinator ??= new ContributionLifecycleCoordinator(this);

    private ThreadAccessCoordinator ThreadAccess => _threadAccessCoordinator ??= new ThreadAccessCoordinator(this);

    private ThreadConfigurationCoordinator ThreadConfig =>
        _threadConfigurationCoordinator ??= new ThreadConfigurationCoordinator(this);

    private TurnControlCoordinator TurnControl => _turnControlCoordinator ??= new TurnControlCoordinator(this);

    private ThreadGoalCoordinator Goals => _threadGoalCoordinator ??= new ThreadGoalCoordinator(this);

    private ThreadQueueCoordinator ThreadQueue => _threadQueueCoordinator ??= new ThreadQueueCoordinator(this);

    private MaintenanceCoordinator Maintenance => _maintenanceCoordinator ??= new MaintenanceCoordinator(this);

    private SessionGate Gate => sessionGate;

    private SessionPersistenceService Persistence => persistence;

    private ILogger<SessionService>? Logger => logger;

    private TraceCollector? TraceCollector => traceCollector;

    private IBackgroundTerminalService? BackgroundTerminalService => backgroundTerminalService;

    private ThreadToolDispatchPolicyRegistry? ToolDispatchPolicyRegistry => toolDispatchPolicyRegistry;

    private McpAppTransientContextStore? McpAppTransientContexts => mcpAppTransientContextStore;

    private ChatClientAgent DefaultAgent =>
        _defaultAgent ??= agentFactory.CreateAgentForMode(AgentMode.Agent);

    private AgentFactory AgentFactory => agentFactory;

    internal int DebugRuntimeCount => _runtimeRegistry.Count;

    internal ThreadRuntime? DebugGetRuntime(string threadId) =>
        _runtimeRegistry.TryGetRuntime(threadId, out var runtime) ? runtime : null;

    private void ClearContextUsageAnchor(string threadId)
    {
        if (_runtimeRegistry.TryGetRuntime(threadId, out var runtime))
            runtime.ContextUsageAnchor = null;
    }

    private TurnExecutionState? TryGetTurnRuntime(TurnKey turnKey) =>
        _runtimeRegistry.TryGetRuntime(turnKey.ThreadId, out var runtime)
            && runtime.TryGetTurn(turnKey.TurnId, out var turnRuntime)
                ? turnRuntime
                : null;

    private TurnExecutionState? GetOrAddTurnRuntime(TurnKey turnKey) =>
        _runtimeRegistry.TryGetRuntime(turnKey.ThreadId, out var runtime)
            ? runtime.GetOrAddTurn(turnKey.TurnId)
            : null;

    /// <summary>
    /// Suppresses immediate Session Core goal broadcasts within the current async flow.
    /// AppServer uses this to preserve response-before-notification ordering for direct goal mutations.
    /// </summary>
    public static IDisposable SuppressGoalBroadcastNotifications()
    {
        var previous = SuppressGoalBroadcastContext.Value;
        SuppressGoalBroadcastContext.Value = true;
        return new GoalBroadcastSuppression(previous);
    }

    private static IDisposable AllowGoalBroadcastNotifications()
    {
        var previous = SuppressGoalBroadcastContext.Value;
        SuppressGoalBroadcastContext.Value = false;
        return new GoalBroadcastSuppression(previous);
    }

    /// <inheritdoc />
    public Action<SessionThread>? ThreadCreatedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<string>? ThreadDeletedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<SessionThread>? ThreadRenamedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<SessionThread>? ThreadUpdatedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<string, ThreadStatus, ThreadStatus>? ThreadStatusChangedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<string, SessionThreadRuntimeSignal, SessionTurn?>? ThreadRuntimeSignalForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<ThreadGoal, string?>? ThreadGoalUpdatedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<string>? ThreadGoalClearedForBroadcast { get; set; }

    private void PublishGoalUpdated(ThreadGoal goal, string? turnId)
    {
        if (!SuppressGoalBroadcastContext.Value)
            ThreadGoalUpdatedForBroadcast?.Invoke(goal, turnId);
    }

    private void PublishGoalCleared(string threadId)
    {
        if (!SuppressGoalBroadcastContext.Value)
            ThreadGoalClearedForBroadcast?.Invoke(threadId);
    }

    private sealed class GoalBroadcastSuppression(bool previous) : IDisposable
    {
        public void Dispose() => SuppressGoalBroadcastContext.Value = previous;
    }

    /// <summary>
    /// Optional hook invoked after a session-backed SubAgent edge is created or changes status.
    /// Hosts broadcast <c>subagent/graphChanged</c> so clients can refresh child thread metadata.
    /// </summary>
    public Action<string, string>? SubAgentGraphChangedForBroadcast { get; set; }

    private ApprovalPolicy ResolveApprovalPolicy(ApprovalPolicy threadPolicy)
    {
        if (threadPolicy != ApprovalPolicy.Default)
            return threadPolicy;

        return _appConfigMonitor?.Current.Permissions.DefaultApprovalPolicy ?? ApprovalPolicy.Default;
    }

    private TimeSpan ResolveApprovalTimeout(int? approvalTimeoutSeconds) =>
        approvalTimeoutSeconds.HasValue
            ? TimeSpan.FromSeconds(approvalTimeoutSeconds.Value)
            : _approvalTimeout;

    /// <inheritdoc />
    private Task RecordGoalUsageAsync(
        TurnKey turnKey,
        TokenUsageInfo latestTurnUsage,
        CancellationToken ct = default) =>
        Goals.RecordUsageAsync(turnKey, latestTurnUsage, ct);

    private async Task<ThreadGoal?> AccountGoalUsageAsync(
        TurnKey turnKey,
        TokenUsageInfo latestTurnUsage,
        string? notificationTurnId,
        GoalAccountingMode mode = GoalAccountingMode.ActiveOnly,
        CancellationToken ct = default,
        GoalBudgetLimitSteering budgetLimitSteering = GoalBudgetLimitSteering.Suppress) =>
        await Goals.AccountUsageAsync(turnKey, latestTurnUsage, notificationTurnId, mode, ct, budgetLimitSteering);

    private async Task AccountGoalToolCompletionAsync(
        TurnKey turnKey,
        string toolName,
        string callId,
        CancellationToken ct = default) =>
        await Goals.AccountToolCompletionAsync(turnKey, toolName, callId, ct);

    private async Task PauseActiveGoalForInterruptAsync(TurnKey turnKey, CancellationToken ct = default)
        => await Goals.PauseActiveForInterruptAsync(turnKey, ct);

    private async Task MarkActiveGoalBlockedForTurnErrorAsync(TurnKey turnKey, CancellationToken ct = default)
        => await Goals.MarkActiveBlockedForTurnErrorAsync(turnKey, ct);

    private async Task MaybeContinueGoalIfIdleAsync(string threadId, CancellationToken ct = default)
        => await Goals.MaybeContinueIfIdleAsync(threadId, ct);

    private static string NormalizeRequiredThreadId(string threadId)
    {
        var normalized = threadId.Trim();
        if (normalized.Length == 0)
            throw new ArgumentException("threadId is required.", nameof(threadId));
        return normalized;
    }

    private static bool IsLegacyGoalBudgetGuidanceInput(QueuedTurnInput input) =>
        string.Equals(input.TriggerKind, "goal", StringComparison.Ordinal)
        && (string.Equals(input.TriggerLabel, "Goal budget reached", StringComparison.Ordinal)
            || string.Equals(input.DisplayText, "Goal budget reached", StringComparison.Ordinal));

    // =========================================================================
    // Thread lifecycle
    // =========================================================================

    /// <inheritdoc/>
    public async Task<SessionThread> CreateThreadAsync(
        SessionIdentity identity,
        ThreadConfiguration? config = null,
        HistoryMode historyMode = HistoryMode.Server,
        string? threadId = null,
        string? displayName = null,
        CancellationToken ct = default,
        ThreadSource? source = null)
        => await ThreadCreation.CreateAsync(identity, config, historyMode, threadId, displayName, ct, source);

    /// <inheritdoc/>
    public async Task<ThreadResetResult> ResetConversationAsync(
        SessionIdentity identity,
        ThreadConfiguration? config = null,
        HistoryMode historyMode = HistoryMode.Server,
        string? displayName = null,
        CancellationToken ct = default)
        => await ThreadCreation.ResetConversationAsync(identity, config, historyMode, displayName, ct);

    /// <inheritdoc/>
    public async Task<SessionThread> ForkThreadAsync(
        string threadId,
        ThreadForkOptions? options = null,
        CancellationToken ct = default)
        => await ThreadCreation.ForkAsync(threadId, options, ct);

    /// <inheritdoc/>
    public async Task<WorktreeCreateAndForkResult> CreateWorktreeAndForkAsync(
        WorktreeCreateAndForkOptions options,
        CancellationToken ct = default) =>
        await Worktrees.CreateAndForkAsync(options, ct);

    /// <inheritdoc/>
    public async Task<WorktreeCreateAndStartResult> CreateWorktreeAndStartAsync(
        WorktreeCreateAndStartOptions options,
        CancellationToken ct = default) =>
        await Worktrees.CreateAndStartAsync(options, ct);

    /// <inheritdoc/>
    public async Task<WorktreeHandoffResult> HandoffThreadWorktreeAsync(
        WorktreeHandoffOptions options,
        CancellationToken ct = default) =>
        await Worktrees.HandoffAsync(options, ct);

    /// <inheritdoc/>
    public async Task<WorktreeEnsureResult> EnsureManagedWorktreeAsync(
        WorktreeEnsureOptions options,
        CancellationToken ct = default) =>
        await Worktrees.EnsureAsync(options, ct);

    /// <inheritdoc/>
    public async Task<SessionThread> ConfigureThreadExecutionWorkspaceAsync(
        ThreadExecutionWorkspaceOptions options,
        CancellationToken ct = default) =>
        await Worktrees.ConfigureExecutionWorkspaceAsync(options, ct);

    /// <inheritdoc/>
    public async Task RemoveManagedWorktreeAsync(
        WorktreeRemoveOptions options,
        CancellationToken ct = default) =>
        await Worktrees.RemoveAsync(options, ct);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ThreadWorktreeStatus>> ListWorktreesAsync(
        SessionIdentity? identity = null,
        CancellationToken ct = default) =>
        await Worktrees.ListAsync(identity, ct);

    /// <inheritdoc/>
    public async Task<ThreadWorktreeStatus> GetWorktreeStatusAsync(
        string threadId,
        CancellationToken ct = default) =>
        await Worktrees.GetStatusAsync(threadId, ct);

    /// <inheritdoc/>
    public async Task<SessionThread> ResumeThreadAsync(string threadId, CancellationToken ct = default)
        => await InvokeThreadCommandAsync(
            threadId,
            commandCt => ThreadLifecycle.ResumeAsync(threadId, commandCt),
            ct);

    /// <inheritdoc/>
    public async Task PauseThreadAsync(string threadId, CancellationToken ct = default)
        => await InvokeThreadCommandAsync(
            threadId,
            commandCt => ThreadLifecycle.PauseAsync(threadId, commandCt),
            ct);

    /// <inheritdoc/>
    public async Task<ThreadGoal?> GetThreadGoalAsync(string threadId, CancellationToken ct = default)
        => await Goals.GetAsync(threadId, ct);

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> GetItemWidgetStates(string threadId)
        => ThreadAccess.GetItemWidgetStates(threadId);

    /// <inheritdoc/>
    public void SetItemWidgetState(string threadId, string callId, string? widgetStateJson)
        => ThreadAccess.SetItemWidgetState(threadId, callId, widgetStateJson);

    /// <inheritdoc/>
    public async Task<ThreadGoal> SetThreadGoalAsync(
        string threadId,
        ThreadGoalUpdate update,
        GoalSetMode mode = GoalSetMode.UpsertOrUpdate,
        CancellationToken ct = default)
    {
        var goal = await InvokeThreadCommandAsync(
            threadId,
            commandCt => Goals.SetAsync(threadId, update, mode, commandCt),
            ct);

        if (goal.Status == ThreadGoalStatus.Active)
            _ = Goals.MaybeContinueIfIdleAsync(threadId, CancellationToken.None);
        return goal;
    }

    /// <inheritdoc/>
    public async Task<ThreadGoalClearResult> ClearThreadGoalAsync(string threadId, CancellationToken ct = default)
        => await InvokeThreadCommandAsync(
            threadId,
            commandCt => Goals.ClearAsync(threadId, commandCt),
            ct);

    /// <inheritdoc/>
    public async Task ArchiveThreadAsync(string threadId, CancellationToken ct = default)
    {
        await InvokeThreadCommandAsync(
            threadId,
            commandCt => ThreadLifecycle.ArchiveAsync(threadId, commandCt),
            ct);
        McpAppTransientContexts?.ClearThread(threadId);
    }

    /// <inheritdoc/>
    public async Task UnarchiveThreadAsync(string threadId, CancellationToken ct = default)
        => await InvokeThreadCommandAsync(
            threadId,
            commandCt => ThreadLifecycle.UnarchiveAsync(threadId, commandCt),
            ct);

    /// <inheritdoc/>
    public async Task DeleteThreadPermanentlyAsync(string threadId, CancellationToken ct = default)
    {
        var deleteOrder = await InvokeThreadCommandAsync(
            threadId,
            commandCt => ThreadLifecycle.PreparePermanentDeletionAsync(threadId, commandCt),
            ct);
        await ThreadLifecycle.ExecutePermanentDeletionAsync(deleteOrder, ct);
        _sessionApprovalScopes.RemoveThread(threadId);
        McpAppTransientContexts?.ClearThread(threadId);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(
        SessionIdentity identity,
        bool includeArchived = false,
        IReadOnlyList<string>? crossChannelOrigins = null,
        CancellationToken ct = default,
        bool includeSubAgents = false,
        ThreadDiscoveryScope scope = ThreadDiscoveryScope.Identity)
        => await ThreadIndex.FindAsync(identity, includeArchived, crossChannelOrigins, ct, includeSubAgents, scope);

    public async Task<int> CountWorkspaceThreadsAsync(string workspacePath, CancellationToken ct = default)
        => await ThreadIndex.CountWorkspaceThreadsAsync(workspacePath, ct);

    public Task<string?> FindThreadContentMatchAsync(string threadId, string searchTerm, CancellationToken ct = default)
        => Persistence.FindThreadContentMatchAsync(threadId, searchTerm, ct);

    public async Task UpsertThreadSpawnEdgeAsync(ThreadSpawnEdge edge, CancellationToken ct = default)
        => await SubAgents.UpsertThreadSpawnEdgeAsync(edge, ct);

    public async Task SetThreadSpawnEdgeStatusAsync(
        string parentThreadId,
        string childThreadId,
        string status,
        CancellationToken ct = default)
        => await SubAgents.SetThreadSpawnEdgeStatusAsync(parentThreadId, childThreadId, status, ct);

    public Task<IReadOnlyList<ThreadSpawnEdge>> ListSubAgentChildrenAsync(
        string parentThreadId,
        bool includeClosed = false,
        CancellationToken ct = default)
        => SubAgents.ListChildrenAsync(parentThreadId, includeClosed, ct);

    public async Task AddSubAgentMailboxEntryAsync(SubAgentMailboxEntry entry, CancellationToken ct = default)
        => await SubAgents.AddMailboxEntryAsync(entry, ct);

    public Task<IReadOnlyList<SubAgentMailboxEntry>> ListPendingSubAgentMailboxAsync(
        string rootThreadId,
        string targetAgentPath,
        CancellationToken ct = default)
        => SubAgents.ListPendingMailboxAsync(rootThreadId, targetAgentPath, ct);

    public async Task MarkSubAgentMailboxDeliveredAsync(
        string rootThreadId,
        IReadOnlyList<string> entryIds,
        DateTimeOffset deliveredAt,
        CancellationToken ct = default)
        => await SubAgents.MarkMailboxDeliveredAsync(rootThreadId, entryIds, deliveredAt, ct);

    public async Task<SessionTurn> StartSubAgentSyntheticTurnAsync(
        string threadId,
        IList<AIContent> content,
        string runtimeType,
        string? profileName,
        CancellationToken ct = default)
    {
        var capturedContent = content.ToList();
        return await InvokeThreadCommandAsync(
            threadId,
            commandCt => SubAgents.StartSyntheticTurnAsync(
                threadId,
                capturedContent,
                runtimeType,
                profileName,
                commandCt),
            ct);
    }

    public async Task<SessionTurn> CompleteSubAgentSyntheticTurnAsync(
        string threadId,
        string turnId,
        string text,
        bool isError,
        SubAgentTokenUsage? tokensUsed,
        CancellationToken ct = default)
        => await InvokeThreadCommandAsync(
            threadId,
            commandCt => SubAgents.CompleteSyntheticTurnAsync(
                threadId,
                turnId,
                text,
                isError,
                tokensUsed,
                commandCt),
            ct);

    public async Task<SessionTurn> CancelSubAgentSyntheticTurnAsync(
        string threadId,
        string turnId,
        string reason,
        CancellationToken ct = default)
        => await InvokeThreadCommandAsync(
            threadId,
            commandCt => SubAgents.CancelSyntheticTurnAsync(threadId, turnId, reason, commandCt),
            ct);

    public async Task ArchiveSubAgentTreeForCloseAsync(string childThreadId, CancellationToken ct = default)
        => await InvokeThreadCommandAsync(
            childThreadId,
            commandCt => ThreadLifecycle.ArchiveSubAgentTreeForCloseAsync(childThreadId, commandCt),
            ct);

    private static bool IsSubAgentThread(SessionThread thread) =>
        string.Equals(thread.Source.Kind, ThreadSourceKinds.SubAgent, StringComparison.OrdinalIgnoreCase)
        || string.Equals(thread.OriginChannel, SubAgentThreadOrigin.ChannelName, StringComparison.OrdinalIgnoreCase);

    private static AgentControlToolAccess ResolveAgentControlToolAccess(SessionThread thread)
    {
        var defaultAccess = IsSubAgentThread(thread)
            ? AgentControlToolAccess.Disabled
            : AgentControlToolAccess.Full;
        var config = thread.Configuration;
        if (config == null)
            return defaultAccess;

        var legacy = config.AgentControlToolAccess;
        var structured = ParseAgentControlToolAccess(config.ToolPolicy?.AgentControl);
        if (legacy == AgentControlToolAccess.Disabled || structured == AgentControlToolAccess.Disabled)
            return AgentControlToolAccess.Disabled;
        if (legacy == AgentControlToolAccess.AllowList || structured == AgentControlToolAccess.AllowList)
            return AgentControlToolAccess.AllowList;
        if (legacy == AgentControlToolAccess.Full || structured == AgentControlToolAccess.Full)
            return AgentControlToolAccess.Full;
        return defaultAccess;
    }

    private static AgentControlToolAccess? ParseAgentControlToolAccess(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim() switch
        {
            var v when string.Equals(v, "disabled", StringComparison.OrdinalIgnoreCase) => AgentControlToolAccess.Disabled,
            var v when string.Equals(v, "full", StringComparison.OrdinalIgnoreCase) => AgentControlToolAccess.Full,
            var v when string.Equals(v, "allowList", StringComparison.OrdinalIgnoreCase) => AgentControlToolAccess.AllowList,
            var v when string.Equals(v, "allow-list", StringComparison.OrdinalIgnoreCase) => AgentControlToolAccess.AllowList,
            _ => null
        };
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<SessionEvent> SubscribeThreadAsync(
        string threadId,
        bool replayRecent = false,
        CancellationToken ct = default)
        => ThreadAccess.Subscribe(threadId, replayRecent, ct);

    /// <inheritdoc/>
    public async Task<SessionThread> GetThreadAsync(string threadId, CancellationToken ct = default) =>
        await ThreadAccess.GetThreadAsync(threadId, ct);

    /// <inheritdoc/>
    public Task<ThreadHistorySnapshot> ReadThreadSnapshotAsync(
        string threadId,
        CancellationToken ct = default) =>
        ThreadAccess.ReadThreadSnapshotAsync(threadId, ct);

    /// <inheritdoc/>
    public Task<ThreadHistoryPage<SessionTurn>> ListThreadTurnsAsync(
        string threadId,
        ThreadHistoryCursor? cursor,
        int limit,
        ThreadHistorySortDirection direction,
        CancellationToken ct = default) =>
        ThreadAccess.ListThreadTurnsAsync(threadId, cursor, limit, direction, ct);

    /// <inheritdoc/>
    public Task<ThreadHistoryPage<ThreadHistoryItem>> ListThreadItemsAsync(
        string threadId,
        string? turnId,
        ThreadHistoryCursor? cursor,
        int limit,
        ThreadHistorySortDirection direction,
        CancellationToken ct = default) =>
        ThreadAccess.ListThreadItemsAsync(threadId, turnId, cursor, limit, direction, ct);

    /// <inheritdoc/>
    public async Task<SessionThread> EnsureThreadLoadedAsync(string threadId, CancellationToken ct = default)
        => await ThreadAccess.EnsureThreadLoadedAsync(threadId, ct);

    // =========================================================================
    // Turn orchestration
    // =========================================================================

    /// <inheritdoc/>
    public Task ResolveApprovalAsync(
        string threadId,
        string turnId,
        string requestId,
        SessionApprovalDecision decision,
        CancellationToken ct = default)
        => _runtimeRegistry.TryGetRuntime(threadId, out var runtime)
            ? runtime.Commands.InvokeAsync(
                _ => TurnControl.ResolveApproval(threadId, turnId, requestId, decision),
                ct)
            : Task.CompletedTask;

    /// <inheritdoc/>
    public Task ResolveUserInputRequestAsync(
        string threadId,
        string turnId,
        string requestId,
        RequestUserInputResponse response,
        CancellationToken ct = default)
        => _runtimeRegistry.TryGetRuntime(threadId, out var runtime)
            ? runtime.Commands.InvokeAsync(
                _ => TurnControl.ResolveUserInputRequest(threadId, turnId, requestId, response),
                ct)
            : Task.CompletedTask;

    /// <inheritdoc/>
    public Task CancelTurnAsync(string threadId, string turnId, CancellationToken ct = default)
        => InvokeThreadCommandAsync(
            threadId,
            _ => TurnControl.CancelTurn(threadId, turnId),
            ct);

    /// <inheritdoc/>
    public Task CancelThreadMaintenanceAsync(string threadId, CancellationToken ct = default)
        => _runtimeRegistry.TryGetRuntime(threadId, out var runtime)
            ? runtime.Commands.InvokeAsync(
                _ => TurnControl.CancelThreadMaintenance(threadId),
                ct)
            : Task.CompletedTask;

    /// <inheritdoc/>
    public async Task CleanBackgroundTerminalsAsync(string threadId, CancellationToken ct = default)
        => await TurnControl.CleanBackgroundTerminalsAsync(threadId, ct);

    /// <inheritdoc/>
    public async Task<QueuedTurnInput> EnqueueTurnInputAsync(
        string threadId,
        IList<AIContent> content,
        SenderContext? sender = null,
        CancellationToken ct = default,
        SessionInputSnapshot? inputSnapshot = null)
    {
        var capturedContent = content.ToList();
        var capturedSnapshot = CloneInputSnapshot(inputSnapshot);
        return await InvokeThreadCommandAsync(
            threadId,
            commandCt => ThreadQueue.EnqueueAsync(threadId, capturedContent, sender, commandCt, capturedSnapshot),
            ct);
    }

    /// <inheritdoc/>
    public async Task<string> SteerTurnAsync(
        string threadId,
        string expectedTurnId,
        IList<AIContent> content,
        SenderContext? sender = null,
        CancellationToken ct = default,
        SessionInputSnapshot? inputSnapshot = null)
    {
        var capturedContent = content.ToList();
        var capturedSnapshot = CloneInputSnapshot(inputSnapshot);
        return await InvokeThreadCommandAsync(
            threadId,
            commandCt => ThreadQueue.SteerAsync(
                threadId,
                expectedTurnId,
                capturedContent,
                sender,
                commandCt,
                capturedSnapshot),
            ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<QueuedTurnInput>> RemoveQueuedTurnInputAsync(
        string threadId,
        string queuedInputId,
        CancellationToken ct = default)
    {
        var result = await InvokeThreadCommandAsync(
            threadId,
            commandCt => ThreadQueue.RemoveAsync(threadId, queuedInputId, commandCt),
            ct);
        McpAppTransientContexts?.ClearQueuedInput(queuedInputId);
        return result;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<QueuedTurnInput>> ReorderQueuedTurnInputsAsync(
        string threadId,
        IReadOnlyList<string> orderedQueuedInputIds,
        CancellationToken ct = default)
    {
        var capturedQueuedInputIds = orderedQueuedInputIds.ToArray();
        return await InvokeThreadCommandAsync(
            threadId,
            commandCt => ThreadQueue.ReorderAsync(threadId, capturedQueuedInputIds, commandCt),
            ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<QueuedTurnInput>> UpdateQueuedTurnInputAsync(
        string threadId,
        string queuedInputId,
        string expectedTurnId,
        string status,
        CancellationToken ct = default)
        => await InvokeThreadCommandAsync(
            threadId,
            commandCt => ThreadQueue.UpdateAsync(threadId, queuedInputId, expectedTurnId, status, commandCt),
            ct);

    /// <inheritdoc/>
    public async Task<SessionThread> RollbackThreadAsync(string threadId, int numTurns, CancellationToken ct = default)
        => await InvokeThreadCommandAsync(
            threadId,
            commandCt => RollbackThreadCoreAsync(threadId, numTurns, commandCt),
            ct);

    private async Task<SessionThread> RollbackThreadCoreAsync(string threadId, int numTurns, CancellationToken ct)
    {
        if (numTurns <= 0)
            throw new ArgumentOutOfRangeException(nameof(numTurns), "numTurns must be >= 1.");

        var thread = await GetOrLoadThreadAsync(threadId, ct);
        if (thread.Status == ThreadStatus.Archived)
            throw new InvalidOperationException($"Thread '{threadId}' is archived and cannot be rolled back.");
        if (thread.Turns.Any(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput))
            throw new InvalidOperationException($"Thread '{threadId}' has a running Turn. Cancel it before rollback.");
        if (thread.Turns.Count < numTurns)
            throw new InvalidOperationException($"Thread '{threadId}' has only {thread.Turns.Count} turns; cannot roll back {numTurns}.");

        thread.Turns.RemoveRange(thread.Turns.Count - numTurns, numTurns);
        thread.LastActiveAt = DateTimeOffset.UtcNow;

        await persistence.RollbackThreadAsync(thread, numTurns, ct);
        traceCollector?.RecordThreadRollback(threadId, thread.Id, numTurns, thread.Turns.Count, thread.LastActiveAt);
        InvalidatePromptRequestSnapshot(threadId, "rollback");
        ResetWorldStateBaseline(threadId, "rollback");
        ClearContextUsageAnchor(threadId);
        agentFactory.RemoveTokenTracker(threadId);
        ForgetContextPages(threadId);
        var survivingSession = await TryLoadSurvivingModelHistoryAsync(threadId, ct);
        await SaveContextUsageFromSessionAsync(thread, survivingSession, ct);
        ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.HistoryRolledBack, null);
        return thread;
    }

    /// <inheritdoc/>
    public async Task<ThreadCompactResult> CompactThreadAsync(string threadId, CancellationToken ct = default)
    {
        var work = await InvokeThreadCommandAsync(
            threadId,
            _ => Task.FromResult(Maintenance.StartCompact(threadId, ct)),
            ct);
        return await work.ConfigureAwait(false);
    }

    // =========================================================================
    // Configuration
    // =========================================================================

    /// <inheritdoc/>
    public async Task SetThreadModeAsync(string threadId, string mode, CancellationToken ct = default)
        => await InvokeThreadCommandAsync(
            threadId,
            commandCt => ThreadConfig.SetModeAsync(threadId, mode, commandCt),
            ct);

    /// <inheritdoc/>
    public async Task UpdateThreadConfigurationAsync(
        string threadId,
        ThreadConfiguration config,
        CancellationToken ct = default)
    {
        var capturedConfig = ThreadConfigurationCloner.Clone(config);
        await InvokeThreadCommandAsync(
            threadId,
            commandCt => ThreadConfig.UpdateAsync(threadId, capturedConfig, commandCt),
            ct);
    }

    /// <inheritdoc/>
    public async Task<SessionThread> UpdateThreadWorkspaceAsync(
        string threadId,
        string? cwd,
        IReadOnlyList<string>? runtimeWorkspaceRoots,
        CancellationToken ct = default)
    {
        var capturedWorkspaceRoots = runtimeWorkspaceRoots?.ToArray();
        return await InvokeThreadCommandAsync(
            threadId,
            commandCt => ThreadConfig.UpdateWorkspaceAsync(
                threadId,
                cwd,
                capturedWorkspaceRoots,
                commandCt),
            ct);
    }

    /// <inheritdoc/>
    public async Task<SessionThread> UpdateThreadSourceControlTargetAsync(
        string threadId,
        ThreadSourceControlTarget? target,
        CancellationToken ct = default)
        => await InvokeThreadCommandAsync(
            threadId,
            commandCt => UpdateThreadSourceControlTargetCoreAsync(threadId, target, commandCt),
            ct);

    private async Task<SessionThread> UpdateThreadSourceControlTargetCoreAsync(
        string threadId,
        ThreadSourceControlTarget? target,
        CancellationToken ct)
    {
        using (await Gate.AcquireAsync(threadId, ct))
        {
            var thread = await GetOrLoadThreadAsync(threadId, ct);
            if (target == null)
            {
                ThreadSourceControlMetadata.ClearSourceControlTarget(thread.Metadata);
            }
            else if (string.Equals(target.Provider, "perforce", StringComparison.OrdinalIgnoreCase))
            {
                ThreadSourceControlMetadata.ApplyPerforceTarget(thread.Metadata, target.Changelist);
            }
            else
            {
                throw new ArgumentException("Only provider 'perforce' is supported for thread source-control targets.");
            }

            await PersistThreadWithMaterializationAsync(thread, ct);
            ThreadUpdatedForBroadcast?.Invoke(thread);
            return thread;
        }
    }

    /// <inheritdoc />
    public async Task RefreshThreadAgentAsync(string threadId, CancellationToken ct = default)
        => await InvokeThreadCommandAsync(
            threadId,
            commandCt => ThreadConfig.RefreshAgentAsync(threadId, commandCt),
            ct);

    /// <inheritdoc />
    public async Task<ToolExecutionResult> DispatchThreadToolAsync(
        string threadId,
        ToolName toolName,
        JsonObject arguments,
        string callId,
        ToolInvocationAudience audience = ToolInvocationAudience.Host,
        CancellationToken cancellationToken = default,
        ToolInvocationOrigin? origin = null)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            throw new ArgumentException("A thread identifier is required.", nameof(threadId));
        if (string.IsNullOrWhiteSpace(callId))
            throw new ArgumentException("A provider call identifier is required.", nameof(callId));

        var thread = await GetOrLoadThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        var snapshot = await GetEffectiveToolSnapshotAsync(threadId, cancellationToken).ConfigureAwait(false);

        return await agentFactory.DispatchToolAsync(
            snapshot,
            toolName,
            arguments,
            new ToolInvocationRequest(
                threadId,
                null,
                callId,
                audience,
                origin,
                ThreadWorkspaceResolver.Resolve(thread).Cwd),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<EffectiveToolSnapshot> GetEffectiveToolSnapshotAsync(
        string threadId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            throw new ArgumentException("A thread identifier is required.", nameof(threadId));

        var thread = await GetOrLoadThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        var runtime = _runtimeRegistry.SetThread(thread);
        var activeTurn = thread.Turns.LastOrDefault(static turn =>
            turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput);
        if (activeTurn != null
            && runtime.TryGetTurn(activeTurn.Id, out var turnRuntime)
            && turnRuntime.ToolSnapshot != null)
            return turnRuntime.ToolSnapshot;

        if (runtime.LatestToolSnapshot == null || runtime.ToolSnapshotDirty)
        {
            using (await AcquireThreadAgentLockAsync(threadId, cancellationToken).ConfigureAwait(false))
            {
                if (runtime.LatestToolSnapshot == null || runtime.ToolSnapshotDirty)
                    SetThreadAgent(threadId, await BuildAgentForThreadAsync(thread, cancellationToken).ConfigureAwait(false));
            }
        }

        return runtime.LatestToolSnapshot
            ?? throw new InvalidOperationException($"Thread '{threadId}' has no effective tool snapshot.");
    }

    /// <inheritdoc />
    public async Task<McpClientManager?> GetEffectiveMcpRuntimeAsync(
        string threadId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            throw new ArgumentException("A thread identifier is required.", nameof(threadId));
        var thread = await GetOrLoadThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        var runtime = _runtimeRegistry.SetThread(thread);
        if ((thread.Configuration?.McpServers is not null || runtime.GetBindingMcpServers().Count > 0)
            && (runtime.LatestToolSnapshot == null || runtime.ToolSnapshotDirty))
        {
            using (await AcquireThreadAgentLockAsync(threadId, cancellationToken).ConfigureAwait(false))
            {
                if (runtime.LatestToolSnapshot == null || runtime.ToolSnapshotDirty)
                    SetThreadAgent(threadId, await BuildAgentForThreadAsync(thread, cancellationToken).ConfigureAwait(false));
            }
        }

        return runtime.McpManager ?? agentFactory.RuntimeContext.McpClientManager;
    }

    /// <inheritdoc />
    public async Task SetBindingMcpServersAsync(
        string threadId,
        string bindingId,
        IReadOnlyList<McpServerConfig> servers,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
        ArgumentNullException.ThrowIfNull(servers);

        var normalized = servers.Select(server =>
        {
            var clone = server.Clone();
            clone.Origin = McpServerOrigin.Binding(bindingId, server.Origin.DeclaredName ?? server.Name);
            return clone;
        }).ToArray();
        await InvokeThreadCommandAsync(
            threadId,
            async commandCt =>
            {
                var thread = await GetOrLoadThreadAsync(threadId, commandCt).ConfigureAwait(false);
                var runtime = _runtimeRegistry.SetThread(thread);
                using (await AcquireThreadAgentLockAsync(threadId, commandCt).ConfigureAwait(false))
                {
                    runtime.SetBindingMcpServers(bindingId, normalized);
                    SetThreadAgent(threadId, await BuildAgentForThreadAsync(thread, commandCt).ConfigureAwait(false));
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RebuildAgentAndPersistThreadAsync(SessionThread thread, CancellationToken ct)
    {
        using (await AcquireThreadAgentLockAsync(thread.Id, ct))
        {
            SetThreadAgent(thread.Id, await BuildAgentForThreadAsync(thread, ct));
            ReleaseStableContextPages(thread.Id);
            await PersistThreadWithMaterializationAsync(thread, ct);
        }

        await ReleaseRetiredThreadToolResourcesIfIdleAsync(thread, ct, threadGateHeld: true);
    }

    private async ValueTask ReleaseRetiredThreadToolResourcesIfIdleAsync(
        SessionThread thread,
        CancellationToken ct,
        bool threadGateHeld = false)
    {
        if (threadGateHeld)
        {
            if (!HasLiveTurnWork(thread))
                await TryReleaseRetiredThreadToolResourcesAsync(thread.Id);
            return;
        }

        if (HasLiveTurnWork(thread))
            return;

        using (await Gate.AcquireAsync(thread.Id, ct))
        {
            if (!HasLiveTurnWork(thread))
                await TryReleaseRetiredThreadToolResourcesAsync(thread.Id);
        }
    }

    private async ValueTask TryReleaseRetiredThreadToolResourcesAsync(string threadId)
    {
        try
        {
            await AgentFactory.ReleaseRetiredThreadToolResourcesAsync(threadId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(
                ex,
                "Failed to release retired tool resources for thread {ThreadId}.",
                threadId);
        }
    }

    private static bool HasLiveTurnWork(SessionThread thread) =>
        thread.Turns.Any(turn => turn.Status is
            TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput);

    /// <inheritdoc />
    public void InvalidateThreadAgents()
        => ThreadConfig.InvalidateAgents();

    /// <inheritdoc/>
    public async Task RenameThreadAsync(string threadId, string displayName, CancellationToken ct = default)
        => await InvokeThreadCommandAsync(
            threadId,
            commandCt => ThreadLifecycle.RenameAsync(threadId, displayName, commandCt),
            ct);

    // =========================================================================
    // Private helpers
    // =========================================================================

    private CompactionPipeline GetCompactionPipelineForThread(string threadId) =>
        Maintenance.GetCompactionPipelineForThread(threadId);

    private CompactionPipeline GetCompactionPipelineForThread(SessionThread thread) =>
        Maintenance.GetCompactionPipelineForThread(thread);

    private CompactionPipeline GetCompactionPipelineForThread(string threadId, SessionThread? thread) =>
        Maintenance.GetCompactionPipelineForThread(threadId, thread);

    private CompactionCoordinator GetCompactionCoordinatorForThread(string threadId) =>
        Maintenance.GetCompactionCoordinatorForThread(threadId);

    private CompactionCoordinator GetCompactionCoordinatorForThread(SessionThread thread) =>
        Maintenance.GetCompactionCoordinatorForThread(thread);

    private CompactionCoordinator GetCompactionCoordinatorForThread(string threadId, SessionThread? thread) =>
        Maintenance.GetCompactionCoordinatorForThread(threadId, thread);

    private void ReleaseStableContextPages(string threadId)
    {
        agentFactory.RuntimeContext.ContextPageManager.ReleaseStablePages(threadId);
    }

    private void ForgetContextPages(string threadId)
    {
        agentFactory.RuntimeContext.ContextPageManager.ForgetThread(threadId);
    }

    private MemoryStore ResolveMemoryStore(ThreadConfiguration? config) =>
        MemoryScopes.Resolve(config?.MemoryScope, DataPath)
        ?? (string.IsNullOrEmpty(config?.WorkspaceOverride)
            ? agentFactory.RuntimeContext.MemoryStore
            : new MemoryStore(Path.Combine(config.WorkspaceOverride, Path.GetFileName(DataPath))));

    private ThreadLoadGate AddThreadLoadGateReference(string threadId)
    {
        lock (_threadLoadGatesLock)
        {
            if (!_threadLoadGates.TryGetValue(threadId, out var gate))
            {
                gate = new ThreadLoadGate();
                _threadLoadGates[threadId] = gate;
            }

            gate.ReferenceCount++;
            return gate;
        }
    }

    private void ReleaseThreadLoadGateReference(string threadId, ThreadLoadGate gate)
    {
        lock (_threadLoadGatesLock)
        {
            gate.ReferenceCount--;
            if (gate.ReferenceCount == 0)
            {
                _threadLoadGates.Remove(threadId);
                gate.Semaphore.Dispose();
            }
        }
    }

    private async Task<SessionThread> GetOrLoadThreadAsync(string threadId, CancellationToken ct)
    {
        if (_runtimeRegistry.IsPendingPermanentDeletion(threadId))
            throw new InvalidOperationException($"Thread '{threadId}' is being permanently deleted.");

        if (_runtimeRegistry.TryGetThread(threadId, out var cached))
            return cached;

        var loadGate = AddThreadLoadGateReference(threadId);
        var gateAcquired = false;
        try
        {
            await loadGate.Semaphore.WaitAsync(ct);
            gateAcquired = true;

            if (_runtimeRegistry.IsPendingPermanentDeletion(threadId))
                throw new InvalidOperationException($"Thread '{threadId}' is being permanently deleted.");

            if (_runtimeRegistry.TryGetThread(threadId, out cached))
                return cached;

            var thread = await persistence.LoadThreadAsync(threadId, ct)
                         ?? throw new KeyNotFoundException($"Thread '{threadId}' not found.");

            if (_runtimeRegistry.IsPendingPermanentDeletion(threadId))
                throw new InvalidOperationException($"Thread '{threadId}' is being permanently deleted.");

            var interruptedTurn = thread.Turns.SingleOrDefault(static turn =>
                turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput);
            var persistedModelHistory = await persistence.LoadModelHistoryAsync(threadId, ct);
            await ReconcilePersistedInputHistoryAsync(thread, persistedModelHistory, ct);
            if (interruptedTurn != null)
            {
                interruptedTurn.Status = TurnStatus.Cancelled;
                interruptedTurn.CompletedAt = DateTimeOffset.UtcNow;
                await new TurnCommitter(this, thread, interruptedTurn)
                {
                    Session = persistedModelHistory,
                    PersistedModelHistoryCount = persistedModelHistory.Count
                }.CommitAsync();
            }

            var runtime = _runtimeRegistry.SetThread(thread);
            runtime.Materialized = true;
            if (interruptedTurn != null)
            {
                runtime.Broker.PublishTurnCancelled(interruptedTurn, "interrupted");
                ThreadRuntimeSignalForBroadcast?.Invoke(
                    thread.Id,
                    SessionThreadRuntimeSignal.TurnCancelled,
                    interruptedTurn);
            }

            return thread;
        }
        finally
        {
            if (gateAcquired)
                loadGate.Semaphore.Release();
            ReleaseThreadLoadGateReference(threadId, loadGate);
        }
    }

    private async Task InvokeThreadCommandAsync(
        string threadId,
        Func<CancellationToken, Task> action,
        CancellationToken ct)
    {
        await GetOrLoadThreadAsync(threadId, ct).ConfigureAwait(false);
        if (!_runtimeRegistry.TryGetRuntime(threadId, out var runtime))
            throw new InvalidOperationException($"Thread '{threadId}' has no active runtime.");
        await runtime.Commands.InvokeAsync(action, ct).ConfigureAwait(false);
    }

    private async Task<TResult> InvokeThreadCommandAsync<TResult>(
        string threadId,
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken ct)
    {
        await GetOrLoadThreadAsync(threadId, ct).ConfigureAwait(false);
        if (!_runtimeRegistry.TryGetRuntime(threadId, out var runtime))
            throw new InvalidOperationException($"Thread '{threadId}' has no active runtime.");
        return await runtime.Commands.InvokeAsync(action, ct).ConfigureAwait(false);
    }

    private async Task<IDisposable> AcquireThreadQueueLockAsync(string threadId, CancellationToken ct)
    {
        if (!_runtimeRegistry.TryGetRuntime(threadId, out var runtime))
            return NoopReleaser.Instance;

        await runtime.QueueLock.WaitAsync(ct);
        return new SemaphoreSlimReleaser(runtime.QueueLock);
    }

    private async Task<IDisposable> AcquireThreadAgentLockAsync(string threadId, CancellationToken ct)
    {
        if (!_runtimeRegistry.TryGetRuntime(threadId, out var runtime))
            return NoopReleaser.Instance;

        await runtime.AgentLock.WaitAsync(ct);
        return new SemaphoreSlimReleaser(runtime.AgentLock);
    }

    private void PublishQueueUpdated(string threadId, IReadOnlyList<QueuedTurnInput> queuedInputs) =>
        GetOrCreateBroker(threadId).PublishThreadQueueUpdated(queuedInputs);

    internal async Task CompleteThreadMaintenanceAsync(string threadId, ThreadMaintenanceState state)
    {
        if (!_runtimeRegistry.TryGetRuntime(threadId, out var runtime))
        {
            state.Dispose();
            return;
        }

        try
        {
            await runtime.Commands.InvokeAsync(
                async _ =>
                {
                    Maintenance.CompleteThreadMaintenance(threadId, state);
                    await ThreadQueue.TryStartNextAsync(threadId, CancellationToken.None).ConfigureAwait(false);
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException)
        {
            state.Dispose();
        }
        catch (Exception ex)
        {
            state.Dispose();
            logger?.LogError(ex, "Failed to complete maintenance for thread {ThreadId}.", threadId);
        }
    }

    private void ThrowIfThreadMaintenanceActive(string threadId)
        => Maintenance.ThrowIfThreadMaintenanceActive(threadId);

    private sealed class SemaphoreSlimReleaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }

    private sealed class NoopReleaser : IDisposable
    {
        public static readonly NoopReleaser Instance = new();

        private NoopReleaser()
        {
        }

        public void Dispose()
        {
        }
    }

    /// <inheritdoc/>
    public async Task TryStartNextQueuedTurnAsync(string threadId, CancellationToken ct = default)
        => await InvokeThreadCommandAsync(
            threadId,
            commandCt => ThreadQueue.TryStartNextAsync(threadId, commandCt),
            ct);

    private void RecordTurnTokenUsage(SessionThread thread, SessionTurn turn)
    {
        if (tokenUsageStore == null || turn.TokenUsage == null || string.IsNullOrWhiteSpace(turn.OriginChannel))
            return;

        var initiator = turn.Initiator;
        var hasSubjectUser = !string.IsNullOrWhiteSpace(initiator?.UserId);
        var subjectKind = hasSubjectUser ? TokenUsageSubjectKinds.User : TokenUsageSubjectKinds.Thread;
        var subjectId = hasSubjectUser
            ? initiator!.UserId!
            : thread.Id;
        var subjectLabel = hasSubjectUser
            ? initiator!.UserName ?? initiator.UserId!
            : thread.DisplayName ?? thread.Id;
        var hasGroupContext = !string.IsNullOrWhiteSpace(initiator?.GroupId);
        var lastResponse = traceCollector?.GetLastResponseModel(thread.Id);
        var rootThreadId = thread.Source?.SubAgent?.RootThreadId;

        tokenUsageStore.Record(new TokenUsageRecord
        {
            Timestamp = turn.CompletedAt ?? DateTimeOffset.UtcNow,
            SourceId = turn.OriginChannel!,
            SourceMode = TokenUsageSourceModes.ServerManaged,
            SubjectKind = subjectKind,
            SubjectId = subjectId,
            SubjectLabel = subjectLabel,
            ContextKind = hasGroupContext ? TokenUsageContextKinds.Group : null,
            ContextId = hasGroupContext ? initiator!.GroupId : null,
            ContextLabel = hasGroupContext ? initiator!.GroupId : null,
            ThreadId = thread.Id,
            SessionKey = thread.Id,
            RootThreadId = string.IsNullOrWhiteSpace(rootThreadId) ? thread.Id : rootThreadId,
            Model = lastResponse?.ModelId,
            ReasoningEffort = lastResponse?.ReasoningEffort,
            Speed = (thread.Configuration?.Speed ?? InferenceSpeed.Standard).ToString().ToLowerInvariant(),
            InputTokens = turn.TokenUsage.InputTokens,
            OutputTokens = turn.TokenUsage.OutputTokens,
            CachedInputTokens = turn.TokenUsage.CachedInputTokens,
            CacheWriteInputTokens = turn.TokenUsage.CacheWriteInputTokens,
            ReasoningOutputTokens = turn.TokenUsage.ReasoningOutputTokens,
            LlmCallCount = turn.TokenUsage.LlmCallCount
        });
    }

    /// <summary>
    /// Records the wall-clock duration of a completed Turn into the trace store, feeding
    /// the workspace "longest task" aggregate (spec §27A.5). Keyed by the thread's main
    /// trace session (threadId), matching how token-usage trace events are recorded.
    /// </summary>
    private void RecordTurnDurationTrace(string threadId, SessionTurn turn)
    {
        if (traceCollector == null || turn.CompletedAt == null)
            return;

        var durationMs = (turn.CompletedAt.Value - turn.StartedAt).TotalMilliseconds;
        if (durationMs > 0)
            traceCollector.RecordTurnCompleted(threadId, durationMs);
    }

    private async Task PersistThreadStatusAsync(SessionThread thread, CancellationToken ct)
    {
        await PersistThreadIfMaterializedAsync(thread, ct);
    }

    private ThreadEventBroker GetOrCreateBroker(string threadId) =>
        _runtimeRegistry.TryGetRuntime(threadId, out var runtime)
            ? runtime.Broker
            : new ThreadEventBroker(threadId);

    /// <summary>
    /// Returns or creates an <see cref="AgentModeManager"/> for the given thread,
    /// ensuring the mode manager reflects the requested <paramref name="mode"/>.
    /// </summary>
    private AgentModeManager GetOrCreateModeManager(string threadId, AgentMode mode)
    {
        var runtime = _runtimeRegistry.TryGetRuntime(threadId, out var existingRuntime)
            ? existingRuntime
            : null;
        if (runtime == null)
        {
            var mm = new AgentModeManager();
            if (mode != AgentMode.Agent)
                mm.SwitchMode(mode);
            return mm;
        }

        runtime.ModeManager ??= new AgentModeManager();
        if (runtime.ModeManager.CurrentMode != mode)
            runtime.ModeManager.SwitchMode(mode);
        return runtime.ModeManager;
    }

    private static string ResolveApprovalSource(string? channelName) =>
        channelName?.ToLowerInvariant() ?? "console";

    private static void FailTurn(
        SessionTurn turn,
        SessionEventChannel channel,
        string errorMsg,
        ProviderFailure? providerFailure = null)
    {
        turn.Status = TurnStatus.Failed;
        turn.Error = errorMsg;
        turn.ProviderError = providerFailure is null
            ? null
            : ProviderFailureKinds.ToWireName(providerFailure.Kind);
        turn.HttpStatus = providerFailure?.HttpStatus;
        turn.CompletedAt = DateTimeOffset.UtcNow;
        channel.EmitTurnFailed(turn, errorMsg);
    }

    private static bool IsConfiguredNetworkTimeoutCancellation(OperationCanceledException ex)
    {
        var text = ex.ToString();
        return text.Contains("exceeded the configured timeout", StringComparison.OrdinalIgnoreCase)
            && (text.Contains("NetworkTimeout", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Network timeout", StringComparison.OrdinalIgnoreCase));
    }

    private async Task TryAppendCompactionCheckpointAsync(
        string threadId,
        string coveredThroughTurnId,
        IReadOnlyList<ChatMessage> replacementHistory,
        PendingCompactionCheckpoint checkpoint,
        CancellationToken ct)
        => await Maintenance.TryAppendCompactionCheckpointAsync(
            threadId,
            coveredThroughTurnId,
            replacementHistory,
            checkpoint,
            ct);

    private static bool TrySnapshotInMemoryHistory(
        List<ChatMessage> session,
        out IReadOnlyList<ChatMessage> history)
    {
        history = session.ToList();
        return true;
    }

    private static SessionTurn? ResolveNewestTerminalTurn(SessionThread thread) =>
        thread.Turns
            .Where(static candidate =>
                candidate.Status is TurnStatus.Completed or TurnStatus.Failed or TurnStatus.Cancelled)
            .OrderBy(static candidate => candidate.StartedAt)
            .ThenBy(static candidate => candidate.Id, StringComparer.Ordinal)
            .LastOrDefault();

    private static bool HasPendingQueuedInput(SessionThread thread) =>
        thread.QueuedInputs.Any(static input =>
            string.Equals(input.Status, "queued", StringComparison.OrdinalIgnoreCase)
            || string.Equals(input.Status, "guidancePending", StringComparison.OrdinalIgnoreCase));

    private string BuildEmptyProviderResponseMessage(
        string threadId,
        List<ChatMessage>? session,
        TokenTracker? tokenTracker,
        string fallbackMessage)
    {
        const string contextHint =
            " The current conversation appears to be near or over the model context window; compact or roll back history, then retry.";
        var message = string.IsNullOrWhiteSpace(fallbackMessage)
            ? "The model provider returned an empty streaming response before any assistant content, reasoning output, or tool call was received."
            : fallbackMessage;

        try
        {
            if (session is null
                || !TrySnapshotInMemoryHistory(session, out var history)
                || history.Count == 0)
            {
                return message;
            }

            var estimate = PrepareContextTokenEstimate(
                threadId,
                history,
                tokenTracker?.LastContextTokens ?? 0,
                TryGetLastPromptRequestSnapshot(threadId)).Estimate;
            var threshold = GetCompactionPipelineForThread(threadId).EvaluateThreshold(estimate.Tokens);
            return threshold.AboveWarning
                ? message + contextHint
                : message;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to attach context-window hint for empty provider response on thread {ThreadId}", threadId);
            return message;
        }
    }

    private bool IsMaterialized(string threadId) =>
        _runtimeRegistry.TryGetRuntime(threadId, out var runtime) && runtime.Materialized;

    private bool IsPendingPermanentDeletion(string threadId) =>
        _runtimeRegistry.IsPendingPermanentDeletion(threadId);

    private async Task PersistThreadWithMaterializationAsync(SessionThread thread, CancellationToken ct)
    {
        if (IsPendingPermanentDeletion(thread.Id))
            return;
        if (thread.Ephemeral)
        {
            SetMaterializedIfCurrent(thread, materialized: false);
            return;
        }

        await persistence.SaveThreadAsync(thread, ct);
        SetMaterializedIfCurrent(thread, materialized: true);
    }

    private async Task PersistTurnStateWithMaterializationAsync(
        SessionThread thread,
        SessionTurn turn,
        CancellationToken ct)
    {
        if (IsPendingPermanentDeletion(thread.Id))
            return;
        if (thread.Ephemeral)
        {
            SetMaterializedIfCurrent(thread, materialized: false);
            return;
        }

        await persistence.SaveTurnAsync(thread, turn, ct);
        SetMaterializedIfCurrent(thread, materialized: true);
    }

    private async Task PersistTurnCommitWithMaterializationAsync(
        SessionThread thread,
        SessionTurn turn,
        IReadOnlyList<ChatMessage> modelHistory,
        TurnCompactionHistory? compaction,
        CancellationToken ct)
    {
        if (IsPendingPermanentDeletion(thread.Id))
            return;
        if (thread.Ephemeral)
        {
            SetMaterializedIfCurrent(thread, materialized: false);
            return;
        }

        await persistence.CommitTurnAsync(
            new TurnPersistenceCommit(thread, turn, modelHistory, compaction),
            ct);
        SetMaterializedIfCurrent(thread, materialized: true);
    }

    private async Task PersistThreadIfMaterializedAsync(SessionThread thread, CancellationToken ct)
    {
        if (IsPendingPermanentDeletion(thread.Id))
            return;
        if (!IsMaterialized(thread.Id))
            return;
        await PersistThreadWithMaterializationAsync(thread, ct);
    }

    private void SetMaterializedIfCurrent(SessionThread thread, bool materialized)
    {
        if (IsPendingPermanentDeletion(thread.Id))
            return;
        if (_runtimeRegistry.TryGetRuntime(thread.Id, out var runtime)
            && ReferenceEquals(runtime.Thread, thread))
        {
            runtime.Materialized = materialized;
        }
    }

    private static SessionItem CreateErrorItem(
        SessionTurn turn, int seq, string message, string code, bool fatal)
    {
        return new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(seq),
            TurnId = turn.Id,
            Type = ItemType.Error,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ErrorPayload { Message = message, Code = code, Fatal = fatal }
        };
    }

    private static SessionItem CreateCompactionNoticeItem(
        SessionTurn turn,
        int seq,
        string trigger,
        CompactionStatus status) =>
        MaintenanceCoordinator.CreateCompactionNoticeItem(turn, seq, trigger, status);

    private static string CompactionOutcomeToWire(CompactionOutcome outcome) =>
        MaintenanceCoordinator.CompactionOutcomeToWire(outcome);

    private sealed record PendingCompactionCheckpoint(
        string Trigger,
        string Mode,
        long TokensBefore,
        long TokensAfter,
        IReadOnlyList<ChatMessage>? ReplacementHistory = null);

    private sealed class ContextCompactionFailedException(string message) : InvalidOperationException(message);
}
