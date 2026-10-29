using Microsoft.Extensions.AI;
using DotCraft.Sessions.Wire;
using ContextUsageSnapshot = DotCraft.Sessions.Wire.ContextUsageSnapshot;
using DotCraft.GenerativeUi;
using DotCraft.Visualizations;

namespace DotCraft.Sessions;

public enum ThreadDiscoveryScope
{
    /// <summary>Match workspace, user, and channel context, plus explicitly allowed origins.</summary>
    Identity,

    /// <summary>Match every thread in the exact workspace regardless of user or channel identity.</summary>
    Workspace
}

public interface ISessionService
{
    Task<SessionThread> CreateThreadAsync(
        SessionIdentity identity,
        ThreadConfiguration? config = null,
        HistoryMode historyMode = HistoryMode.Server,
        string? threadId = null,
        string? displayName = null,
        CancellationToken ct = default,
        ThreadSource? source = null,
        bool ephemeral = false);

    Task<SessionThread> ForkThreadAsync(
        string threadId,
        ThreadForkOptions? options = null,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Thread fork is not supported by this session service.");

    Task<WorktreeCreateAndForkResult> CreateWorktreeAndForkAsync(
        WorktreeCreateAndForkOptions options,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Git worktree handoff is not supported by this session service.");

    Task<WorktreeCreateAndStartResult> CreateWorktreeAndStartAsync(
        WorktreeCreateAndStartOptions options,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Git worktree start is not supported by this session service.");

    Task<WorktreeHandoffResult> HandoffThreadWorktreeAsync(
        WorktreeHandoffOptions options,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Git worktree handoff is not supported by this session service.");

    Task<WorktreeEnsureResult> EnsureManagedWorktreeAsync(
        WorktreeEnsureOptions options,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Git worktree ensure is not supported by this session service.");

    /// <summary>Sets a Thread's execution workspace without moving its state workspace.</summary>
    Task<SessionThread> ConfigureThreadExecutionWorkspaceAsync(
        ThreadExecutionWorkspaceOptions options,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Thread execution workspace configuration is not supported by this session service.");

    Task RemoveManagedWorktreeAsync(
        WorktreeRemoveOptions options,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Git worktree removal is not supported by this session service.");

    /// <summary>Lists the managed worktrees of the workspace, including ones no thread is bound to.</summary>
    Task<IReadOnlyList<ThreadWorktreeStatus>> ListWorktreesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ThreadWorktreeStatus>>([]);

    /// <summary>Removes a managed worktree and its branch, refusing to lose work unless <paramref name="discardChanges"/> is set.</summary>
    Task RemoveWorktreeAsync(
        string path,
        bool discardChanges,
        bool skipCleanup = false,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Git worktree removal is not supported by this session service.");

    void CancelWorktreeOperation(string operationId)
    {
    }

    Task<WorktreeSetupResult> RunWorktreeSetupAsync(
        WorktreeRunSetupOptions options,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Worktree environments are not supported by this session service.");

    Task<ThreadWorktreeInfo> SetWorktreeEnvironmentAsync(
        WorktreeSetEnvironmentOptions options,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Worktree environments are not supported by this session service.");

    Task<ThreadWorktreeStatus> GetWorktreeStatusAsync(
        string threadId,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Git worktree status is not supported by this session service.");

    /// <summary>
    /// Resumes a Thread from canonical history. Cold loading first cancels a non-terminal Turn left by the previous process.
    /// </summary>
    Task<SessionThread> ResumeThreadAsync(string threadId, CancellationToken ct = default);

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

    /// <summary>Creates a Thread with imported history, or returns the existing Thread with the requested id unchanged.</summary>
    Task<ThreadImportResult> ImportThreadAsync(
        ThreadImportRequest request,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Thread import is not supported by this session service.");

    /// <summary>Appends imported turns to a Thread that contains only imported turns; refuses a thread continued locally.</summary>
    Task<ThreadImportResult> AppendImportedTurnsAsync(
        ThreadImportAppendRequest request,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Thread import append is not supported by this session service.");

    Task<ThreadGoal?> GetThreadGoalAsync(string threadId, CancellationToken ct = default) =>
        throw new NotSupportedException("Thread goals are not supported by this session service.");

    /// <summary>Returns a Thread's stored goal without loading the Thread, for read projections.</summary>
    Task<ThreadGoal?> GetStoredThreadGoalAsync(string threadId, CancellationToken ct = default) =>
        GetThreadGoalAsync(threadId, ct);

    VisualizationWidgetState? GetVisualizationWidgetState(string threadId, string path) =>
        throw new NotSupportedException("Visualization widget state is not supported by this session service.");

    void SetVisualizationWidgetState(string threadId, string path, VisualizationWidgetState state) =>
        throw new NotSupportedException("Visualization widget state is not supported by this session service.");

    IReadOnlyList<GenerativeUiBlockState> GetGenerativeUiStates(string threadId) =>
        throw new NotSupportedException("Generative UI state is not supported by this session service.");

    /// <summary>Replaces a Generative UI block's view state; a <c>null</c> state removes it.</summary>
    void SetGenerativeUiState(string threadId, string turnId, string itemId, int blockIndex, string? stateJson) =>
        throw new NotSupportedException("Generative UI state is not supported by this session service.");

    Task<ThreadGoal> SetThreadGoalAsync(
        string threadId,
        ThreadGoalUpdate update,
        GoalSetMode mode = GoalSetMode.UpsertOrUpdate,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Thread goals are not supported by this session service.");

    Task<ThreadGoalClearResult> ClearThreadGoalAsync(string threadId, CancellationToken ct = default) =>
        throw new NotSupportedException("Thread goals are not supported by this session service.");

    Task ArchiveThreadAsync(string threadId, CancellationToken ct = default);

    Task UnarchiveThreadAsync(string threadId, CancellationToken ct = default);

    Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(
        SessionIdentity identity,
        bool includeArchived = false,
        IReadOnlyList<string>? crossChannelOrigins = null,
        CancellationToken ct = default,
        bool includeSubAgents = false,
        ThreadDiscoveryScope scope = ThreadDiscoveryScope.Identity);

    /// <summary>Counts top-level Threads in a workspace, including archived Threads.</summary>
    Task<int> CountWorkspaceThreadsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns an excerpt of the first user or agent message in a Thread's rollout that contains the term, ignoring case,
    /// or null when none does.
    /// </summary>
    Task<string?> FindThreadContentMatchAsync(string threadId, string searchTerm, CancellationToken ct = default) =>
        throw new NotSupportedException("Thread search is not supported by this session service.");

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

    Task<BoardTopicPage> ListBoardTopicsAsync(
        string threadId,
        string? channelName,
        string? cursor,
        int limit,
        CancellationToken ct = default) =>
        throw new NotSupportedException("The discussion board is not supported by this session service.");

    Task<BoardTopicPosts> ReadBoardTopicAsync(string threadId, string topicId, CancellationToken ct = default) =>
        throw new NotSupportedException("The discussion board is not supported by this session service.");

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

    IAsyncEnumerable<SessionEvent> SubscribeThreadAsync(
        string threadId,
        bool replayRecent = false,
        CancellationToken ct = default);

    IAsyncEnumerable<SessionEvent> SubmitInputAsync(
        string threadId,
        IList<AIContent> content,
        SenderContext? sender = null,
        ChatMessage[]? messages = null,
        CancellationToken ct = default,
        SessionInputSnapshot? inputSnapshot = null);

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

    /// <summary>
    /// Starts a chosen or the first queued input as a new Turn on an idle Thread, resuming a paused queue.
    /// <paramref name="observe"/> wraps only this Turn's events, not the queued Turns that start after it.
    /// </summary>
    Task<string> StartQueuedTurnAsync(
        string threadId,
        string? queuedInputId = null,
        CancellationToken ct = default,
        Func<IAsyncEnumerable<SessionEvent>, IAsyncEnumerable<SessionEvent>>? observe = null) =>
        throw new NotSupportedException("Starting queued input is not supported by this session service.");

    Task<IReadOnlyList<QueuedTurnInput>> RemoveQueuedTurnInputAsync(
        string threadId,
        string queuedInputId,
        CancellationToken ct = default);

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

    Task ResolveApprovalAsync(
        string threadId,
        string turnId,
        string requestId,
        SessionApprovalDecision decision,
        CancellationToken ct = default);

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

    Task CleanBackgroundTerminalsAsync(string threadId, CancellationToken ct = default);

    Task<IReadOnlyList<BackgroundTask>> ListBackgroundTasksAsync(string threadId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<BackgroundTask>>([]);

    /// <summary>Stops a background task the Thread owns on behalf of the user and waits a bounded time for it to settle.</summary>
    Task<BackgroundTask> StopBackgroundTaskAsync(string threadId, string taskId, CancellationToken ct = default) =>
        throw new NotSupportedException("Background tasks are not supported by this session service.");

    /// <summary>
    /// Removes Turns from the end of a Thread without reverting tool side effects.
    /// </summary>
    Task<SessionThread> RollbackThreadAsync(string threadId, int numTurns, CancellationToken ct = default);

    Task SetThreadModeAsync(string threadId, string mode, CancellationToken ct = default);

    /// <summary>Compacts model-visible context for an idle server-managed Thread.</summary>
    Task<ThreadCompactResult> CompactThreadAsync(string threadId, CancellationToken ct = default) =>
        throw new NotSupportedException("Manual context compaction is not supported by this session service.");

    Task CancelThreadMaintenanceAsync(string threadId, CancellationToken ct = default) =>
        Task.CompletedTask;

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

    Task<SessionThread> UpdateThreadSourceControlTargetAsync(
        string threadId,
        ThreadSourceControlTarget? target,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Thread source-control target updates are not supported by this session service.");

    /// <summary>
    /// Returns loaded Thread state with its recent Turns. Cold loading first cancels a non-terminal Turn left by the previous process.
    /// </summary>
    Task<SessionThread> GetThreadAsync(string threadId, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetInstructionSourcesAsync(
        string threadId,
        CancellationToken ct = default);

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

    Task<ThreadHistoryPage<ThreadHistoryItem>> ListThreadItemsAsync(
        string threadId,
        string? turnId,
        ThreadHistoryCursor? cursor,
        int limit,
        ThreadHistorySortDirection direction,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Paged Thread history is not supported by this session service.");

    /// <summary>Returns one visible Turn with its Items, or null when the Thread has no such Turn.</summary>
    async Task<SessionTurn?> ReadThreadTurnAsync(string threadId, string turnId, CancellationToken ct = default) =>
        (await GetThreadAsync(threadId, ct).ConfigureAwait(false)).Turns
        .LastOrDefault(turn => string.Equals(turn.Id, turnId, StringComparison.Ordinal));

    /// <summary>Returns one Item of a visible Turn, or null when it does not exist.</summary>
    async Task<SessionItem?> ReadThreadItemAsync(
        string threadId,
        string turnId,
        string itemId,
        CancellationToken ct = default) =>
        (await ReadThreadTurnAsync(threadId, turnId, ct).ConfigureAwait(false))?.Items
        .FirstOrDefault(item => string.Equals(item.Id, itemId, StringComparison.Ordinal));

    /// <summary>Enumerates every visible Turn in order; without Items, older Turns carry only their input.</summary>
    async IAsyncEnumerable<SessionTurn> ReadThreadTurnsAsync(
        string threadId,
        ThreadHistorySortDirection direction,
        bool includeItems = true,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var turns = (await GetThreadAsync(threadId, ct).ConfigureAwait(false)).Turns.ToArray();
        if (direction == ThreadHistorySortDirection.Descending)
            Array.Reverse(turns);
        foreach (var turn in turns)
            yield return turn;
    }

    async Task<ThreadHistoryAggregates> ReadThreadAggregatesAsync(string threadId, CancellationToken ct = default) =>
        ThreadHistoryTotals.Of((await GetThreadAsync(threadId, ct).ConfigureAwait(false)).Turns);

    /// <summary>
    /// Loads a Thread and prepares its configured agent without resuming it. Cold loading first cancels a non-terminal Turn left by the previous process.
    /// </summary>
    Task<SessionThread> EnsureThreadLoadedAsync(string threadId, CancellationToken ct = default);

    Task DeleteThreadPermanentlyAsync(string threadId, CancellationToken ct = default);

    Task RenameThreadAsync(string threadId, string displayName, CancellationToken ct = default);

    ContextUsageSnapshot? TryGetContextUsageSnapshot(string threadId);

    ContextUsageBreakdown? TryGetContextUsageBreakdown(string threadId) => null;

    ThreadSummaryRuntime GetThreadRuntimeSnapshot(SessionThread thread) =>
        ThreadSummaryRuntime.FromThread(thread);

    /// <summary>Returns whether a loaded Thread is hidden; unloaded Threads report false.</summary>
    bool IsHiddenThread(string threadId) => false;

    /// <summary>Returns the Thread when it is loaded in this process, without loading it.</summary>
    SessionThread? TryGetLoadedThread(string threadId) => null;

    Action<SessionThread>? ThreadCreatedForBroadcast { get; set; }

    Action<string>? ThreadDeletedForBroadcast { get; set; }

    Action<SessionThread>? ThreadRenamedForBroadcast { get; set; }

    /// <summary>Notifies hosts after Thread metadata or execution workspace changes.</summary>
    Action<SessionThread>? ThreadUpdatedForBroadcast
    {
        get => null;
        set { }
    }

    Action<string, ThreadStatus, ThreadStatus>? ThreadStatusChangedForBroadcast { get; set; }

    Action<string, SessionThreadRuntimeSignal, SessionTurn?>? ThreadRuntimeSignalForBroadcast { get; set; }

    Action<ThreadGoal, string?>? ThreadGoalUpdatedForBroadcast
    {
        get => null;
        set { }
    }

    Action<string>? ThreadGoalClearedForBroadcast
    {
        get => null;
        set { }
    }
}
