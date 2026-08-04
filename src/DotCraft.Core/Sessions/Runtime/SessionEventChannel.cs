using System.Threading.Channels;
using ContextUsageSnapshot = DotCraft.Sessions.Wire.ContextUsageSnapshot;

namespace DotCraft.Sessions;

/// <summary>
/// Wraps a System.Threading.Channels channel to provide typed event emission
/// during Turn execution. One instance is created per Turn.
/// </summary>
internal sealed class SessionEventChannel(
    string id,
    string turnId,
    Func<string>? nextEventId = null,
    Action<SessionEvent>? publish = null,
    Action<SessionEvent>? debugTap = null)
{
    private readonly Channel<SessionEvent> _channel = Channel.CreateUnbounded<SessionEvent>(new UnboundedChannelOptions
    {
        SingleWriter = false,
        SingleReader = false
    });

    private int _eventSequence;

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    private string NextEventId() =>
        nextEventId?.Invoke() ?? $"evt_{Interlocked.Increment(ref _eventSequence):D4}";

    private void Write(SessionEventType type, string? itemId, object? payload)
    {
        var evt = new SessionEvent
        {
            EventId = NextEventId(),
            EventType = type,
            ThreadId = id,
            TurnId = turnId,
            ItemId = itemId,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = payload
        };
        _channel.Writer.TryWrite(evt);
        debugTap?.Invoke(evt);
        publish?.Invoke(evt);
    }

    private void WriteThreadLevel(SessionEventType type, string? threadId, object? payload)
    {
        var evt = new SessionEvent
        {
            EventId = NextEventId(),
            EventType = type,
            ThreadId = threadId ?? id,
            TurnId = null,
            ItemId = null,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = payload
        };
        _channel.Writer.TryWrite(evt);
        debugTap?.Invoke(evt);
        publish?.Invoke(evt);
    }

    // -------------------------------------------------------------------------
    // Turn events
    // -------------------------------------------------------------------------

    public void EmitTurnStarted(SessionTurn turn) =>
        Write(SessionEventType.TurnStarted, null, SnapshotTurn(turn));

    public void EmitTurnCompleted(SessionTurn turn) =>
        Write(SessionEventType.TurnCompleted, null, SnapshotTurn(turn));

    public void EmitTurnFailed(SessionTurn turn, string error) =>
        Write(SessionEventType.TurnFailed, null, new TurnFailedPayload { Turn = SnapshotTurn(turn), Error = error });

    public void EmitTurnCancelled(SessionTurn turn, string reason) =>
        Write(SessionEventType.TurnCancelled, null, new TurnCancelledPayload { Turn = SnapshotTurn(turn), Reason = reason });

    // -------------------------------------------------------------------------
    // Item events
    // -------------------------------------------------------------------------

    // Spec (appserver-protocol.md §6.3): item/started must carry status="started" with no completedAt.
    public void EmitItemStarted(SessionItem item)
    {
        var snapshot = SnapshotItem(item);
        snapshot.Status = ItemStatus.Started;
        snapshot.CompletedAt = null;
        Write(SessionEventType.ItemStarted, item.Id, snapshot);
    }

    public void EmitItemDelta(SessionItem item, object deltaPayload) =>
        Write(SessionEventType.ItemDelta, item.Id, deltaPayload);

    // Spec (appserver-protocol.md §6.3): item/completed must carry status="completed" with completedAt set.
    public void EmitItemCompleted(SessionItem item)
    {
        var snapshot = SnapshotItem(item);
        snapshot.Status = ItemStatus.Completed;
        snapshot.CompletedAt ??= DateTimeOffset.UtcNow;
        Write(SessionEventType.ItemCompleted, item.Id, snapshot);
    }

    // -------------------------------------------------------------------------
    // Approval events
    // -------------------------------------------------------------------------

    public void EmitApprovalRequested(SessionItem item) =>
        Write(SessionEventType.ApprovalRequested, item.Id, SnapshotItem(item));

    public void EmitApprovalResolved(SessionItem item) =>
        Write(SessionEventType.ApprovalResolved, item.Id, SnapshotItem(item));

    // -------------------------------------------------------------------------
    // User input request events
    // -------------------------------------------------------------------------

    public void EmitUserInputRequested(SessionItem item) =>
        Write(SessionEventType.UserInputRequested, item.Id, SnapshotItem(item));

    public void EmitUserInputResolved(SessionItem item) =>
        Write(SessionEventType.UserInputResolved, item.Id, SnapshotItem(item));

    // -------------------------------------------------------------------------
    // SubAgent progress events
    // -------------------------------------------------------------------------

    /// <summary>
    /// Emits a SubAgent progress snapshot event. Called by <see cref="Agents.SubAgentProgressAggregator"/>
    /// at ~200ms intervals during SubAgent execution.
    /// </summary>
    public void EmitSubAgentProgress(SubAgentProgressPayload payload) =>
        Write(SessionEventType.SubAgentProgress, null, payload);

    // -------------------------------------------------------------------------
    // Usage delta events
    // -------------------------------------------------------------------------

    /// <summary>
    /// Emits an incremental token usage event. Called by <see cref="SessionService"/>
    /// when a streaming <c>UsageContent</c> snapshot yields a positive delta (see <see cref="UsageSnapshotDelta"/>).
    /// </summary>
    public void EmitUsageDelta(
        long inputTokens,
        long outputTokens,
        long cachedInputTokens = 0,
        long cacheWriteInputTokens = 0,
        long reasoningOutputTokens = 0,
        int llmCallDelta = 0,
        long? totalInputTokens = null,
        long? totalOutputTokens = null,
        long? contextInputTokens = null,
        long? turnInputTokens = null,
        long? turnOutputTokens = null,
        int? turnLlmCalls = null,
        ContextUsageSnapshot? contextUsage = null) =>
        Write(SessionEventType.UsageDelta, null, new UsageDeltaPayload
        {
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CachedInputTokens = cachedInputTokens,
            CacheWriteInputTokens = cacheWriteInputTokens,
            ReasoningOutputTokens = reasoningOutputTokens,
            LlmCallDelta = llmCallDelta,
            TotalInputTokens = totalInputTokens,
            TotalOutputTokens = totalOutputTokens,
            ContextInputTokens = contextInputTokens ?? totalInputTokens,
            TurnInputTokens = turnInputTokens,
            TurnOutputTokens = turnOutputTokens ?? totalOutputTokens,
            TurnLlmCalls = turnLlmCalls,
            ContextUsage = contextUsage
        });

    // -------------------------------------------------------------------------
    // System events
    // -------------------------------------------------------------------------

    /// <summary>
    /// Emits a system-level maintenance event (context compaction, memory consolidation).
    /// Called by <see cref="SessionService"/> during the Turn's post-processing phase.
    /// </summary>
    public void EmitSystemEvent(
        string kind,
        string? message = null,
        double? percentLeft = null,
        long? tokenCount = null,
        ContextUsageSnapshot? contextUsage = null) =>
        Write(SessionEventType.SystemEvent, null, new SystemEventPayload
        {
            Kind = kind,
            Message = message,
            PercentLeft = percentLeft,
            TokenCount = tokenCount,
            ContextUsage = contextUsage
        });

    // -------------------------------------------------------------------------
    // Snapshot helpers — produce immutable copies so async consumers see a
    // consistent status regardless of when they dequeue the event.
    // -------------------------------------------------------------------------

    private static SessionItem SnapshotItem(SessionItem item) => new()
    {
        Id = item.Id,
        TurnId = item.TurnId,
        Type = item.Type,
        Status = item.Status,
        CreatedAt = item.CreatedAt,
        CompletedAt = item.CompletedAt,
        Payload = item.Payload
    };

    private static SessionTurn SnapshotTurn(SessionTurn turn) => new()
    {
        Id = turn.Id,
        ThreadId = turn.ThreadId,
        Status = turn.Status,
        Input = turn.Input,
        Items = [..turn.Items],
        StartedAt = turn.StartedAt,
        CompletedAt = turn.CompletedAt,
        TokenUsage = turn.TokenUsage,
        Error = turn.Error,
        OriginChannel = turn.OriginChannel,
        Initiator = turn.Initiator
    };

    // -------------------------------------------------------------------------
    // Thread events (emitted when the Turn starts on a new or resumed thread)
    // -------------------------------------------------------------------------

    public void EmitThreadCreated(SessionThread thread) =>
        WriteThreadLevel(SessionEventType.ThreadCreated, thread.Id, thread);

    public void EmitThreadResumed(SessionThread thread, string resumedBy) =>
        WriteThreadLevel(SessionEventType.ThreadResumed, thread.Id,
            new ThreadResumedPayload { Thread = thread, ResumedBy = resumedBy });

    public void EmitThreadStatusChanged(string threadId, ThreadStatus prev, ThreadStatus next) =>
        WriteThreadLevel(SessionEventType.ThreadStatusChanged, threadId,
            new ThreadStatusChangedPayload { PreviousStatus = prev, NewStatus = next });

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    /// <summary>
    /// Signals that no more events will be written. Must be called even on error paths.
    /// </summary>
    public void Complete(Exception? error = null) =>
        _channel.Writer.TryComplete(error);

    /// <summary>
    /// Returns all events from the channel as an async sequence.
    /// Completes when the channel is completed.
    /// </summary>
    public IAsyncEnumerable<SessionEvent> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
