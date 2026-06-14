using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Hooks;
using DotCraft.Memory;
using DotCraft.Dreams;
using DotCraft.Mcp;
using DotCraft.Plugins;
using DotCraft.Protocol.AppServer;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Logging;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Tracing;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Protocol;

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

internal sealed record AutoMemoryConsolidationWork(
    SessionThread Thread,
    SessionTurn Turn,
    IReadOnlyList<ChatMessage> History,
    PromptRequestSnapshot? RequestSnapshot,
    Func<int> NextItemSequence);

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
    ThreadMaintenanceState state) : IDisposable
{
    private int _disposed;

    public string Kind => state.Kind;

    public CancellationToken Token => state.Token;

    public bool IsCancellationRequested => state.Token.IsCancellationRequested;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            owner.CompleteThreadMaintenance(threadId, state);
    }
}

/// <summary>
/// Session Core implementation. Manages Thread/Turn/Item lifecycle, orchestrates agent
/// execution, emits the structured event stream, and delegates persistence to SessionPersistenceService.
/// </summary>
public sealed partial class SessionService(
    AgentFactory agentFactory,
    AIAgent defaultAgent,
    SessionPersistenceService persistence,
    SessionGate sessionGate,
    IChannelRuntimeToolProvider? channelRuntimeToolProvider = null,
    HookRunner? hookRunner = null,
    TraceCollector? traceCollector = null,
    TokenUsageStore? tokenUsageStore = null,
    TimeSpan? approvalTimeout = null,
    ILogger<SessionService>? logger = null,
    ApprovalStore? approvalStore = null,
    IToolProfileRegistry? toolProfileRegistry = null,
    SessionStreamDebugLogger? sessionStreamDebugLogger = null,
    IBackgroundTerminalService? backgroundTerminalService = null,
    IAppConfigMonitor? appConfigMonitor = null)
    : ISessionService, IThreadAgentRefreshService, ISubAgentSyntheticTurnService, ISubAgentThreadLifecycleService
{
    private readonly TimeSpan _approvalTimeout = approvalTimeout ?? TimeSpan.FromMinutes(5);

    // In-memory state
    private readonly ThreadRuntimeRegistry _runtimeRegistry = new();
    private WorktreeCoordinator? _worktreeCoordinator;
    private SubAgentSessionCoordinator? _subAgentSessionCoordinator;
    private ThreadIndexCoordinator? _threadIndexCoordinator;
    private ThreadCreationCoordinator? _threadCreationCoordinator;
    private ThreadLifecycleCoordinator? _threadLifecycleCoordinator;
    private ThreadAccessCoordinator? _threadAccessCoordinator;
    private ThreadConfigurationCoordinator? _threadConfigurationCoordinator;
    private TurnControlCoordinator? _turnControlCoordinator;
    private ThreadGoalCoordinator? _threadGoalCoordinator;
    private ThreadQueueCoordinator? _threadQueueCoordinator;
    private MaintenanceCoordinator? _maintenanceCoordinator;
    private static readonly AsyncLocal<bool> SuppressGoalBroadcastContext = new();
    private static readonly IReadOnlySet<string> EmptyPluginFunctionToolNames = new HashSet<string>(StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> EmptyDynamicToolNames = new HashSet<string>(StringComparer.Ordinal);
    private static readonly HttpClient QueuedInputHttpClient = new();
    private readonly IAppConfigMonitor? _appConfigMonitor = appConfigMonitor;
    private volatile bool _forcePerThreadAgents;

    private WorktreeCoordinator Worktrees => _worktreeCoordinator ??= new WorktreeCoordinator(this);

    private SubAgentSessionCoordinator SubAgents => _subAgentSessionCoordinator ??= new SubAgentSessionCoordinator(this);

    private ThreadIndexCoordinator ThreadIndex => _threadIndexCoordinator ??= new ThreadIndexCoordinator(this);

    private ThreadCreationCoordinator ThreadCreation => _threadCreationCoordinator ??= new ThreadCreationCoordinator(this);

    private ThreadLifecycleCoordinator ThreadLifecycle => _threadLifecycleCoordinator ??= new ThreadLifecycleCoordinator(this);

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

    private IChannelRuntimeToolProvider? ChannelRuntimeToolProvider => channelRuntimeToolProvider;

    private IBackgroundTerminalService? BackgroundTerminalService => backgroundTerminalService;

    private AIAgent DefaultAgent => defaultAgent;

    private AgentFactory AgentFactory => agentFactory;

    internal int DebugRuntimeCount => _runtimeRegistry.Count;

    internal ThreadRuntime? DebugGetRuntime(string threadId) =>
        _runtimeRegistry.TryGetRuntime(threadId, out var runtime) ? runtime : null;

    private AIAgent GetThreadAgentOrDefault(string threadId) =>
        _runtimeRegistry.TryGetRuntime(threadId, out var runtime) && runtime.Agent != null
            ? runtime.Agent
            : defaultAgent;

    private bool HasThreadAgent(string threadId) =>
        _runtimeRegistry.TryGetRuntime(threadId, out var runtime) && runtime.Agent != null;

    private void SetThreadAgent(string threadId, AIAgent agent)
    {
        if (_runtimeRegistry.TryGetRuntime(threadId, out var runtime))
            runtime.Agent = agent;
    }

    private void ClearThreadAgentCaches(string threadId)
    {
        if (!_runtimeRegistry.TryGetRuntime(threadId, out var runtime))
            return;

        runtime.Agent = null;
        runtime.ModeManager = null;
        runtime.CurrentTools = null;
        runtime.PluginFunctionToolNames = null;
        runtime.DynamicToolNames = null;
    }

    private void ClearAllThreadAgentCaches()
    {
        foreach (var runtime in _runtimeRegistry.Values)
        {
            runtime.Agent = null;
            runtime.CurrentTools = null;
        }
    }

    private void ClearContextUsageAnchor(string threadId)
    {
        if (_runtimeRegistry.TryGetRuntime(threadId, out var runtime))
            runtime.ContextUsageAnchor = null;
    }

    private TurnRuntime? TryGetTurnRuntime(TurnKey turnKey) =>
        _runtimeRegistry.TryGetRuntime(turnKey.ThreadId, out var runtime)
            && runtime.TryGetTurn(turnKey.TurnId, out var turnRuntime)
                ? turnRuntime
                : null;

    private TurnRuntime? GetOrAddTurnRuntime(TurnKey turnKey) =>
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
    public Action<string, SessionThreadRuntimeSignal>? ThreadRuntimeSignalForBroadcast { get; set; }

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

    /// <inheritdoc />
    public ContextUsageSnapshot? TryGetContextUsageSnapshot(string threadId)
        => ThreadAccess.TryGetContextUsageSnapshot(threadId);

    /// <inheritdoc />
    public ThreadSummaryRuntime GetThreadRuntimeSnapshot(SessionThread thread)
        => ThreadAccess.GetRuntimeSnapshot(thread);

    internal PromptRequestSnapshot? TryGetLastPromptRequestSnapshot(string threadId) =>
        _runtimeRegistry.TryGetRuntime(threadId, out var runtime)
            ? runtime.LastPromptRequest
            : null;

    internal PromptRequestSnapshot? TryGetValidLastPromptRequestSnapshot(
        string threadId,
        IReadOnlyList<ChatMessage> currentHistory,
        bool invalidateOnMismatch = true)
    {
        return TryGetValidLastPromptRequestSnapshot(
            threadId,
            currentHistory,
            out _,
            invalidateOnMismatch);
    }

    internal PromptRequestSnapshot? TryGetValidLastPromptRequestSnapshot(
        string threadId,
        IReadOnlyList<ChatMessage> currentHistory,
        out string? invalidReason,
        bool invalidateOnMismatch = true)
    {
        invalidReason = null;
        var snapshot = TryGetLastPromptRequestSnapshot(threadId);
        if (snapshot is null)
        {
            invalidReason = "snapshot_absent";
            return null;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.ThreadId)
            && !string.Equals(snapshot.ThreadId, threadId, StringComparison.Ordinal))
        {
            invalidReason = "thread_mismatch";
            if (invalidateOnMismatch)
                InvalidatePromptRequestSnapshot(threadId, invalidReason);
            return null;
        }

        if (snapshot.Messages.Count > currentHistory.Count)
        {
            invalidReason = "snapshot_longer_than_history";
            if (invalidateOnMismatch)
                InvalidatePromptRequestSnapshot(threadId, invalidReason);
            return null;
        }

        var currentPrefixFingerprint = MessageTokenEstimator.ComputePrefixFingerprint(
            currentHistory,
            snapshot.Messages.Count);
        if (!string.Equals(snapshot.MessageFingerprint, currentPrefixFingerprint, StringComparison.Ordinal))
        {
            invalidReason = "history_prefix_mismatch";
            if (invalidateOnMismatch)
                InvalidatePromptRequestSnapshot(threadId, invalidReason);
            return null;
        }

        return snapshot;
    }

    internal PromptRequestSnapshot? TryPrepareManualPromptRequestSnapshot(
        string threadId,
        IReadOnlyList<ChatMessage> currentHistory,
        int? estimatedInputTokens)
    {
        var validSnapshot = TryGetValidLastPromptRequestSnapshot(
            threadId,
            currentHistory,
            out var invalidReason,
            invalidateOnMismatch: false);
        if (validSnapshot is not null)
        {
            return validSnapshot with
            {
                TurnId = null,
                EstimatedInputTokens = estimatedInputTokens ?? validSnapshot.EstimatedInputTokens,
                SnapshotSource = PromptRequestSnapshotSources.ManualValid,
                SnapshotInvalidReason = null
            };
        }

        var template = TryGetLastPromptRequestSnapshot(threadId);
        if (template is null)
            return null;

        if (!string.IsNullOrWhiteSpace(template.ThreadId)
            && !string.Equals(template.ThreadId, threadId, StringComparison.Ordinal))
        {
            return null;
        }

        return RebasePromptRequestSnapshotForManualCompaction(
            template,
            threadId,
            currentHistory,
            estimatedInputTokens,
            invalidReason);
    }

    private static PromptRequestSnapshot RebasePromptRequestSnapshotForManualCompaction(
        PromptRequestSnapshot template,
        string threadId,
        IReadOnlyList<ChatMessage> currentHistory,
        int? estimatedInputTokens,
        string? invalidReason)
    {
        var capturedMessages = MessageGrouper
            .NormalizeFunctionCallArguments(currentHistory)
            .Select(message => message.Clone())
            .ToArray();

        return template with
        {
            Messages = capturedMessages,
            MessageFingerprint = MessageTokenEstimator.ComputePrefixFingerprint(capturedMessages, capturedMessages.Length),
            ThreadId = threadId,
            TurnId = null,
            EstimatedInputTokens = estimatedInputTokens ?? template.EstimatedInputTokens,
            SnapshotSource = PromptRequestSnapshotSources.ManualRebased,
            SnapshotInvalidReason = invalidReason
        };
    }

    private void InvalidatePromptRequestSnapshot(string threadId, string reason)
    {
        if (_runtimeRegistry.TryGetRuntime(threadId, out var runtime)
            && runtime.LastPromptRequest != null)
        {
            runtime.LastPromptRequest = null;
            logger?.LogDebug("Invalidated prompt request snapshot for thread {ThreadId}: {Reason}", threadId, reason);
        }
    }

    internal long? TryEstimateContextTokensFromAnchor(
        string threadId,
        IReadOnlyList<ChatMessage> modelVisibleHistory) =>
        _runtimeRegistry.TryGetRuntime(threadId, out var runtime) && runtime.ContextUsageAnchor is { } anchor
            ? ContextUsageTokenCounter.EstimateFromAnchor(anchor, modelVisibleHistory)
            : null;

    private ContextUsageAnchor? TryGetInMemoryContextUsageAnchor(string threadId) =>
        _runtimeRegistry.TryGetRuntime(threadId, out var runtime)
            ? runtime.ContextUsageAnchor
            : null;

    private ContextUsageSnapshot CreateContextUsageSnapshot(
        string threadId,
        long tokens,
        string? source = null,
        bool isEstimate = false)
    {
        var pipeline = GetCompactionPipelineForThread(threadId);
        var threshold = pipeline.EvaluateThreshold(tokens);

        return new ContextUsageSnapshot
        {
            Tokens = threshold.Tokens,
            ContextWindow = pipeline.EffectiveContextWindow,
            AutoCompactThreshold = threshold.AutoThreshold,
            WarningThreshold = threshold.WarningThreshold,
            ErrorThreshold = threshold.ErrorThreshold,
            PercentLeft = threshold.PercentLeft,
            Source = source,
            IsEstimate = isEstimate
        };
    }

    private async Task<ContextUsageSnapshot> SaveContextUsageSnapshotAsync(
        string threadId,
        long tokens,
        CancellationToken ct = default)
        => await SaveContextUsageSnapshotAsync(
            threadId,
            tokens,
            anchor: null,
            source: null,
            isEstimate: false,
            ct: ct);

    private async Task<ContextUsageSnapshot> SaveContextUsageSnapshotAsync(
        string threadId,
        long tokens,
        ContextUsageAnchor? anchor,
        CancellationToken ct = default)
        => await SaveContextUsageSnapshotAsync(
            threadId,
            tokens,
            anchor,
            source: null,
            isEstimate: false,
            ct: ct);

    private async Task<ContextUsageSnapshot> SaveContextUsageSnapshotAsync(
        string threadId,
        long tokens,
        string? source,
        bool isEstimate,
        CancellationToken ct = default)
        => await SaveContextUsageSnapshotAsync(
            threadId,
            tokens,
            anchor: null,
            source: source,
            isEstimate: isEstimate,
            ct: ct);

    private async Task<ContextUsageSnapshot> SaveContextUsageSnapshotAsync(
        string threadId,
        long tokens,
        ContextUsageAnchor? anchor,
        string? source,
        bool isEstimate,
        CancellationToken ct = default)
    {
        var normalizedTokens = Math.Max(0, tokens);
        if (anchor is not null)
        {
            var normalizedAnchor = anchor with { Tokens = Math.Max(0, anchor.Tokens) };
            await persistence.SaveContextUsageAnchorAsync(
                threadId,
                normalizedTokens,
                normalizedAnchor,
                source,
                isEstimate,
                ct);
            if (_runtimeRegistry.TryGetRuntime(threadId, out var runtime))
                runtime.ContextUsageAnchor = normalizedAnchor;
        }
        else
        {
            await persistence.SaveContextUsageTokensAsync(threadId, normalizedTokens, source, isEstimate, ct);
        }

        return CreateContextUsageSnapshot(threadId, normalizedTokens, source, isEstimate);
    }

    private ContextUsageAnchor? UpdateContextUsageAnchor(string threadId, long anchorTokens)
    {
        var snapshot = TryGetLastPromptRequestSnapshot(threadId);
        var anchorMessageCount = snapshot?.Messages.Count;
        if (snapshot is null || anchorMessageCount is not > 0)
            return null;

        var normalizedTokens = Math.Max(0, anchorTokens);
        var fingerprint = MessageTokenEstimator.ComputePrefixFingerprint(
            snapshot.Messages,
            anchorMessageCount.Value);
        var runtime = _runtimeRegistry.TryGetRuntime(threadId, out var existingRuntime)
            ? existingRuntime
            : null;
        var anchor = new ContextUsageAnchor(
            normalizedTokens,
            anchorMessageCount.Value,
            fingerprint,
            snapshot.RequestFingerprint,
            snapshot.ContextUsageFingerprint,
            snapshot.BaseInstructionsTokenEstimate,
            ContextUsageAnchorBoundary.Request);
        if (runtime != null)
            runtime.ContextUsageAnchor = anchor;
        return anchor;
    }

    private ContextTokenUsageEstimate EstimateContextTokens(
        string threadId,
        IReadOnlyList<ChatMessage> modelVisibleHistory,
        long latestContextTokens,
        PromptRequestSnapshot? requestSnapshot = null)
    {
        var persistedSnapshot = persistence.LoadContextUsageSnapshot(threadId);
        var persistedAnchor = persistence.LoadContextUsageAnchor(threadId);
        return ContextTokenUsageEstimator.Estimate(
            modelVisibleHistory,
            TryGetInMemoryContextUsageAnchor(threadId),
            persistedAnchor,
            latestContextTokens,
            persistedSnapshot?.Tokens,
            requestSnapshot?.RequestFingerprint,
            requestSnapshot?.ContextUsageFingerprint,
            requestSnapshot?.BaseInstructionsTokenEstimate,
            persistedSnapshot?.Source,
            persistedSnapshot?.IsEstimate ?? false);
    }

    private bool GoalsEnabled => Goals.Enabled;

    private async Task<ThreadGoal?> AccountGoalUsageAsync(
        TurnKey turnKey,
        TokenUsageInfo latestTurnUsage,
        string? notificationTurnId,
        CancellationToken ct = default) =>
        await Goals.AccountUsageAsync(turnKey, latestTurnUsage, notificationTurnId, ct);

    private async Task PauseActiveGoalForInterruptAsync(TurnKey turnKey, CancellationToken ct = default)
        => await Goals.PauseActiveForInterruptAsync(turnKey, ct);

    private async Task MaybeContinueGoalIfIdleAsync(string threadId, CancellationToken ct = default)
        => await Goals.MaybeContinueIfIdleAsync(threadId, ct);

    private static string NormalizeRequiredThreadId(string threadId)
    {
        var normalized = threadId.Trim();
        if (normalized.Length == 0)
            throw new ArgumentException("threadId is required.", nameof(threadId));
        return normalized;
    }

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

    private ThreadConfiguration CaptureThreadConfigurationForNewThread(ThreadConfiguration? source)
    {
        var captured = source == null
            ? new ThreadConfiguration()
            : CloneThreadConfiguration(source);

        if (string.IsNullOrWhiteSpace(captured.Model))
        {
            var currentConfig = _appConfigMonitor?.Current ?? agentFactory.ToolProviderContext.Config;
            var runtime = agentFactory.ToolProviderContext.ChatClientRegistry
                .ResolveMainRuntime(currentConfig, captured.ProviderId);
            captured.ProviderId = runtime.ProviderId;
            captured.Model = runtime.Model;
        }
        else
        {
            var currentConfig = _appConfigMonitor?.Current ?? agentFactory.ToolProviderContext.Config;
            var runtime = agentFactory.ToolProviderContext.ChatClientRegistry
                .ResolveMainRuntime(currentConfig, captured.ProviderId, captured.Model);
            captured.ProviderId = runtime.ProviderId;
            captured.Model = captured.Model.Trim();
        }

        captured.Reasoning ??= CloneReasoningConfig(
            (_appConfigMonitor?.Current ?? agentFactory.ToolProviderContext.Config).Reasoning);

        return captured;
    }

    private static ThreadConfiguration CloneThreadConfiguration(ThreadConfiguration source) => new()
    {
        AgentProfileId = source.AgentProfileId,
        AgentProfileSource = source.AgentProfileSource,
        AgentProfileFingerprint = source.AgentProfileFingerprint,
        AgentBuilderTargetId = source.AgentBuilderTargetId,
        AgentBuilderTargetSource = source.AgentBuilderTargetSource,
        McpServers = source.McpServers == null ? null : [.. source.McpServers],
        Mode = source.Mode,
        Extensions = source.Extensions == null ? null : [.. source.Extensions],
        CustomTools = source.CustomTools == null ? null : [.. source.CustomTools],
        ProviderId = source.ProviderId,
        Model = source.Model,
        Reasoning = CloneNullableReasoningConfig(source.Reasoning),
        WorkspaceOverride = source.WorkspaceOverride,
        ExecutionWorkspaceOverride = source.ExecutionWorkspaceOverride,
        ToolProfile = source.ToolProfile,
        UseToolProfileOnly = source.UseToolProfileOnly,
        AgentInstructions = source.AgentInstructions,
        ToolAllowList = source.ToolAllowList == null ? null : [.. source.ToolAllowList],
        ToolDenyList = source.ToolDenyList == null ? null : [.. source.ToolDenyList],
        ToolPolicy = CloneToolPolicy(source.ToolPolicy),
        McpPolicy = CloneMcpPolicy(source.McpPolicy),
        PluginPolicy = ClonePluginPolicy(source.PluginPolicy),
        SkillsPolicy = CloneSkillsPolicy(source.SkillsPolicy),
        TeamsPolicy = CloneTeamsPolicy(source.TeamsPolicy),
        AgentControlToolAccess = source.AgentControlToolAccess,
        AllowedAgentControlTools = source.AllowedAgentControlTools == null ? null : [.. source.AllowedAgentControlTools],
        PromptProfile = source.PromptProfile,
        RoleInstructions = source.RoleInstructions,
        OverrideBasePrompt = source.OverrideBasePrompt,
        ApprovalPolicy = source.ApprovalPolicy,
        AutomationTaskDirectory = source.AutomationTaskDirectory,
        RequireApprovalOutsideWorkspace = source.RequireApprovalOutsideWorkspace
    };

    private static AppConfig.ReasoningConfig CloneReasoningConfig(AppConfig.ReasoningConfig source) => new()
    {
        Enabled = source.Enabled,
        Effort = source.Effort,
        Output = source.Output
    };

    private static ThreadToolPolicy? CloneToolPolicy(ThreadToolPolicy? source) =>
        source == null
            ? null
            : new ThreadToolPolicy
            {
                Allow = source.Allow == null ? null : [.. source.Allow],
                Deny = source.Deny == null ? null : [.. source.Deny],
                AgentControl = source.AgentControl,
                AllowedAgentControlTools = source.AllowedAgentControlTools == null ? null : [.. source.AllowedAgentControlTools]
            };

    private static ThreadMcpPolicy? CloneMcpPolicy(ThreadMcpPolicy? source) =>
        source == null
            ? null
            : new ThreadMcpPolicy
            {
                Servers = source.Servers == null ? null : [.. source.Servers],
                Tools = CloneNamePolicy(source.Tools)
            };

    private static ThreadPluginPolicy? ClonePluginPolicy(ThreadPluginPolicy? source) =>
        source == null
            ? null
            : new ThreadPluginPolicy
            {
                Allow = source.Allow == null ? null : [.. source.Allow],
                Deny = source.Deny == null ? null : [.. source.Deny]
            };

    private static ThreadSkillsPolicy? CloneSkillsPolicy(ThreadSkillsPolicy? source) =>
        source == null
            ? null
            : new ThreadSkillsPolicy
            {
                Preload = source.Preload == null ? null : [.. source.Preload],
                Allow = source.Allow == null ? null : [.. source.Allow],
                Deny = source.Deny == null ? null : [.. source.Deny],
                AllowManage = source.AllowManage
            };

    private static ThreadTeamsPolicy? CloneTeamsPolicy(ThreadTeamsPolicy? source) =>
        source == null
            ? null
            : new ThreadTeamsPolicy
            {
                ReservedTools = source.ReservedTools
            };

    private static ThreadNamePolicy? CloneNamePolicy(ThreadNamePolicy? source) =>
        source == null
            ? null
            : new ThreadNamePolicy
            {
                Allow = source.Allow == null ? null : [.. source.Allow],
                Deny = source.Deny == null ? null : [.. source.Deny]
            };

    private static AppConfig.ReasoningConfig? CloneNullableReasoningConfig(AppConfig.ReasoningConfig? source) =>
        source == null ? null : CloneReasoningConfig(source);

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
        => await ThreadLifecycle.ResumeAsync(threadId, ct);

    /// <inheritdoc/>
    public async Task PauseThreadAsync(string threadId, CancellationToken ct = default)
        => await ThreadLifecycle.PauseAsync(threadId, ct);

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
        => await Goals.SetAsync(threadId, update, mode, ct);

    /// <inheritdoc/>
    public async Task<ThreadGoalClearResult> ClearThreadGoalAsync(string threadId, CancellationToken ct = default)
        => await Goals.ClearAsync(threadId, ct);

    /// <inheritdoc/>
    public async Task ArchiveThreadAsync(string threadId, CancellationToken ct = default)
        => await ThreadLifecycle.ArchiveAsync(threadId, ct);

    /// <inheritdoc/>
    public async Task UnarchiveThreadAsync(string threadId, CancellationToken ct = default)
        => await ThreadLifecycle.UnarchiveAsync(threadId, ct);

    /// <inheritdoc/>
    public async Task DeleteThreadPermanentlyAsync(string threadId, CancellationToken ct = default)
        => await ThreadLifecycle.DeletePermanentlyAsync(threadId, ct);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(
        SessionIdentity identity,
        bool includeArchived = false,
        IReadOnlyList<string>? crossChannelOrigins = null,
        CancellationToken ct = default,
        bool includeSubAgents = false)
        => await ThreadIndex.FindAsync(identity, includeArchived, crossChannelOrigins, ct, includeSubAgents);

    public async Task<int> CountWorkspaceThreadsAsync(string workspacePath, CancellationToken ct = default)
        => await ThreadIndex.CountWorkspaceThreadsAsync(workspacePath, ct);

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
        => await SubAgents.StartSyntheticTurnAsync(threadId, content, runtimeType, profileName, ct);

    public async Task<SessionTurn> CompleteSubAgentSyntheticTurnAsync(
        string threadId,
        string turnId,
        string text,
        bool isError,
        SubAgentTokenUsage? tokensUsed,
        CancellationToken ct = default)
        => await SubAgents.CompleteSyntheticTurnAsync(threadId, turnId, text, isError, tokensUsed, ct);

    public async Task<SessionTurn> CancelSubAgentSyntheticTurnAsync(
        string threadId,
        string turnId,
        string reason,
        CancellationToken ct = default)
        => await SubAgents.CancelSyntheticTurnAsync(threadId, turnId, reason, ct);

    public async Task ArchiveSubAgentTreeForCloseAsync(string childThreadId, CancellationToken ct = default)
        => await ThreadLifecycle.ArchiveSubAgentTreeForCloseAsync(childThreadId, ct);

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
    public async Task<SessionThread> EnsureThreadLoadedAsync(string threadId, CancellationToken ct = default)
        => await ThreadAccess.EnsureThreadLoadedAsync(threadId, ct);

    // =========================================================================
    // Turn orchestration
    // =========================================================================

    /// <inheritdoc/>
    public IAsyncEnumerable<SessionEvent> SubmitInputAsync(
        string threadId,
        IList<AIContent> content,
        SenderContext? sender = null,
        ChatMessage[]? messages = null,
        CancellationToken ct = default,
        SessionInputSnapshot? inputSnapshot = null)
    {
        // This method returns immediately; execution happens in a background Task.
        // We use a SessionEventChannel to bridge the background task to the caller.
        var channel = StartTurnAsync(threadId, content, sender, messages, inputSnapshot, ct);
        return channel.ReadAllAsync(ct);
    }

    private SessionEventChannel StartTurnAsync(
        string threadId,
        IList<AIContent> content,
        SenderContext? sender,
        ChatMessage[]? messages,
        SessionInputSnapshot? inputSnapshot,
        CancellationToken callerCt)
    {
        // Step 1: Validate synchronously before starting the background Task
        if (!_runtimeRegistry.TryGetThread(threadId, out var thread))
            throw new KeyNotFoundException($"Thread '{threadId}' not found. Call CreateThreadAsync or ResumeThreadAsync first.");

        if (thread.Status != ThreadStatus.Active)
            throw new InvalidOperationException($"Thread '{threadId}' is not Active (current status: {thread.Status}). Cannot submit input.");

        if (thread.Turns.Any(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput))
            throw new InvalidOperationException($"Thread '{threadId}' already has a running Turn. Wait for it to complete or cancel it first.");

        ThrowIfThreadMaintenanceActive(threadId);

        if (thread.HistoryMode == HistoryMode.Client && messages is not { Length: > 0 })
            throw new InvalidOperationException($"Thread '{threadId}' requires client-managed history, but no messages were provided.");

        if (thread.HistoryMode == HistoryMode.Server && messages is { Length: > 0 })
            throw new InvalidOperationException($"Thread '{threadId}' uses server-managed history and does not accept client-supplied messages.");

        var channelInfo = ChannelSessionScope.Current;
        var turnOriginChannel = channelInfo?.Channel ?? thread.OriginChannel;
        var turnChannelContext = channelInfo?.DefaultDeliveryTarget ?? thread.ChannelContext;
        var triggerInfo = TurnTriggerScope.Current;
        var lastActivityBeforeTurn = thread.LastActiveAt;

        // Step 2: Create Turn and UserMessage Item
        var turnSeq = thread.Turns.Count + 1;
        var turn = new SessionTurn
        {
            Id = SessionIdGenerator.NewTurnId(turnSeq),
            ThreadId = threadId,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            OriginChannel = turnOriginChannel,
            Initiator = new TurnInitiatorContext
            {
                ChannelName = turnOriginChannel,
                UserId = sender?.SenderId ?? channelInfo?.UserId ?? thread.UserId,
                UserName = sender?.SenderName,
                UserRole = sender?.SenderRole,
                ChannelContext = turnChannelContext,
                GroupId = sender?.GroupId ?? channelInfo?.GroupId
            }
        };

        var itemSeq = 0;

        // Extract plain text from content parts for display and persistence
        var text = inputSnapshot?.DisplayText
            ?? string.Concat(content.OfType<TextContent>().Select(t => t.Text));
        var images = ExtractUserMessageImages(content);
        var currentSubAgentSource = thread.Source.SubAgent;

        var userItem = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(NextItemSeq()),
            TurnId = turn.Id,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
                Payload = new UserMessagePayload
            {
                Text = text,
                DeliveryMode = inputSnapshot?.DeliveryMode,
                NativeInputParts = inputSnapshot?.NativeInputParts,
                MaterializedInputParts = inputSnapshot?.MaterializedInputParts,
                SenderId = sender?.SenderId,
                SenderName = sender?.SenderName,
                SenderRole = sender?.SenderRole,
                ChannelName = turnOriginChannel,
                ChannelContext = turnChannelContext,
                GroupId = sender?.GroupId ?? channelInfo?.GroupId,
                Images = images.Count > 0 ? images : null,
                TriggerKind = triggerInfo?.Kind,
                TriggerLabel = triggerInfo?.Label,
                TriggerRefId = triggerInfo?.RefId
            }
        };

        turn.Input = userItem;
        turn.Items.Add(userItem);
        thread.Turns.Add(turn);
        thread.LastActiveAt = DateTimeOffset.UtcNow;

        // Set a display name from the first user message so the session list shows a preview.
        var setTitleFromFirstUserMessage = false;
        if (string.IsNullOrEmpty(thread.DisplayName))
        {
            thread.DisplayName = text.Length > 50 ? text[..50] + "..." : text;
            setTitleFromFirstUserMessage = true;
        }

        if (setTitleFromFirstUserMessage)
            ThreadRenamedForBroadcast?.Invoke(thread);

        // Step 3: Create event channel
        var broker = GetOrCreateBroker(threadId);
        var eventChannel = broker.CreateTurnChannel(turn.Id, LogStreamDebugSessionEvent);

        void LogStreamDebugSessionEvent(SessionEvent evt)
        {
            if (sessionStreamDebugLogger == null || evt.EventType != SessionEventType.ItemDelta)
                return;
            if (!sessionStreamDebugLogger.ShouldCapture(evt.ThreadId, evt.TurnId))
                return;

            if (evt.DeltaPayload is { } agentDelta)
            {
                sessionStreamDebugLogger.Log(
                    "session_event_delta",
                    evt.ThreadId,
                    evt.TurnId,
                    new
                    {
                        itemId = evt.ItemId,
                        deltaKind = agentDelta.DeltaKind,
                        deltaChars = agentDelta.TextDelta.Length,
                        deltaText = sessionStreamDebugLogger.IncludeFullText ? agentDelta.TextDelta : null
                    });
            }
        }

        // Step 4: Emit initial events synchronously so the caller sees them before awaiting
        eventChannel.EmitTurnStarted(turn);
        ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.TurnStarted);
        eventChannel.EmitItemStarted(userItem);
        eventChannel.EmitItemCompleted(userItem);

        // Step 5: Run execution in background
        var turnKey = new TurnKey(threadId, turn.Id);
        var cts = new CancellationTokenSource();
        var turnRuntime = GetOrAddTurnRuntime(turnKey);
        if (turnRuntime != null)
            turnRuntime.Cancellation = cts;

        _ = Task.Run(async () =>
        {
            // Link caller cancellation with our internal CTS inside the lambda so it lives
            // for the full duration of the background task rather than being disposed on method return.
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(callerCt, cts.Token);
            var executionCt = linkedCts.Token;

            IDisposable? gateLock = null;
            IDisposable? approvalOverride = null;
            AIAgent agent = defaultAgent;
            AgentSession? session = null;
            TokenTracker? tokenTracker = null;
            SessionItem? agentMessageItem = null;
            SessionItem? reasoningItem = null;
            var agentText = string.Empty;
            var reasoningText = string.Empty;
            var agentDeltaIndex = 0;
            var mainTraceUsageBaseline = 0;
            long inputTokens = 0, outputTokens = 0, cachedInputTokens = 0, cacheWriteInputTokens = 0, reasoningOutputTokens = 0;
            var llmCallCount = 0;
            Dictionary<int, SessionItem>? streamingToolCallItemsByIndex = null;
            Dictionary<int, string>? streamingToolNameByIndex = null;
            Dictionary<string, SessionItem>? streamingToolCallItemsByCallId = null;
            PendingCompactionCheckpoint? pendingCompactionCheckpoint = null;

            void FinalizeStreamingAgentMessage()
            {
                // Finalize the current AgentMessage so any subsequent text starts a
                // fresh item, preserving the natural interleaving in stored turns.
                if (agentMessageItem == null)
                    return;

                agentMessageItem.Payload = new AgentMessagePayload { Text = agentText };
                agentMessageItem.Status = ItemStatus.Completed;
                agentMessageItem.CompletedAt = DateTimeOffset.UtcNow;
                eventChannel.EmitItemCompleted(agentMessageItem);
                agentMessageItem = null;
                agentText = string.Empty;
            }

            void FinalizeStreamingReasoning()
            {
                if (reasoningItem == null)
                    return;

                reasoningItem.Payload = new ReasoningContentPayload { Text = reasoningText };
                reasoningItem.Status = ItemStatus.Completed;
                reasoningItem.CompletedAt = DateTimeOffset.UtcNow;
                eventChannel.EmitItemCompleted(reasoningItem);
                reasoningItem = null;
                reasoningText = string.Empty;
            }

            async Task PersistCancelledTurnAsync()
            {
                await TrySaveThreadAsync(thread);
                if (session is not null)
                {
                    await TrySaveSessionAsync(agent, session, threadId);
                    await TryAppendPendingCompactionCheckpointAsync();
                    return;
                }

                if (!persistence.SessionFileExists(threadId))
                    await TryRebuildAndSaveSessionAsync(agent, threadId);
            }

            async Task TryAppendPendingCompactionCheckpointAsync()
            {
                if (pendingCompactionCheckpoint is null || session is null)
                    return;

                var checkpoint = pendingCompactionCheckpoint;
                pendingCompactionCheckpoint = null;
                await TryAppendCompactionCheckpointAsync(
                    threadId,
                    turn.Id,
                    session,
                    checkpoint,
                    CancellationToken.None);
            }

            async Task<ChatMessage?> TryDrainTurnContextMessageAsync(CancellationToken drainCt)
            {
                var mailboxMessage = await TryDrainSubAgentMailboxMessageAsync(drainCt);
                if (mailboxMessage != null)
                    return mailboxMessage;

                return await TryDrainGuidanceMessageAsync(drainCt);
            }

            async Task<ChatMessage?> TryDrainSubAgentMailboxMessageAsync(CancellationToken drainCt)
            {
                var rootThreadId = currentSubAgentSource?.RootThreadId;
                if (string.IsNullOrWhiteSpace(rootThreadId))
                    rootThreadId = thread.Id;

                var currentPathValue = currentSubAgentSource?.AgentPath;
                if (string.IsNullOrWhiteSpace(currentPathValue))
                    currentPathValue = AgentPath.Root;
                if (!AgentPath.TryParse(currentPathValue, out var currentPath))
                    return null;

                var pending = await ListPendingSubAgentMailboxAsync(
                    rootThreadId,
                    currentPath.Value,
                    drainCt);
                if (pending.Count == 0)
                    return null;

                var materializedText = BuildSubAgentMailboxModelText(pending);
                if (string.IsNullOrWhiteSpace(materializedText))
                    return null;

                var displayText = BuildSubAgentMailboxDisplayText(pending);
                var materializedPart = new SessionWireInputPart { Type = "text", Text = materializedText };
                var nativePart = new SessionWireInputPart { Type = "text", Text = displayText };
                var item = new SessionItem
                {
                    Id = SessionIdGenerator.NewItemId(NextItemSeq()),
                    TurnId = turn.Id,
                    Type = ItemType.UserMessage,
                    Status = ItemStatus.Completed,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Payload = new UserMessagePayload
                    {
                        Text = displayText,
                        DeliveryMode = SubAgentMailboxDelivery.DeliveryMode,
                        NativeInputParts = [nativePart],
                        MaterializedInputParts = [materializedPart],
                        ChannelName = turn.OriginChannel,
                        ChannelContext = turn.Initiator?.ChannelContext,
                        GroupId = turn.Initiator?.GroupId,
                        TriggerKind = SubAgentMailboxDelivery.DeliveryMode,
                        TriggerLabel = pending.Count == 1 ? pending[0].SenderAgentPath : $"{pending.Count} messages",
                        TriggerRefId = currentPath.Value
                    }
                };

                FinalizeStreamingAgentMessage();
                FinalizeStreamingReasoning();

                turn.Items.Add(item);
                thread.LastActiveAt = DateTimeOffset.UtcNow;
                await PersistThreadWithMaterializationAsync(thread, CancellationToken.None);
                await MarkSubAgentMailboxDeliveredAsync(
                    rootThreadId,
                    pending.Select(entry => entry.Id).ToArray(),
                    DateTimeOffset.UtcNow,
                    CancellationToken.None);

                eventChannel.EmitItemStarted(item);
                eventChannel.EmitItemCompleted(item);
                return new ChatMessage(ChatRole.User, (IList<AIContent>)[new TextContent(materializedText)]);
            }

            async Task<ChatMessage?> TryDrainGuidanceMessageAsync(CancellationToken drainCt)
            {
                QueuedTurnInput queued;
                using (await AcquireThreadQueueLockAsync(threadId, drainCt))
                {
                    var queueIndex = thread.QueuedInputs.FindIndex(q =>
                        string.Equals(q.Status, "guidancePending", StringComparison.Ordinal) &&
                        string.Equals(q.ReadyAfterTurnId, turn.Id, StringComparison.Ordinal));
                    if (queueIndex < 0)
                        return null;

                    queued = thread.QueuedInputs[queueIndex];
                }

                var contentParts = await ThreadQueue.ResolveInputPartsAsync(queued.MaterializedInputParts.ToList(), drainCt);
                if (contentParts.Count == 0)
                    return null;

                var nativeParts = queued.NativeInputParts.ToList();
                var materializedParts = queued.MaterializedInputParts.ToList();
                var displayText = !string.IsNullOrWhiteSpace(queued.DisplayText)
                    ? queued.DisplayText
                    : SessionWireMapper.BuildDisplayText(nativeParts);
                var images = ExtractUserMessageImages(contentParts);

                var item = new SessionItem
                {
                    Id = SessionIdGenerator.NewItemId(NextItemSeq()),
                    TurnId = turn.Id,
                    Type = ItemType.UserMessage,
                    Status = ItemStatus.Completed,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Payload = new UserMessagePayload
                    {
                        Text = displayText,
                        DeliveryMode = "guidance",
                        NativeInputParts = nativeParts,
                        MaterializedInputParts = materializedParts,
                        SenderId = queued.Sender?.SenderId,
                        SenderName = queued.Sender?.SenderName,
                        SenderRole = queued.Sender?.SenderRole,
                        ChannelName = turn.OriginChannel,
                        ChannelContext = turn.Initiator?.ChannelContext,
                        GroupId = queued.Sender?.GroupId ?? turn.Initiator?.GroupId,
                        Images = images.Count > 0 ? images : null,
                        TriggerKind = queued.TriggerKind,
                        TriggerLabel = queued.TriggerLabel,
                        TriggerRefId = queued.TriggerRefId
                    }
                };

                IReadOnlyList<QueuedTurnInput> queueSnapshot;
                using (await AcquireThreadQueueLockAsync(threadId, CancellationToken.None))
                {
                    var queue = thread.QueuedInputs.ToList();
                    var queueIndex = queue.FindIndex(q =>
                        string.Equals(q.Id, queued.Id, StringComparison.Ordinal) &&
                        string.Equals(q.Status, "guidancePending", StringComparison.Ordinal) &&
                        string.Equals(q.ReadyAfterTurnId, turn.Id, StringComparison.Ordinal));
                    if (queueIndex < 0)
                        return null;

                    FinalizeStreamingAgentMessage();
                    FinalizeStreamingReasoning();

                    turn.Items.Add(item);
                    queue.RemoveAt(queueIndex);
                    thread.QueuedInputs = queue;
                    thread.LastActiveAt = DateTimeOffset.UtcNow;
                    await PersistThreadWithMaterializationAsync(thread, CancellationToken.None);
                    queueSnapshot = queue.ToList();
                }

                eventChannel.EmitItemStarted(item);
                eventChannel.EmitItemCompleted(item);
                PublishQueueUpdated(thread.Id, queueSnapshot);
                return new ChatMessage(ChatRole.User, contentParts);
            }

            async Task RestoreUndrainedGuidanceAsync()
            {
                IReadOnlyList<QueuedTurnInput> queueSnapshot;
                using (await AcquireThreadQueueLockAsync(threadId, CancellationToken.None))
                {
                    var restored = false;
                    var queue = thread.QueuedInputs.ToList();
                    for (var i = 0; i < queue.Count; i++)
                    {
                        var queued = queue[i];
                        if (!string.Equals(queued.Status, "guidancePending", StringComparison.Ordinal) ||
                            !string.Equals(queued.ReadyAfterTurnId, turn.Id, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        queue[i] = queued with { Status = "queued" };
                        restored = true;
                    }

                    if (!restored)
                        return;

                    thread.QueuedInputs = queue;
                    thread.LastActiveAt = DateTimeOffset.UtcNow;
                    await PersistThreadWithMaterializationAsync(thread, CancellationToken.None);
                    queueSnapshot = queue.ToList();
                }

                PublishQueueUpdated(thread.Id, queueSnapshot);
            }

            async Task<IReadOnlyList<ChatMessage>?> TryCompactBeforeSamplingAsync(
                IReadOnlyList<ChatMessage> modelVisibleHistory,
                PromptRequestSnapshot? requestSnapshot,
                CancellationToken compactionCt)
            {
                if (session is null || tokenTracker is null || modelVisibleHistory.Count == 0)
                    return null;

                var usageEstimate = EstimateContextTokens(
                    threadId,
                    modelVisibleHistory,
                    tokenTracker.LastContextTokens,
                    requestSnapshot);
                if (!usageEstimate.EligibleForAutoCompact)
                    return null;

                var tokenHint = usageEstimate.Tokens;
                var pipeline = GetCompactionPipelineForThread(thread);
                var threshold = pipeline.EvaluateThreshold(tokenHint);
                if (!threshold.AboveAuto)
                    return null;

                var preCompactUsage = CreateContextUsageSnapshot(
                    threadId,
                    tokenHint,
                    usageEstimate.Source,
                    usageEstimate.IsEstimate);
                eventChannel.EmitSystemEvent(
                    "compacting",
                    percentLeft: threshold.PercentLeft,
                    tokenCount: threshold.Tokens,
                    contextUsage: preCompactUsage);

                CompactionHistoryResult result;
                try
                {
                    result = await pipeline.TryAutoCompactHistoryAsync(
                        modelVisibleHistory,
                        threadId,
                        tokenHint,
                        lastActivityBeforeTurn,
                        compactionCt,
                        requestSnapshot);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Pre-sampling compaction failed for thread {ThreadId}", threadId);
                    eventChannel.EmitSystemEvent(
                        "compactFailed",
                        message: ex.Message,
                        percentLeft: threshold.PercentLeft,
                        tokenCount: threshold.Tokens,
                        contextUsage: preCompactUsage);
                    return null;
                }

                var status = result.Status;
                switch (status.Outcome)
                {
                    case CompactionOutcome.Micro:
                    case CompactionOutcome.Partial:
                        tokenTracker.Reset();
                        session.SetInMemoryChatHistory(
                            [.. result.Messages],
                            jsonSerializerOptions: SessionPersistenceJsonOptions.Default);
                        pendingCompactionCheckpoint = new PendingCompactionCheckpoint(
                            "auto",
                            CompactionOutcomeToWire(status.Outcome),
                            status.ThresholdBefore.Tokens,
                            status.ThresholdAfter.Tokens);
                        InvalidatePromptRequestSnapshot(threadId, "auto_compaction");
                        await TrySaveSessionAsync(agent, session, threadId);
                        var contextUsage = await SaveContextUsageSnapshotAsync(
                            threadId,
                            status.ThresholdAfter.Tokens,
                            source: "compacted_estimate",
                            isEstimate: true,
                            ct: CancellationToken.None);
                        ClearContextUsageAnchor(threadId);
                        ReleaseStableContextPages(threadId);
                        if (status.Outcome == CompactionOutcome.Partial)
                            traceCollector?.RecordContextCompaction(threadId);
                        eventChannel.EmitSystemEvent(
                            "compacted",
                            percentLeft: status.ThresholdAfter.PercentLeft,
                            tokenCount: status.ThresholdAfter.Tokens,
                            contextUsage: contextUsage);
                        if (status.Outcome == CompactionOutcome.Partial)
                        {
                            var noticeItem = CreateCompactionNoticeItem(
                                turn,
                                NextItemSeq(),
                                trigger: "auto",
                                status);
                            turn.Items.Add(noticeItem);
                            eventChannel.EmitItemStarted(noticeItem);
                            eventChannel.EmitItemCompleted(noticeItem);
                        }
                        ThreadRuntimeSignalForBroadcast?.Invoke(
                            threadId,
                            SessionThreadRuntimeSignal.ContextCompacted);
                        return result.Messages;

                    case CompactionOutcome.Skipped:
                        eventChannel.EmitSystemEvent(
                            "compactSkipped",
                            message: status.FailureReason,
                            percentLeft: status.ThresholdAfter.PercentLeft,
                            tokenCount: status.ThresholdAfter.Tokens,
                            contextUsage: preCompactUsage);
                        return null;

                    case CompactionOutcome.Failed:
                        eventChannel.EmitSystemEvent(
                            "compactFailed",
                            message: status.FailureReason,
                            percentLeft: status.ThresholdAfter.PercentLeft,
                            tokenCount: status.ThresholdAfter.Tokens,
                            contextUsage: preCompactUsage);
                        return null;

                    default:
                        return null;
                }
            }

            async Task FailAndPersistTurnAsync(string errorMsg, string errorCode)
            {
                FinalizeStreamingAgentMessage();
                FinalizeStreamingReasoning();
                await RestoreUndrainedGuidanceAsync();

                var errorItem = CreateErrorItem(turn, NextItemSeq(), errorMsg, errorCode, fatal: true);
                turn.Items.Add(errorItem);
                eventChannel.EmitItemStarted(errorItem);
                eventChannel.EmitItemCompleted(errorItem);

                FailTurn(turn, eventChannel, errorMsg);
                ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.TurnFailed);
                await TrySaveThreadAsync(thread);
                if (session is not null)
                {
                    if (TryAppendFailedTurnTailToSession(session, turn))
                    {
                        await TrySaveSessionAsync(agent, session, threadId);
                        await TryAppendPendingCompactionCheckpointAsync();
                        return;
                    }
                }
                else if (persistence.SessionFileExists(threadId))
                {
                    return;
                }

                await TryRebuildAndSaveSessionAsync(agent, threadId);
            }

            try
            {
                // Step 5a: Acquire SessionGate
                try
                {
                    gateLock = await sessionGate.AcquireAsync(threadId, executionCt);
                }
                catch (SessionGateOverflowException ex)
                {
                    logger?.LogWarning("Session gate overflow for thread {ThreadId}: {Message}", threadId, ex.Message);
                    await FailAndPersistTurnAsync(
                        $"Session queue overflow: {ex.Message}",
                        "session_gate_overflow");
                    return;
                }

                // Step 5b: Load/create AgentSession
                using (await AcquireThreadAgentLockAsync(threadId, executionCt))
                    agent = GetThreadAgentOrDefault(threadId);

                // Bind tracing and token tracking before session creation so session metadata
                // captured during CreateSessionAsync / LoadOrCreateSessionAsync is attributed
                // to the correct ephemeral or persisted thread.
                traceCollector?.BindThreadMainSession(threadId);
                TracingChatClient.CurrentSessionKey = threadId;
                TracingChatClient.ResetCallState(threadId);
                mainTraceUsageBaseline = traceCollector?.GetTokenUsageCount(threadId) ?? 0;
                tokenTracker = agentFactory.GetOrCreateTokenTracker(threadId);
                TokenTracker.Current = tokenTracker;

                if (thread.HistoryMode == HistoryMode.Client && messages != null)
                {
                    // Client-managed: construct from provided messages
                    session = await agent.CreateSessionAsync(executionCt);
                    var chatHistory = session.GetService<ChatHistoryProvider>();
                    if (chatHistory is InMemoryChatHistoryProvider memProvider)
                    {
                        memProvider.SetMessages(session, [.. messages]);
                    }
                }
                else
                {
                    session = await persistence.LoadOrCreateSessionAsync(agent, threadId, executionCt);
                }

                // Step 5c: Append runtime context to the multimodal content list
                var turnMode = thread.Configuration?.Mode?.Equals("plan", StringComparison.OrdinalIgnoreCase) == true
                    ? AgentMode.Plan
                    : AgentMode.Agent;
                var runtimeModeManager = GetOrCreateModeManager(threadId, turnMode);
                var hasActivePlan = agentFactory.PlanStore?.StructuredPlanExists(threadId) == true;
                ThreadGoal? threadGoalForContext = null;
                if (GoalsEnabled)
                {
                    threadGoalForContext = await persistence.GetThreadGoalAsync(threadId, executionCt);
                    if (threadGoalForContext is { Status: ThreadGoalStatus.Active }
                        && turnMode != AgentMode.Plan
                        && thread.Source.SubAgent == null)
                    {
                        var now = DateTimeOffset.UtcNow;
                        var goalTurnRuntime = GetOrAddTurnRuntime(turnKey);
                        if (goalTurnRuntime != null)
                        {
                            goalTurnRuntime.GoalSnapshot = new GoalTurnSnapshot(
                                threadGoalForContext.GoalId,
                                now,
                                now,
                                new TokenUsageInfo());
                        }
                    }
                }

                var userMessage = new ChatMessage(
                    ChatRole.User,
                    content.AppendRuntimeContext(
                        turn.Initiator,
                        runtimeModeManager,
                        thread.WorkspacePath,
                        hasActivePlan,
                        threadGoalForContext));

                // Step 5d: Run PrePrompt hooks
                if (hookRunner != null)
                {
                    var hookInput = new HookInput { SessionId = threadId, Prompt = text };
                    var hookResult = await hookRunner.RunAsync(HookEvent.PrePrompt, hookInput, executionCt);
                    if (hookResult.Blocked)
                    {
                        var errorMsg = $"Prompt blocked by hook: {hookResult.BlockReason ?? "no reason given"}";
                        await FailAndPersistTurnAsync(errorMsg, "hook_blocked");
                        return;
                    }
                }

                // Step 5e: Set up approval service override
                var approvalPolicy = ResolveApprovalPolicy(thread.Configuration?.ApprovalPolicy ?? ApprovalPolicy.Default);
                IApprovalService turnApprovalService;
                switch (approvalPolicy)
                {
                    case ApprovalPolicy.AutoApprove:
                        turnApprovalService = new AutoApproveApprovalService();
                        break;
                    case ApprovalPolicy.Interrupt:
                        turnApprovalService = new InterruptOnApprovalService();
                        break;
                    default:
                        var sessionApproval = new SessionApprovalService(
                            eventChannel,
                            turn,
                            NextItemSeq,
                            _approvalTimeout,
                            cts.Cancel,
                            approvalStore,
                            ThreadRuntimeSignalForBroadcast);
                        var approvalTurnRuntime = GetOrAddTurnRuntime(turnKey);
                        if (approvalTurnRuntime != null)
                            approvalTurnRuntime.PendingApproval = sessionApproval;
                        turnApprovalService = sessionApproval;
                        break;
                }

                approvalOverride = SessionScopedApprovalService.SetOverride(turnApprovalService);

                var userInputRequestService = new SessionUserInputRequestService(
                    eventChannel,
                    turn,
                    NextItemSeq,
                    cts.Token,
                    ThreadRuntimeSignalForBroadcast);
                var userInputTurnRuntime = GetOrAddTurnRuntime(turnKey);
                if (userInputTurnRuntime != null)
                    userInputTurnRuntime.PendingUserInput = userInputRequestService;

                // Set ApprovalContext for tools that read ApprovalContextScope
                var approvalContextDisposable = sender != null
                    ? ApprovalContextScope.Set(new ApprovalContext
                    {
                        UserId = sender.SenderId,
                        UserRole = sender.SenderRole,
                        GroupId = long.TryParse(sender.GroupId ?? channelInfo?.GroupId, out var groupId) ? groupId : 0,
                        Source = ResolveApprovalSource(channelInfo?.Channel)
                    })
                    : null;

                // Step 5g: Run agent
                var pluginFunctionCallIds = new HashSet<string>(StringComparer.Ordinal);
                var dynamicToolCallIds = new HashSet<string>(StringComparer.Ordinal);
                int? currentUsageRequestIndex = null;
                ChatFinishReason? lastFinishReason = null;
                var usageAccumulator = new TokenUsageRequestAccumulator();
                var pluginFunctionToolNames = GetPluginFunctionToolNames(threadId);
                var dynamicToolNames = GetDynamicToolNames(threadId);

                // SubAgent progress aggregator: lazily created when SpawnAgent tool calls appear
                SubAgentProgressAggregator? progressAggregator = null;

                var effectiveWorkspacePath =
                    !string.IsNullOrWhiteSpace(thread.Configuration?.WorkspaceOverride)
                        ? thread.Configuration.WorkspaceOverride!
                        : agentFactory.ToolProviderContext.WorkspacePath;
                var requireApprovalOutsideWorkspace =
                    thread.Configuration?.RequireApprovalOutsideWorkspace
                    ?? agentFactory.ToolProviderContext.Config.Tools.File.RequireApprovalOutsideWorkspace;
                var effectivePathBlacklist = !string.IsNullOrWhiteSpace(thread.Configuration?.WorkspaceOverride)
                    ? new PathBlacklist([])
                    : agentFactory.ToolProviderContext.PathBlacklist;
                var supportsCommandExecutionStreaming =
                    AppServer.AppServerRequestContext.CurrentConnection?.SupportsCommandExecutionStreaming == true;
                var supportsToolExecutionLifecycle =
                    AppServer.AppServerRequestContext.CurrentConnection?.SupportsToolExecutionLifecycle == true;

                using var pluginFunctionScope = PluginFunctionExecutionScope.Set(
                    new PluginFunctionExecutionContext
                    {
                        ThreadId = threadId,
                        TurnId = turn.Id,
                        OriginChannel = turnOriginChannel,
                        ChannelContext = turn.Initiator?.ChannelContext ?? thread.ChannelContext,
                        SenderId = turn.Initiator?.UserId ?? thread.UserId,
                        GroupId = turn.Initiator?.GroupId,
                        WorkspacePath = effectiveWorkspacePath,
                        RequireApprovalOutsideWorkspace = requireApprovalOutsideWorkspace,
                        ApprovalService = turnApprovalService,
                        PathBlacklist = effectivePathBlacklist,
                        Turn = turn,
                        NextItemSequence = NextItemSeq,
                        EmitItemStarted = eventChannel.EmitItemStarted,
                        EmitItemCompleted = eventChannel.EmitItemCompleted,
                        SessionService = this
                    });
                using var commandExecutionScope = CommandExecutionRuntimeScope.Set(
                    new CommandExecutionRuntimeContext
                    {
                        ThreadId = threadId,
                        TurnId = turn.Id,
                        Turn = turn,
                        NextItemSequence = NextItemSeq,
                        EmitItemStarted = eventChannel.EmitItemStarted,
                        EmitItemDelta = eventChannel.EmitItemDelta,
                        EmitItemCompleted = eventChannel.EmitItemCompleted,
                        SupportsCommandExecutionStreaming = supportsCommandExecutionStreaming
                    });
                using var toolExecutionScope = ToolExecutionRuntimeScope.Set(
                    new ToolExecutionRuntimeContext
                    {
                        TurnId = turn.Id,
                        Turn = turn,
                        NextItemSequence = NextItemSeq,
                        EmitItemStarted = eventChannel.EmitItemStarted,
                        EmitItemCompleted = eventChannel.EmitItemCompleted,
                        SupportsToolExecutionLifecycle = supportsToolExecutionLifecycle
                    });
                using var goalToolScope = GoalsEnabled
                    && turnMode != AgentMode.Plan
                    && thread.Source.SubAgent == null
                        ? GoalToolRuntimeScope.Set(new GoalToolRuntimeContext(this, threadId, turn.Id))
                        : null;
                using var requestUserInputScope = thread.Source.SubAgent == null
                        ? RequestUserInputRuntimeScope.Set(new RequestUserInputRuntimeContext(async questions =>
                        {
                            var requestId = Guid.NewGuid().ToString("N")[..12];
                            return await userInputRequestService.RequestAsync(requestId, questions);
                        }))
                        : null;
                using var subAgentSessionScope = SubAgentSessionScope.Set(new SubAgentSessionContext
                {
                    SessionService = this,
                    ParentThread = thread,
                    ParentTurnId = turn.Id,
                    RootThreadId = currentSubAgentSource?.RootThreadId ?? thread.Id,
                    Depth = currentSubAgentSource?.Depth ?? 0
                });
                try
                {
                    using var preSamplingCompactionScope = PreSamplingCompactionRuntimeScope.Set(
                        new PreSamplingCompactionRuntimeContext
                        {
                            ProviderId = thread.Configuration?.ProviderId,
                            Mode = thread.Configuration?.Mode ?? "agent",
                            ThreadId = threadId,
                            TurnId = turn.Id,
                            EstimatedInputTokens = tokenTracker.LastInputTokens > 0
                                ? (int)Math.Min(int.MaxValue, tokenTracker.LastInputTokens)
                                : null,
                            CaptureSnapshotAsync = (snapshot, _) =>
                            {
                                if (_runtimeRegistry.TryGetRuntime(threadId, out var runtime))
                                    runtime.LastPromptRequest = snapshot;
                                return Task.CompletedTask;
                            },
                            TryCompactWithSnapshotAsync = TryCompactBeforeSamplingAsync,
                            TryCompactAsync = (history, compactCt) => TryCompactBeforeSamplingAsync(history, null, compactCt)
                        });
                    using var guidanceScope = TurnGuidanceRuntimeScope.Set(new TurnGuidanceRuntimeContext
                    {
                        ThreadId = threadId,
                        TurnId = turn.Id,
                        TryDrainGuidanceMessageAsync = TryDrainTurnContextMessageAsync
                    });
                    using var modelStreamRetryScope = ModelStreamRetryRuntimeScope.Set(
                        new ModelStreamRetryRuntimeContext
                        {
                            NotifyRetry = (attempt, maxAttempts, _) =>
                                eventChannel.EmitSystemEvent(
                                    "streamError",
                                    $"Reconnecting... {attempt}/{maxAttempts}")
                        });

                    await foreach (var update in agent.RunStreamingAsync(userMessage, session)
                        .WithCancellation(executionCt))
                    {
                        var chatResponseUpdate = update.AsChatResponseUpdate();
                        if (chatResponseUpdate.FinishReason.HasValue)
                            lastFinishReason = chatResponseUpdate.FinishReason.Value;

                        var updateRequestIndex = TokenUsageRequestMetadata.TryGetRequestIndex(chatResponseUpdate);
                        if (updateRequestIndex.HasValue)
                            currentUsageRequestIndex = updateRequestIndex;

                        foreach (var responseContent in update.Contents)
                        {
                            switch (responseContent)
                            {
                                case TextContent tc:
                                    // Agent message text
                                    FinalizeStreamingReasoning();
                                    var chunk = tc.Text ?? string.Empty;
                                    if (agentMessageItem == null)
                                    {
                                        agentMessageItem = new SessionItem
                                        {
                                            Id = SessionIdGenerator.NewItemId(NextItemSeq()),
                                            TurnId = turn.Id,
                                            Type = ItemType.AgentMessage,
                                            Status = ItemStatus.Streaming,
                                            CreatedAt = DateTimeOffset.UtcNow,
                                            Payload = new AgentMessagePayload { Text = string.Empty }
                                        };
                                        turn.Items.Add(agentMessageItem);
                                        eventChannel.EmitItemStarted(agentMessageItem);
                                    }
                                    agentText += chunk;
                                    agentDeltaIndex += 1;
                                    if (sessionStreamDebugLogger?.ShouldCapture(threadId, turn.Id) == true)
                                    {
                                        sessionStreamDebugLogger.Log(
                                            "agent_delta_source",
                                            threadId,
                                            turn.Id,
                                            new
                                            {
                                                itemId = agentMessageItem.Id,
                                                deltaIndex = agentDeltaIndex,
                                                chunkChars = chunk.Length,
                                                chunkText = sessionStreamDebugLogger.IncludeFullText ? chunk : null,
                                                cumulativeChars = agentText.Length,
                                                cumulativeText = sessionStreamDebugLogger.IncludeFullText ? agentText : null
                                            });
                                    }
                                    eventChannel.EmitItemDelta(agentMessageItem, new AgentMessageDelta { TextDelta = chunk });
                                    break;

                                case TextReasoningContent reasoning:
                                    if (ReasoningContentHelper.TryGetText(reasoning, out var rText))
                                    {
                                        FinalizeStreamingAgentMessage();
                                        if (reasoningItem == null)
                                        {
                                            reasoningItem = new SessionItem
                                            {
                                                Id = SessionIdGenerator.NewItemId(NextItemSeq()),
                                                TurnId = turn.Id,
                                                Type = ItemType.ReasoningContent,
                                                Status = ItemStatus.Streaming,
                                                CreatedAt = DateTimeOffset.UtcNow,
                                                Payload = new ReasoningContentPayload { Text = string.Empty }
                                            };
                                            turn.Items.Add(reasoningItem);
                                            eventChannel.EmitItemStarted(reasoningItem);
                                        }
                                        reasoningText += rText;
                                        eventChannel.EmitItemDelta(reasoningItem,
                                            new ReasoningContentDelta { TextDelta = rText });
                                    }
                                    break;

                                case ToolCallArgumentsDeltaContent toolArgsDelta:
                                {
                                    if (string.IsNullOrEmpty(toolArgsDelta.ArgumentsDelta))
                                        break;

                                    var toolCallIndex = toolArgsDelta.ToolCallIndex;
                                    if (!string.IsNullOrWhiteSpace(toolArgsDelta.ToolName))
                                    {
                                        streamingToolNameByIndex ??= [];
                                        streamingToolNameByIndex[toolCallIndex] = toolArgsDelta.ToolName;
                                    }

                                    var resolvedToolName =
                                        streamingToolNameByIndex != null
                                        && streamingToolNameByIndex.TryGetValue(toolCallIndex, out var cachedToolName)
                                            ? cachedToolName
                                            : null;
                                    if (string.IsNullOrWhiteSpace(resolvedToolName))
                                        break;
                                    if (IsPluginFunctionTool(pluginFunctionToolNames, resolvedToolName)
                                        || IsDynamicTool(dynamicToolNames, resolvedToolName))
                                        break;

                                    FinalizeStreamingReasoning();
                                    streamingToolCallItemsByIndex ??= [];
                                    if (!streamingToolCallItemsByIndex.TryGetValue(toolCallIndex, out var streamingToolCallItem))
                                    {
                                        streamingToolCallItem = new SessionItem
                                        {
                                            Id = SessionIdGenerator.NewItemId(NextItemSeq()),
                                            TurnId = turn.Id,
                                            Type = ItemType.ToolCall,
                                            Status = ItemStatus.Streaming,
                                            CreatedAt = DateTimeOffset.UtcNow,
                                            Payload = new ToolCallPayload
                                            {
                                                ToolName = resolvedToolName,
                                                CallId = toolArgsDelta.CallId ?? string.Empty,
                                                Arguments = null
                                            }
                                        };
                                        streamingToolCallItemsByIndex[toolCallIndex] = streamingToolCallItem;
                                        turn.Items.Add(streamingToolCallItem);
                                        eventChannel.EmitItemStarted(streamingToolCallItem);
                                    }

                                    if (!string.IsNullOrWhiteSpace(toolArgsDelta.CallId))
                                    {
                                        streamingToolCallItemsByCallId ??= new Dictionary<string, SessionItem>(StringComparer.Ordinal);
                                        if (!streamingToolCallItemsByCallId.ContainsKey(toolArgsDelta.CallId))
                                            streamingToolCallItemsByCallId[toolArgsDelta.CallId] = streamingToolCallItem;
                                    }

                                    eventChannel.EmitItemDelta(streamingToolCallItem, new ToolCallArgumentsDelta
                                    {
                                        ToolName = resolvedToolName,
                                        CallId = toolArgsDelta.CallId,
                                        Delta = toolArgsDelta.ArgumentsDelta
                                    });
                                    break;
                                }

                                case FunctionCallContent fc:
                                {
                                    var isPluginFunctionTool = IsPluginFunctionTool(pluginFunctionToolNames, fc.Name);
                                    var isDynamicTool = IsDynamicTool(dynamicToolNames, fc.Name);
                                    if (isPluginFunctionTool && !string.IsNullOrWhiteSpace(fc.CallId))
                                        pluginFunctionCallIds.Add(fc.CallId);
                                    if (isDynamicTool && !string.IsNullOrWhiteSpace(fc.CallId))
                                        dynamicToolCallIds.Add(fc.CallId);
                                    FinalizeStreamingReasoning();
                                    RegisterCommandExecutionIfNeeded(
                                        fc,
                                        turn,
                                        NextItemSeq,
                                        eventChannel,
                                        supportsCommandExecutionStreaming,
                                        effectiveWorkspacePath);
                                    if (isPluginFunctionTool || isDynamicTool)
                                        break;
                                    RegisterToolExecutionIfNeeded(
                                        fc,
                                        turn,
                                        NextItemSeq,
                                        eventChannel,
                                        supportsToolExecutionLifecycle);

                                    SessionItem? toolCallItem = null;
                                    if (!string.IsNullOrWhiteSpace(fc.CallId)
                                        && streamingToolCallItemsByCallId != null
                                        && streamingToolCallItemsByCallId.TryGetValue(fc.CallId, out var existingStreamingToolCallItem))
                                    {
                                        toolCallItem = existingStreamingToolCallItem;
                                        toolCallItem.Status = ItemStatus.Completed;
                                        toolCallItem.CompletedAt = DateTimeOffset.UtcNow;
                                        toolCallItem.Payload = new ToolCallPayload
                                        {
                                            ToolName = fc.Name,
                                            Arguments = fc.Arguments != null
                                                ? JsonNode.Parse(
                                                    System.Text.Json.JsonSerializer.Serialize(
                                                        fc.Arguments)) as JsonObject
                                                : null,
                                            CallId = fc.CallId
                                        };
                                        eventChannel.EmitItemCompleted(toolCallItem);
                                        streamingToolCallItemsByCallId.Remove(fc.CallId);
                                        TryRemoveStreamingToolCallIndexByItemReference(
                                            streamingToolCallItemsByIndex,
                                            existingStreamingToolCallItem);
                                    }
                                    else
                                    {
                                        toolCallItem = new SessionItem
                                        {
                                            Id = SessionIdGenerator.NewItemId(NextItemSeq()),
                                            TurnId = turn.Id,
                                            Type = ItemType.ToolCall,
                                            Status = ItemStatus.Completed,
                                            CreatedAt = DateTimeOffset.UtcNow,
                                            CompletedAt = DateTimeOffset.UtcNow,
                                            Payload = new ToolCallPayload
                                            {
                                                ToolName = fc.Name,
                                                Arguments = fc.Arguments != null
                                                    ? JsonNode.Parse(
                                                        System.Text.Json.JsonSerializer.Serialize(
                                                            fc.Arguments)) as JsonObject
                                                    : null,
                                                CallId = fc.CallId
                                            }
                                        };
                                        turn.Items.Add(toolCallItem);
                                        eventChannel.EmitItemStarted(toolCallItem);
                                        eventChannel.EmitItemCompleted(toolCallItem);
                                    }

                                    // Track SubAgent progress when SpawnAgent tool calls are detected
                                    if (string.Equals(fc.Name, "SpawnAgent", StringComparison.Ordinal)
                                        && fc.Arguments != null)
                                    {
                                        var rawLabel = fc.Arguments.TryGetValue("agentNickname", out var labelObj)
                                            ? labelObj?.ToString()
                                            : null;
                                        var rawTask = fc.Arguments.TryGetValue("agentPrompt", out var taskObj)
                                            ? taskObj?.ToString()
                                            : null;

                                        if (rawLabel != null || rawTask != null)
                                        {
                                            var normalizedLabel = SubAgentManager.NormalizeLabel(
                                                rawLabel, rawTask ?? "task");
                                            progressAggregator ??= new SubAgentProgressAggregator(
                                                eventChannel, threadId, turn.Id);
                                            progressAggregator.TrackLabel(normalizedLabel);
                                        }
                                    }
                                    break;
                                }

                                case FunctionResultContent fr:
                                {
                                    FinalizeStreamingReasoning();
                                    if (!string.IsNullOrWhiteSpace(fr.CallId)
                                        && pluginFunctionCallIds.Remove(fr.CallId))
                                    {
                                        // Plugin function calls are represented by pluginFunctionCall
                                        // items emitted from the plugin function wrapper.
                                        // Even when toolResult is suppressed, keep text segmentation aligned
                                        // with normal tools so post-tool text starts a new agent message item.
                                        FinalizeStreamingAgentMessage();
                                        break;
                                    }
                                    if (!string.IsNullOrWhiteSpace(fr.CallId)
                                        && dynamicToolCallIds.Remove(fr.CallId))
                                    {
                                        // Runtime dynamic tools are represented by dynamicToolCall
                                        // items emitted from the dynamic tool wrapper.
                                        FinalizeStreamingAgentMessage();
                                        break;
                                    }
                                    var resultText = ImageContentSanitizingChatClient.DescribeResult(fr.Result);
                                    var toolResultItem = new SessionItem
                                    {
                                        Id = SessionIdGenerator.NewItemId(NextItemSeq()),
                                        TurnId = turn.Id,
                                        Type = ItemType.ToolResult,
                                        Status = ItemStatus.Completed,
                                        CreatedAt = DateTimeOffset.UtcNow,
                                        CompletedAt = DateTimeOffset.UtcNow,
                                        Payload = new ToolResultPayload
                                        {
                                            CallId = fr.CallId,
                                            Result = resultText,
                                            Success = fr.Exception == null
                                                && !StreamingFunctionInvokingChatClient.IsInvalidToolArgumentsResult(fr)
                                        }
                                    };
                                    turn.Items.Add(toolResultItem);
                                    eventChannel.EmitItemStarted(toolResultItem);
                                    eventChannel.EmitItemCompleted(toolResultItem);
                                    FinalizeStreamingAgentMessage();
                                    break;
                                }

                                case UsageContent usage:
                                {
                                    var snapshot = TokenUsageExtractor.FromUsageContent(usage);
                                    var curIn = snapshot.InputTokens;
                                    var curOut = snapshot.OutputTokens;
                                    if (snapshot.InputTokens > 0 || snapshot.OutputTokens > 0)
                                    {
                                        var usageDelta = usageAccumulator.ApplySnapshot(snapshot, currentUsageRequestIndex);
                                        var delta = usageDelta.Usage;
                                        if (delta.InputTokens > 0
                                            || delta.OutputTokens > 0
                                            || delta.CachedInputTokens > 0
                                            || delta.CacheWriteInputTokens > 0
                                            || delta.ReasoningOutputTokens > 0)
                                        {
                                            llmCallCount += usageDelta.LlmCallDelta;
                                            inputTokens += delta.InputTokens;
                                            outputTokens += delta.OutputTokens;
                                            cachedInputTokens += delta.CachedInputTokens;
                                            cacheWriteInputTokens += delta.CacheWriteInputTokens;
                                            reasoningOutputTokens += delta.ReasoningOutputTokens;
                                            tokenTracker.UpdateWithStreamingDeltas(
                                                delta.InputTokens,
                                                delta.OutputTokens,
                                                delta.CachedInputTokens,
                                                delta.CacheWriteInputTokens,
                                                delta.ReasoningOutputTokens,
                                                curIn,
                                                curOut);
                                            var anchor = UpdateContextUsageAnchor(threadId, tokenTracker.LastInputTokens);
                                            var contextUsage = await SaveContextUsageSnapshotAsync(
                                                threadId,
                                                tokenTracker.LastContextTokens,
                                                anchor,
                                                source: "provider_context",
                                                isEstimate: false,
                                                ct: CancellationToken.None);
                                            eventChannel.EmitUsageDelta(
                                                delta.InputTokens,
                                                delta.OutputTokens,
                                                cachedInputTokens: delta.CachedInputTokens,
                                                cacheWriteInputTokens: delta.CacheWriteInputTokens,
                                                reasoningOutputTokens: delta.ReasoningOutputTokens,
                                                llmCallDelta: usageDelta.LlmCallDelta,
                                                totalInputTokens: tokenTracker.LastInputTokens,
                                                totalOutputTokens: outputTokens,
                                                contextInputTokens: tokenTracker.LastInputTokens,
                                                turnInputTokens: inputTokens,
                                                turnOutputTokens: outputTokens,
                                                turnLlmCalls: llmCallCount,
                                                contextUsage: contextUsage);
                                            await AccountGoalUsageAsync(
                                                turnKey,
                                                new TokenUsageInfo
                                                {
                                                    InputTokens = inputTokens,
                                                    OutputTokens = outputTokens,
                                                    CachedInputTokens = cachedInputTokens,
                                                    CacheWriteInputTokens = cacheWriteInputTokens,
                                                    ReasoningOutputTokens = reasoningOutputTokens,
                                                    LlmCallCount = llmCallCount,
                                                    TotalTokens = inputTokens + outputTokens
                                                },
                                                turn.Id,
                                                CancellationToken.None);
                                        }
                                    }

                                    break;
                                }
                            }
                        }
                    }
                }
                finally
                {
                    // Stop SubAgent progress aggregator before cleaning up AsyncLocal context
                    if (progressAggregator != null)
                        await progressAggregator.DisposeAsync();

                    TracingChatClient.ResetCallState(threadId);
                    TracingChatClient.CurrentSessionKey = null;
                    TokenTracker.Current = null;
                    approvalContextDisposable?.Dispose();
                }

                // Step 5h: Finalize any still-streaming items.
                FinalizeStreamingAgentMessage();
                FinalizeStreamingReasoning();

                // Step 5i: Accumulate token usage (include SubAgent tokens)
                var totalInput = inputTokens + tokenTracker.SubAgentInputTokens;
                var totalOutput = outputTokens + tokenTracker.SubAgentOutputTokens;
                var totalCachedInput = cachedInputTokens + tokenTracker.SubAgentCachedInputTokens;
                var totalCacheWriteInput = cacheWriteInputTokens + tokenTracker.SubAgentCacheWriteInputTokens;
                var totalReasoningOutput = reasoningOutputTokens + tokenTracker.SubAgentReasoningOutputTokens;
                var mainTraceUsageDelta = Math.Max(
                    0,
                    (traceCollector?.GetTokenUsageCount(threadId) ?? mainTraceUsageBaseline) - mainTraceUsageBaseline);
                if (totalInput > 0 || totalOutput > 0)
                {
                    turn.TokenUsage = new TokenUsageInfo
                    {
                        InputTokens = totalInput,
                        OutputTokens = totalOutput,
                        CachedInputTokens = Math.Clamp(totalCachedInput, 0, totalInput),
                        CacheWriteInputTokens = Math.Clamp(totalCacheWriteInput, 0, totalInput),
                        ReasoningOutputTokens = totalReasoningOutput,
                        LlmCallCount = Math.Max(llmCallCount, mainTraceUsageDelta) + tokenTracker.SubAgentLlmCallCount,
                        TotalTokens = totalInput + totalOutput
                    };
                    await AccountGoalUsageAsync(turnKey, turn.TokenUsage, turn.Id, CancellationToken.None);
                }

                if (lastFinishReason == ChatFinishReason.Length)
                {
                    await FailAndPersistTurnAsync(
                        "The model response was truncated because it reached the provider output token limit.",
                        "agent_length_limit");
                    return;
                }

                // Step 5j: Run Stop hooks
                if (hookRunner != null)
                {
                    var stopInput = new HookInput { SessionId = threadId, Response = agentText };
                    await hookRunner.RunAsync(HookEvent.Stop, stopInput, CancellationToken.None);
                }

                // Step 5k: Post-turn threshold notification.
                // Auto compaction runs before model sampling through
                // PreSamplingCompactionRuntimeScope. If the final response
                // itself pushes the context over the threshold and no follow-up
                // model call is needed, keep the snapshot visible and compact
                // before the next sampling request.
                {
                    var compactionPipeline = GetCompactionPipelineForThread(thread);
                    var contextTokens = tokenTracker.LastContextTokens;
                    var threshold = compactionPipeline.EvaluateThreshold(contextTokens);
                    var contextUsage = CreateContextUsageSnapshot(
                        threadId,
                        contextTokens,
                        contextTokens > 0 ? "provider_context" : null,
                        isEstimate: false);
                    if (threshold.AboveError)
                    {
                        eventChannel.EmitSystemEvent(
                            "compactError",
                            percentLeft: threshold.PercentLeft,
                            tokenCount: threshold.Tokens,
                            contextUsage: contextUsage);
                    }
                    else if (threshold.AboveWarning)
                    {
                        eventChannel.EmitSystemEvent(
                            "compactWarning",
                            percentLeft: threshold.PercentLeft,
                            tokenCount: threshold.Tokens,
                            contextUsage: contextUsage);
                    }
                }

                // Step 5m: Release gate
                gateLock.Dispose();
                gateLock = null;

                await RestoreUndrainedGuidanceAsync();

                // Steps 5n-5r: Complete Turn
                turn.Status = TurnStatus.Completed;
                turn.CompletedAt = DateTimeOffset.UtcNow;
                thread.LastActiveAt = DateTimeOffset.UtcNow;
                RecordTurnTokenUsage(thread, turn);
                RecordTurnDurationTrace(threadId, turn);
                eventChannel.EmitTurnCompleted(turn);

                try
                {
                    await PersistThreadWithMaterializationAsync(thread, CancellationToken.None);
                    await TrySaveSessionAsync(agent, session, threadId);
                    await TryAppendPendingCompactionCheckpointAsync();
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Failed to persist thread state after turn completion for thread {ThreadId}", threadId);
                }

                _ = TryScheduleMemoryConsolidation(
                    threadId,
                    thread,
                    turn,
                    session,
                    eventChannel,
                    NextItemSeq);
                ThreadRuntimeSignalForBroadcast?.Invoke(
                    threadId,
                    EndsWithSuccessfulCreatePlanInPlanMode(thread, turn)
                        ? SessionThreadRuntimeSignal.TurnCompletedAwaitingPlanConfirmation
                        : SessionThreadRuntimeSignal.TurnCompleted);

                await TryStartNextQueuedTurnAsync(threadId, CancellationToken.None);
                await MaybeContinueGoalIfIdleAsync(threadId, CancellationToken.None);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // Explicit CancelTurn call
                FinalizeStreamingAgentMessage();
                FinalizeStreamingReasoning();
                await RestoreUndrainedGuidanceAsync();
                await AccountGoalUsageAsync(
                    turnKey,
                    new TokenUsageInfo
                    {
                        InputTokens = inputTokens,
                        OutputTokens = outputTokens,
                        CachedInputTokens = cachedInputTokens,
                        CacheWriteInputTokens = cacheWriteInputTokens,
                        ReasoningOutputTokens = reasoningOutputTokens,
                        LlmCallCount = llmCallCount,
                        TotalTokens = inputTokens + outputTokens
                    },
                    turn.Id,
                    CancellationToken.None);
                await PauseActiveGoalForInterruptAsync(turnKey, CancellationToken.None);
                turn.Status = TurnStatus.Cancelled;
                turn.CompletedAt = DateTimeOffset.UtcNow;
                eventChannel.EmitTurnCancelled(turn, "Cancelled by request");
                ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.TurnCancelled);
                await PersistCancelledTurnAsync();
            }
            catch (OperationCanceledException) when (callerCt.IsCancellationRequested)
            {
                // Caller cancellation
                FinalizeStreamingAgentMessage();
                FinalizeStreamingReasoning();
                await RestoreUndrainedGuidanceAsync();
                turn.Status = TurnStatus.Cancelled;
                turn.CompletedAt = DateTimeOffset.UtcNow;
                eventChannel.EmitTurnCancelled(turn, "Caller cancelled");
                ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.TurnCancelled);
                await PersistCancelledTurnAsync();
            }
            catch (OperationCanceledException ex) when (IsConfiguredNetworkTimeoutCancellation(ex))
            {
                logger?.LogError(ex, "Turn execution failed due to network timeout for thread {ThreadId}", threadId);
                await FailAndPersistTurnAsync(ex.Message, "agent_error");
            }
            catch (OperationCanceledException)
            {
                // Preserve historical behavior for cancellation-shaped exceptions that
                // are not the SDK network timeout and are not tied to a known source.
                FinalizeStreamingAgentMessage();
                FinalizeStreamingReasoning();
                await RestoreUndrainedGuidanceAsync();
                turn.Status = TurnStatus.Cancelled;
                turn.CompletedAt = DateTimeOffset.UtcNow;
                eventChannel.EmitTurnCancelled(turn, "Caller cancelled");
                ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.TurnCancelled);
                await PersistCancelledTurnAsync();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Turn execution failed for thread {ThreadId}", threadId);

                // Step 5k-R: Reactive compaction on prompt-too-long / context-overflow errors.
                // On success we still fail this turn (the model's streaming response is
                // already gone), but the compacted history lets the user re-send their
                // prompt and succeed without any manual cleanup.
                var reactiveMessage = ex.Message;
                if (CompactionErrors.IsPromptTooLong(ex) && session is not null)
                {
                    try
                    {
                        eventChannel.EmitSystemEvent("compacting");
                        var status = await GetCompactionPipelineForThread(thread).TryReactiveCompactAsync(
                            session,
                            threadId,
                            thread.LastActiveAt,
                            CancellationToken.None);
                        if (status.Success)
                        {
                            tokenTracker?.Reset();
                            pendingCompactionCheckpoint = new PendingCompactionCheckpoint(
                                "reactive",
                                CompactionOutcomeToWire(status.Outcome),
                                status.ThresholdBefore.Tokens,
                                status.ThresholdAfter.Tokens);
                            InvalidatePromptRequestSnapshot(threadId, "reactive_compaction");
                            var contextUsage = await SaveContextUsageSnapshotAsync(
                                threadId,
                                status.ThresholdAfter.Tokens,
                                source: "compacted_estimate",
                                isEstimate: true,
                                ct: CancellationToken.None);
                            ClearContextUsageAnchor(threadId);
                            ReleaseStableContextPages(threadId);
                            traceCollector?.RecordContextCompaction(threadId);
                            eventChannel.EmitSystemEvent(
                                "compacted",
                                percentLeft: status.ThresholdAfter.PercentLeft,
                                tokenCount: status.ThresholdAfter.Tokens,
                                contextUsage: contextUsage);
                            {
                                var noticeItem = CreateCompactionNoticeItem(
                                    turn,
                                    NextItemSeq(),
                                    trigger: "reactive",
                                    status);
                                turn.Items.Add(noticeItem);
                                eventChannel.EmitItemStarted(noticeItem);
                                eventChannel.EmitItemCompleted(noticeItem);
                            }
                            ThreadRuntimeSignalForBroadcast?.Invoke(
                                threadId,
                                SessionThreadRuntimeSignal.ContextCompacted);
                            reactiveMessage =
                                "The request exceeded the model's context window. "
                                + "History has been compacted; please re-send the message.";
                        }
                        else
                        {
                            eventChannel.EmitSystemEvent(
                                "compactFailed",
                                message: status.FailureReason,
                                percentLeft: status.ThresholdAfter.PercentLeft,
                                tokenCount: status.ThresholdAfter.Tokens);
                        }
                    }
                    catch (Exception compactEx)
                    {
                        logger?.LogWarning(
                            compactEx,
                            "Reactive compaction failed for thread {ThreadId}",
                            threadId);
                    }
                }

                await FailAndPersistTurnAsync(
                    reactiveMessage,
                    "agent_error");
            }
            finally
            {
                approvalOverride?.Dispose();
                gateLock?.Dispose();
                if (_runtimeRegistry.TryGetRuntime(turnKey.ThreadId, out var runtime)
                    && runtime.TryRemoveTurn(turnKey.TurnId, out var removedTurnRuntime))
                {
                    removedTurnRuntime.Cancellation?.Dispose();
                }
                eventChannel.Complete();
            }
        }, CancellationToken.None); // Run regardless of caller ct; we handle it internally

        return eventChannel;

        int NextItemSeq() => Interlocked.Increment(ref itemSeq);
    }

    /// <inheritdoc/>
    public Task ResolveApprovalAsync(
        string threadId,
        string turnId,
        string requestId,
        SessionApprovalDecision decision,
        CancellationToken ct = default)
        => TurnControl.ResolveApproval(threadId, turnId, requestId, decision);

    /// <inheritdoc/>
    public Task ResolveUserInputRequestAsync(
        string threadId,
        string turnId,
        string requestId,
        RequestUserInputResponse response,
        CancellationToken ct = default)
        => TurnControl.ResolveUserInputRequest(threadId, turnId, requestId, response);

    /// <inheritdoc/>
    public Task CancelTurnAsync(string threadId, string turnId, CancellationToken ct = default)
        => TurnControl.CancelTurn(threadId, turnId);

    /// <inheritdoc/>
    public Task CancelThreadMaintenanceAsync(string threadId, CancellationToken ct = default)
        => TurnControl.CancelThreadMaintenance(threadId);

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
        => await ThreadQueue.EnqueueAsync(threadId, content, sender, ct, inputSnapshot);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<QueuedTurnInput>> RemoveQueuedTurnInputAsync(
        string threadId,
        string queuedInputId,
        CancellationToken ct = default)
        => await ThreadQueue.RemoveAsync(threadId, queuedInputId, ct);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<QueuedTurnInput>> ReorderQueuedTurnInputsAsync(
        string threadId,
        IReadOnlyList<string> orderedQueuedInputIds,
        CancellationToken ct = default)
        => await ThreadQueue.ReorderAsync(threadId, orderedQueuedInputIds, ct);

    /// <inheritdoc/>
    public async Task<TurnSteerResult> SteerTurnAsync(
        string threadId,
        string expectedTurnId,
        string queuedInputId,
        CancellationToken ct = default,
        SenderContext? sender = null)
        => await ThreadQueue.SteerAsync(threadId, expectedTurnId, queuedInputId, ct, sender);

    /// <inheritdoc/>
    public async Task<SessionThread> RollbackThreadAsync(string threadId, int numTurns, CancellationToken ct = default)
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

        var removedTurns = thread.Turns
            .Skip(thread.Turns.Count - numTurns)
            .ToList();
        thread.Turns.RemoveRange(thread.Turns.Count - numTurns, numTurns);
        thread.LastActiveAt = DateTimeOffset.UtcNow;

        await persistence.RollbackThreadAsync(thread, numTurns, ct);
        traceCollector?.RecordThreadRollback(threadId, thread.Id, numTurns, thread.Turns.Count, thread.LastActiveAt);
        InvalidatePromptRequestSnapshot(threadId, "rollback");
        ClearContextUsageAnchor(threadId);
        ForgetContextPages(threadId);
        var agent = GetThreadAgentOrDefault(threadId);
        var updatedSession = await TryUpdateSessionAfterRollbackAsync(agent, threadId, removedTurns, ct);
        await SaveContextUsageFromSessionAsync(threadId, updatedSession, ct);
        ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.TurnCompleted);
        return thread;
    }

    /// <inheritdoc/>
    public async Task<ThreadCompactResult> CompactThreadAsync(string threadId, CancellationToken ct = default)
        => await Maintenance.CompactAsync(threadId, ct);

    /// <inheritdoc/>
    public async Task<ThreadMemoryConsolidationResult> ConsolidateThreadMemoryAsync(
        string threadId,
        CancellationToken ct = default)
        => await Maintenance.ConsolidateMemoryAsync(threadId, ct);

    // =========================================================================
    // Configuration
    // =========================================================================

    /// <inheritdoc/>
    public async Task SetThreadModeAsync(string threadId, string mode, CancellationToken ct = default)
        => await ThreadConfig.SetModeAsync(threadId, mode, ct);

    /// <inheritdoc/>
    public async Task UpdateThreadConfigurationAsync(
        string threadId,
        ThreadConfiguration config,
        CancellationToken ct = default)
        => await ThreadConfig.UpdateAsync(threadId, config, ct);

    /// <inheritdoc />
    public async Task RefreshThreadAgentAsync(string threadId, CancellationToken ct = default)
        => await ThreadConfig.RefreshAgentAsync(threadId, ct);

    private async Task RebuildAgentAndPersistThreadAsync(SessionThread thread, CancellationToken ct)
    {
        using (await AcquireThreadAgentLockAsync(thread.Id, ct))
        {
            SetThreadAgent(thread.Id, await BuildAgentForThreadAsync(thread, ct));
            ReleaseStableContextPages(thread.Id);
            await PersistThreadWithMaterializationAsync(thread, ct);
        }
    }

    /// <inheritdoc />
    public void InvalidateThreadAgents()
        => ThreadConfig.InvalidateAgents();

    /// <inheritdoc/>
    public async Task RenameThreadAsync(string threadId, string displayName, CancellationToken ct = default)
        => await ThreadLifecycle.RenameAsync(threadId, displayName, ct);

    // =========================================================================
    // Private helpers
    // =========================================================================

    private CompactionPipeline GetCompactionPipelineForThread(string threadId) =>
        Maintenance.GetCompactionPipelineForThread(threadId);

    private CompactionPipeline GetCompactionPipelineForThread(SessionThread thread) =>
        Maintenance.GetCompactionPipelineForThread(thread);

    private CompactionPipeline GetCompactionPipelineForThread(string threadId, SessionThread? thread) =>
        Maintenance.GetCompactionPipelineForThread(threadId, thread);

    private void ReleaseStableContextPages(string threadId) =>
        agentFactory.ToolProviderContext.ContextPageManager?.ReleaseStablePages(threadId);

    private void ForgetContextPages(string threadId) =>
        agentFactory.ToolProviderContext.ContextPageManager?.ForgetThread(threadId);

    private void MarkMemoryContextDirty() =>
        agentFactory.ToolProviderContext.ContextPageManager?.MarkDirty(ContextPageKeys.MemoryLongTerm("*"));

    private async Task<SessionThread> GetOrLoadThreadAsync(string threadId, CancellationToken ct)
    {
        if (_runtimeRegistry.TryGetThread(threadId, out var cached))
            return cached;

        var thread = await persistence.LoadThreadAsync(threadId, ct)
                     ?? throw new KeyNotFoundException($"Thread '{threadId}' not found.");

        _runtimeRegistry.SetThread(thread);
        _runtimeRegistry.SetThread(thread).Materialized = true;
        return thread;
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

    internal void CompleteThreadMaintenance(string threadId, ThreadMaintenanceState state)
        => Maintenance.CompleteThreadMaintenance(threadId, state);

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
        => await ThreadQueue.TryStartNextAsync(threadId, ct);

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
    /// the workspace "longest task" aggregate (spec §27A.3). Keyed by the thread's main
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

    private async Task EnsurePerThreadAgentIfMissingAsync(
        string threadId, SessionThread thread, CancellationToken ct)
    {
        if (!_forcePerThreadAgents && !RequiresPerThreadAgent(thread))
            return;

        using (await AcquireThreadAgentLockAsync(threadId, ct))
        {
            if (!HasThreadAgent(threadId))
                SetThreadAgent(threadId, await BuildAgentForThreadAsync(thread, ct));
        }
    }

    private bool RequiresPerThreadAgent(SessionThread thread)
    {
        if (channelRuntimeToolProvider != null)
            return true;

        var config = thread.Configuration;
        if (config == null)
            return false;

        return HasAgentShapingConfiguration(config)
               || ThreadRuntimeDiffersFromCurrentDefault(config.ProviderId, config.Model)
               || ThreadReasoningDiffersFromCurrentDefault(config.Reasoning);
    }

    private bool ThreadRuntimeDiffersFromCurrentDefault(string? providerId, string? model)
    {
        if (string.IsNullOrWhiteSpace(providerId) && string.IsNullOrWhiteSpace(model))
            return false;

        try
        {
            var currentConfig = _appConfigMonitor?.Current ?? agentFactory.ToolProviderContext.Config;
            var currentDefault = agentFactory.ToolProviderContext.ChatClientRegistry.ResolveMainRuntime(currentConfig);
            if (!string.IsNullOrWhiteSpace(providerId)
                && !string.Equals(providerId.Trim(), currentDefault.ProviderId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(model)
                && !string.Equals(model.Trim(), currentDefault.Model, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private bool ThreadReasoningDiffersFromCurrentDefault(AppConfig.ReasoningConfig? reasoning)
    {
        if (reasoning == null)
            return false;

        var currentConfig = _appConfigMonitor?.Current ?? agentFactory.ToolProviderContext.Config;
        return reasoning.Enabled != currentConfig.Reasoning.Enabled
               || reasoning.Effort != currentConfig.Reasoning.Effort
               || reasoning.Output != currentConfig.Reasoning.Output;
    }

    private static bool HasAgentShapingConfiguration(ThreadConfiguration config)
    {
        if (!string.IsNullOrWhiteSpace(config.AgentProfileId))
            return true;
        if (!string.IsNullOrWhiteSpace(config.AgentProfileSource))
            return true;
        if (!string.IsNullOrWhiteSpace(config.AgentProfileFingerprint))
            return true;
        if (!string.Equals(config.Mode, "agent", StringComparison.OrdinalIgnoreCase))
            return true;
        if (config.McpServers is { Length: > 0 })
            return true;
        if (config.Extensions is { Length: > 0 })
            return true;
        if (config.CustomTools is { Length: > 0 })
            return true;
        if (!string.IsNullOrWhiteSpace(config.WorkspaceOverride))
            return true;
        if (!string.IsNullOrWhiteSpace(config.ExecutionWorkspaceOverride))
            return true;
        if (!string.IsNullOrWhiteSpace(config.ToolProfile))
            return true;
        if (config.UseToolProfileOnly)
            return true;
        if (!string.IsNullOrWhiteSpace(config.AgentInstructions))
            return true;
        if (config.ToolAllowList is { Length: > 0 })
            return true;
        if (config.ToolDenyList is { Length: > 0 })
            return true;
        if (HasPolicy(config.ToolPolicy))
            return true;
        if (HasPolicy(config.McpPolicy))
            return true;
        if (HasPolicy(config.PluginPolicy))
            return true;
        if (HasPolicy(config.SkillsPolicy))
            return true;
        if (HasPolicy(config.TeamsPolicy))
            return true;
        if (config.AgentControlToolAccess.HasValue)
            return true;
        if (config.AllowedAgentControlTools is { Length: > 0 })
            return true;
        if (!string.IsNullOrWhiteSpace(config.PromptProfile))
            return true;
        if (!string.IsNullOrWhiteSpace(config.RoleInstructions))
            return true;
        if (config.OverrideBasePrompt)
            return true;
        if (config.ApprovalPolicy != ApprovalPolicy.Default)
            return true;
        if (!string.IsNullOrWhiteSpace(config.AutomationTaskDirectory))
            return true;
        return config.RequireApprovalOutsideWorkspace.HasValue;
    }

    private static bool HasPolicy(ThreadToolPolicy? policy) =>
        policy != null
        && (policy.Allow != null
            || policy.Deny != null
            || !string.IsNullOrWhiteSpace(policy.AgentControl)
            || policy.AllowedAgentControlTools != null);

    private static bool HasPolicy(ThreadMcpPolicy? policy) =>
        policy != null
        && (policy.Servers != null || HasPolicy(policy.Tools));

    private static bool HasPolicy(ThreadNamePolicy? policy) =>
        policy != null && (policy.Allow != null || policy.Deny != null);

    private static bool HasPolicy(ThreadPluginPolicy? policy) =>
        policy != null && (policy.Allow != null || policy.Deny != null);

    private static bool HasPolicy(ThreadSkillsPolicy? policy) =>
        policy != null
        && (policy.Preload != null
            || policy.Allow != null
            || policy.Deny != null
            || policy.AllowManage.HasValue);

    private static bool HasPolicy(ThreadTeamsPolicy? policy) =>
        policy != null && !string.IsNullOrWhiteSpace(policy.ReservedTools);

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

    private static List<UserMessageImage> ExtractUserMessageImages(IList<AIContent> content)
    {
        var images = new List<UserMessageImage>();
        foreach (var part in content.OfType<DataContent>())
        {
            if (part.AdditionalProperties == null)
                continue;
            if (!TryGetStringProperty(part.AdditionalProperties, AppServer.AppServerInputMetadataKeys.LocalImagePath, out var path))
                continue;
            var image = new UserMessageImage
            {
                Path = path,
                MimeType = TryGetStringProperty(
                    part.AdditionalProperties,
                    AppServer.AppServerInputMetadataKeys.LocalImageMimeType,
                    out var mimeType)
                    ? mimeType
                    : null,
                FileName = TryGetStringProperty(
                    part.AdditionalProperties,
                    AppServer.AppServerInputMetadataKeys.LocalImageFileName,
                    out var fileName)
                    ? fileName
                    : null
            };
            images.Add(image);
        }
        return images;
    }

    private static bool TryGetStringProperty(
        IReadOnlyDictionary<string, object?> properties,
        string key,
        out string value)
    {
        value = string.Empty;
        if (!properties.TryGetValue(key, out var raw) || raw == null)
            return false;
        var text = raw as string ?? raw.ToString();
        if (string.IsNullOrWhiteSpace(text))
            return false;
        value = text.Trim();
        return true;
    }

    private static void RegisterCommandExecutionIfNeeded(
        FunctionCallContent functionCall,
        SessionTurn turn,
        Func<int> nextItemSeq,
        SessionEventChannel eventChannel,
        bool supportsCommandExecutionStreaming,
        string defaultWorkspacePath)
    {
        if (!string.Equals(functionCall.Name, "Exec", StringComparison.Ordinal))
            return;

        if (CommandExecutionRuntimeScope.Current is not { } runtime)
            return;

        var args = functionCall.Arguments;
        var command = args != null && args.TryGetValue("command", out var commandObj)
            ? commandObj?.ToString()
            : null;
        if (string.IsNullOrWhiteSpace(command))
            return;

        var workingDirectory = args != null && args.TryGetValue("workingDir", out var cwdObj)
            ? cwdObj?.ToString()
            : null;
        workingDirectory = !string.IsNullOrWhiteSpace(workingDirectory)
            ? Path.GetFullPath(workingDirectory)
            : defaultWorkspacePath;

        runtime.RegisterPendingShellExecution(new PendingShellExecutionRegistration
        {
            CallId = functionCall.CallId,
            Command = command,
            WorkingDirectory = workingDirectory,
            Source = "host"
        });

        if (!supportsCommandExecutionStreaming)
            return;

        var item = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(nextItemSeq()),
            TurnId = turn.Id,
            Type = ItemType.CommandExecution,
            Status = ItemStatus.Started,
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new CommandExecutionPayload
            {
                CallId = functionCall.CallId,
                Command = command,
                WorkingDirectory = workingDirectory,
                Source = "host",
                Status = "inProgress",
                AggregatedOutput = string.Empty
            }
        };
        turn.Items.Add(item);
        eventChannel.EmitItemStarted(item);
        runtime.RegisterPending(new PendingCommandExecutionRegistration
        {
            CallId = functionCall.CallId,
            Command = command,
            WorkingDirectory = workingDirectory,
            Source = "host",
            Item = item
        });
    }

    private static void RegisterToolExecutionIfNeeded(
        FunctionCallContent functionCall,
        SessionTurn turn,
        Func<int> nextItemSeq,
        SessionEventChannel eventChannel,
        bool supportsToolExecutionLifecycle)
    {
        if (!supportsToolExecutionLifecycle)
            return;

        if (ToolExecutionRuntimeScope.Current is not { } runtime)
            return;

        if (string.IsNullOrWhiteSpace(functionCall.CallId))
            return;

        var item = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(nextItemSeq()),
            TurnId = turn.Id,
            Type = ItemType.ToolExecution,
            Status = ItemStatus.Started,
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new ToolExecutionPayload
            {
                CallId = functionCall.CallId,
                ToolName = functionCall.Name,
                Status = "inProgress"
            }
        };
        turn.Items.Add(item);
        eventChannel.EmitItemStarted(item);
        runtime.RegisterPending(new PendingToolExecutionRegistration
        {
            CallId = functionCall.CallId,
            ToolName = functionCall.Name,
            Item = item
        });
    }

    private static void FailTurn(SessionTurn turn, SessionEventChannel channel, string errorMsg)
    {
        turn.Status = TurnStatus.Failed;
        turn.Error = errorMsg;
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

    private static bool EndsWithSuccessfulCreatePlanInPlanMode(SessionThread thread, SessionTurn turn)
    {
        if (!string.Equals(thread.Configuration?.Mode, "plan", StringComparison.OrdinalIgnoreCase))
            return false;

        for (var idx = turn.Items.Count - 1; idx >= 0; idx--)
        {
            if (turn.Items[idx].Payload is not ToolCallPayload toolCall)
                continue;

            if (!string.Equals(toolCall.ToolName, "CreatePlan", StringComparison.Ordinal))
                return false;

            return turn.Items
                .Where(item => item.Payload is ToolResultPayload)
                .Select(item => item.Payload as ToolResultPayload)
                .Any(result =>
                    result != null
                    && string.Equals(result.CallId, toolCall.CallId, StringComparison.Ordinal)
                    && result.Success);
        }

        return false;
    }

    private bool TryScheduleMemoryConsolidation(
        string threadId,
        SessionThread thread,
        SessionTurn turn,
        AgentSession session,
        SessionEventChannel eventChannel,
        Func<int> nextItemSequence)
        => Maintenance.TryScheduleMemoryConsolidation(
            threadId,
            thread,
            turn,
            session,
            eventChannel,
            nextItemSequence);

    private async Task TryAppendCompactionCheckpointAsync(
        string threadId,
        string coveredThroughTurnId,
        AgentSession session,
        PendingCompactionCheckpoint checkpoint,
        CancellationToken ct)
        => await Maintenance.TryAppendCompactionCheckpointAsync(
            threadId,
            coveredThroughTurnId,
            session,
            checkpoint,
            ct);

    private static bool TrySnapshotInMemoryHistory(
        AgentSession session,
        out IReadOnlyList<ChatMessage> history)
    {
        history = [];
        if (!session.TryGetInMemoryChatHistory(
                out var messages,
                jsonSerializerOptions: SessionPersistenceJsonOptions.Default))
        {
            return false;
        }

        history = messages.ToList();
        return true;
    }

    private async Task TrySaveThreadAsync(SessionThread thread)
    {
        if (IsPendingPermanentDeletion(thread.Id))
            return;

        try
        {
            await PersistThreadWithMaterializationAsync(thread, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to persist thread state for thread {ThreadId}", thread.Id);
        }
    }

    private static bool TryAppendFailedTurnTailToSession(AgentSession session, SessionTurn turn)
    {
        if (!session.TryGetInMemoryChatHistory(
                out var history,
                jsonSerializerOptions: SessionPersistenceJsonOptions.Default))
        {
            return false;
        }

        var turnTail = ThreadStore.BuildModelVisibleHistoryFromTurn(turn);
        if (turnTail.Count == 0)
            return true;

        var overlap = FindHistoryTailOverlap(history, turnTail);
        if (overlap >= turnTail.Count)
            return true;

        var merged = new List<ChatMessage>(history.Count + turnTail.Count - overlap);
        merged.AddRange(history);
        for (var i = overlap; i < turnTail.Count; i++)
            merged.Add(turnTail[i]);

        session.SetInMemoryChatHistory(merged, jsonSerializerOptions: SessionPersistenceJsonOptions.Default);
        return true;
    }

    private async Task<AgentSession?> TryUpdateSessionAfterRollbackAsync(
        AIAgent agent,
        string threadId,
        IReadOnlyList<SessionTurn> removedTurns,
        CancellationToken ct)
    {
        try
        {
            var session = await persistence.LoadOrCreateSessionAsync(agent, threadId, ct);
            if (TryTrimRolledBackTailFromSession(session, removedTurns))
            {
                await TrySaveSessionAsync(agent, session, threadId);
                return session;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to trim agent session after rollback for thread {ThreadId}", threadId);
        }

        await TryRebuildAndSaveSessionAsync(agent, threadId);

        try
        {
            return await persistence.LoadOrCreateSessionAsync(agent, threadId, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to load rebuilt agent session after rollback for thread {ThreadId}", threadId);
            return null;
        }
    }

    private async Task SaveContextUsageFromSessionAsync(
        string threadId,
        AgentSession? session,
        CancellationToken ct)
    {
        try
        {
            var tokens = 0L;
            if (session is not null && TrySnapshotInMemoryHistory(session, out var history) && history.Count > 0)
                tokens = MessageTokenEstimator.Estimate(history);

            await SaveContextUsageSnapshotAsync(
                threadId,
                tokens,
                source: "history_estimate",
                isEstimate: true,
                ct: ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to update context usage after rollback for thread {ThreadId}", threadId);
        }
    }

    private static bool TryTrimRolledBackTailFromSession(
        AgentSession session,
        IReadOnlyList<SessionTurn> removedTurns)
    {
        if (!session.TryGetInMemoryChatHistory(
                out var history,
                jsonSerializerOptions: SessionPersistenceJsonOptions.Default))
        {
            return false;
        }

        var removedTail = new List<ChatMessage>();
        foreach (var turn in removedTurns.OrderBy(t => t.StartedAt).ThenBy(t => t.Id, StringComparer.Ordinal))
            removedTail.AddRange(ThreadStore.BuildModelVisibleHistoryFromTurn(turn));

        if (removedTail.Count == 0)
            return true;
        if (removedTail.Count > history.Count)
            return false;

        var historyStart = history.Count - removedTail.Count;
        for (var i = 0; i < removedTail.Count; i++)
        {
            if (ChatMessagesEquivalent(history[historyStart + i], removedTail[i]))
                continue;

            return false;
        }

        var trimmed = history.Take(historyStart).ToList();
        session.SetInMemoryChatHistory(trimmed, jsonSerializerOptions: SessionPersistenceJsonOptions.Default);
        return true;
    }

    private static int FindHistoryTailOverlap(
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ChatMessage> tail)
    {
        var max = Math.Min(history.Count, tail.Count);
        for (var length = max; length > 0; length--)
        {
            var historyStart = history.Count - length;
            var matched = true;
            for (var i = 0; i < length; i++)
            {
                if (ChatMessagesEquivalent(history[historyStart + i], tail[i]))
                    continue;

                matched = false;
                break;
            }

            if (matched)
                return length;
        }

        return 0;
    }

    private static bool ChatMessagesEquivalent(ChatMessage left, ChatMessage right)
    {
        if (left.Role != right.Role)
            return false;

        var leftContents = BuildContentSignatures(left);
        var rightContents = BuildContentSignatures(right);
        return leftContents.SequenceEqual(rightContents, StringComparer.Ordinal);
    }

    private static List<string> BuildContentSignatures(ChatMessage message)
    {
        var signatures = new List<string>();
        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case TextContent text:
                {
                    var normalized = NormalizeTextForHistorySignature(text.Text);
                    if (!string.IsNullOrWhiteSpace(normalized))
                        signatures.Add("text:" + normalized);
                    break;
                }
                case TextReasoningContent reasoning:
                {
                    if (ReasoningContentHelper.TryGetText(reasoning, out var reasoningText) &&
                        !string.IsNullOrWhiteSpace(reasoningText))
                    {
                        signatures.Add("reasoning:" + reasoningText.Trim());
                    }

                    break;
                }
                case FunctionCallContent call:
                    signatures.Add($"call:{call.CallId}:{call.Name}");
                    break;
                case FunctionResultContent result:
                    signatures.Add(
                        $"result:{result.CallId}:{ImageContentSanitizingChatClient.DescribeResult(result.Result)}");
                    break;
                default:
                    signatures.Add($"{content.GetType().FullName}:{content}");
                    break;
            }
        }

        return signatures;
    }

    private static string NormalizeTextForHistorySignature(string? text)
    {
        var normalized = StripSystemReminderBlocks(text).Trim();
        var runtimeContextIndex = normalized.IndexOf("\n[Runtime Context]", StringComparison.Ordinal);
        if (runtimeContextIndex >= 0)
            normalized = normalized[..runtimeContextIndex].Trim();
        return normalized;
    }

    private static string BuildSubAgentMailboxModelText(IReadOnlyList<SubAgentMailboxEntry> entries)
    {
        var messages = entries
            .Select(entry => entry.Message.Trim())
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToArray();
        if (messages.Length == 0)
            return string.Empty;
        if (messages.Length == 1)
            return messages[0];

        var sb = new StringBuilder();
        sb.AppendLine("## SubAgent Mailbox");
        sb.AppendLine();
        foreach (var entry in entries)
        {
            var message = entry.Message.Trim();
            if (string.IsNullOrWhiteSpace(message))
                continue;

            sb.Append("From ");
            sb.Append(entry.SenderAgentPath);
            sb.AppendLine(":");
            sb.AppendLine(message);
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    private static string BuildSubAgentMailboxDisplayText(IReadOnlyList<SubAgentMailboxEntry> entries)
    {
        if (entries.Count == 1)
            return $"SubAgent message from {entries[0].SenderAgentPath}";

        return $"{entries.Count} SubAgent messages";
    }

    private static string StripSystemReminderBlocks(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        const string startTag = "<system-reminder>";
        const string endTag = "</system-reminder>";

        var result = text;
        var searchStart = 0;
        while (searchStart < result.Length)
        {
            var start = result.IndexOf(startTag, searchStart, StringComparison.Ordinal);
            if (start < 0)
                break;

            var end = result.IndexOf(endTag, start + startTag.Length, StringComparison.Ordinal);
            var removeLength = end < 0
                ? result.Length - start
                : end + endTag.Length - start;
            result = result.Remove(start, removeLength);
            searchStart = start;
        }

        return result;
    }

    private async Task<bool> TrySaveSessionAsync(AIAgent agent, AgentSession session, string threadId)
    {
        if (IsPendingPermanentDeletion(threadId))
            return false;

        if (!_runtimeRegistry.TryGetThread(threadId, out var thread) || thread.HistoryMode != HistoryMode.Server)
            return false;
            
        try
        {
            await persistence.SaveSessionAsync(agent, session, threadId, ct: CancellationToken.None);
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to save agent session for thread {ThreadId}", threadId);
            return false;
        }
    }

    private async Task TryRebuildAndSaveSessionAsync(AIAgent agent, string threadId)
    {
        if (IsPendingPermanentDeletion(threadId))
            return;

        if (!_runtimeRegistry.TryGetThread(threadId, out var thread) || thread.HistoryMode != HistoryMode.Server)
            return;

        try
        {
            await persistence.RebuildAndSaveSessionFromThreadAsync(agent, threadId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to rebuild agent session for thread {ThreadId}; clearing stale session.", threadId);
            try
            {
                persistence.DeleteSessionFile(threadId);
            }
            catch (Exception deleteEx)
            {
                logger?.LogWarning(deleteEx, "Failed to clear stale agent session for thread {ThreadId}", threadId);
            }
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
            _runtimeRegistry.SetThread(thread).Materialized = false;
            return;
        }

        await persistence.SaveThreadAsync(thread, ct);
        _runtimeRegistry.SetThread(thread).Materialized = true;
    }

    private async Task PersistThreadIfMaterializedAsync(SessionThread thread, CancellationToken ct)
    {
        if (IsPendingPermanentDeletion(thread.Id))
            return;
        if (!IsMaterialized(thread.Id))
            return;
        await PersistThreadWithMaterializationAsync(thread, ct);
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
        long TokensAfter);

    private async Task<AIAgent> BuildAgentForThreadAsync(
        SessionThread thread,
        CancellationToken ct)
    {
        var previousSessionKey = TracingChatClient.CurrentSessionKey;
        TracingChatClient.CurrentSessionKey = thread.Id;
        try
        {
            return await BuildAgentForThreadCoreAsync(thread, ct);
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = previousSessionKey;
        }
    }

    private async Task<AIAgent> BuildAgentForThreadCoreAsync(
        SessionThread thread,
        CancellationToken ct)
    {
        var threadId = thread.Id;
        var threadRuntimeState = _runtimeRegistry.SetThread(thread);
        var config = thread.Configuration ?? new ThreadConfiguration();
        var mode = config.Mode.Equals("plan", StringComparison.OrdinalIgnoreCase)
            ? AgentMode.Plan
            : AgentMode.Agent;
        var mm = GetOrCreateModeManager(threadId, mode);
        var baseCtx = agentFactory.ToolProviderContext;
        var currentConfig = _appConfigMonitor?.Current ?? baseCtx.Config;
        var agentControlToolAccess = ResolveAgentControlToolAccess(thread);
        var runtime = baseCtx.ChatClientRegistry.ResolveMainRuntime(currentConfig, config.ProviderId, config.Model);
        var effectiveMainModel = runtime.Model;
        var threadChatClient = ResolveThreadChatClient(baseCtx, runtime);
        var externalCliSessionStore = new ThreadExternalCliSessionStore(thread);
        var threadBaseContext = CloneContextWithChatClient(
            baseCtx,
            currentConfig,
            threadChatClient,
            runtime.ProviderId,
            runtime.Protocol,
            effectiveMainModel,
            externalCliSessionStore,
            thread);

        ToolProviderContext? scopedContext = null;
        if (!string.IsNullOrEmpty(config.WorkspaceOverride))
        {
            var craftPath = Path.Combine(config.WorkspaceOverride, ".craft");
            Directory.CreateDirectory(craftPath);

            var scopedMemory = new MemoryStore(craftPath);
            var scopedDreamStore = new DreamStore(craftPath);
            var scopedSkills = new SkillsLoader(craftPath);

            scopedContext = new ToolProviderContext
            {
                Config = currentConfig,
                ChatClient = threadChatClient,
                ChatClientRegistry = baseCtx.ChatClientRegistry,
                EffectiveProviderId = runtime.ProviderId,
                EffectiveProviderProtocol = runtime.Protocol,
                EffectiveMainModel = effectiveMainModel,
                EffectiveReasoning = CloneReasoningConfig(config.Reasoning ?? currentConfig.Reasoning),
                WorkspacePath = config.WorkspaceOverride,
                BotPath = craftPath,
                MemoryStore = scopedMemory,
                DreamStore = scopedDreamStore,
                SkillsLoader = scopedSkills,
                ContextPageManager = baseCtx.ContextPageManager,
                ThreadSystemPromptContextProviders = baseCtx.ThreadSystemPromptContextProviders,
                ApprovalService = baseCtx.ApprovalService,
                PathBlacklist = new PathBlacklist([]),
                BackgroundTerminalService = baseCtx.BackgroundTerminalService,
                TraceCollector = baseCtx.TraceCollector,
                LspServerManager = baseCtx.LspServerManager,
                AcpExtensionProxy = baseCtx.AcpExtensionProxy,
                NodeReplProxy = baseCtx.NodeReplProxy,
                CronTools = baseCtx.CronTools,
                McpClientManager = baseCtx.McpClientManager,
                DeferredToolRegistry = baseCtx.DeferredToolRegistry,
                ExternalCliSessionStore = externalCliSessionStore,
                AutomationTaskDirectory = config.AutomationTaskDirectory,
                RequireApprovalOutsideWorkspace = config.RequireApprovalOutsideWorkspace,
                CurrentThreadId = thread.Id,
                CurrentThreadSource = thread.Source,
                AgentBuilderTargetId = config.AgentBuilderTargetId,
                AgentBuilderTargetSource = config.AgentBuilderTargetSource,
                CurrentOriginChannel = thread.OriginChannel,
                CurrentChannelContext = thread.ChannelContext,
                AgentControlToolAccess = agentControlToolAccess,
                AllowedAgentControlTools = ResolveAllowedAgentControlTools(config),
                ToolAllowList = ToSet(config.ToolAllowList),
                ToolDenyList = ToSet(config.ToolDenyList),
                PromptProfile = config.PromptProfile,
                RoleInstructions = config.RoleInstructions
            };
        }

        var toolContext = scopedContext ?? threadBaseContext;
        if (!string.IsNullOrWhiteSpace(config.ExecutionWorkspaceOverride))
            toolContext = CloneContextWithExecutionWorkspace(toolContext, config.ExecutionWorkspaceOverride);

        if (config.McpServers is { Length: > 0 })
        {
            if (threadRuntimeState.McpManager != null)
            {
                await threadRuntimeState.McpManager.DisposeAsync();
                threadRuntimeState.McpManager = null;
            }

            var threadMcpManager = new McpClientManager();
            await threadMcpManager.ConnectAsync(config.McpServers, ct);
            await threadMcpManager.WaitForStartupCompletionAsync(ct);
            threadRuntimeState.McpManager = threadMcpManager;
            toolContext = CloneContextWithMcpManager(toolContext, threadMcpManager);
        }
        else if (toolContext.McpClientManager != null)
        {
            await toolContext.McpClientManager.WaitForStartupCompletionAsync(ct);
        }

        toolContext.DeferredToolRegistry = null;
        var capabilityPolicy = new ThreadCapabilityPolicyEvaluator(config, toolContext);
        toolContext.ToolCallPolicy = capabilityPolicy.EvaluateCall;
        toolContext.ToolInvocationPolicy = capabilityPolicy.EvaluateInvocation;

        List<AITool>? profileTools = null;
        if (!string.IsNullOrEmpty(config.ToolProfile))
        {
            if (toolProfileRegistry == null
                || !toolProfileRegistry.TryGet(config.ToolProfile, out var profileProviders)
                || profileProviders == null)
            {
                throw new InvalidOperationException($"Tool profile '{config.ToolProfile}' is not registered.");
            }

            profileTools = agentFactory.CreateToolsFromProviders(profileProviders, toolContext);
        }

        if (config.UseToolProfileOnly)
        {
            if (profileTools is not { Count: > 0 })
                throw new InvalidOperationException("UseToolProfileOnly requires a registered ToolProfile with at least one tool.");
            ApplyThreadToolFilters(profileTools, capabilityPolicy);
            DeferredToolLoadingPlanner.Apply(profileTools, toolContext);
            ApplyThreadToolFilters(profileTools, capabilityPolicy);
            threadRuntimeState.CurrentTools = profileTools.ToArray();
            return agentFactory.CreateAgentWithTools(profileTools, mm, toolContext, config.AgentInstructions);
        }

        var toolsWithMcp = agentFactory.CreateToolsForMode(mode, toolContext);
        if (profileTools != null)
            toolsWithMcp.AddRange(profileTools);
        AppendChannelTools(toolsWithMcp, thread);
        ApplyThreadToolFilters(toolsWithMcp, capabilityPolicy);
        DeferredToolLoadingPlanner.Apply(toolsWithMcp, toolContext);
        ApplyThreadToolFilters(toolsWithMcp, capabilityPolicy);
        threadRuntimeState.CurrentTools = toolsWithMcp.ToArray();
        return agentFactory.CreateAgentWithTools(toolsWithMcp, mm, toolContext);
    }

    private static void ApplyThreadToolFilters(List<AITool> tools, ThreadCapabilityPolicyEvaluator policy) =>
        tools.RemoveAll(tool => !policy.AllowsTool(tool));

    private void AppendChannelTools(List<AITool> tools, SessionThread thread)
    {
        if (channelRuntimeToolProvider != null)
        {
            var reservedNames = new HashSet<string>(tools.Select(t => t.Name), StringComparer.Ordinal);
            var channelTools = channelRuntimeToolProvider.CreateToolsForThread(thread, reservedNames);
            if (channelTools.Count > 0)
                tools.AddRange(channelTools);
        }

        var pluginFunctionToolNames = tools
            .OfType<IPluginFunctionTool>()
            .Select(tool => tool.PluginFunctionDescriptor?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);

        if (_runtimeRegistry.TryGetRuntime(thread.Id, out var runtime))
            runtime.PluginFunctionToolNames = pluginFunctionToolNames.Count == 0 ? null : pluginFunctionToolNames;

        var dynamicToolNames = tools
            .OfType<IDynamicToolRuntimeTool>()
            .Select(tool => tool.Spec.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);

        if (_runtimeRegistry.TryGetRuntime(thread.Id, out runtime))
            runtime.DynamicToolNames = dynamicToolNames.Count == 0 ? null : dynamicToolNames;
    }

    private IReadOnlySet<string> GetPluginFunctionToolNames(string threadId)
        => _runtimeRegistry.TryGetRuntime(threadId, out var runtime)
            ? runtime.PluginFunctionToolNames ?? EmptyPluginFunctionToolNames
            : EmptyPluginFunctionToolNames;

    private static bool IsPluginFunctionTool(IReadOnlySet<string> pluginFunctionToolNames, string? toolName)
        => !string.IsNullOrWhiteSpace(toolName) && pluginFunctionToolNames.Contains(toolName);

    private IReadOnlySet<string> GetDynamicToolNames(string threadId)
        => _runtimeRegistry.TryGetRuntime(threadId, out var runtime)
            ? runtime.DynamicToolNames ?? EmptyDynamicToolNames
            : EmptyDynamicToolNames;

    private static bool IsDynamicTool(IReadOnlySet<string> dynamicToolNames, string? toolName)
        => !string.IsNullOrWhiteSpace(toolName) && dynamicToolNames.Contains(toolName);

    private static IChatClient ResolveThreadChatClient(ToolProviderContext baseContext, EffectiveModelRuntime runtime) =>
        baseContext.ChatClientRegistry.GetChatClient(runtime);

    private static ToolProviderContext CloneContextWithChatClient(
        ToolProviderContext source,
        AppConfig config,
        IChatClient chatClient,
        string effectiveProviderId,
        string effectiveProviderProtocol,
        string effectiveMainModel,
        IExternalCliSessionStore? externalCliSessionStore = null,
        SessionThread? thread = null)
    {
        var cloned = new ToolProviderContext
        {
            Config = config,
            ChatClient = chatClient,
            ChatClientRegistry = source.ChatClientRegistry,
            EffectiveProviderId = effectiveProviderId,
            EffectiveProviderProtocol = effectiveProviderProtocol,
            EffectiveMainModel = effectiveMainModel,
            EffectiveReasoning = CloneReasoningConfig(thread?.Configuration?.Reasoning ?? config.Reasoning),
            WorkspacePath = source.WorkspacePath,
            BotPath = source.BotPath,
            MemoryStore = source.MemoryStore,
            DreamStore = source.DreamStore,
            SkillsLoader = source.SkillsLoader,
            ContextPageManager = source.ContextPageManager,
            ThreadSystemPromptContextProviders = source.ThreadSystemPromptContextProviders,
            ApprovalService = source.ApprovalService,
            PathBlacklist = source.PathBlacklist,
            BackgroundTerminalService = source.BackgroundTerminalService,
            CronTools = source.CronTools,
            McpClientManager = source.McpClientManager,
            LspServerManager = source.LspServerManager,
            TraceCollector = source.TraceCollector,
            AcpExtensionProxy = source.AcpExtensionProxy,
            NodeReplProxy = source.NodeReplProxy,
            ExternalCliSessionStore = externalCliSessionStore ?? source.ExternalCliSessionStore,
            AgentFileSystem = source.AgentFileSystem,
            AutomationTaskDirectory = source.AutomationTaskDirectory,
            RequireApprovalOutsideWorkspace = source.RequireApprovalOutsideWorkspace,
            DeferredToolRegistry = source.DeferredToolRegistry,
            CurrentThreadId = thread?.Id ?? source.CurrentThreadId,
            CurrentThreadSource = thread?.Source ?? source.CurrentThreadSource,
            AgentBuilderTargetId = thread?.Configuration?.AgentBuilderTargetId ?? source.AgentBuilderTargetId,
            AgentBuilderTargetSource = thread?.Configuration?.AgentBuilderTargetSource ?? source.AgentBuilderTargetSource,
            CurrentOriginChannel = thread?.OriginChannel ?? source.CurrentOriginChannel,
            CurrentChannelContext = thread?.ChannelContext ?? source.CurrentChannelContext,
            AgentControlToolAccess = thread == null
                ? source.AgentControlToolAccess
                : ResolveAgentControlToolAccess(thread),
            AllowedAgentControlTools = thread == null ? source.AllowedAgentControlTools : ResolveAllowedAgentControlTools(thread.Configuration),
            ToolAllowList = thread == null ? source.ToolAllowList : ToSet(thread.Configuration?.ToolAllowList),
            ToolDenyList = thread == null ? source.ToolDenyList : ToSet(thread.Configuration?.ToolDenyList),
            ToolCallPolicy = source.ToolCallPolicy,
            ToolInvocationPolicy = source.ToolInvocationPolicy,
            PromptProfile = thread?.Configuration?.PromptProfile ?? source.PromptProfile,
            RoleInstructions = thread?.Configuration?.RoleInstructions ?? source.RoleInstructions
        };
        return cloned;
    }

    private static ToolProviderContext CloneContextWithExecutionWorkspace(
        ToolProviderContext source,
        string executionWorkspacePath) =>
        new()
        {
            Config = source.Config,
            ChatClient = source.ChatClient,
            ChatClientRegistry = source.ChatClientRegistry,
            EffectiveProviderId = source.EffectiveProviderId,
            EffectiveProviderProtocol = source.EffectiveProviderProtocol,
            EffectiveMainModel = source.EffectiveMainModel,
            EffectiveReasoning = CloneReasoningConfig(source.EffectiveReasoning),
            WorkspacePath = executionWorkspacePath,
            BotPath = source.BotPath,
            MemoryStore = source.MemoryStore,
            DreamStore = source.DreamStore,
            SkillsLoader = source.SkillsLoader,
            ContextPageManager = source.ContextPageManager,
            ThreadSystemPromptContextProviders = source.ThreadSystemPromptContextProviders,
            SkillMutationApplier = source.SkillMutationApplier,
            ApprovalService = source.ApprovalService,
            PathBlacklist = source.PathBlacklist,
            BackgroundTerminalService = source.BackgroundTerminalService,
            CronTools = source.CronTools,
            McpClientManager = source.McpClientManager,
            LspServerManager = source.LspServerManager,
            TraceCollector = source.TraceCollector,
            AcpExtensionProxy = source.AcpExtensionProxy,
            NodeReplProxy = source.NodeReplProxy,
            ExternalCliSessionStore = source.ExternalCliSessionStore,
            AgentFileSystem = new HostAgentFileSystem(executionWorkspacePath),
            AutomationTaskDirectory = source.AutomationTaskDirectory,
            RequireApprovalOutsideWorkspace = source.RequireApprovalOutsideWorkspace,
            DeferredToolRegistry = source.DeferredToolRegistry,
            CurrentThreadId = source.CurrentThreadId,
            CurrentThreadSource = source.CurrentThreadSource,
            AgentBuilderTargetId = source.AgentBuilderTargetId,
            AgentBuilderTargetSource = source.AgentBuilderTargetSource,
            CurrentOriginChannel = source.CurrentOriginChannel,
            CurrentChannelContext = source.CurrentChannelContext,
            AgentControlToolAccess = source.AgentControlToolAccess,
            AllowedAgentControlTools = source.AllowedAgentControlTools,
            ToolAllowList = source.ToolAllowList,
            ToolDenyList = source.ToolDenyList,
            ToolCallPolicy = source.ToolCallPolicy,
            ToolInvocationPolicy = source.ToolInvocationPolicy,
            PromptProfile = source.PromptProfile,
            RoleInstructions = source.RoleInstructions
        };

    private static ToolProviderContext CloneContextWithMcpManager(
        ToolProviderContext source,
        McpClientManager mcpClientManager) =>
        new()
        {
            Config = source.Config,
            ChatClient = source.ChatClient,
            ChatClientRegistry = source.ChatClientRegistry,
            EffectiveProviderId = source.EffectiveProviderId,
            EffectiveProviderProtocol = source.EffectiveProviderProtocol,
            EffectiveMainModel = source.EffectiveMainModel,
            EffectiveReasoning = CloneReasoningConfig(source.EffectiveReasoning),
            WorkspacePath = source.WorkspacePath,
            BotPath = source.BotPath,
            MemoryStore = source.MemoryStore,
            DreamStore = source.DreamStore,
            SkillsLoader = source.SkillsLoader,
            ContextPageManager = source.ContextPageManager,
            ThreadSystemPromptContextProviders = source.ThreadSystemPromptContextProviders,
            SkillMutationApplier = source.SkillMutationApplier,
            ApprovalService = source.ApprovalService,
            PathBlacklist = source.PathBlacklist,
            BackgroundTerminalService = source.BackgroundTerminalService,
            CronTools = source.CronTools,
            McpClientManager = mcpClientManager,
            LspServerManager = source.LspServerManager,
            TraceCollector = source.TraceCollector,
            AcpExtensionProxy = source.AcpExtensionProxy,
            NodeReplProxy = source.NodeReplProxy,
            ExternalCliSessionStore = source.ExternalCliSessionStore,
            AgentFileSystem = source.AgentFileSystem,
            AutomationTaskDirectory = source.AutomationTaskDirectory,
            RequireApprovalOutsideWorkspace = source.RequireApprovalOutsideWorkspace,
            CurrentThreadId = source.CurrentThreadId,
            CurrentThreadSource = source.CurrentThreadSource,
            AgentBuilderTargetId = source.AgentBuilderTargetId,
            AgentBuilderTargetSource = source.AgentBuilderTargetSource,
            CurrentOriginChannel = source.CurrentOriginChannel,
            CurrentChannelContext = source.CurrentChannelContext,
            AgentControlToolAccess = source.AgentControlToolAccess,
            AllowedAgentControlTools = source.AllowedAgentControlTools,
            ToolAllowList = source.ToolAllowList,
            ToolDenyList = source.ToolDenyList,
            ToolCallPolicy = source.ToolCallPolicy,
            ToolInvocationPolicy = source.ToolInvocationPolicy,
            PromptProfile = source.PromptProfile,
            RoleInstructions = source.RoleInstructions
        };

    private static IReadOnlySet<string>? ToSet(IEnumerable<string>? values)
    {
        if (values == null)
            return null;

        var set = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);
        return set.Count == 0 ? null : set;
    }

    private static IReadOnlySet<string>? ResolveAllowedAgentControlTools(ThreadConfiguration? config)
    {
        if (config == null)
            return null;

        var legacyRequiresAllowList = config.AgentControlToolAccess == AgentControlToolAccess.AllowList;
        var structuredRequiresAllowList =
            ParseAgentControlToolAccess(config.ToolPolicy?.AgentControl) == AgentControlToolAccess.AllowList;
        if (!legacyRequiresAllowList && !structuredRequiresAllowList)
            return null;

        HashSet<string>? result = null;
        if (legacyRequiresAllowList)
            result = ToSetPreserveEmpty(config.AllowedAgentControlTools);

        if (structuredRequiresAllowList)
        {
            var structured = ToSetPreserveEmpty(config.ToolPolicy?.AllowedAgentControlTools);
            result = result == null
                ? structured
                : Intersect(result, structured);
        }

        return result ?? new HashSet<string>(StringComparer.Ordinal);
    }

    private static HashSet<string> ToSetPreserveEmpty(IEnumerable<string>? values)
    {
        if (values == null)
            return new HashSet<string>(StringComparer.Ordinal);

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> Intersect(IReadOnlySet<string> left, IReadOnlySet<string> right)
    {
        var result = new HashSet<string>(left, StringComparer.Ordinal);
        result.IntersectWith(right);
        return result;
    }

    internal static bool TryRemoveStreamingToolCallIndexByItemReference(
        Dictionary<int, SessionItem>? streamingToolCallItemsByIndex,
        SessionItem targetItem)
    {
        if (streamingToolCallItemsByIndex == null)
            return false;

        int? matchedIndex = null;
        foreach (var kvp in streamingToolCallItemsByIndex)
        {
            if (!ReferenceEquals(kvp.Value, targetItem))
                continue;
            matchedIndex = kvp.Key;
            break;
        }

        return matchedIndex.HasValue && streamingToolCallItemsByIndex.Remove(matchedIndex.Value);
    }
}
