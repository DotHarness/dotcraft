using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Context.Compaction;
using DotCraft.Context.WorldState;
using DotCraft.GenerativeUi;
using DotCraft.Visualizations;
using DotCraft.Plugins;
using DotCraft.Persistence;
using DotCraft.Persistence.Upgrades;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using DotCraft.Sessions.Wire;
using DotCraft.Tools;

namespace DotCraft.Sessions;

internal sealed record TurnPersistenceCommit(
    SessionThread Thread,
    SessionTurn Turn,
    IReadOnlyList<ChatMessage> ModelHistory,
    TurnCompactionHistory? Compaction);

internal sealed record TurnCompactionHistory(
    string Trigger,
    string Mode,
    long TokensBefore,
    long TokensAfter,
    IReadOnlyList<ChatMessage> ReplacementHistory);

/// <summary>
/// Manages thread persistence under the .craft directory.
/// Canonical thread and model-visible history is stored as thread JSONL under threads/active|archived.
/// SQLite contains classified durable state, continuity state, diagnostics, and rebuildable projections.
/// </summary>
public sealed partial class ThreadStore : IAsyncDisposable
{
    private readonly string _botPath;
    private readonly ThreadMetadataStore _metadataStore;
    private readonly ThreadRolloutStore _rolloutStore;
    private readonly IRolloutReplayer _rolloutReplayer;
    private readonly ThreadAttachmentStore _attachmentStore;
    private readonly ThreadHistoryProjectionStore _historyProjectionStore;
    private readonly SemaphoreSlim _reconcileGate = new(1, 1);
    private readonly ILogger _logger;
    private static readonly TimeSpan OrphanAttachmentAge = TimeSpan.FromDays(1);
    private static readonly TimeSpan OrphanSweepInterval = TimeSpan.FromHours(1);
    private DateTimeOffset _lastOrphanSweep = DateTimeOffset.MinValue;

    public ThreadStore(string botPath)
        : this(botPath, null)
    {
    }

    public ThreadStore(
        string botPath,
        WorkspaceStateDatabase? stateRuntime,
        Func<string, CancellationToken, Task>? beforeRolloutDeleteAsync = null,
        ILogger<ThreadStore>? logger = null)
        : this(botPath, stateRuntime, beforeRolloutDeleteAsync, new OrderedRolloutWriter(), logger)
    {
    }

    internal ThreadStore(
        string botPath,
        WorkspaceStateDatabase? stateRuntime,
        Func<string, CancellationToken, Task>? beforeRolloutDeleteAsync,
        IRolloutWriter rolloutWriter,
        ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
        _botPath = Path.GetFullPath(botPath);
        StateDatabase = stateRuntime ?? new WorkspaceStateDatabase(botPath);
        _metadataStore = new ThreadMetadataStore(StateDatabase);
        _rolloutStore = new ThreadRolloutStore(botPath, beforeRolloutDeleteAsync, rolloutWriter);
        _rolloutReplayer = new RolloutReplayer();
        _attachmentStore = new ThreadAttachmentStore(StateDatabase, botPath);
        _historyProjectionStore = new ThreadHistoryProjectionStore(StateDatabase, _attachmentStore);
        VisualizationWidgetStates = new VisualizationWidgetStateStore(StateDatabase);
        GenerativeUiStates = new GenerativeUiStateStore(StateDatabase);
        ReadStates = new ThreadReadStateStore(StateDatabase);
    }

    internal WorkspaceStateDatabase StateDatabase { get; }

    public VisualizationWidgetStateStore VisualizationWidgetStates { get; }

    public GenerativeUiStateStore GenerativeUiStates { get; }

    internal ThreadReadStateStore ReadStates { get; }

    internal string DataPath => _botPath;

    /// <summary>
    /// Persists a thread to canonical thread JSONL storage and upserts queryable metadata in SQLite.
    /// </summary>
    public async Task SaveThreadAsync(SessionThread thread, CancellationToken ct = default)
    {
        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, thread.Id, ct);
        var result = await SaveThreadUnderGateAsync(thread, ct);
        await TryProjectHistoryAsync(thread.Id, result, ct);
    }

    /// <summary>
    /// Appends the records of an Item or queue change without replaying the rollout. A thread whose
    /// rollout is missing or must move is saved whole instead.
    /// </summary>
    internal async Task AppendThreadChangesAsync(
        SessionThread thread,
        IReadOnlyList<ThreadRolloutRecord> records,
        bool deleteUnreferencedAttachments,
        CancellationToken ct = default)
    {
        if (records.Count == 0)
            return;

        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, thread.Id, ct);
        var result = await TrackPersistedOrSkipAsync(
            thread.Id,
            () => _rolloutStore.AppendChangeRecordsAsync(thread, records, ct),
            thread.Turns);
        if (result == null)
        {
            result = await SaveThreadUnderGateAsync(thread, ct);
            var omitted = records
                .Where(record => record.ItemAppended is { } item
                                 && thread.Turns.All(turn => !string.Equals(turn.Id, item.TurnId, StringComparison.Ordinal)))
                .ToList();
            if (omitted.Count > 0)
            {
                result = await TrackPersistedOrSkipAsync(
                    thread.Id,
                    () => _rolloutStore.AppendChangeRecordsAsync(thread, omitted, ct),
                    thread.Turns) ?? result;
            }
        }
        await TryProjectHistoryAsync(thread.Id, result, ct, deleteUnreferencedAttachments);
    }

    /// <summary>
    /// Appends a rollback record for an already-pruned thread and updates metadata/cache.
    /// </summary>
    public async Task RollbackThreadAsync(
        SessionThread thread,
        int numTurns,
        CancellationToken ct = default)
    {
        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, thread.Id, ct);
        var result = await WriteModelHistoryAsync(
            thread.Id,
            () => TrackPersistedAsync(thread.Id, () => _rolloutStore.AppendRollbackAsync(thread, numTurns, ct)));
        ReleaseTrimmedProviderHistory(thread.Id);
        await TryProjectHistoryAsync(thread.Id, result, ct);
    }

    internal async Task SaveThreadBaselineAsync(SessionThread thread, CancellationToken ct = default)
    {
        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, thread.Id, ct);
        var result = await TrackPersistedAsync(thread.Id, () => _rolloutStore.AppendThreadBaselineAsync(thread, ct), thread.Turns);
        await TryProjectHistoryAsync(thread.Id, result, ct);
    }

    internal async Task SaveTurnAsync(
        SessionThread thread,
        SessionTurn turn,
        CancellationToken ct = default)
    {
        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, thread.Id, ct);
        var result = await TrackPersistedAsync(thread.Id, () => _rolloutStore.AppendTurnStateAsync(thread, turn, ct), thread.Turns);
        await TryProjectHistoryAsync(thread.Id, result, ct);
    }

    internal async Task<ModelHistoryRevision> CommitTurnAsync(TurnPersistenceCommit commit, CancellationToken ct = default)
    {
        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, commit.Thread.Id, ct);
        var codec = new ModelHistoryCodec();
        var modelHistory = commit.ModelHistory
            .Select(message => codec.Encode(message, commit.Turn.Id))
            .ToList();
        var compaction = commit.Compaction == null
            ? null
            : new TurnCompactionCommit(
                commit.Compaction.Trigger,
                commit.Compaction.Mode,
                commit.Compaction.TokensBefore,
                commit.Compaction.TokensAfter,
                DateTimeOffset.UtcNow,
                commit.Compaction.ReplacementHistory.Select(message => codec.Encode(message)).ToList());

        Task<RolloutAppendResult> Append() => TrackPersistedAsync(commit.Thread.Id, () => _rolloutStore.AppendTurnCommitAsync(
            commit.Thread,
            commit.Turn,
            modelHistory,
            compaction,
            ct), commit.Thread.Turns);
        var result = modelHistory.Count > 0 || compaction != null
            ? await WriteModelHistoryAsync(commit.Thread.Id, Append, checkpoint: compaction != null)
            : await Append();
        if (compaction != null)
            RecordResidentCheckpoint(commit.Thread.Id, compaction.CreatedAt);
        var revision = GetModelHistoryRevision(commit.Thread.Id);
        await TryProjectHistoryAsync(commit.Thread.Id, result, ct);
        return revision;
    }

    internal async Task AppendCompactionCheckpointAsync(
        string threadId,
        string coveredThroughTurnId,
        IReadOnlyList<ChatMessage> replacementHistory,
        string trigger,
        string mode,
        long tokensBefore,
        long tokensAfter,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, threadId, ct);
        var codec = new ModelHistoryCodec();
        var replacementMessages = replacementHistory.Select(message => codec.Encode(message)).ToList();
        var createdAt = DateTimeOffset.UtcNow;

        var result = await WriteModelHistoryAsync(
            threadId,
            () => TrackPersistedAsync(threadId, () => _rolloutStore.AppendCompactionCheckpointAsync(
                threadId,
                coveredThroughTurnId,
                trigger,
                mode,
                tokensBefore,
                tokensAfter,
                replacementMessages,
                createdAt,
                ct)),
            checkpoint: true);
        RecordResidentCheckpoint(threadId, createdAt);
        await TryProjectHistoryAsync(threadId, result, ct);
    }

    internal Task<IReadOnlyList<ThreadCompactionCheckpoint>> LoadCompactionCheckpointsAsync(
        string threadId,
        CancellationToken ct = default) =>
        _rolloutStore.LoadCompactionCheckpointsAsync(threadId, ct);

    /// <summary>
    /// Loads a thread by replaying canonical thread history.
    /// </summary>
    public async Task<SessionThread?> LoadThreadAsync(string threadId, CancellationToken ct = default)
    {
        using var readLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, threadId, ct);
        foreach (var archived in new[] { false, true })
        {
            var path = _rolloutStore.GetExpectedPath(threadId, archived);
            if (File.Exists(path)
                && await ReplayThreadAsync(path, ct) is { } thread
                && string.Equals(thread.Id, threadId, StringComparison.Ordinal))
            {
                return thread;
            }
        }

        return null;
    }

    /// <summary>
    /// Loads a thread from a restricted rollout path under threads/active or threads/archived.
    /// </summary>
    public async Task<SessionThread?> LoadThreadFromPathAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!_rolloutStore.TryNormalizeAllowedPath(path, out var normalizedPath))
            throw new ArgumentException("Thread path must resolve directly under the configured data directory's threads/active or threads/archived directory.", nameof(path));
        return File.Exists(normalizedPath) ? await ReplayThreadAsync(normalizedPath, ct) : null;
    }

    /// <summary>
    /// Deletes a thread JSONL history and metadata row.
    /// </summary>
    public void DeleteThread(string threadId)
    {
        using var writeLock = ThreadRolloutWriteGate.Acquire(_botPath, threadId);
        new AgentMessageBoardStore(StateDatabase).Delete(threadId);
        _rolloutStore.CloseThreadAsync(threadId).GetAwaiter().GetResult();
        var cleanupCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_rolloutStore.ResolveExistingPath(threadId) is { } rolloutPath)
        {
            try
            {
                cleanupCandidates.UnionWith(_historyProjectionStore
                    .ProjectCommittedAsync(threadId, rolloutPath, new FileInfo(rolloutPath).Length, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());
            }
            catch (ThreadHistoryUnavailableException ex)
            {
                System.Diagnostics.Trace.TraceWarning(
                    $"Thread projection was unavailable before deleting '{threadId}': {ex.Message}");
            }
        }
        cleanupCandidates.UnionWith(_attachmentStore.LoadCandidatePaths(threadId));
        _rolloutStore.DeleteThreadAsync(threadId).GetAwaiter().GetResult();
        _residentProviderHistory.TryRemove(threadId, out _);
        _persistedBaselines.TryRemove(threadId, out _);
        _modelHistoryRevisions.TryRemove(threadId, out _);
        _metadataStore.DeleteThread(threadId);
        _attachmentStore.CleanupCandidates(cleanupCandidates);
    }

    internal async Task PersistModelHistoryAsync(
        IReadOnlyList<ChatMessage> history,
        string threadId,
        string turnId,
        int persistedPrefixLength,
        CancellationToken ct = default)
    {
        if (persistedPrefixLength < 0 || persistedPrefixLength > history.Count)
        {
            throw new InvalidOperationException($"Invalid model-history prefix length for thread '{threadId}'.");
        }

        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, threadId, ct);
        var codec = new ModelHistoryCodec();
        var appended = history
            .Skip(persistedPrefixLength)
            .Select(message => codec.Encode(message, turnId))
            .ToList();
        var result = appended.Count == 0
            ? await _rolloutStore.AppendModelHistoryAsync(threadId, turnId, appended, ct)
            : await WriteModelHistoryAsync(
                threadId,
                () => TrackPersistedAsync(threadId, () => _rolloutStore.AppendModelHistoryAsync(threadId, turnId, appended, ct)));
        await TryProjectHistoryAsync(threadId, result, ct);
    }

    internal async Task AppendWorldStateAsync(
        string threadId,
        string turnId,
        bool full,
        JsonObject state,
        CancellationToken ct = default)
    {
        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, threadId, ct);
        var result = await TrackPersistedAsync(threadId, () => _rolloutStore.AppendWorldStateAsync(
            threadId,
            turnId,
            full,
            state,
            ct));
        await TryProjectHistoryAsync(threadId, result, ct);
    }

    internal async Task AppendModelHistoryAsync(
        string threadId,
        IReadOnlyList<ChatMessage> history,
        string turnId,
        CancellationToken ct = default)
    {
        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, threadId, ct);
        var codec = new ModelHistoryCodec();
        var encoded = history.Select(message => codec.Encode(message, turnId)).ToList();
        var result = encoded.Count == 0
            ? await _rolloutStore.AppendModelHistoryAsync(threadId, turnId, encoded, ct)
            : await WriteModelHistoryAsync(
                threadId,
                () => TrackPersistedAsync(threadId, () => _rolloutStore.AppendModelHistoryAsync(threadId, turnId, encoded, ct)));
        await TryProjectHistoryAsync(threadId, result, ct);
    }

    internal async Task AppendProviderHistoryItemsAsync(
        ProviderHistoryItemsAppendedPayload payload,
        CancellationToken ct = default)
    {
        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, payload.ThreadId, ct);
        var result = await WriteProviderHistoryAsync(
            payload.ThreadId,
            () => TrackPersistedAsync(payload.ThreadId, () => _rolloutStore.AppendProviderHistoryItemsAsync(payload, ct)));
        await TryProjectHistoryAsync(payload.ThreadId, result, ct);
    }

    internal async Task ReplaceProviderHistoryAsync(
        ProviderHistoryReplacedPayload payload,
        CancellationToken ct = default)
    {
        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, payload.ThreadId, ct);
        var result = await WriteProviderHistoryAsync(
            payload.ThreadId,
            () => TrackPersistedAsync(payload.ThreadId, () => _rolloutStore.AppendProviderHistoryReplacementAsync(payload, ct)));
        await TryProjectHistoryAsync(payload.ThreadId, result, ct);
    }

    internal async Task AbortProviderHistoryAttemptAsync(
        ProviderHistoryAttemptAbortedPayload payload,
        CancellationToken ct = default)
    {
        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, payload.ThreadId, ct);
        var result = await WriteProviderHistoryAsync(
            payload.ThreadId,
            () => TrackPersistedAsync(payload.ThreadId, () => _rolloutStore.AppendProviderHistoryAttemptAbortedAsync(payload, ct)));
        await TryProjectHistoryAsync(payload.ThreadId, result, ct);
    }

    /// <summary>
    /// Loads the persisted context-window usage token count for a thread.
    /// Returns null when no context usage snapshot has been recorded yet.
    /// </summary>
    public long? LoadContextUsageTokens(string threadId)
        => _metadataStore.LoadContextUsageTokens(threadId);

    /// <summary>
    /// Loads the persisted context-window usage token count and diagnostic metadata for a thread.
    /// Returns null when no context usage snapshot has been recorded yet.
    /// </summary>
    public ContextUsagePersistenceSnapshot? LoadContextUsageSnapshot(string threadId)
        => _metadataStore.LoadContextUsageSnapshot(threadId);

    /// <summary>
    /// Loads the persisted provider-usage anchor metadata for a thread.
    /// Returns null when the metadata is absent or predates anchor support.
    /// </summary>
    public ContextUsageAnchor? LoadContextUsageAnchor(string threadId)
        => _metadataStore.LoadContextUsageAnchor(threadId);

    /// <summary>
    /// Persists the current context-window usage token count for a thread.
    /// </summary>
    public Task SaveContextUsageTokensAsync(string threadId, long tokens, CancellationToken ct = default)
        => SaveContextUsageTokensAsync(threadId, tokens, source: null, isEstimate: false, ct: ct);

    public Task SaveContextUsageTokensAsync(
        string threadId,
        long tokens,
        string? source,
        CancellationToken ct = default)
        => SaveContextUsageTokensAsync(threadId, tokens, source, isEstimate: false, ct: ct);

    public Task SaveContextUsageTokensAsync(
        string threadId,
        long tokens,
        string? source,
        bool isEstimate,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _metadataStore.SaveContextUsageTokens(threadId, tokens, source, isEstimate);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Persists a context-window usage token count with anchor validation metadata.
    /// </summary>
    public Task SaveContextUsageAnchorAsync(string threadId, ContextUsageAnchor anchor, CancellationToken ct = default)
        => SaveContextUsageAnchorAsync(threadId, anchor.Tokens, anchor, source: null, isEstimate: false, ct: ct);

    public Task SaveContextUsageAnchorAsync(
        string threadId,
        long displayTokens,
        ContextUsageAnchor anchor,
        string? source,
        CancellationToken ct = default)
        => SaveContextUsageAnchorAsync(threadId, displayTokens, anchor, source, isEstimate: false, ct: ct);

    public Task SaveContextUsageAnchorAsync(
        string threadId,
        long displayTokens,
        ContextUsageAnchor anchor,
        string? source,
        bool isEstimate,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _metadataStore.SaveContextUsageAnchor(threadId, displayTokens, anchor, source, isEstimate);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Loads the current persistent goal for a thread, if one exists.
    /// </summary>
    public Task<ThreadGoal?> GetThreadGoalAsync(string threadId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_metadataStore.LoadThreadGoal(threadId));
    }

    /// <summary>
    /// Upserts the current persistent goal for a thread.
    /// </summary>
    public Task UpsertThreadGoalAsync(ThreadGoal goal, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _metadataStore.UpsertThreadGoal(goal);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Atomically adds usage to the current thread goal when its id still matches the expected goal id.
    /// </summary>
    public Task<GoalAccountingOutcome> AccountThreadGoalUsageAsync(
        string threadId,
        string expectedGoalId,
        TokenUsageInfo usageDelta,
        long timeDeltaSeconds,
        GoalAccountingMode mode,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_metadataStore.AccountThreadGoalUsage(
            threadId,
            expectedGoalId,
            usageDelta,
            timeDeltaSeconds,
            mode));
    }

    /// <summary>
    /// Deletes the current persistent goal for a thread.
    /// </summary>
    public Task<bool> DeleteThreadGoalAsync(string threadId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_metadataStore.DeleteThreadGoal(threadId));
    }

    public Task UpsertThreadSpawnEdgeAsync(ThreadSpawnEdge edge, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _metadataStore.UpsertThreadSpawnEdge(edge);
        return Task.CompletedTask;
    }

    public Task SetThreadSpawnEdgeStatusAsync(
        string parentThreadId,
        string childThreadId,
        string status,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _metadataStore.SetThreadSpawnEdgeStatus(parentThreadId, childThreadId, status);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ThreadSpawnEdge>> ListSubAgentChildrenAsync(
        string parentThreadId,
        bool includeClosed = false,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ThreadSpawnEdge>>(_metadataStore.ListSubAgentChildren(parentThreadId, includeClosed));
    }

    public Task AddSubAgentMailboxEntryAsync(SubAgentMailboxEntry entry, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _metadataStore.AddSubAgentMailboxEntry(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SubAgentMailboxEntry>> ListPendingSubAgentMailboxAsync(
        string rootThreadId,
        string targetAgentPath,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<SubAgentMailboxEntry>>(
            _metadataStore.ListPendingSubAgentMailbox(rootThreadId, targetAgentPath));
    }

    public Task MarkSubAgentMailboxDeliveredAsync(
        string rootThreadId,
        IReadOnlyList<string> entryIds,
        DateTimeOffset deliveredAt,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _metadataStore.MarkSubAgentMailboxDelivered(rootThreadId, entryIds, deliveredAt);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await FlushAndCloseAsync(CancellationToken.None);
        _reconcileGate.Dispose();
    }

    public Task FlushAndCloseAsync(CancellationToken ct = default) => _rolloutStore.ShutdownAsync(ct);

    public IAsyncDisposable StartBackgroundUpgrade() => WorkspaceUpgrades.StartInBackground(this);

    internal async Task WithExclusiveRolloutAsync(string threadId, Action<string> work, CancellationToken ct)
    {
        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, threadId, ct);
        await _rolloutStore.CloseThreadAsync(threadId, ct);
        if (_rolloutStore.ResolveExistingPath(threadId) is { } path)
            work(path);
    }

    private async Task TryProjectHistoryAsync(
        string threadId,
        RolloutAppendResult result,
        CancellationToken ct,
        bool deleteUnreferencedAttachments = true)
    {
        try
        {
            var removed = await _historyProjectionStore.ProjectCommittedAsync(
                threadId,
                result.Path,
                result.Receipt.ConfirmedOffset,
                ct);
            if (deleteUnreferencedAttachments)
                _attachmentStore.CleanupCandidates(removed);
        }
        catch (Exception ex) when (ex is ThreadHistoryUnavailableException or OperationCanceledException)
        {
            if (ex is OperationCanceledException && ct.IsCancellationRequested)
                throw;
            System.Diagnostics.Trace.TraceWarning(
                $"Thread history projection update failed after rollout commit for '{threadId}': {ex.Message}");
        }
    }

    internal bool ThreadExists(string threadId) => _rolloutStore.ResolveExistingPath(threadId) != null;

    private string ResolvePersistedHistoryPath(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        return _rolloutStore.ResolveExistingPath(threadId)
            ?? throw new ThreadNotFoundException(threadId);
    }

    private static void ValidateHistoryPageLimit(int limit)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "History page limit must be positive.");
    }
}
