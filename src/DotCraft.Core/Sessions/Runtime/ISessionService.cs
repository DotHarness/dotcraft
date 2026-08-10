using Microsoft.Extensions.AI;
using DotCraft.Sessions.Wire;
using ContextUsageSnapshot = DotCraft.Sessions.Wire.ContextUsageSnapshot;

namespace DotCraft.Sessions;

/// <summary>
/// Controls which identity fields participate in thread discovery.
/// </summary>
public enum ThreadDiscoveryScope
{
    /// <summary>Match workspace, user, and channel context, plus explicitly allowed origins.</summary>
    Identity,

    /// <summary>Match every thread in the exact workspace regardless of user or channel identity.</summary>
    Workspace
}

/// <summary>
/// The central Session Core API consumed by all Channel Adapters.
/// Manages Thread/Turn/Item lifecycle, event emission, and persistence.
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// Creates a new Thread for the given identity.
    /// </summary>
    /// <param name="identity">Channel and user context for the new Thread.</param>
    /// <param name="config">Optional per-thread agent configuration. Null means workspace defaults.</param>
    /// <param name="historyMode">
    /// <see cref="HistoryMode.Server"/> (default): Session Core manages conversation history.
    /// <see cref="HistoryMode.Client"/>: The adapter provides message history with each SubmitInput call.
    /// </param>
    /// <param name="threadId">
    /// Optional pre-assigned thread ID. When provided, this ID is used instead of generating a new one.
    /// The caller is responsible for ensuring uniqueness (e.g. by using <see cref="SessionIdGenerator.NewThreadId"/>).
    /// </param>
    /// <param name="displayName">
    /// Optional explicit display name for the thread. If null, a display name is automatically set
    /// from the first user message text during <see cref="SubmitInputAsync"/>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<SessionThread> CreateThreadAsync(
        SessionIdentity identity,
        ThreadConfiguration? config = null,
        HistoryMode historyMode = HistoryMode.Server,
        string? threadId = null,
        string? displayName = null,
        CancellationToken ct = default,
        ThreadSource? source = null);

    /// <summary>
    /// Archives reusable threads for the identity and creates a fresh thread.
    /// Used by <c>/new</c> semantics.
    /// </summary>
    Task<ThreadResetResult> ResetConversationAsync(
        SessionIdentity identity,
        ThreadConfiguration? config = null,
        HistoryMode historyMode = HistoryMode.Server,
        string? displayName = null,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a sibling Thread by copying history from an existing thread up to an optional fork point.
    /// When no explicit fork display name is supplied, the fork uses the source thread's visible display
    /// name, falling back to the first retained user message.
    /// </summary>
    Task<SessionThread> ForkThreadAsync(
        string threadId,
        ThreadForkOptions? options = null,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Thread fork is not supported by this session service.");

    /// <summary>
    /// Creates a Git worktree, copies dirty source changes when requested, then forks
    /// the source thread with its execution workspace bound to the new worktree.
    /// </summary>
    Task<WorktreeCreateAndForkResult> CreateWorktreeAndForkAsync(
        WorktreeCreateAndForkOptions options,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Git worktree handoff is not supported by this session service.");

    /// <summary>
    /// Creates a Git worktree and starts a new thread with its execution workspace
    /// bound to that worktree.
    /// </summary>
    Task<WorktreeCreateAndStartResult> CreateWorktreeAndStartAsync(
        WorktreeCreateAndStartOptions options,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Git worktree start is not supported by this session service.");

    /// <summary>
    /// Moves an existing thread between the local workspace and a DotCraft-managed
    /// Git worktree without changing the thread ID.
    /// </summary>
    Task<WorktreeHandoffResult> HandoffThreadWorktreeAsync(
        WorktreeHandoffOptions options,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Git worktree handoff is not supported by this session service.");

    /// <summary>
    /// Ensures a named managed worktree exists and is bound as the execution workspace of an existing thread.
    /// </summary>
    Task<WorktreeEnsureResult> EnsureManagedWorktreeAsync(
        WorktreeEnsureOptions options,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Git worktree ensure is not supported by this session service.");

    /// <summary>
    /// Sets a thread's execution workspace without moving its state workspace.
    /// </summary>
    Task<SessionThread> ConfigureThreadExecutionWorkspaceAsync(
        ThreadExecutionWorkspaceOptions options,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Thread execution workspace configuration is not supported by this session service.");

    /// <summary>
    /// Force-removes a managed worktree and optionally its branch.
    /// </summary>
    Task RemoveManagedWorktreeAsync(
        WorktreeRemoveOptions options,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Git worktree removal is not supported by this session service.");

    /// <summary>
    /// Lists DotCraft-managed Git worktrees registered through thread metadata.
    /// </summary>
    Task<IReadOnlyList<ThreadWorktreeStatus>> ListWorktreesAsync(
        SessionIdentity? identity = null,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ThreadWorktreeStatus>>([]);

    /// <summary>
    /// Returns current Git status for the DotCraft-managed worktree bound to a thread.
    /// </summary>
    Task<ThreadWorktreeStatus> GetWorktreeStatusAsync(
        string threadId,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Git worktree status is not supported by this session service.");

    /// <summary>
    /// Resumes a Paused or previously inactive Thread.
    /// Loads Thread state and reconstructs a runtime agent session from canonical rollout history.
    /// </summary>
    Task<SessionThread> ResumeThreadAsync(string threadId, CancellationToken ct = default);

    /// <summary>
    /// Flushes and exports the latest executable Session for a durable server-managed Thread into
    /// restricted workspace-local recovery staging.
    /// </summary>
    Task<ThreadRecoveryPackage> ExportThreadRecoveryAsync(
        string threadId,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Thread recovery export is not supported by this session service.");

    /// <summary>
    /// Validates and atomically restores a JSON recovery snapshot under its original Thread ID.
    /// The target Thread must not already exist in active, archived, or in-memory state.
    /// </summary>
    Task<string> RestoreThreadRecoveryAsync(
        string packagePath,
        string expectedThreadId,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Thread recovery restore is not supported by this session service.");

    /// <summary>
    /// Transitions an Active Thread to Paused status.
    /// </summary>
    Task PauseThreadAsync(string threadId, CancellationToken ct = default);

    /// <summary>
    /// Returns the current goal attached to a thread, if one exists.
    /// </summary>
    Task<ThreadGoal?> GetThreadGoalAsync(string threadId, CancellationToken ct = default) =>
        throw new NotSupportedException("Thread goals are not supported by this session service.");

    /// <summary>
    /// Loads stored Interactive Tool UI <c>widgetState</c> for a thread, keyed by <c>callId</c>
    /// (M-iv). UI-only; never surfaced to the model. Default: none.
    /// </summary>
    IReadOnlyDictionary<string, string> GetItemWidgetStates(string threadId) =>
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Upserts (non-empty) or clears (null/empty) an Interactive Tool UI item's <c>widgetState</c>,
    /// keyed by <c>callId</c> (M-iv). Decoupled from the conversation — no turn/item. Default: no-op.
    /// </summary>
    void SetItemWidgetState(string threadId, string callId, string? widgetStateJson) { }

    /// <summary>
    /// Creates, replaces, or updates the current goal attached to a thread.
    /// </summary>
    Task<ThreadGoal> SetThreadGoalAsync(
        string threadId,
        ThreadGoalUpdate update,
        GoalSetMode mode = GoalSetMode.UpsertOrUpdate,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Thread goals are not supported by this session service.");

    /// <summary>
    /// Clears the current goal attached to a thread.
    /// </summary>
    Task<ThreadGoalClearResult> ClearThreadGoalAsync(string threadId, CancellationToken ct = default) =>
        throw new NotSupportedException("Thread goals are not supported by this session service.");

    /// <summary>
    /// Transitions a Thread to Archived status. Archived threads are read-only.
    /// </summary>
    Task ArchiveThreadAsync(string threadId, CancellationToken ct = default);

    /// <summary>
    /// Restores an Archived Thread to Active status.
    /// </summary>
    Task UnarchiveThreadAsync(string threadId, CancellationToken ct = default);

    /// <summary>
    /// Discovers Threads matching the given identity (workspace + user + channel context).
    /// Returns summaries ordered by LastActiveAt descending.
    /// </summary>
    /// <param name="identity"></param>
    /// <param name="includeArchived"></param>
    /// <param name="crossChannelOrigins">
    /// When non-null and non-empty, also includes threads that match workspace + userId and have
    /// <see cref="ThreadSummary.OriginChannel"/> in this list (case-insensitive), ignoring channel context.
    /// </param>
    /// <param name="ct"></param>
    /// <param name="includeSubAgents">Whether session-backed sub-agent threads are included.</param>
    /// <param name="scope">Identity-scoped discovery by default; workspace scope ignores user, channel context, and origin.</param>
    Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(
        SessionIdentity identity,
        bool includeArchived = false,
        IReadOnlyList<string>? crossChannelOrigins = null,
        CancellationToken ct = default,
        bool includeSubAgents = false,
        ThreadDiscoveryScope scope = ThreadDiscoveryScope.Identity);

    /// <summary>
    /// Counts non-internal, top-level (non-sub-agent) threads in <paramref name="workspacePath"/>,
    /// across all users and channels and including archived threads. Used for workspace-scoped
    /// metrics (e.g. the Profile page) where identity/channel scoping is not desired.
    /// </summary>
    Task<int> CountWorkspaceThreadsAsync(string workspacePath, CancellationToken ct = default);

    Task UpsertThreadSpawnEdgeAsync(ThreadSpawnEdge edge, CancellationToken ct = default);

    Task SetThreadSpawnEdgeStatusAsync(
        string parentThreadId,
        string childThreadId,
        string status,
        CancellationToken ct = default);

    Task<IReadOnlyList<ThreadSpawnEdge>> ListSubAgentChildrenAsync(
        string parentThreadId,
        bool includeClosed = false,
        CancellationToken ct = default);

    Task AddSubAgentMailboxEntryAsync(SubAgentMailboxEntry entry, CancellationToken ct = default) =>
        throw new NotSupportedException("SubAgent mailbox is not supported by this session service.");

    Task<IReadOnlyList<SubAgentMailboxEntry>> ListPendingSubAgentMailboxAsync(
        string rootThreadId,
        string targetAgentPath,
        CancellationToken ct = default) =>
        throw new NotSupportedException("SubAgent mailbox is not supported by this session service.");

    Task MarkSubAgentMailboxDeliveredAsync(
        string rootThreadId,
        IReadOnlyList<string> entryIds,
        DateTimeOffset deliveredAt,
        CancellationToken ct = default) =>
        throw new NotSupportedException("SubAgent mailbox is not supported by this session service.");

    /// <summary>
    /// Subscribes to thread-level events independently of turn execution.
    /// Multiple passive subscribers may observe the same thread concurrently.
    /// </summary>
    IAsyncEnumerable<SessionEvent> SubscribeThreadAsync(
        string threadId,
        bool replayRecent = false,
        CancellationToken ct = default);

    /// <summary>
    /// Submits user input to a Thread, starting a new Turn.
    /// Returns an async event stream that delivers Turn/Item lifecycle events.
    /// </summary>
    /// <param name="threadId">Target Thread ID.</param>
    /// <param name="content">
    /// User's input as a list of <see cref="AIContent"/> parts (text, images, etc.).
    /// For text-only input, use the <c>string</c> extension method in <see cref="SessionServiceExtensions"/>.
    /// </param>
    /// <param name="sender">Sender identity and role for group sessions. Null for single-user channels.</param>
    /// <param name="messages">
    /// Client-provided conversation history for client-managed history mode (Section 15).
    /// Null for server-managed mode (Session Core loads history from persistence).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="inputSnapshot"></param>
    IAsyncEnumerable<SessionEvent> SubmitInputAsync(
        string threadId,
        IList<AIContent> content,
        SenderContext? sender = null,
        ChatMessage[]? messages = null,
        CancellationToken ct = default,
        SessionInputSnapshot? inputSnapshot = null);

    /// <summary>
    /// Adds user input to the thread's FIFO queue while another turn is active.
    /// Queued input starts a new turn automatically after the active turn completes successfully.
    /// </summary>
    Task<QueuedTurnInput> EnqueueTurnInputAsync(
        string threadId,
        IList<AIContent> content,
        SenderContext? sender = null,
        CancellationToken ct = default,
        SessionInputSnapshot? inputSnapshot = null);

    /// <summary>
    /// Attempts to start the next queued input if the thread is currently idle.
    /// Implementations must not preempt a running turn, pending approval, pending model input,
    /// or active thread maintenance task.
    /// </summary>
    Task TryStartNextQueuedTurnAsync(string threadId, CancellationToken ct = default) =>
        Task.CompletedTask;

    /// <summary>
    /// Removes one queued input from a thread.
    /// </summary>
    Task<IReadOnlyList<QueuedTurnInput>> RemoveQueuedTurnInputAsync(
        string threadId,
        string queuedInputId,
        CancellationToken ct = default);

    /// <summary>
    /// Replaces the current queued input order for a thread.
    /// </summary>
    Task<IReadOnlyList<QueuedTurnInput>> ReorderQueuedTurnInputsAsync(
        string threadId,
        IReadOnlyList<string> orderedQueuedInputIds,
        CancellationToken ct = default);

    /// <summary>
    /// Sets one queued input's desired status before it is admitted into a turn.
    /// </summary>
    Task<IReadOnlyList<QueuedTurnInput>> UpdateQueuedTurnInputAsync(
        string threadId,
        string queuedInputId,
        string expectedTurnId,
        string status,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves a pending approval request within a WaitingApproval Turn.
    /// Resumes agent execution with the user's decision.
    /// </summary>
    Task ResolveApprovalAsync(
        string threadId,
        string turnId,
        string requestId,
        SessionApprovalDecision decision,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves a pending model-initiated user input request within a WaitingInput Turn.
    /// Resumes agent tool execution with the client's answers.
    /// </summary>
    Task ResolveUserInputRequestAsync(
        string threadId,
        string turnId,
        string requestId,
        RequestUserInputResponse response,
        CancellationToken ct = default);

    /// <summary>
    /// Cancels a Running, WaitingApproval, or WaitingInput Turn.
    /// </summary>
    Task CancelTurnAsync(string threadId, string turnId, CancellationToken ct = default);

    /// <summary>
    /// Terminates all background terminal processes owned by the given Thread.
    /// </summary>
    Task CleanBackgroundTerminalsAsync(string threadId, CancellationToken ct = default);

    /// <summary>
    /// Drops one or more turns from the end of a Thread.
    /// This modifies conversation history only; it does not revert filesystem changes made by tools.
    /// </summary>
    Task<SessionThread> RollbackThreadAsync(string threadId, int numTurns, CancellationToken ct = default);

    /// <summary>
    /// Changes the agent mode for a Thread (e.g., "agent" → "plan").
    /// Rebuilds the agent's tool set. Does not create a Turn.
    /// </summary>
    Task SetThreadModeAsync(string threadId, string mode, CancellationToken ct = default);

    /// <summary>
    /// Manually compacts the model-visible context for an idle server-managed Thread.
    /// Emits thread-scoped system events and persists a compaction notice on success.
    /// </summary>
    Task<ThreadCompactResult> CompactThreadAsync(string threadId, CancellationToken ct = default) =>
        throw new NotSupportedException("Manual context compaction is not supported by this session service.");

    /// <summary>
    /// Manually consolidates the model-visible context for an idle server-managed Thread
    /// into durable workspace memory.
    /// Emits thread-scoped system events and persists a memory notice on success.
    /// </summary>
    Task<ThreadMemoryConsolidationResult> ConsolidateThreadMemoryAsync(string threadId, CancellationToken ct = default) =>
        throw new NotSupportedException("Manual memory consolidation is not supported by this session service.");

    /// <summary>
    /// Requests cancellation for active thread-level maintenance, such as manual compaction or memory consolidation.
    /// Implementations may treat this as a no-op when no maintenance is active.
    /// </summary>
    Task CancelThreadMaintenanceAsync(string threadId, CancellationToken ct = default) =>
        Task.CompletedTask;

    /// <summary>
    /// Updates the per-thread agent configuration (e.g., MCP servers, extensions).
    /// </summary>
    Task UpdateThreadConfigurationAsync(
        string threadId,
        ThreadConfiguration config,
        CancellationToken ct = default);

    /// <summary>
    /// Applies sticky cwd/runtime-root overrides and rebuilds the effective thread agent.
    /// A null roots value preserves the current list; an empty list replaces it with no roots.
    /// </summary>
    Task<SessionThread> UpdateThreadWorkspaceAsync(
        string threadId,
        string? cwd,
        IReadOnlyList<string>? runtimeWorkspaceRoots,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Thread workspace updates are not supported by this session service.");

    /// <summary>
    /// Updates the per-thread source-control write target metadata.
    /// </summary>
    Task<SessionThread> UpdateThreadSourceControlTargetAsync(
        string threadId,
        ThreadSourceControlTarget? target,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Thread source-control target updates are not supported by this session service.");

    /// <summary>
    /// Returns the full Thread state including all Turns and Items.
    /// </summary>
    Task<SessionThread> GetThreadAsync(string threadId, CancellationToken ct = default);

    /// <summary>
    /// Returns the provider-neutral persisted Thread header without historical Turns or Items.
    /// </summary>
    Task<ThreadHistorySnapshot> ReadThreadSnapshotAsync(
        string threadId,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Paged Thread history is not supported by this session service.");

    /// <summary>
    /// Returns one bounded page of persisted Turn metadata without Items.
    /// </summary>
    Task<ThreadHistoryPage<SessionTurn>> ListThreadTurnsAsync(
        string threadId,
        ThreadHistoryCursor? cursor,
        int limit,
        ThreadHistorySortDirection direction,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Paged Thread history is not supported by this session service.");

    /// <summary>
    /// Returns one bounded page of persisted Items, optionally restricted to a Turn.
    /// </summary>
    Task<ThreadHistoryPage<ThreadHistoryItem>> ListThreadItemsAsync(
        string threadId,
        string? turnId,
        ThreadHistoryCursor? cursor,
        int limit,
        ThreadHistorySortDirection direction,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Paged Thread history is not supported by this session service.");

    /// <summary>
    /// Loads the Thread into the in-memory cache (same as <see cref="GetThreadAsync"/>)
    /// and ensures the per-thread agent is built when <see cref="SessionThread.Configuration"/> is non-null.
    /// Does not change thread status, persist, or emit <c>thread/resumed</c>.
    /// Use before turn execution when the thread may have been loaded from disk only (e.g. after host restart).
    /// </summary>
    Task<SessionThread> EnsureThreadLoadedAsync(string threadId, CancellationToken ct = default);

    /// <summary>
    /// Permanently deletes a Thread and all its associated files from disk.
    /// Unlike <see cref="ArchiveThreadAsync"/>, this operation is irreversible.
    /// </summary>
    Task DeleteThreadPermanentlyAsync(string threadId, CancellationToken ct = default);

    /// <summary>
    /// Updates the display name of a Thread.
    /// </summary>
    Task RenameThreadAsync(string threadId, string displayName, CancellationToken ct = default);

    /// <summary>
    /// Returns the persisted context-window usage snapshot for the thread.
    /// Returns null when no context usage state exists yet; otherwise a valid
    /// snapshot (tokens may be zero).
    /// </summary>
    ContextUsageSnapshot? TryGetContextUsageSnapshot(string threadId);

    /// <summary>
    /// Returns the current process-local runtime snapshot for a loaded Thread.
    /// Implementations should include active thread maintenance when available.
    /// </summary>
    ThreadSummaryRuntime GetThreadRuntimeSnapshot(SessionThread thread) =>
        ThreadSummaryRuntime.FromThread(thread);

    /// <summary>
    /// Optional hook invoked after a thread is successfully created and persisted (any channel).
    /// Hosts use this to notify all wire clients (e.g. broadcast <c>thread/started</c> on AppServer).
    /// </summary>
    Action<SessionThread>? ThreadCreatedForBroadcast { get; set; }

    /// <summary>
    /// Optional hook invoked after <see cref="DeleteThreadPermanentlyAsync"/> completes successfully
    /// (memory and disk state removed). Hosts use this to notify all wire clients (e.g. broadcast
    /// <c>thread/deleted</c> on AppServer).
    /// </summary>
    Action<string>? ThreadDeletedForBroadcast { get; set; }

    /// <summary>
    /// Optional hook invoked after a thread's display name is updated in Session Core (successful
    /// <see cref="RenameThreadAsync"/> or first user message auto-title). Hosts broadcast
    /// <c>thread/renamed</c> on AppServer so UIs keep thread lists in sync.
    /// </summary>
    Action<SessionThread>? ThreadRenamedForBroadcast { get; set; }

    /// <summary>
    /// Optional hook invoked after a thread's metadata or execution workspace changes in Session Core.
    /// Hosts broadcast <c>thread/updated</c> on AppServer so UIs refresh compact thread state.
    /// </summary>
    Action<SessionThread>? ThreadUpdatedForBroadcast
    {
        get => null;
        set { }
    }

    /// <summary>
    /// Optional hook invoked after a thread's lifecycle status changes in Session Core. Hosts broadcast
    /// <c>thread/statusChanged</c> on AppServer so UIs keep thread lists synchronized.
    /// </summary>
    Action<string, ThreadStatus, ThreadStatus>? ThreadStatusChangedForBroadcast { get; set; }

    /// <summary>
    /// Optional hook invoked when a thread's aggregated runtime state changes in a way that may
    /// affect workspace-level activity indicators (running, waiting approval, waiting plan confirmation).
    /// Hosts may aggregate these signals and broadcast lightweight snapshots such as
    /// <c>thread/runtimeChanged</c> to connected clients.
    /// </summary>
    Action<string, SessionThreadRuntimeSignal>? ThreadRuntimeSignalForBroadcast { get; set; }

    /// <summary>
    /// Optional hook invoked after a thread goal changes in Session Core or goal runtime.
    /// Hosts broadcast <c>thread/goal/updated</c> so clients keep goal controls synchronized.
    /// </summary>
    Action<ThreadGoal, string?>? ThreadGoalUpdatedForBroadcast
    {
        get => null;
        set { }
    }

    /// <summary>
    /// Optional hook invoked after a thread goal is cleared in Session Core.
    /// Hosts broadcast <c>thread/goal/cleared</c> so clients drop cached goal snapshots.
    /// </summary>
    Action<string>? ThreadGoalClearedForBroadcast
    {
        get => null;
        set { }
    }
}
