using DotCraft.Protocol;
using DotCraft.Agents;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// A full <see cref="ISessionService"/> implementation for AppServer unit tests.
/// Thread lifecycle methods are backed by a real <see cref="ThreadStore"/> so that
/// <c>thread/start</c>, <c>thread/list</c>, etc. produce realistic wire responses.
/// <c>SubmitInputAsync</c> yields canned <see cref="SessionEvent"/> sequences queued
/// per thread via <see cref="EnqueueSubmitEvents"/>.
/// </summary>
internal sealed class TestableSessionService : ISessionService, IThreadAgentRefreshService, ISubAgentSyntheticTurnService
{
    private readonly ThreadStore _store;
    private readonly Dictionary<string, SessionThread> _cache = new();
    private readonly Dictionary<string, Queue<SessionEvent[]>> _submitQueue = new();
    private readonly List<(string threadId, string turnId)> _cancelledTurns = new();
    private readonly List<string> _cancelledMaintenances = new();
    private readonly List<(string threadId, string turnId, string requestId, SessionApprovalDecision decision)> _resolvedApprovals = new();
    private readonly List<(string threadId, string turnId, string requestId, RequestUserInputResponse response)> _resolvedUserInputs = new();
    private readonly List<SessionEventType> _yieldedSubmitEventTypes = new();

    public IReadOnlyList<(string threadId, string turnId)> CancelledTurns => _cancelledTurns;
    public IReadOnlyList<string> CancelledMaintenances => _cancelledMaintenances;
    public IReadOnlyList<(string threadId, string turnId, string requestId, SessionApprovalDecision decision)> ResolvedApprovals => _resolvedApprovals;
    public IReadOnlyList<(string threadId, string turnId, string requestId, RequestUserInputResponse response)> ResolvedUserInputs => _resolvedUserInputs;
    public IReadOnlyList<SessionEventType> YieldedSubmitEventTypes => _yieldedSubmitEventTypes;
    public IReadOnlyList<AIContent> LastSubmittedContent { get; private set; } = [];
    public IReadOnlyList<ChatMessage>? LastSubmittedMessages { get; private set; }
    public CancellationToken LastSubmitCancellationToken { get; private set; }
    public Func<string, IList<AIContent>, ChatMessage[]?, IEnumerable<SessionEvent>>? SubmitInputHandler { get; set; }
    public Func<SessionThread, CancellationToken, Task>? CreateThreadHandler { get; set; }
    public Func<string, CancellationToken, Task<ThreadMemoryConsolidationResult>>? ConsolidateThreadMemoryHandler { get; set; }
    public Func<SessionThread, ThreadSummaryRuntime>? RuntimeSnapshotHandler { get; set; }
    public IReadOnlyList<string> RefreshedThreadAgents => _refreshedThreadAgents;
    private readonly List<string> _refreshedThreadAgents = new();

    /// <inheritdoc />
    public Action<SessionThread>? ThreadCreatedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<string>? ThreadDeletedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<SessionThread>? ThreadRenamedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<string, ThreadStatus, ThreadStatus>? ThreadStatusChangedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<string, SessionThreadRuntimeSignal>? ThreadRuntimeSignalForBroadcast { get; set; }

    /// <inheritdoc />
    public ContextUsageSnapshot? TryGetContextUsageSnapshot(string threadId) => null;

    /// <inheritdoc />
    public ThreadSummaryRuntime GetThreadRuntimeSnapshot(SessionThread thread) =>
        RuntimeSnapshotHandler?.Invoke(thread) ?? ThreadSummaryRuntime.FromThread(thread);

    public TestableSessionService(ThreadStore store) => _store = store;

    // -------------------------------------------------------------------------
    // Canned event configuration
    // -------------------------------------------------------------------------

    /// <summary>
    /// Queues a batch of events to be returned by the next <c>SubmitInputAsync</c>
    /// call for the given thread. Multiple calls enqueue multiple batches.
    /// </summary>
    public void EnqueueSubmitEvents(string threadId, params SessionEvent[] events)
    {
        if (!_submitQueue.TryGetValue(threadId, out var queue))
            _submitQueue[threadId] = queue = new Queue<SessionEvent[]>();
        queue.Enqueue(events);
    }

    // -------------------------------------------------------------------------
    // ISessionService — thread lifecycle (backed by ThreadStore)
    // -------------------------------------------------------------------------

    public async Task<SessionThread> CreateThreadAsync(
        SessionIdentity identity,
        ThreadConfiguration? config = null,
        HistoryMode historyMode = HistoryMode.Server,
        string? threadId = null,
        string? displayName = null,
        CancellationToken ct = default,
        ThreadSource? source = null)
    {
        var thread = new SessionThread
        {
            Id = threadId ?? SessionIdGenerator.NewThreadId(),
            WorkspacePath = identity.WorkspacePath,
            UserId = identity.UserId,
            OriginChannel = identity.ChannelName,
            Status = ThreadStatus.Active,
            HistoryMode = historyMode,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            Configuration = config,
            DisplayName = displayName,
            Source = source ?? ThreadSource.User()
        };
        if (identity.ChannelContext != null)
        {
            thread.ChannelContext = identity.ChannelContext;
            thread.Metadata["channelContext"] = identity.ChannelContext;
        }
        if (CreateThreadHandler != null)
            await CreateThreadHandler(thread, ct);

        _cache[thread.Id] = thread;
        await _store.SaveThreadAsync(thread, ct);
        ThreadCreatedForBroadcast?.Invoke(thread);
        return thread;
    }

    public async Task<SessionThread> ResumeThreadAsync(string threadId, CancellationToken ct = default)
    {
        var t = await GetOrLoadAsync(threadId, ct);
        if (t.Status != ThreadStatus.Active)
        {
            t.Status = ThreadStatus.Active;
            t.LastActiveAt = DateTimeOffset.UtcNow;
            await _store.SaveThreadAsync(t, ct);
        }
        return t;
    }

    public async Task<ThreadResetResult> ResetConversationAsync(
        SessionIdentity identity,
        ThreadConfiguration? config = null,
        HistoryMode historyMode = HistoryMode.Server,
        string? displayName = null,
        CancellationToken ct = default)
    {
        var active = await FindThreadsAsync(identity, includeArchived: false, crossChannelOrigins: null, ct);
        var archived = new List<string>();
        foreach (var summary in active.Where(s => s.Status is ThreadStatus.Active or ThreadStatus.Paused))
        {
            await ArchiveThreadAsync(summary.Id, ct);
            archived.Add(summary.Id);
        }

        var thread = await CreateThreadAsync(identity, config, historyMode, displayName: displayName, ct: ct);
        return new ThreadResetResult { Thread = thread, ArchivedThreadIds = archived, CreatedLazily = true };
    }

    public async Task PauseThreadAsync(string threadId, CancellationToken ct = default)
    {
        var t = await GetOrLoadAsync(threadId, ct);
        if (t.Status == ThreadStatus.Paused) return;
        t.Status = ThreadStatus.Paused;
        await _store.SaveThreadAsync(t, ct);
    }

    public async Task ArchiveThreadAsync(string threadId, CancellationToken ct = default)
    {
        var t = await GetOrLoadAsync(threadId, ct);
        if (t.Status == ThreadStatus.Archived) return;
        t.Status = ThreadStatus.Archived;
        await _store.SaveThreadAsync(t, ct);
    }

    public async Task UnarchiveThreadAsync(string threadId, CancellationToken ct = default)
    {
        var t = await GetOrLoadAsync(threadId, ct);
        if (t.Status == ThreadStatus.Active) return;
        t.Status = ThreadStatus.Active;
        t.LastActiveAt = DateTimeOffset.UtcNow;
        await _store.SaveThreadAsync(t, ct);
    }

    public async Task RenameThreadAsync(string threadId, string displayName, CancellationToken ct = default)
    {
        var t = await GetOrLoadAsync(threadId, ct);
        var previous = t.DisplayName;
        t.DisplayName = displayName;
        await _store.SaveThreadAsync(t, ct);
        if (previous != displayName)
            ThreadRenamedForBroadcast?.Invoke(t);
    }

    public async Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(
        SessionIdentity identity,
        bool includeArchived = false,
        IReadOnlyList<string>? crossChannelOrigins = null,
        CancellationToken ct = default,
        bool includeSubAgents = false)
    {
        var index = await _store.LoadIndexAsync(ct);
        var hasCross = crossChannelOrigins is { Count: > 0 };
        return index
            .Where(s =>
            {
                if (!string.Equals(s.WorkspacePath, identity.WorkspacePath, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!(includeArchived || s.Status != ThreadStatus.Archived))
                    return false;
                if (!includeSubAgents && (string.Equals(s.Source.Kind, ThreadSourceKinds.SubAgent, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.OriginChannel, SubAgentThreadOrigin.ChannelName, StringComparison.OrdinalIgnoreCase)))
                    return false;
                if (includeSubAgents
                    && (string.Equals(s.Source.Kind, ThreadSourceKinds.SubAgent, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(s.OriginChannel, SubAgentThreadOrigin.ChannelName, StringComparison.OrdinalIgnoreCase))
                    && (identity.UserId == null || s.UserId == identity.UserId))
                    return true;

                var identityMatch =
                    (identity.UserId == null || s.UserId == identity.UserId)
                    && (identity.ChannelContext == null
                        ? s.ChannelContext == null
                        : s.ChannelContext == identity.ChannelContext);

                if (identityMatch)
                    return true;

                if (!hasCross)
                    return false;

                foreach (var o in crossChannelOrigins!)
                {
                    if (string.Equals(o, s.OriginChannel, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            })
            .Select(summary =>
            {
                if (!_cache.TryGetValue(summary.Id, out var cachedThread))
                    return summary;

                var hydrated = ThreadSummary.FromThread(cachedThread);
                hydrated.Runtime = GetThreadRuntimeSnapshot(cachedThread);
                return hydrated;
            })
            .OrderByDescending(s => s.LastActiveAt)
            .ToList();
    }

    public Task UpsertThreadSpawnEdgeAsync(ThreadSpawnEdge edge, CancellationToken ct = default) =>
        _store.UpsertThreadSpawnEdgeAsync(edge, ct);

    public Task SetThreadSpawnEdgeStatusAsync(
        string parentThreadId,
        string childThreadId,
        string status,
        CancellationToken ct = default) =>
        _store.SetThreadSpawnEdgeStatusAsync(parentThreadId, childThreadId, status, ct);

    public Task<IReadOnlyList<ThreadSpawnEdge>> ListSubAgentChildrenAsync(
        string parentThreadId,
        bool includeClosed = false,
        CancellationToken ct = default) =>
        _store.ListSubAgentChildrenAsync(parentThreadId, includeClosed, ct);

    public async Task<SessionThread> GetThreadAsync(string threadId, CancellationToken ct = default) =>
        await GetOrLoadAsync(threadId, ct);

    public Task<SessionThread> EnsureThreadLoadedAsync(string threadId, CancellationToken ct = default) =>
        GetThreadAsync(threadId, ct);

    public async Task<ThreadGoal?> GetThreadGoalAsync(string threadId, CancellationToken ct = default)
    {
        _ = await GetOrLoadAsync(threadId, ct);
        return await _store.GetThreadGoalAsync(threadId, ct);
    }

    public async Task<ThreadGoal> SetThreadGoalAsync(
        string threadId,
        ThreadGoalUpdate update,
        GoalSetMode mode = GoalSetMode.UpsertOrUpdate,
        CancellationToken ct = default)
    {
        _ = await GetOrLoadAsync(threadId, ct);
        var existing = await _store.GetThreadGoalAsync(threadId, ct);
        if (existing == null && string.IsNullOrWhiteSpace(update.Objective))
            throw new InvalidOperationException($"Thread '{threadId}' has no goal.");

        var objective = update.Objective?.Trim();
        var now = DateTimeOffset.UtcNow;
        var replace = existing != null
            && !string.IsNullOrWhiteSpace(objective)
            && (!string.Equals(existing.Objective, objective, StringComparison.Ordinal)
                || existing.Status == ThreadGoalStatus.Complete
                || mode == GoalSetMode.ReplaceExisting);
        var goal = replace || existing == null
            ? new ThreadGoal
            {
                ThreadId = threadId,
                GoalId = SessionIdGenerator.NewGoalId(),
                Objective = objective ?? throw new InvalidOperationException("Goal objective is required."),
                Status = ThreadGoalStatus.Active,
                TokenBudget = update.HasTokenBudget ? update.TokenBudget : null,
                TokensUsed = new TokenUsageInfo(),
                CreatedAt = now,
                UpdatedAt = now
            }
            : existing with
            {
                Objective = objective ?? existing.Objective,
                Status = update.Status ?? existing.Status,
                TokenBudget = update.HasTokenBudget ? update.TokenBudget : existing.TokenBudget,
                UpdatedAt = now
            };
        await _store.UpsertThreadGoalAsync(goal, ct);
        return goal;
    }

    public async Task<ThreadGoalClearResult> ClearThreadGoalAsync(string threadId, CancellationToken ct = default)
    {
        _ = await GetOrLoadAsync(threadId, ct);
        return new ThreadGoalClearResult(await _store.DeleteThreadGoalAsync(threadId, ct));
    }

    public Task SetThreadModeAsync(string threadId, string mode, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task RefreshThreadAgentAsync(string threadId, CancellationToken ct = default)
    {
        _refreshedThreadAgents.Add(threadId);
        return Task.CompletedTask;
    }

    public void InvalidateThreadAgents()
    {
    }

    public async Task UpdateThreadConfigurationAsync(
        string threadId, ThreadConfiguration config, CancellationToken ct = default)
    {
        var t = await GetOrLoadAsync(threadId, ct);
        t.Configuration = config;
        await _store.SaveThreadAsync(t, ct);
    }

    public Task DeleteThreadPermanentlyAsync(string threadId, CancellationToken ct = default)
    {
        _cache.Remove(threadId);
        _store.DeleteThread(threadId);
        _store.DeleteSessionFile(threadId);
        ThreadDeletedForBroadcast?.Invoke(threadId);
        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // ISessionService — turn operations (backed by canned events)
    // -------------------------------------------------------------------------

    public IAsyncEnumerable<SessionEvent> SubmitInputAsync(
        string threadId,
        IList<AIContent> content,
        SenderContext? sender = null,
        ChatMessage[]? messages = null,
        CancellationToken ct = default,
        SessionInputSnapshot? inputSnapshot = null)
    {
        LastSubmittedContent = content.ToList();
        LastSubmittedMessages = messages?.ToList();
        LastSubmitCancellationToken = ct;
        if (SubmitInputHandler != null)
            return YieldEvents([.. SubmitInputHandler(threadId, content, messages)], ct);
        if (_submitQueue.TryGetValue(threadId, out var queue) && queue.TryDequeue(out var events))
            return YieldEvents(events, ct);

        return EmptyEvents();
    }

    public async Task<SessionTurn> StartSubAgentSyntheticTurnAsync(
        string threadId,
        IList<AIContent> content,
        string runtimeType,
        string? profileName,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        var text = string.Concat(content.OfType<TextContent>().Select(c => c.Text));
        var turn = new SessionTurn
        {
            Id = SessionIdGenerator.NewTurnId(thread.Turns.Count + 1),
            ThreadId = threadId,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            OriginChannel = thread.OriginChannel
        };
        var userItem = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(1),
            TurnId = turn.Id,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new UserMessagePayload { Text = text }
        };
        turn.Input = userItem;
        turn.Items.Add(userItem);
        thread.Turns.Add(turn);
        thread.Metadata["subagent.syntheticRuntime"] = runtimeType;
        if (!string.IsNullOrWhiteSpace(profileName))
            thread.Metadata["subagent.profileName"] = profileName;
        await _store.SaveThreadAsync(thread, ct);
        return turn;
    }

    public async Task<SessionTurn> CompleteSubAgentSyntheticTurnAsync(
        string threadId,
        string turnId,
        string text,
        bool isError,
        SubAgentTokenUsage? tokensUsed,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        var turn = thread.Turns.Single(t => t.Id == turnId);
        var item = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(turn.Items.Count + 1),
            TurnId = turn.Id,
            Type = isError ? ItemType.Error : ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = isError
                ? new ErrorPayload { Message = text, Code = "subagent_error", Fatal = true }
                : new AgentMessagePayload { Text = text }
        };
        turn.Items.Add(item);
        turn.Status = isError ? TurnStatus.Failed : TurnStatus.Completed;
        turn.CompletedAt = DateTimeOffset.UtcNow;
        turn.Error = isError ? text : null;
        if (tokensUsed != null)
        {
            turn.TokenUsage = new TokenUsageInfo
            {
                InputTokens = tokensUsed.InputTokens,
                OutputTokens = tokensUsed.OutputTokens,
                TotalTokens = tokensUsed.InputTokens + tokensUsed.OutputTokens
            };
        }
        await _store.SaveThreadAsync(thread, ct);
        return turn;
    }

    public async Task<SessionTurn> CancelSubAgentSyntheticTurnAsync(
        string threadId,
        string turnId,
        string reason,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        var turn = thread.Turns.Single(t => t.Id == turnId);
        turn.Status = TurnStatus.Cancelled;
        turn.CompletedAt = DateTimeOffset.UtcNow;
        await _store.SaveThreadAsync(thread, ct);
        return turn;
    }

    public IAsyncEnumerable<SessionEvent> SubscribeThreadAsync(
        string threadId,
        bool replayRecent = false,
        CancellationToken ct = default)
    {
        // Keep the subscription alive until the token is cancelled so that
        // AppServerConnection.HasSubscription(threadId) stays true for the lifetime of the subscription.
        // In the real SessionService, this stream is driven by the ThreadEventBroker.
        return BlockUntilCancelledAsync(ct);
    }

    private static async IAsyncEnumerable<SessionEvent> BlockUntilCancelledAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { }
        yield break;
    }

    public Task ResolveApprovalAsync(
        string threadId, string turnId, string requestId,
        SessionApprovalDecision decision, CancellationToken ct = default)
    {
        _resolvedApprovals.Add((threadId, turnId, requestId, decision));
        return Task.CompletedTask;
    }

    public Task ResolveUserInputRequestAsync(
        string threadId,
        string turnId,
        string requestId,
        RequestUserInputResponse response,
        CancellationToken ct = default)
    {
        _resolvedUserInputs.Add((threadId, turnId, requestId, response));
        return Task.CompletedTask;
    }

    public Task CancelTurnAsync(string threadId, string turnId, CancellationToken ct = default)
    {
        _cancelledTurns.Add((threadId, turnId));
        return Task.CompletedTask;
    }

    public Task CancelThreadMaintenanceAsync(string threadId, CancellationToken ct = default)
    {
        _cancelledMaintenances.Add(threadId);
        return Task.CompletedTask;
    }

    public Task CleanBackgroundTerminalsAsync(string threadId, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<ThreadMemoryConsolidationResult> ConsolidateThreadMemoryAsync(
        string threadId,
        CancellationToken ct = default) =>
        ConsolidateThreadMemoryHandler?.Invoke(threadId, ct)
        ?? Task.FromResult(new ThreadMemoryConsolidationResult
        {
            Outcome = "skipped",
            Message = "not_configured"
        });

    public async Task<SessionThread> RollbackThreadAsync(string threadId, int numTurns, CancellationToken ct = default)
    {
        if (numTurns <= 0)
            throw new ArgumentOutOfRangeException(nameof(numTurns), "numTurns must be >= 1.");

        var thread = await GetOrLoadAsync(threadId, ct);
        if (thread.Status == ThreadStatus.Archived)
            throw new InvalidOperationException($"Thread '{threadId}' is archived and cannot be rolled back.");
        if (thread.Turns.Any(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput))
            throw new InvalidOperationException($"Thread '{threadId}' has a running Turn. Cancel it before rollback.");
        if (thread.Turns.Count < numTurns)
            throw new InvalidOperationException($"Thread '{threadId}' has only {thread.Turns.Count} turns; cannot roll back {numTurns}.");

        thread.Turns.RemoveRange(thread.Turns.Count - numTurns, numTurns);
        thread.LastActiveAt = DateTimeOffset.UtcNow;
        await _store.RollbackThreadAsync(thread, numTurns, ct);
        return thread;
    }

    public async Task<QueuedTurnInput> EnqueueTurnInputAsync(
        string threadId,
        IList<AIContent> content,
        SenderContext? sender = null,
        CancellationToken ct = default,
        SessionInputSnapshot? inputSnapshot = null)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        var parts = inputSnapshot?.NativeInputParts?.ToList() ?? content.Select(c => c.ToWireInputPart()).ToList();
        var triggerInfo = TurnTriggerScope.Current;
        var queued = new QueuedTurnInput
        {
            Id = SessionIdGenerator.NewQueuedInputId(),
            ThreadId = threadId,
            NativeInputParts = parts,
            MaterializedInputParts = inputSnapshot?.MaterializedInputParts?.ToList() ?? parts,
            DisplayText = inputSnapshot?.DisplayText ?? SessionWireMapper.BuildDisplayText(parts),
            Sender = sender,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = "queued",
            TriggerKind = triggerInfo?.Kind,
            TriggerLabel = triggerInfo?.Label,
            TriggerRefId = triggerInfo?.RefId
        };
        thread.QueuedInputs.Add(queued);
        thread.LastActiveAt = DateTimeOffset.UtcNow;
        await _store.SaveThreadAsync(thread, ct);
        return queued;
    }

    public async Task TryStartNextQueuedTurnAsync(string threadId, CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        if (thread.Turns.Any(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput))
            return;

        var queued = thread.QueuedInputs.FirstOrDefault(q => string.Equals(q.Status, "queued", StringComparison.Ordinal));
        if (queued == null)
            return;

        thread.QueuedInputs.Remove(queued);
        thread.LastActiveAt = DateTimeOffset.UtcNow;
        await _store.SaveThreadAsync(thread, ct);

        LastSubmittedContent = queued.MaterializedInputParts.Select(part => part.ToAIContent()).ToList();
    }

    public async Task<IReadOnlyList<QueuedTurnInput>> RemoveQueuedTurnInputAsync(
        string threadId,
        string queuedInputId,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        thread.QueuedInputs.RemoveAll(q => string.Equals(q.Id, queuedInputId, StringComparison.Ordinal));
        thread.LastActiveAt = DateTimeOffset.UtcNow;
        await _store.SaveThreadAsync(thread, ct);
        return thread.QueuedInputs.ToList();
    }

    public async Task<IReadOnlyList<QueuedTurnInput>> ReorderQueuedTurnInputsAsync(
        string threadId,
        IReadOnlyList<string> orderedQueuedInputIds,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        if (orderedQueuedInputIds.Count != thread.QueuedInputs.Count)
            throw new ArgumentException("orderedQueuedInputIds must contain every current queued input ID exactly once.");
        var byId = thread.QueuedInputs.ToDictionary(q => q.Id, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var reordered = new List<QueuedTurnInput>(thread.QueuedInputs.Count);
        foreach (var queuedInputId in orderedQueuedInputIds)
        {
            if (!seen.Add(queuedInputId) || !byId.TryGetValue(queuedInputId, out var queuedInput))
                throw new ArgumentException("orderedQueuedInputIds must contain every current queued input ID exactly once.");
            reordered.Add(queuedInput);
        }

        thread.QueuedInputs = reordered;
        thread.LastActiveAt = DateTimeOffset.UtcNow;
        await _store.SaveThreadAsync(thread, ct);
        return thread.QueuedInputs.ToList();
    }

    public async Task<TurnSteerResult> SteerTurnAsync(
        string threadId,
        string expectedTurnId,
        string queuedInputId,
        CancellationToken ct = default,
        SenderContext? sender = null)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        var turn = thread.Turns.LastOrDefault(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput)
            ?? throw new InvalidOperationException("No active turn.");
        if (!string.Equals(turn.Id, expectedTurnId, StringComparison.Ordinal))
            throw new InvalidOperationException("Active turn mismatch.");

        var index = thread.QueuedInputs.FindIndex(q => string.Equals(q.Id, queuedInputId, StringComparison.Ordinal));
        if (index < 0)
            throw new KeyNotFoundException($"Queued input '{queuedInputId}' not found.");
        thread.QueuedInputs[index] = thread.QueuedInputs[index] with
        {
            Status = "guidancePending",
            ReadyAfterTurnId = turn.Id,
            Sender = sender ?? thread.QueuedInputs[index].Sender
        };
        await _store.SaveThreadAsync(thread, ct);
        return new TurnSteerResult { TurnId = turn.Id, QueuedInputs = thread.QueuedInputs.ToList() };
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<SessionThread> GetOrLoadAsync(string threadId, CancellationToken ct)
    {
        if (_cache.TryGetValue(threadId, out var cached)) return cached;
        var loaded = await _store.LoadThreadAsync(threadId, ct)
            ?? throw new KeyNotFoundException($"Thread '{threadId}' not found.");
        _cache[threadId] = loaded;
        return loaded;
    }

    private async IAsyncEnumerable<SessionEvent> YieldEvents(
        SessionEvent[] events,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var e in events)
        {
            ct.ThrowIfCancellationRequested();
            _yieldedSubmitEventTypes.Add(e.EventType);
            yield return e;
        }
    }

    private static async IAsyncEnumerable<SessionEvent> EmptyEvents()
    {
        yield break;
    }
}
