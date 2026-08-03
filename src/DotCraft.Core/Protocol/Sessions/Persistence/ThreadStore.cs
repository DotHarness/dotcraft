using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Context.Compaction;
using DotCraft.Plugins;
using DotCraft.Persistence;
using Microsoft.Extensions.AI;
using DotCraft.Sessions.Wire;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using ThreadGoal = DotCraft.Sessions.ThreadGoal;
using UserMessagePayload = DotCraft.Sessions.UserMessagePayload;

namespace DotCraft.Sessions;

internal sealed record ForkModelHistoryMaterialization(
    IReadOnlyList<ChatMessage> History,
    IReadOnlyList<ChatMessage>? CheckpointReplacementHistory,
    string? CheckpointCoveredThroughTurnId,
    string? CheckpointMode,
    long EstimatedTokens,
    string UsageSource,
    bool UsageIsEstimate)
{
    public bool HasCompatibleCheckpoint =>
        CheckpointReplacementHistory is not null &&
        !string.IsNullOrWhiteSpace(CheckpointCoveredThroughTurnId);
}

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
public sealed class ThreadStore : IAsyncDisposable
{
    private readonly string _botPath;
    private readonly ThreadMetadataStore _metadataStore;
    private readonly ThreadRolloutStore _rolloutStore;
    private readonly IRolloutReplayer _rolloutReplayer;
    private readonly ThreadAttachmentStore _attachmentStore;
    private readonly SemaphoreSlim _reconcileGate = new(1, 1);

    public ThreadStore(string botPath)
        : this(botPath, null)
    {
    }

    internal ThreadStore(
        string botPath,
        WorkspaceStateDatabase? stateRuntime,
        Func<string, CancellationToken, Task>? beforeRolloutDeleteAsync = null)
    {
        _botPath = Path.GetFullPath(botPath);
        StateDatabase = stateRuntime ?? new WorkspaceStateDatabase(botPath);
        _metadataStore = new ThreadMetadataStore(StateDatabase);
        _rolloutStore = new ThreadRolloutStore(botPath, beforeRolloutDeleteAsync);
        _rolloutReplayer = new RolloutReplayer();
        _attachmentStore = new ThreadAttachmentStore(StateDatabase, botPath);
    }

    internal WorkspaceStateDatabase StateDatabase { get; }

    /// <summary>
    /// Persists a thread to canonical thread JSONL storage and upserts queryable metadata in SQLite.
    /// </summary>
    public async Task SaveThreadAsync(SessionThread thread, CancellationToken ct = default)
    {
        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, thread.Id, ct);
        var previous = await _rolloutStore.LoadThreadAsync(thread.Id, ct);

        var result = await _rolloutStore.SaveThreadAsync(thread, previous, ct);
        TryUpdateThreadProjection(thread, result);
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
        var result = await _rolloutStore.AppendRollbackAsync(thread, numTurns, ct);
        TryUpdateThreadProjection(thread, result);
    }

    internal async Task SaveTurnAsync(
        SessionThread thread,
        SessionTurn turn,
        CancellationToken ct = default)
    {
        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, thread.Id, ct);
        var result = await _rolloutStore.AppendTurnStateAsync(thread, turn, ct);
        TryUpdateThreadProjection(thread, result);
    }

    internal async Task CommitTurnAsync(TurnPersistenceCommit commit, CancellationToken ct = default)
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

        var result = await _rolloutStore.AppendTurnCommitAsync(
            commit.Thread,
            commit.Turn,
            modelHistory,
            compaction,
            ct);
        TryUpdateThreadProjection(commit.Thread, result);
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

        var result = await _rolloutStore.AppendCompactionCheckpointAsync(
            threadId,
            coveredThroughTurnId,
            trigger,
            mode,
            tokensBefore,
            tokensAfter,
            replacementMessages,
            DateTimeOffset.UtcNow,
            ct);
        TryUpdateRolloutOffsetProjection(threadId, result);
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
        var thread = await _rolloutStore.LoadThreadAsync(threadId, ct);
        return thread;
    }

    /// <summary>
    /// Loads a thread from a restricted rollout path under threads/active or threads/archived.
    /// </summary>
    public async Task<SessionThread?> LoadThreadFromPathAsync(string path, CancellationToken ct = default)
    {
        var thread = await _rolloutStore.LoadThreadFromPathAsync(path, ct);
        return thread;
    }

    /// <summary>
    /// Deletes a thread JSONL history and metadata row.
    /// </summary>
    public void DeleteThread(string threadId)
    {
        using var writeLock = ThreadRolloutWriteGate.Acquire(_botPath, threadId);
        _rolloutStore.CloseThreadAsync(threadId).GetAwaiter().GetResult();
        var candidatePaths = _rolloutStore.LoadThreadAsync(threadId).GetAwaiter().GetResult() is { } loaded
            ? _attachmentStore.ExtractManagedImagePaths(loaded)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cleanupCandidates = _attachmentStore.LoadCandidatePaths(threadId, candidatePaths);
        _rolloutStore.DeleteThreadAsync(threadId).GetAwaiter().GetResult();
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
        var result = await _rolloutStore.AppendModelHistoryAsync(threadId, turnId, appended, ct);
        TryUpdateRolloutOffsetProjection(threadId, result);
    }

    internal async Task AppendModelHistoryAsync(
        string threadId,
        IReadOnlyList<ChatMessage> history,
        string turnId,
        CancellationToken ct = default)
    {
        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, threadId, ct);
        var codec = new ModelHistoryCodec();
        var result = await _rolloutStore.AppendModelHistoryAsync(
            threadId,
            turnId,
            history.Select(message => codec.Encode(message, turnId)).ToList(),
            ct);
        TryUpdateRolloutOffsetProjection(threadId, result);
    }

    internal async Task<ProviderHistorySnapshot> LoadProviderHistoryAsync(
        SessionThread thread,
        string currentContextWindowId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(thread);
        if (thread.ProviderHistorySchemaVersion != ProviderHistorySchema.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"responses_provider_history_corrupt: Thread '{thread.Id}' uses unsupported provider history schema version {thread.ProviderHistorySchemaVersion}.");
        }

        var records = await _rolloutStore.LoadProviderHistoryRecordsAsync(thread.Id, ct);
        var checkpoints = await _rolloutStore.LoadCompactionCheckpointsAsync(thread.Id, ct);
        var latestCheckpoint = checkpoints.LastOrDefault();
        var latestReplacementTimestamp = records
            .Where(static record => record.ProviderHistoryReplaced is not null)
            .Select(static record => (DateTimeOffset?)record.Timestamp)
            .LastOrDefault();
        if (latestCheckpoint is not null
            && (!latestReplacementTimestamp.HasValue
                || latestCheckpoint.CreatedAt > latestReplacementTimestamp.Value))
        {
            // The rollout checkpoint is authoritative. A process may have stopped after committing
            // it but before publishing the derived provider-history replacement. Returning an empty
            // snapshot makes Session Core advance the window and rebuild the provider prefix from
            // the replayed compacted history.
            return ProviderHistorySnapshot.Empty(currentContextWindowId);
        }

        var survivingTurnIds = thread.Turns
            .Select(turn => turn.Id)
            .ToHashSet(StringComparer.Ordinal);
        return ProviderHistoryReplayer.Replay(
            thread.Id,
            currentContextWindowId,
            survivingTurnIds,
            records);
    }

    internal async Task AppendProviderHistoryItemsAsync(
        ProviderHistoryItemsAppendedPayload payload,
        CancellationToken ct = default)
    {
        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, payload.ThreadId, ct);
        var result = await _rolloutStore.AppendProviderHistoryItemsAsync(payload, ct);
        TryUpdateRolloutOffsetProjection(payload.ThreadId, result);
    }

    internal async Task ReplaceProviderHistoryAsync(
        ProviderHistoryReplacedPayload payload,
        CancellationToken ct = default)
    {
        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, payload.ThreadId, ct);
        var result = await _rolloutStore.AppendProviderHistoryReplacementAsync(payload, ct);
        TryUpdateRolloutOffsetProjection(payload.ThreadId, result);
    }

    internal async Task AbortProviderHistoryAttemptAsync(
        ProviderHistoryAttemptAbortedPayload payload,
        CancellationToken ct = default)
    {
        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, payload.ThreadId, ct);
        var result = await _rolloutStore.AppendProviderHistoryAttemptAbortedAsync(payload, ct);
        TryUpdateRolloutOffsetProjection(payload.ThreadId, result);
    }

    internal async Task<ForkModelHistoryMaterialization> BuildForkModelHistoryMaterializationAsync(
        SessionThread source,
        SessionThread forked,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var orderedForkTurns = OrderTurns(forked.Turns);
        var checkpoints = await _rolloutStore.LoadCompactionCheckpointsAsync(source.Id, ct);
        for (var i = checkpoints.Count - 1; i >= 0; i--)
        {
            var checkpoint = checkpoints[i];
            if (!IsCheckpointCompatibleForFork(source, orderedForkTurns, checkpoint))
                continue;

            if (!TryDeserializeCheckpointHistory(checkpoint, out var checkpointHistory))
                continue;

            var history = checkpointHistory.ToList();
            var coveredTurnIndex = orderedForkTurns.FindIndex(turn =>
                string.Equals(turn.Id, checkpoint.CoveredThroughTurnId, StringComparison.Ordinal));
            for (var turnIndex = coveredTurnIndex + 1; turnIndex < orderedForkTurns.Count; turnIndex++)
                history.AddRange(BuildModelVisibleHistoryFromTurn(orderedForkTurns[turnIndex]));

            return new ForkModelHistoryMaterialization(
                history,
                checkpointHistory,
                checkpoint.CoveredThroughTurnId,
                checkpoint.Mode,
                MessageTokenEstimator.Estimate(history),
                "compacted_estimate",
                UsageIsEstimate: true);
        }

        var rawHistory = BuildModelVisibleHistoryFromTurns(orderedForkTurns);
        return new ForkModelHistoryMaterialization(
            rawHistory,
            CheckpointReplacementHistory: null,
            CheckpointCoveredThroughTurnId: null,
            CheckpointMode: null,
            MessageTokenEstimator.Estimate(rawHistory),
            "history_estimate",
            UsageIsEstimate: true);
    }

    /// <summary>
    /// Rebuilds MEAI model history from canonical rollout records.
    /// </summary>
    public async Task<List<ChatMessage>> LoadModelHistoryAsync(
        string threadId,
        CancellationToken ct = default)
    {
        return await RebuildModelHistoryFromRolloutAsync(threadId, ct);
    }

    internal async Task<List<ChatMessage>> LoadModelHistoryAsync(
        SessionThread thread,
        string? excludedTurnId = null,
        CancellationToken ct = default)
    {
        return await RebuildModelHistoryFromRolloutAsync(thread, excludedTurnId, ct);
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
    /// Upserts the UI-only Interactive Tool UI <c>widgetState</c> for a <c>dynamicToolCall</c> item
    /// (M-iv), keyed by <paramref name="callId"/>. Stored in a mutable side table, not the rollout.
    /// </summary>
    public void SaveItemWidgetState(string threadId, string callId, string widgetStateJson)
        => _metadataStore.SaveItemWidgetState(threadId, callId, widgetStateJson);

    /// <summary>Removes a stored <c>widgetState</c> for an item.</summary>
    public void DeleteItemWidgetState(string threadId, string callId)
        => _metadataStore.DeleteItemWidgetState(threadId, callId);

    /// <summary>Loads every stored <c>widgetState</c> for a thread, keyed by <c>callId</c>.</summary>
    public IReadOnlyDictionary<string, string> LoadItemWidgetStates(string threadId)
        => _metadataStore.LoadItemWidgetStates(threadId);

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

    /// <summary>
    /// Returns all persisted thread summaries from SQLite metadata, ordered by activity.
    /// Rows whose canonical rollout file is missing are omitted because callers cannot read them.
    /// </summary>
    public async Task<List<ThreadSummary>> LoadIndexAsync(CancellationToken ct = default)
    {
        await _reconcileGate.WaitAsync(ct);
        try
        {
            var paths = _rolloutStore.EnumerateRolloutPaths().ToList();
            try
            {
                var projectionStates = _metadataStore.LoadProjectionStates();
                var unreadableThreadIds = new HashSet<string>(StringComparer.Ordinal);

                foreach (var path in paths)
                {
                    ct.ThrowIfCancellationRequested();
                    var length = new FileInfo(path).Length;
                    var threadId = Path.GetFileNameWithoutExtension(path);
                    if (projectionStates.TryGetValue(threadId, out var state)
                        && string.Equals(Path.GetFullPath(state.RolloutPath), path, StringComparison.OrdinalIgnoreCase)
                        && state.ProjectedRolloutOffset == length)
                    {
                        continue;
                    }

                    SessionThread? thread;
                    try
                    {
                        thread = await _rolloutStore.LoadThreadFromPathAsync(path, ct);
                    }
                    catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
                    {
                        unreadableThreadIds.Add(threadId);
                        System.Diagnostics.Trace.TraceWarning(
                            $"Unable to repair rollout '{Path.GetFileName(path)}': {ex.Message}");
                        continue;
                    }
                    if (thread == null)
                        continue;
                    UpdateThreadProjection(thread, path, length);
                }

                return _metadataStore.LoadIndex()
                    .Where(summary => !unreadableThreadIds.Contains(summary.Id)
                                      && _rolloutStore.ResolveExistingPath(summary.Id) != null)
                    .ToList();
            }
            catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException
                                               or InvalidOperationException
                                               or JsonException
                                               or NotSupportedException)
            {
                System.Diagnostics.Trace.TraceWarning($"Thread metadata read-repair failed; using rollout files: {ex.Message}");
                return await LoadFilesystemIndexAsync(paths, ct);
            }
        }
        finally
        {
            _reconcileGate.Release();
        }
    }

    private async Task<List<ThreadSummary>> LoadFilesystemIndexAsync(
        IReadOnlyList<string> paths,
        CancellationToken ct)
    {
        var summaries = new List<ThreadSummary>();
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var thread = await _rolloutStore.LoadThreadFromPathAsync(path, ct);
                if (thread != null)
                    summaries.Add(ThreadSummary.FromThread(thread));
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException or NotSupportedException)
            {
                System.Diagnostics.Trace.TraceWarning($"Unable to read rollout '{Path.GetFileName(path)}': {ex.Message}");
            }
        }

        return summaries
            .OrderByDescending(static summary => summary.LastActiveAt)
            .ThenByDescending(static summary => summary.Id, StringComparer.Ordinal)
            .ToList();
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

    internal Task FlushAndCloseAsync(CancellationToken ct = default) => _rolloutStore.ShutdownAsync(ct);

    private void TryUpdateThreadProjection(SessionThread thread, RolloutAppendResult result)
    {
        try
        {
            UpdateThreadProjection(thread, result.Path, result.Receipt.ConfirmedOffset);
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"Thread projection update failed after rollout commit for '{thread.Id}': {ex.Message}");
        }
    }

    private void UpdateThreadProjection(SessionThread thread, string rolloutPath, long projectedRolloutOffset)
    {
        var previousPaths = _attachmentStore.LoadCandidatePaths(thread.Id);
        using var connection = StateDatabase.OpenConnection();
        using var transaction = connection.BeginTransaction();

        // Establish the parent row before attachment inserts. The confirmed offset is
        // deliberately advanced only after every attachment reference has succeeded.
        _metadataStore.UpsertThread(connection, transaction, thread, rolloutPath, projectedRolloutOffset: 0);
        var currentPaths = _attachmentStore.ReplaceThreadAttachments(connection, transaction, thread);
        ThreadMetadataStore.UpdateProjectedRolloutOffset(
            connection,
            transaction,
            thread.Id,
            rolloutPath,
            projectedRolloutOffset);
        transaction.Commit();

        _attachmentStore.CleanupCandidates(
            previousPaths.Except(currentPaths, StringComparer.OrdinalIgnoreCase));
    }

    private void TryUpdateRolloutOffsetProjection(string threadId, RolloutAppendResult result)
    {
        try
        {
            _metadataStore.UpdateProjectedRolloutOffset(threadId, result.Path, result.Receipt.ConfirmedOffset);
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"Thread rollout offset projection failed after commit for '{threadId}': {ex.Message}");
        }
    }

    private async Task<List<ChatMessage>> RebuildModelHistoryFromRolloutAsync(
        string threadId,
        CancellationToken ct)
    {
        var thread = await _rolloutStore.LoadThreadAsync(threadId, ct);
        if (thread == null)
            return [];

        return await RebuildModelHistoryFromRolloutAsync(thread, excludedTurnId: null, ct);
    }

    private async Task<List<ChatMessage>> RebuildModelHistoryFromRolloutAsync(
        SessionThread thread,
        string? excludedTurnId,
        CancellationToken ct)
    {
        var rolloutPath = _rolloutStore.ResolveExistingPath(thread.Id);
        var replayed = rolloutPath == null
            ? new ModelHistoryReplayResult([], HasModelHistoryRecords: false)
            : await _rolloutReplayer.ReplayModelHistoryAsync(
                rolloutPath,
                thread.Turns,
                excludedTurnId,
                ct,
                thread.Id);
        foreach (var warning in replayed.Warnings ?? [])
        {
            System.Diagnostics.Trace.TraceWarning(
                string.IsNullOrWhiteSpace(warning.TurnId)
                    ? $"Rollout model-history replay warning ({warning.Code}): {warning.Message}"
                    : $"Rollout model-history replay warning for turn '{warning.TurnId}' ({warning.Code}): {warning.Message}");
        }
        var fallbackTurns = thread.Turns
            .Where(turn => !string.Equals(turn.Id, excludedTurnId, StringComparison.Ordinal));
        var history = replayed.HasModelHistoryRecords
            ? replayed.Messages.ToList()
            : BuildModelVisibleHistoryFromTurns(fallbackTurns);
        history = MessageGrouper.NormalizeFunctionCallArguments(history).ToList();

        return history;
    }

    private static bool TryDeserializeCheckpointHistory(
        ThreadCompactionCheckpoint checkpoint,
        out List<ChatMessage> history)
    {
        history = [];
        try
        {
            var codec = new ModelHistoryCodec();
            var restored = checkpoint.ReplacementHistory.Select(codec.Decode).ToList();
            history = MessageGrouper.NormalizeFunctionCallArguments(restored).ToList();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static List<ChatMessage> BuildModelVisibleHistoryFromTurns(IEnumerable<SessionTurn> turns)
    {
        var history = new List<ChatMessage>();
        foreach (var turn in OrderTurns(turns))
            history.AddRange(BuildModelVisibleHistoryFromTurn(turn));

        return history;
    }

    private static List<SessionTurn> OrderTurns(IEnumerable<SessionTurn> turns) =>
        turns.OrderBy(t => t.StartedAt).ThenBy(t => t.Id, StringComparer.Ordinal).ToList();

    private static bool IsCheckpointCompatibleForFork(
        SessionThread source,
        List<SessionTurn> orderedForkTurns,
        ThreadCompactionCheckpoint checkpoint)
    {
        var sourceTurn = source.Turns.FirstOrDefault(turn =>
            string.Equals(turn.Id, checkpoint.CoveredThroughTurnId, StringComparison.Ordinal));
        if (sourceTurn == null)
            return false;

        var forkTurnIndex = orderedForkTurns.FindIndex(turn =>
            string.Equals(turn.Id, checkpoint.CoveredThroughTurnId, StringComparison.Ordinal));
        if (forkTurnIndex < 0)
            return false;

        var forkTurn = orderedForkTurns[forkTurnIndex];
        var forkItems = forkTurnIndex == orderedForkTurns.Count - 1
            ? RemoveTrailingForkNotice(forkTurn.Items)
            : forkTurn.Items;
        return SameItemPrefix(sourceTurn.Items, forkItems, requireCompleteSourcePrefix: true);
    }

    private static IReadOnlyList<SessionItem> RemoveTrailingForkNotice(IReadOnlyList<SessionItem> items)
    {
        if (items.Count == 0 || !IsForkNotice(items[^1]))
            return items;

        return items.Take(items.Count - 1).ToList();
    }

    private static bool IsForkNotice(SessionItem item) =>
        item.Type == ItemType.SystemNotice &&
        item.Payload is SystemNoticePayload { Kind: "forked" };

    private static bool SameItemPrefix(
        IReadOnlyList<SessionItem> sourceItems,
        IReadOnlyList<SessionItem> forkItems,
        bool requireCompleteSourcePrefix)
    {
        if (requireCompleteSourcePrefix && sourceItems.Count != forkItems.Count)
            return false;

        if (forkItems.Count > sourceItems.Count)
            return false;

        for (var i = 0; i < forkItems.Count; i++)
        {
            if (!string.Equals(sourceItems[i].Id, forkItems[i].Id, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    internal static IReadOnlyList<ChatMessage> BuildModelVisibleHistoryFromTurn(SessionTurn turn)
    {
        var history = new List<ChatMessage>();
        var completedItems = turn.Items
            .Where(static item => item.Status == ItemStatus.Completed)
            .ToList();
        var pairedToolCalls = CollectPairedToolCalls(completedItems);
        var assistantBuilder = new AssistantSamplingSegmentBuilder();

        foreach (var item in completedItems)
        {
            if (item.Type == ItemType.UserMessage && TryBuildUserMessage(item, out var userMessage))
            {
                FlushAssistantSegment(history, assistantBuilder);
                history.Add(userMessage);
            }
            else if (item.Type == ItemType.ReasoningContent &&
                     item.AsReasoningContent is { Text: { } reasoningText } &&
                     !string.IsNullOrWhiteSpace(reasoningText))
            {
                assistantBuilder.AddReasoning(reasoningText);
            }
            else if (item.Type == ItemType.AgentMessage && item.AsAgentMessage is { Text: { } agentText } &&
                     !string.IsNullOrWhiteSpace(agentText))
            {
                assistantBuilder.AddText(agentText.Trim());
            }
            else if (item.Type == ItemType.ToolCall &&
                     TryBuildToolCallContent(item, pairedToolCalls, out var toolCallContent))
            {
                assistantBuilder.AddToolCall(toolCallContent);
            }
            else if (item.Type == ItemType.ToolResult &&
                     TryBuildToolResultMessage(item, pairedToolCalls, out var toolResultMessage))
            {
                FlushAssistantSegment(history, assistantBuilder);
                history.Add(toolResultMessage);
            }
            else if (TryBuildSpecializedToolHistory(
                         item,
                         out var specializedCall,
                         out var specializedResult))
            {
                assistantBuilder.AddToolCall(specializedCall);
                FlushAssistantSegment(history, assistantBuilder);
                history.Add(specializedResult);
            }
            else if (item.Type == ItemType.ImageGeneration &&
                     TryBuildImageGenerationMessage(item, out var imageGenerationMessage))
            {
                FlushAssistantSegment(history, assistantBuilder);
                history.Add(imageGenerationMessage);
            }
        }

        FlushAssistantSegment(history, assistantBuilder);
        return history;
    }

    private static bool TryBuildUserMessage(SessionItem item, out ChatMessage message)
    {
        message = new ChatMessage(ChatRole.User, string.Empty);
        if (item.AsUserMessage is not { } user)
            return false;

        if (string.Equals(user.DeliveryMode, SubAgentMailboxDelivery.DeliveryMode, StringComparison.Ordinal))
            return TryBuildSubAgentMailboxMessage(user, out message);

        var parts =
            user.MaterializedInputParts is { Count: > 0 } materialized ? materialized :
            user.NativeInputParts is { Count: > 0 } native ? native :
            null;

        if (parts is { Count: > 0 })
        {
            var contents = parts
                .Select(p => p.ToAIContent())
                .Where(c => c is not TextContent tc || !string.IsNullOrWhiteSpace(tc.Text))
                .ToList();
            if (contents.Count > 0)
            {
                message = new ChatMessage(ChatRole.User, (IList<AIContent>)contents);
                return true;
            }
        }

        if (string.IsNullOrWhiteSpace(user.Text))
            return false;

        message = new ChatMessage(ChatRole.User, user.Text.Trim());
        return true;
    }

    private static bool TryBuildSubAgentMailboxMessage(UserMessagePayload user, out ChatMessage message)
    {
        message = new ChatMessage(ChatRole.User, string.Empty);
        var parts = user.MaterializedInputParts is { Count: > 0 } materialized ? materialized : null;
        if (parts is { Count: > 0 })
        {
            var contents = parts
                .Select(p => p.ToAIContent())
                .Where(c => c is not TextContent tc || !string.IsNullOrWhiteSpace(tc.Text))
                .ToList();
            if (contents.Count > 0)
            {
                message = new ChatMessage(ChatRole.User, (IList<AIContent>)contents);
                return true;
            }
        }

        if (string.IsNullOrWhiteSpace(user.Text))
            return false;

        message = new ChatMessage(ChatRole.User, user.Text.Trim());
        return true;
    }

    private static Dictionary<string, string> CollectPairedToolCalls(IReadOnlyList<SessionItem> items)
    {
        var resultIds = items
            .Select(static item => item.Payload as ToolResultPayload)
            .Where(static payload => !string.IsNullOrWhiteSpace(payload?.CallId))
            .Select(static payload => payload!.CallId)
            .ToHashSet(StringComparer.Ordinal);

        if (resultIds.Count == 0)
            return [];

        var paired = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var payload in items.Select(static item => item.Payload as ToolCallPayload))
        {
            if (string.IsNullOrWhiteSpace(payload?.CallId) ||
                string.IsNullOrWhiteSpace(payload.ToolName) ||
                !resultIds.Contains(payload.CallId))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(payload.ProviderFlatName))
                continue;

            paired.TryAdd(payload.CallId, payload.ProviderFlatName);
        }

        return paired;
    }

    private static void FlushAssistantSegment(
        List<ChatMessage> history,
        AssistantSamplingSegmentBuilder builder)
    {
        if (builder.TryBuild(out var message))
            history.Add(message);
        builder.Clear();
    }

    private static bool TryBuildToolCallContent(
        SessionItem item,
        IReadOnlyDictionary<string, string> pairedToolCalls,
        out FunctionCallContent content)
    {
        content = new FunctionCallContent(string.Empty, string.Empty);
        if (item.Payload is not ToolCallPayload payload ||
            string.IsNullOrWhiteSpace(payload.CallId) ||
            string.IsNullOrWhiteSpace(payload.ToolName) ||
            string.IsNullOrWhiteSpace(payload.ProviderFlatName) ||
            !pairedToolCalls.ContainsKey(payload.CallId) ||
            string.Equals(payload.ToolName, HostedImageGenerationContent.ToolName, StringComparison.Ordinal))
        {
            return false;
        }

        content = CreatePersistedFunctionCall(
            payload.CallId,
            payload.Namespace,
            payload.ToolName,
            payload.ProviderFlatName,
            payload.Arguments);
        return true;
    }

    private static bool TryBuildToolResultMessage(
        SessionItem item,
        IReadOnlyDictionary<string, string> pairedToolCalls,
        out ChatMessage message)
    {
        message = new ChatMessage(ChatRole.Tool, string.Empty);
        if (item.Payload is not ToolResultPayload payload ||
            string.IsNullOrWhiteSpace(payload.CallId) ||
            !pairedToolCalls.TryGetValue(payload.CallId, out var toolName))
        {
            return false;
        }

        if (string.Equals(toolName, HostedImageGenerationContent.ToolName, StringComparison.Ordinal))
            return TryBuildHostedImageGenerationMessage(payload, out message);

        var result = BuildModelToolResult(
            payload.ContentItems,
            payload.Result,
            payload.ErrorCode,
            payload.ErrorMessage);
        message = new ChatMessage(
            ChatRole.Tool,
            (IList<AIContent>)[new FunctionResultContent(payload.CallId, result)]);
        return true;
    }

    private static bool TryBuildSpecializedToolHistory(
        SessionItem item,
        out FunctionCallContent call,
        out ChatMessage resultMessage)
    {
        call = new FunctionCallContent(string.Empty, string.Empty);
        resultMessage = new ChatMessage(ChatRole.Tool, string.Empty);

        string callId;
        string? functionNamespace;
        string toolName;
        string providerFlatName;
        JsonObject? arguments;
        IReadOnlyList<PluginFunctionContentItem>? contentItems;
        string? errorCode;
        string? errorMessage;

        switch (item.Payload)
        {
            case McpToolCallPayload mcp
                when !string.IsNullOrWhiteSpace(mcp.CallId)
                     && !string.IsNullOrWhiteSpace(mcp.ToolName)
                     && !string.Equals(mcp.Status, "inProgress", StringComparison.OrdinalIgnoreCase):
                callId = mcp.CallId;
                if (string.IsNullOrWhiteSpace(mcp.ProviderFlatName))
                    return false;
                toolName = mcp.ToolName;
                functionNamespace = mcp.Namespace;
                providerFlatName = mcp.ProviderFlatName;
                arguments = mcp.Arguments;
                contentItems = mcp.ModelContentItems;
                errorCode = mcp.ErrorCode;
                errorMessage = mcp.ErrorMessage;
                break;

            case DynamicToolCallPayload dynamic
                when !string.IsNullOrWhiteSpace(dynamic.CallId)
                     && !string.IsNullOrWhiteSpace(dynamic.ToolName)
                     && !string.Equals(dynamic.Status, "inProgress", StringComparison.OrdinalIgnoreCase):
                callId = dynamic.CallId;
                if (string.IsNullOrWhiteSpace(dynamic.ProviderFlatName))
                    return false;
                toolName = dynamic.ToolName;
                functionNamespace = dynamic.Namespace;
                providerFlatName = dynamic.ProviderFlatName;
                arguments = dynamic.Arguments;
                contentItems = dynamic.ContentItems;
                errorCode = dynamic.ErrorCode;
                errorMessage = dynamic.ErrorMessage;
                break;

            default:
                return false;
        }

        call = CreatePersistedFunctionCall(
            callId,
            functionNamespace,
            toolName,
            providerFlatName,
            arguments);
        var result = BuildModelToolResult(contentItems, null, errorCode, errorMessage);
        resultMessage = new ChatMessage(
            ChatRole.Tool,
            (IList<AIContent>)[new FunctionResultContent(callId, result)]);
        return true;
    }

    private static object BuildModelToolResult(
        IReadOnlyList<PluginFunctionContentItem>? contentItems,
        string? fallback,
        string? errorCode,
        string? errorMessage)
    {
        var contents = new List<AIContent>();
        if (contentItems != null)
        {
            foreach (var item in contentItems)
            {
                if (string.Equals(item.Type, "text", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(item.Text))
                {
                    contents.Add(new TextContent(item.Text));
                }
                else if (string.Equals(item.Type, "image", StringComparison.OrdinalIgnoreCase)
                         && !string.IsNullOrWhiteSpace(item.DataBase64)
                         && !string.IsNullOrWhiteSpace(item.MediaType))
                {
                    try
                    {
                        contents.Add(new DataContent(Convert.FromBase64String(item.DataBase64), item.MediaType));
                    }
                    catch (FormatException)
                    {
                        // Invalid persisted image data is omitted; textual fallback remains usable.
                    }
                }
            }
        }

        if (contents.Count == 1 && contents[0] is TextContent text)
            return text.Text;
        if (contents.Count > 0)
            return contents;
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;
        if (!string.IsNullOrWhiteSpace(errorMessage))
            return string.IsNullOrWhiteSpace(errorCode) ? errorMessage : $"{errorCode}: {errorMessage}";

        return "Tool completed without model-visible content.";
    }

    private static FunctionCallContent CreatePersistedFunctionCall(
        string callId,
        string? functionNamespace,
        string localName,
        string providerFlatName,
        JsonObject? arguments)
    {
        var call = new FunctionCallContent(callId, localName, BuildToolArguments(arguments))
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["dotcraft.tool.provider_flat_name"] = providerFlatName
            }
        };
        if (!string.IsNullOrWhiteSpace(functionNamespace))
        {
            call.AdditionalProperties["openai.responses.function_call.namespace"] = functionNamespace;
            call.AdditionalProperties["namespace"] = functionNamespace;
        }

        return call;
    }

    private static bool TryBuildHostedImageGenerationMessage(
        ToolResultPayload payload,
        out ChatMessage message)
    {
        message = new ChatMessage(ChatRole.Assistant, string.Empty);
        var imageItem = payload.ContentItems?
            .FirstOrDefault(static item =>
                string.Equals(item.Type, "image", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(item.DataBase64));

        byte[]? imageBytes = null;
        if (imageItem?.DataBase64 is { } dataBase64)
        {
            try
            {
                imageBytes = Convert.FromBase64String(dataBase64);
            }
            catch (FormatException)
            {
                imageBytes = null;
            }
        }

        var content = new HostedImageGenerationContent
        {
            Id = payload.CallId,
            Status = payload.Success ? "completed" : "failed",
            RevisedPrompt = string.IsNullOrWhiteSpace(payload.Result) ? null : payload.Result,
            ImageBytes = imageBytes,
            MediaType = string.IsNullOrWhiteSpace(imageItem?.MediaType) ? "image/png" : imageItem.MediaType!,
            ErrorMessage = payload.Success ? null : payload.Result
        };
        message = new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[content]);
        return true;
    }

    private static bool TryBuildImageGenerationMessage(
        SessionItem item,
        out ChatMessage message)
    {
        message = new ChatMessage(ChatRole.Assistant, string.Empty);
        if (item.AsImageGeneration is not { } payload ||
            string.IsNullOrWhiteSpace(payload.CallId) ||
            string.Equals(payload.Status, "inProgress", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        byte[]? imageBytes = null;
        string? errorMessage = payload.ErrorMessage;
        if (string.Equals(payload.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(payload.Result))
            {
                try
                {
                    imageBytes = Convert.FromBase64String(payload.Result);
                }
                catch (FormatException)
                {
                    errorMessage = "Image generation returned invalid image data.";
                }
            }
            else
            {
                errorMessage = "Image generation completed without image data.";
            }
        }

        var content = new HostedImageGenerationContent
        {
            Id = payload.CallId,
            Status = imageBytes is { Length: > 0 } ? "completed" : "failed",
            RevisedPrompt = payload.RevisedPrompt,
            ImageBytes = imageBytes,
            MediaType = string.IsNullOrWhiteSpace(payload.MediaType) ? "image/png" : payload.MediaType,
            ErrorMessage = errorMessage
        };
        message = new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[content]);
        return true;
    }

    private static IDictionary<string, object?>? BuildToolArguments(JsonObject? arguments)
    {
        if (arguments is null || arguments.Count == 0)
            return new Dictionary<string, object?>();

        return JsonSerializer.Deserialize<Dictionary<string, object?>>(
            arguments.ToJsonString(),
            SessionPersistenceJsonOptions.Default);
    }

    private sealed class AssistantSamplingSegmentBuilder
    {
        private readonly List<AIContent> _reasoning = [];
        private readonly List<AIContent> _visible = [];
        private readonly List<AIContent> _toolCalls = [];

        public void AddReasoning(string text) => _reasoning.Add(new TextReasoningContent(text));

        public void AddText(string text) => _visible.Add(new TextContent(text));

        public void AddToolCall(FunctionCallContent content) => _toolCalls.Add(content);

        public bool TryBuild(out ChatMessage message)
        {
            message = new ChatMessage(ChatRole.Assistant, string.Empty);
            if (_visible.Count == 0 && _toolCalls.Count == 0)
                return false;

            var contents = new List<AIContent>();
            if (_toolCalls.Count > 0)
                contents.AddRange(_reasoning);
            contents.AddRange(_visible);
            contents.AddRange(_toolCalls);

            message = new ChatMessage(ChatRole.Assistant, contents);
            return true;
        }

        public void Clear()
        {
            _reasoning.Clear();
            _visible.Clear();
            _toolCalls.Clear();
        }
    }
}
