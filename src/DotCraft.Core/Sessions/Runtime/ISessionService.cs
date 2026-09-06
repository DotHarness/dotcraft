using Microsoft.Extensions.AI;
using DotCraft.Sessions.Wire;
using ContextUsageSnapshot = DotCraft.Sessions.Wire.ContextUsageSnapshot;

namespace DotCraft.Sessions;

/// <summary>Controls which identity fields participate in thread discovery.</summary>
public enum ThreadDiscoveryScope
{
    /// <summary>Match workspace, user, and channel context, plus explicitly allowed origins.</summary>
    Identity,

    /// <summary>Match every thread in the exact workspace regardless of user or channel identity.</summary>
    Workspace
}

/// <summary>Provides the Session Core API for thread, turn, and item lifecycles.</summary>
public interface ISessionService
{
    /// <summary>Creates a Thread for the given identity.</summary>
    Task<SessionThread> CreateThreadAsync(
        SessionIdentity identity,
        ThreadConfiguration? config = null,
        HistoryMode historyMode = HistoryMode.Server,
        string? threadId = null,
        string? displayName = null,
        CancellationToken ct = default,
        ThreadSource? source = null);

    /// <summary>Archives reusable threads for an identity and creates a new Thread.</summary>
    Task<ThreadResetResult> ResetConversationAsync(
        SessionIdentity identity,
        ThreadConfiguration? config = null,
        HistoryMode historyMode = HistoryMode.Server,
        string? displayName = null,
        CancellationToken ct = default);

    /// <summary>Forks a Thread at an optional history point.</summary>
    Task<SessionThread> ForkThreadAsync(
        string threadId,
        ThreadForkOptions? options = null,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Thread fork is not supported by this session service.");

    /// <summary>Creates a managed worktree and forks a Thread into it.</summary>
    Task<WorktreeCreateAndForkResult> CreateWorktreeAndForkAsync(
        WorktreeCreateAndForkOptions options,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Git worktree handoff is not supported by this session service.");

    /// <summary>Creates a managed worktree and starts a Thread in it.</summary>
    Task<WorktreeCreateAndStartResult> CreateWorktreeAndStartAsync(
        WorktreeCreateAndStartOptions options,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Git worktree start is not supported by this session service.");

    /// <summary>Moves a Thread between its workspace and a managed worktree.</summary>
    Task<WorktreeHandoffResult> HandoffThreadWorktreeAsync(
        WorktreeHandoffOptions options,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Git worktree handoff is not supported by this session service.");

    /// <summary>Ensures a managed worktree exists and is bound to a Thread.</summary>
    Task<WorktreeEnsureResult> EnsureManagedWorktreeAsync(
        WorktreeEnsureOptions options,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Git worktree ensure is not supported by this session service.");

    /// <summary>Sets a Thread's execution workspace without moving its state workspace.</summary>
    Task<SessionThread> ConfigureThreadExecutionWorkspaceAsync(
        ThreadExecutionWorkspaceOptions options,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Thread execution workspace configuration is not supported by this session service.");

    /// <summary>Removes a managed worktree and optionally its branch.</summary>
    Task RemoveManagedWorktreeAsync(
        WorktreeRemoveOptions options,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Git worktree removal is not supported by this session service.");

    /// <summary>Lists managed worktrees registered through Thread metadata.</summary>
    Task<IReadOnlyList<ThreadWorktreeStatus>> ListWorktreesAsync(
        SessionIdentity? identity = null,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ThreadWorktreeStatus>>([]);

    /// <summary>Returns Git status for a Thread's managed worktree.</summary>
    Task<ThreadWorktreeStatus> GetWorktreeStatusAsync(
        string threadId,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Git worktree status is not supported by this session service.");

    /// <summary>
    /// Resumes a Thread from canonical history. Cold loading first cancels a non-terminal Turn left by the previous process.
    /// </summary>
    Task<SessionThread> ResumeThreadAsync(string threadId, CancellationToken ct = default);

    /// <summary>Exports a durable Thread into workspace-local recovery staging.</summary>
    Task<ThreadRecoveryPackage> ExportThreadRecoveryAsync(
        string threadId,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Thread recovery export is not supported by this session service.");

    /// <summary>Restores a validated recovery snapshot under its original Thread ID.</summary>
    Task<string> RestoreThreadRecoveryAsync(
        string packagePath,
        string expectedThreadId,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Thread recovery restore is not supported by this session service.");

    /// <summary>Pauses an active Thread.</summary>
    Task PauseThreadAsync(string threadId, CancellationToken ct = default);

    /// <summary>Returns the current goal attached to a Thread.</summary>
    Task<ThreadGoal?> GetThreadGoalAsync(string threadId, CancellationToken ct = default) =>
        throw new NotSupportedException("Thread goals are not supported by this session service.");

    /// <summary>Returns UI-only tool widget state keyed by call ID.</summary>
    IReadOnlyDictionary<string, string> GetItemWidgetStates(string threadId) =>
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Sets or clears UI-only tool widget state.</summary>
    void SetItemWidgetState(string threadId, string callId, string? widgetStateJson) { }

    /// <summary>Creates, replaces, or updates a Thread goal.</summary>
    Task<ThreadGoal> SetThreadGoalAsync(
        string threadId,
        ThreadGoalUpdate update,
        GoalSetMode mode = GoalSetMode.UpsertOrUpdate,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Thread goals are not supported by this session service.");

    /// <summary>Clears a Thread goal.</summary>
    Task<ThreadGoalClearResult> ClearThreadGoalAsync(string threadId, CancellationToken ct = default) =>
        throw new NotSupportedException("Thread goals are not supported by this session service.");

    /// <summary>Archives a Thread, making it read-only.</summary>
    Task ArchiveThreadAsync(string threadId, CancellationToken ct = default);

    /// <summary>Restores an archived Thread to active status.</summary>
    Task UnarchiveThreadAsync(string threadId, CancellationToken ct = default);

    /// <summary>Finds Threads in the requested identity or workspace scope.</summary>
    Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(
        SessionIdentity identity,
        bool includeArchived = false,
        IReadOnlyList<string>? crossChannelOrigins = null,
        CancellationToken ct = default,
        bool includeSubAgents = false,
        ThreadDiscoveryScope scope = ThreadDiscoveryScope.Identity);

    /// <summary>Counts top-level Threads in a workspace, including archived Threads.</summary>
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

    /// <summary>Subscribes to Thread events independently of Turn execution.</summary>
    IAsyncEnumerable<SessionEvent> SubscribeThreadAsync(
        string threadId,
        bool replayRecent = false,
        CancellationToken ct = default);

    /// <summary>Starts a Turn and streams its lifecycle events.</summary>
    IAsyncEnumerable<SessionEvent> SubmitInputAsync(
        string threadId,
        IList<AIContent> content,
        SenderContext? sender = null,
        ChatMessage[]? messages = null,
        CancellationToken ct = default,
        SessionInputSnapshot? inputSnapshot = null);

    /// <summary>Queues input for a later Turn.</summary>
    Task<QueuedTurnInput> EnqueueTurnInputAsync(
        string threadId,
        IList<AIContent> content,
        SenderContext? sender = null,
        CancellationToken ct = default,
        SessionInputSnapshot? inputSnapshot = null);

    /// <summary>Adds guidance to the active regular Turn.</summary>
    Task<string> SteerTurnAsync(
        string threadId,
        string expectedTurnId,
        IList<AIContent> content,
        SenderContext? sender = null,
        CancellationToken ct = default,
        SessionInputSnapshot? inputSnapshot = null);

    /// <summary>Starts the next queued input when the Thread is idle.</summary>
    Task TryStartNextQueuedTurnAsync(string threadId, CancellationToken ct = default) =>
        Task.CompletedTask;

    /// <summary>Removes queued input from a Thread.</summary>
    Task<IReadOnlyList<QueuedTurnInput>> RemoveQueuedTurnInputAsync(
        string threadId,
        string queuedInputId,
        CancellationToken ct = default);

    /// <summary>Reorders all queued input for a Thread.</summary>
    Task<IReadOnlyList<QueuedTurnInput>> ReorderQueuedTurnInputsAsync(
        string threadId,
        IReadOnlyList<string> orderedQueuedInputIds,
        CancellationToken ct = default);

    /// <summary>Updates queued input before Turn admission.</summary>
    Task<IReadOnlyList<QueuedTurnInput>> UpdateQueuedTurnInputAsync(
        string threadId,
        string queuedInputId,
        string expectedTurnId,
        string status,
        CancellationToken ct = default);

    /// <summary>Resolves a pending approval request.</summary>
    Task ResolveApprovalAsync(
        string threadId,
        string turnId,
        string requestId,
        SessionApprovalDecision decision,
        CancellationToken ct = default);

    /// <summary>Resolves a pending model-initiated input request.</summary>
    Task ResolveUserInputRequestAsync(
        string threadId,
        string turnId,
        string requestId,
        RequestUserInputResponse response,
        CancellationToken ct = default);

    /// <summary>
    /// Cancels a non-terminal Turn. Cold loading first cancels a Turn left by the previous process.
    /// </summary>
    Task CancelTurnAsync(string threadId, string turnId, CancellationToken ct = default);

    /// <summary>Terminates all background terminals owned by a Thread.</summary>
    Task CleanBackgroundTerminalsAsync(string threadId, CancellationToken ct = default);

    /// <summary>
    /// Removes Turns from the end of a Thread without reverting tool side effects.
    /// </summary>
    Task<SessionThread> RollbackThreadAsync(string threadId, int numTurns, CancellationToken ct = default);

    /// <summary>Changes a Thread's agent mode.</summary>
    Task SetThreadModeAsync(string threadId, string mode, CancellationToken ct = default);

    /// <summary>Compacts model-visible context for an idle server-managed Thread.</summary>
    Task<ThreadCompactResult> CompactThreadAsync(string threadId, CancellationToken ct = default) =>
        throw new NotSupportedException("Manual context compaction is not supported by this session service.");

    /// <summary>Consolidates model-visible context into durable workspace memory.</summary>
    Task<ThreadMemoryConsolidationResult> ConsolidateThreadMemoryAsync(string threadId, CancellationToken ct = default) =>
        throw new NotSupportedException("Manual memory consolidation is not supported by this session service.");

    /// <summary>Cancels active Thread maintenance.</summary>
    Task CancelThreadMaintenanceAsync(string threadId, CancellationToken ct = default) =>
        Task.CompletedTask;

    /// <summary>Updates a Thread's agent configuration.</summary>
    Task UpdateThreadConfigurationAsync(
        string threadId,
        ThreadConfiguration config,
        CancellationToken ct = default);

    /// <summary>
    /// Updates sticky workspace overrides and rebuilds the effective agent.
    /// </summary>
    Task<SessionThread> UpdateThreadWorkspaceAsync(
        string threadId,
        string? cwd,
        IReadOnlyList<string>? runtimeWorkspaceRoots,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Thread workspace updates are not supported by this session service.");

    /// <summary>Updates a Thread's source-control write target.</summary>
    Task<SessionThread> UpdateThreadSourceControlTargetAsync(
        string threadId,
        ThreadSourceControlTarget? target,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Thread source-control target updates are not supported by this session service.");

    /// <summary>
    /// Returns full Thread state. Cold loading first cancels a non-terminal Turn left by the previous process.
    /// </summary>
    Task<SessionThread> GetThreadAsync(string threadId, CancellationToken ct = default);

    /// <summary>Returns the ordered sources of a Thread's stable AGENTS.md snapshot.</summary>
    Task<IReadOnlyList<string>> GetInstructionSourcesAsync(
        string threadId,
        CancellationToken ct = default);

    /// <summary>Returns a persisted Thread header without history.</summary>
    Task<ThreadHistorySnapshot> ReadThreadSnapshotAsync(
        string threadId,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Paged Thread history is not supported by this session service.");

    /// <summary>Returns a bounded page of persisted Turns without Items.</summary>
    Task<ThreadHistoryPage<SessionTurn>> ListThreadTurnsAsync(
        string threadId,
        ThreadHistoryCursor? cursor,
        int limit,
        ThreadHistorySortDirection direction,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Paged Thread history is not supported by this session service.");

    /// <summary>Returns a bounded page of persisted Items.</summary>
    Task<ThreadHistoryPage<ThreadHistoryItem>> ListThreadItemsAsync(
        string threadId,
        string? turnId,
        ThreadHistoryCursor? cursor,
        int limit,
        ThreadHistorySortDirection direction,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Paged Thread history is not supported by this session service.");

    /// <summary>
    /// Loads a Thread and prepares its configured agent without resuming it. Cold loading first cancels a non-terminal Turn left by the previous process.
    /// </summary>
    Task<SessionThread> EnsureThreadLoadedAsync(string threadId, CancellationToken ct = default);

    /// <summary>Permanently deletes a Thread and its files.</summary>
    Task DeleteThreadPermanentlyAsync(string threadId, CancellationToken ct = default);

    /// <summary>Updates a Thread's display name.</summary>
    Task RenameThreadAsync(string threadId, string displayName, CancellationToken ct = default);

    /// <summary>Returns persisted context-window usage when available.</summary>
    ContextUsageSnapshot? TryGetContextUsageSnapshot(string threadId);

    /// <summary>Returns the process-local runtime snapshot for a loaded Thread.</summary>
    ThreadSummaryRuntime GetThreadRuntimeSnapshot(SessionThread thread) =>
        ThreadSummaryRuntime.FromThread(thread);

    /// <summary>Notifies hosts after a Thread is created and persisted.</summary>
    Action<SessionThread>? ThreadCreatedForBroadcast { get; set; }

    /// <summary>Notifies hosts after a Thread is permanently deleted.</summary>
    Action<string>? ThreadDeletedForBroadcast { get; set; }

    /// <summary>Notifies hosts after a Thread display name changes.</summary>
    Action<SessionThread>? ThreadRenamedForBroadcast { get; set; }

    /// <summary>Notifies hosts after Thread metadata or execution workspace changes.</summary>
    Action<SessionThread>? ThreadUpdatedForBroadcast
    {
        get => null;
        set { }
    }

    /// <summary>Notifies hosts after a Thread lifecycle status changes.</summary>
    Action<string, ThreadStatus, ThreadStatus>? ThreadStatusChangedForBroadcast { get; set; }

    /// <summary>Notifies hosts when a Thread runtime signal changes.</summary>
    Action<string, SessionThreadRuntimeSignal, SessionTurn?>? ThreadRuntimeSignalForBroadcast { get; set; }

    /// <summary>Notifies hosts after a Thread goal changes.</summary>
    Action<ThreadGoal, string?>? ThreadGoalUpdatedForBroadcast
    {
        get => null;
        set { }
    }

    /// <summary>Notifies hosts after a Thread goal is cleared.</summary>
    Action<string>? ThreadGoalClearedForBroadcast
    {
        get => null;
        set { }
    }
}
