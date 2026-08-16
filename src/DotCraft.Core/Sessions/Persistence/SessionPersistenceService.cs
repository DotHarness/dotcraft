using DotCraft.Persistence;
using DotCraft.Tracing;
using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

public sealed record TraceSessionDeletionDescriptor(
    string SessionKey,
    string? RootThreadId,
    string BindingKind,
    string DeletionScope);

public static class SessionPersistenceDeletionScopes
{
    public const string ThreadCascade = "threadCascade";
    public const string TraceOnly = "traceOnly";
}

/// <summary>
/// Unified persistence facade for Session Core thread and tracing state.
/// ThreadStore and TraceStore remain the low-level implementations behind this facade.
/// </summary>
public sealed class SessionPersistenceService(
    ThreadStore threadStore,
    TraceStore? traceStore = null,
    TokenUsageStore? tokenUsageStore = null,
    WorkspaceStateDatabase? stateRuntime = null)
{
    private readonly TraceStore _traceStore = traceStore ?? new TraceStore();
    private readonly TokenUsageStore? _tokenUsageStore = tokenUsageStore;
    private readonly WorkspaceStateDatabase _stateRuntime = stateRuntime ?? threadStore.StateDatabase;
    private readonly ResponsesContextWindowStore _responsesContextWindowStore = new(stateRuntime ?? threadStore.StateDatabase);

    public Task SaveThreadAsync(SessionThread thread, CancellationToken ct = default)
        => threadStore.SaveThreadAsync(thread, ct);

    internal Task SaveTurnAsync(SessionThread thread, SessionTurn turn, CancellationToken ct = default)
        => threadStore.SaveTurnAsync(thread, turn, ct);

    internal Task CommitTurnAsync(TurnPersistenceCommit commit, CancellationToken ct = default)
        => threadStore.CommitTurnAsync(commit, ct);

    internal WorkspaceStateDatabase StateDatabase => _stateRuntime;

    internal string DataPath => threadStore.DataPath;

    public Task<SessionThread?> LoadThreadAsync(string threadId, CancellationToken ct = default)
        => threadStore.LoadThreadAsync(threadId, ct);

    internal Task<ThreadRecoveryPackage> ExportRecoveryAsync(
        string threadId,
        string workspacePath,
        CancellationToken ct = default)
        => threadStore.ExportRecoveryAsync(threadId, workspacePath, ct);

    internal Task<string> RestoreRecoveryAsync(
        string packagePath,
        string expectedThreadId,
        string workspacePath,
        CancellationToken ct = default)
        => threadStore.RestoreRecoveryAsync(packagePath, expectedThreadId, workspacePath, ct);

    public Task<ThreadHistorySnapshot> ReadThreadSnapshotAsync(
        string threadId,
        CancellationToken ct = default)
        => threadStore.ReadThreadSnapshotAsync(threadId, ct);

    public Task<ThreadHistoryPage<SessionTurn>> ListThreadTurnsAsync(
        string threadId,
        ThreadHistoryCursor? cursor,
        int limit,
        ThreadHistorySortDirection direction,
        CancellationToken ct = default)
        => threadStore.ListThreadTurnsAsync(threadId, cursor, limit, direction, ct);

    public Task<ThreadHistoryPage<ThreadHistoryItem>> ListThreadItemsAsync(
        string threadId,
        string? turnId,
        ThreadHistoryCursor? cursor,
        int limit,
        ThreadHistorySortDirection direction,
        CancellationToken ct = default)
        => threadStore.ListThreadItemsAsync(threadId, turnId, cursor, limit, direction, ct);

    public Task<SessionThread?> LoadThreadFromPathAsync(string path, CancellationToken ct = default)
        => threadStore.LoadThreadFromPathAsync(path, ct);

    public Task<List<ThreadSummary>> LoadIndexAsync(CancellationToken ct = default)
        => threadStore.LoadIndexAsync(ct);

    internal Task PersistModelHistoryAsync(
        IReadOnlyList<ChatMessage> history,
        string threadId,
        string turnId,
        int persistedPrefixLength,
        CancellationToken ct = default)
        => threadStore.PersistModelHistoryAsync(history, threadId, turnId, persistedPrefixLength, ct);

    internal Task AppendModelHistoryAsync(
        string threadId,
        IReadOnlyList<ChatMessage> history,
        string turnId,
        CancellationToken ct = default)
        => threadStore.AppendModelHistoryAsync(threadId, history, turnId, ct);

    internal Task<ForkModelHistoryMaterialization> BuildForkModelHistoryMaterializationAsync(
        SessionThread source,
        SessionThread forked,
        CancellationToken ct = default)
        => threadStore.BuildForkModelHistoryMaterializationAsync(source, forked, ct);

    public Task<List<ChatMessage>> LoadModelHistoryAsync(
        string threadId,
        CancellationToken ct = default)
        => threadStore.LoadModelHistoryAsync(threadId, ct);

    internal Task<List<ChatMessage>> LoadModelHistoryAsync(
        SessionThread thread,
        string? excludedTurnId,
        CancellationToken ct = default)
        => threadStore.LoadModelHistoryAsync(thread, excludedTurnId, ct);

    public Task RollbackThreadAsync(SessionThread thread, int numTurns, CancellationToken ct = default)
        => threadStore.RollbackThreadAsync(thread, numTurns, ct);

    /// <summary>
    /// Appends an internal recovery checkpoint for the current compacted model-visible history.
    /// </summary>
    public Task AppendCompactionCheckpointAsync(
        string threadId,
        string coveredThroughTurnId,
        IReadOnlyList<ChatMessage> replacementHistory,
        string trigger,
        string mode,
        long tokensBefore,
        long tokensAfter,
        CancellationToken ct = default)
        => threadStore.AppendCompactionCheckpointAsync(
            threadId,
            coveredThroughTurnId,
            replacementHistory,
            trigger,
            mode,
            tokensBefore,
            tokensAfter,
            ct);

    public long? LoadContextUsageTokens(string threadId)
        => threadStore.LoadContextUsageTokens(threadId);

    /// <summary>
    /// Loads the persisted context-window usage token count and diagnostic metadata for a thread.
    /// </summary>
    public ContextUsagePersistenceSnapshot? LoadContextUsageSnapshot(string threadId)
        => threadStore.LoadContextUsageSnapshot(threadId);

    public ContextUsageAnchor? LoadContextUsageAnchor(string threadId)
        => threadStore.LoadContextUsageAnchor(threadId);

    public Task SaveContextUsageTokensAsync(string threadId, long tokens, CancellationToken ct = default)
        => threadStore.SaveContextUsageTokensAsync(threadId, tokens, ct);

    public Task SaveContextUsageTokensAsync(string threadId, long tokens, string? source, CancellationToken ct = default)
        => threadStore.SaveContextUsageTokensAsync(threadId, tokens, source, ct);

    public Task SaveContextUsageTokensAsync(
        string threadId,
        long tokens,
        string? source,
        bool isEstimate,
        CancellationToken ct = default)
        => threadStore.SaveContextUsageTokensAsync(threadId, tokens, source, isEstimate, ct);

    public Task SaveContextUsageAnchorAsync(string threadId, ContextUsageAnchor anchor, CancellationToken ct = default)
        => threadStore.SaveContextUsageAnchorAsync(threadId, anchor, ct);

    public Task SaveContextUsageAnchorAsync(
        string threadId,
        long displayTokens,
        ContextUsageAnchor anchor,
        string? source,
        CancellationToken ct = default)
        => threadStore.SaveContextUsageAnchorAsync(threadId, displayTokens, anchor, source, ct);

    public Task SaveContextUsageAnchorAsync(
        string threadId,
        long displayTokens,
        ContextUsageAnchor anchor,
        string? source,
        bool isEstimate,
        CancellationToken ct = default)
        => threadStore.SaveContextUsageAnchorAsync(threadId, displayTokens, anchor, source, isEstimate, ct);

    internal ResponsesContextWindowRecord GetOrCreateResponsesContextWindow(string threadId)
        => _responsesContextWindowStore.GetOrCreate(threadId);

    internal ResponsesContextWindowRecord AdvanceResponsesContextWindow(string threadId)
        => _responsesContextWindowStore.Advance(threadId);

    internal ResponsesContextWindowRecord ReconcileResponsesContextWindow(
        string threadId,
        string committedWindowId) =>
        _responsesContextWindowStore.Reconcile(threadId, committedWindowId);

    internal Task<ProviderHistorySnapshot> LoadProviderHistoryAsync(
        SessionThread thread,
        string currentContextWindowId,
        CancellationToken ct = default)
        => threadStore.LoadProviderHistoryAsync(thread, currentContextWindowId, ct);

    internal Task AppendProviderHistoryItemsAsync(
        ProviderHistoryItemsAppendedPayload payload,
        CancellationToken ct = default)
        => threadStore.AppendProviderHistoryItemsAsync(payload, ct);

    internal Task ReplaceProviderHistoryAsync(
        ProviderHistoryReplacedPayload payload,
        CancellationToken ct = default)
        => threadStore.ReplaceProviderHistoryAsync(payload, ct);

    internal Task AbortProviderHistoryAttemptAsync(
        ProviderHistoryAttemptAbortedPayload payload,
        CancellationToken ct = default)
        => threadStore.AbortProviderHistoryAttemptAsync(payload, ct);

    public IReadOnlyDictionary<string, string> GetItemWidgetStates(string threadId)
        => threadStore.LoadItemWidgetStates(threadId);

    public void SaveItemWidgetState(string threadId, string callId, string widgetStateJson)
        => threadStore.SaveItemWidgetState(threadId, callId, widgetStateJson);

    public void DeleteItemWidgetState(string threadId, string callId)
        => threadStore.DeleteItemWidgetState(threadId, callId);

    public Task<ThreadGoal?> GetThreadGoalAsync(string threadId, CancellationToken ct = default)
        => threadStore.GetThreadGoalAsync(threadId, ct);

    public Task UpsertThreadGoalAsync(ThreadGoal goal, CancellationToken ct = default)
        => threadStore.UpsertThreadGoalAsync(goal, ct);

    public Task<GoalAccountingOutcome> AccountThreadGoalUsageAsync(
        string threadId,
        string expectedGoalId,
        TokenUsageInfo usageDelta,
        long timeDeltaSeconds,
        GoalAccountingMode mode,
        CancellationToken ct = default)
        => threadStore.AccountThreadGoalUsageAsync(threadId, expectedGoalId, usageDelta, timeDeltaSeconds, mode, ct);

    public Task<bool> DeleteThreadGoalAsync(string threadId, CancellationToken ct = default)
        => threadStore.DeleteThreadGoalAsync(threadId, ct);

    public Task UpsertThreadSpawnEdgeAsync(ThreadSpawnEdge edge, CancellationToken ct = default)
        => threadStore.UpsertThreadSpawnEdgeAsync(edge, ct);

    public Task SetThreadSpawnEdgeStatusAsync(
        string parentThreadId,
        string childThreadId,
        string status,
        CancellationToken ct = default)
        => threadStore.SetThreadSpawnEdgeStatusAsync(parentThreadId, childThreadId, status, ct);

    public Task<IReadOnlyList<ThreadSpawnEdge>> ListSubAgentChildrenAsync(
        string parentThreadId,
        bool includeClosed = false,
        CancellationToken ct = default)
        => threadStore.ListSubAgentChildrenAsync(parentThreadId, includeClosed, ct);

    public Task AddSubAgentMailboxEntryAsync(SubAgentMailboxEntry entry, CancellationToken ct = default)
        => threadStore.AddSubAgentMailboxEntryAsync(entry, ct);

    public Task<IReadOnlyList<SubAgentMailboxEntry>> ListPendingSubAgentMailboxAsync(
        string rootThreadId,
        string targetAgentPath,
        CancellationToken ct = default)
        => threadStore.ListPendingSubAgentMailboxAsync(rootThreadId, targetAgentPath, ct);

    public Task MarkSubAgentMailboxDeliveredAsync(
        string rootThreadId,
        IReadOnlyList<string> entryIds,
        DateTimeOffset deliveredAt,
        CancellationToken ct = default)
        => threadStore.MarkSubAgentMailboxDeliveredAsync(rootThreadId, entryIds, deliveredAt, ct);

    public TraceSessionDeletionDescriptor DescribeSessionDeletion(string sessionKey)
        => _traceStore.DescribeSessionDeletion(sessionKey);

    public Dictionary<string, TraceSessionDeletionDescriptor> DescribeSessionDeletions(IEnumerable<string> sessionKeys)
        => _traceStore.DescribeSessionDeletions(sessionKeys);

    public Task DeleteThreadCascadeAsync(string threadId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var sessionKeys = _traceStore.GetBoundSessionKeys(threadId);
        foreach (var sessionKey in sessionKeys)
            _traceStore.ClearSession(sessionKey);

        threadStore.DeleteThread(threadId);
        DeleteUsageRecords([threadId], sessionKeys);
        return Task.CompletedTask;
    }

    public async Task<bool> DeleteTraceSessionAsync(
        string sessionKey,
        Func<string, CancellationToken, Task>? deleteThreadAsync = null,
        CancellationToken ct = default)
    {
        var descriptor = DescribeSessionDeletion(sessionKey);
        if (descriptor.DeletionScope == SessionPersistenceDeletionScopes.ThreadCascade
            && !string.IsNullOrWhiteSpace(descriptor.RootThreadId))
        {
            if (deleteThreadAsync != null)
            {
                try
                {
                    await deleteThreadAsync(descriptor.RootThreadId, ct);
                    DeleteUsageRecords([descriptor.RootThreadId], [sessionKey]);
                    return true;
                }
                catch (KeyNotFoundException)
                {
                    await DeleteThreadCascadeAsync(descriptor.RootThreadId, ct);
                    return true;
                }
            }

            await DeleteThreadCascadeAsync(descriptor.RootThreadId, ct);
            return true;
        }

        var deleted = _traceStore.DeleteStandaloneSession(sessionKey);
        DeleteUsageRecords([], [sessionKey]);
        return deleted;
    }

    public async Task DeleteTraceSessionsAsync(
        IEnumerable<string> sessionKeys,
        Func<string, CancellationToken, Task>? deleteThreadAsync = null,
        CancellationToken ct = default)
    {
        var descriptors = DescribeSessionDeletions(sessionKeys).Values.ToList();
        var boundThreadIds = descriptors
            .Where(d => d.DeletionScope == SessionPersistenceDeletionScopes.ThreadCascade
                && !string.IsNullOrWhiteSpace(d.RootThreadId))
            .Select(d => d.RootThreadId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var standaloneSessions = descriptors
            .Where(d => d.DeletionScope == SessionPersistenceDeletionScopes.TraceOnly)
            .Select(d => d.SessionKey)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var threadId in boundThreadIds)
        {
            var sessionKeysForThread = descriptors
                .Where(d => string.Equals(d.RootThreadId, threadId, StringComparison.Ordinal))
                .Select(d => d.SessionKey)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (deleteThreadAsync != null)
            {
                try
                {
                    await deleteThreadAsync(threadId, ct);
                    DeleteUsageRecords([threadId], sessionKeysForThread);
                    continue;
                }
                catch (KeyNotFoundException)
                {
                    // Fall through to persistence-only cleanup when the runtime thread is already gone.
                }
            }

            await DeleteThreadCascadeAsync(threadId, ct);
        }

        foreach (var sessionKey in standaloneSessions)
        {
            _traceStore.DeleteStandaloneSession(sessionKey);
            DeleteUsageRecords([], [sessionKey]);
        }
    }

    /// <summary>
    /// Runs lightweight database maintenance after bulk state deletion.
    /// </summary>
    public bool CompactStateIfWorthwhile(bool force = false)
    {
        try
        {
            return _stateRuntime.CompactIfWorthwhile(force);
        }
        catch
        {
            return false;
        }
    }

    private void DeleteUsageRecords(IEnumerable<string> threadIds, IEnumerable<string> sessionKeys)
        => _tokenUsageStore?.DeleteDashboardUsageRecords(threadIds, sessionKeys);
}
