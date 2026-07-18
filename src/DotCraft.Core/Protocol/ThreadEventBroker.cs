using System.Collections.Concurrent;
using System.Threading.Channels;

namespace DotCraft.Protocol;

/// <summary>
/// Thread-scoped event broker that fans out lifecycle events to multiple subscribers.
/// </summary>
internal sealed class ThreadEventBroker(string threadId)
{
    private readonly ConcurrentDictionary<long, Channel<SessionEvent>> _subscribers = new();
    private readonly Lock _recentEventsLock = new();
    private readonly Queue<SessionEvent> _recentEvents = [];
    private long _subscriberSequence;
    private int _eventSequence;

    private const int ReplayBufferSize = 32;

    /// <summary>
    /// Creates a turn-scoped event channel backed by this broker.
    /// </summary>
    public SessionEventChannel CreateTurnChannel(
        string turnId,
        Action<SessionEvent>? debugTap = null) =>
        new(threadId, turnId, NextEventId, Publish, debugTap);

    /// <summary>
    /// Publishes a thread-level lifecycle event.
    /// </summary>
    public void PublishThreadEvent(SessionEventType eventType, object payload)
    {
        Publish(new SessionEvent
        {
            EventId = NextEventId(),
            EventType = eventType,
            ThreadId = threadId,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = payload
        });
    }

    /// <summary>
    /// Publishes a live thread status change event. Status snapshots are recovered
    /// through thread/read or thread/list and are not retained for replay.
    /// </summary>
    public void PublishThreadStatusChanged(ThreadStatus previousStatus, ThreadStatus newStatus)
    {
        Publish(
            new SessionEvent
            {
                EventId = NextEventId(),
                EventType = SessionEventType.ThreadStatusChanged,
                ThreadId = threadId,
                Timestamp = DateTimeOffset.UtcNow,
                Payload = new ThreadStatusChangedPayload
                {
                    PreviousStatus = previousStatus,
                    NewStatus = newStatus
                }
            },
            retainForReplay: false);
    }

    public void PublishTurnStarted(SessionTurn turn)
    {
        Publish(new SessionEvent
        {
            EventId = NextEventId(),
            EventType = SessionEventType.TurnStarted,
            ThreadId = threadId,
            TurnId = turn.Id,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = SnapshotTurn(turn)
        });
    }

    public void PublishTurnCompleted(SessionTurn turn)
    {
        Publish(new SessionEvent
        {
            EventId = NextEventId(),
            EventType = SessionEventType.TurnCompleted,
            ThreadId = threadId,
            TurnId = turn.Id,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = SnapshotTurn(turn)
        });
    }

    public void PublishTurnFailed(SessionTurn turn, string error)
    {
        Publish(new SessionEvent
        {
            EventId = NextEventId(),
            EventType = SessionEventType.TurnFailed,
            ThreadId = threadId,
            TurnId = turn.Id,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new TurnFailedPayload { Turn = SnapshotTurn(turn), Error = error }
        });
    }

    public void PublishTurnCancelled(SessionTurn turn, string reason)
    {
        Publish(new SessionEvent
        {
            EventId = NextEventId(),
            EventType = SessionEventType.TurnCancelled,
            ThreadId = threadId,
            TurnId = turn.Id,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new TurnCancelledPayload { Turn = SnapshotTurn(turn), Reason = reason }
        });
    }

    /// <summary>
    /// Publishes the current queued input snapshot for this thread.
    /// </summary>
    public void PublishThreadQueueUpdated(IReadOnlyList<QueuedTurnInput> queuedInputs)
    {
        PublishThreadEvent(
            SessionEventType.ThreadQueueUpdated,
            new ThreadQueueUpdatedPayload
            {
                ThreadId = threadId,
                QueuedInputs = queuedInputs.ToList()
            });
    }

    /// <summary>
    /// Publishes a thread-scoped system maintenance event.
    /// </summary>
    public void PublishSystemEvent(
        string kind,
        string? message = null,
        double? percentLeft = null,
        long? tokenCount = null,
        ContextUsageSnapshot? contextUsage = null)
    {
        var presentation = BuildSystemEventPresentation(kind, message);
        PublishThreadEvent(
            SessionEventType.SystemEvent,
            new SystemEventPayload
            {
                Kind = kind,
                MessageKey = presentation.Key,
                FallbackText = presentation.FallbackText,
                Message = presentation.FallbackText,
                PercentLeft = percentLeft,
                TokenCount = tokenCount,
                ContextUsage = contextUsage
            });
    }

    /// <summary>
    /// Publishes a turn-associated system event through the thread broker.
    /// Used for non-blocking background work that may start after the original
    /// turn-scoped channel has already closed.
    /// </summary>
    public void PublishTurnSystemEvent(
        string turnId,
        string kind,
        string? message = null,
        double? percentLeft = null,
        long? tokenCount = null,
        ContextUsageSnapshot? contextUsage = null)
    {
        var presentation = BuildSystemEventPresentation(kind, message);
        Publish(new SessionEvent
        {
            EventId = NextEventId(),
            EventType = SessionEventType.SystemEvent,
            ThreadId = threadId,
            TurnId = turnId,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new SystemEventPayload
            {
                Kind = kind,
                MessageKey = presentation.Key,
                FallbackText = presentation.FallbackText,
                Message = presentation.FallbackText,
                PercentLeft = percentLeft,
                TokenCount = tokenCount,
                ContextUsage = contextUsage
            }
        });
    }

    private static (string? Key, string? FallbackText) BuildSystemEventPresentation(string kind, string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            return ($"system.{kind}", message);

        return kind switch
        {
            "compacting" => ("context.limit_reached", "Context token limit reached, compacting conversation..."),
            "compacted" => ("context.compacted", "Context compacted successfully."),
            "compactSkipped" => ("context.compact_skipped", "Context compaction skipped (insufficient history)."),
            "consolidating" => ("memory.consolidating", "Consolidating memory..."),
            "consolidated" => ("memory.consolidated", "Memory consolidation complete."),
            "consolidationFailed" => ("memory.consolidation_failed", "Memory consolidation failed."),
            _ => ($"system.{kind}", null)
        };
    }

    public void PublishItemEvent(SessionEventType eventType, string turnId, SessionItem item)
    {
        var payload = new SessionItem
        {
            Id = item.Id,
            TurnId = item.TurnId,
            Type = item.Type,
            Status = eventType == SessionEventType.ItemStarted ? ItemStatus.Started : item.Status,
            CreatedAt = item.CreatedAt,
            CompletedAt = eventType == SessionEventType.ItemStarted ? null : item.CompletedAt,
            Payload = item.Payload
        };
        Publish(new SessionEvent
        {
            EventId = NextEventId(),
            EventType = eventType,
            ThreadId = threadId,
            TurnId = turnId,
            ItemId = item.Id,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = payload
        });
    }

    /// <summary>
    /// Subscribes to thread events until the returned async sequence is cancelled or completed.
    /// </summary>
    public IAsyncEnumerable<SessionEvent> SubscribeAsync(
        bool replayRecent = false,
        CancellationToken ct = default)
    {
        var subscriberId = Interlocked.Increment(ref _subscriberSequence);
        var channel = Channel.CreateUnbounded<SessionEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        if (replayRecent)
        {
            lock (_recentEventsLock)
            {
                _subscribers[subscriberId] = channel;
                foreach (var evt in _recentEvents)
                {
                    channel.Writer.TryWrite(CloneForReplay(evt));
                }
            }
        }
        else
        {
            _subscribers[subscriberId] = channel;
        }

        return ReadAllAsync(subscriberId, channel, ct);
    }

    private string NextEventId() =>
        $"evt_{Interlocked.Increment(ref _eventSequence):D4}";

    private static SessionEvent CloneForReplay(SessionEvent evt) => new()
    {
        EventId = evt.EventId,
        EventType = evt.EventType,
        ThreadId = evt.ThreadId,
        TurnId = evt.TurnId,
        ItemId = evt.ItemId,
        Timestamp = evt.Timestamp,
        Payload = evt.Payload,
        IsReplay = true
    };

    private void Publish(SessionEvent evt) => Publish(evt, retainForReplay: true);

    private void Publish(SessionEvent evt, bool retainForReplay)
    {
        lock (_recentEventsLock)
        {
            if (retainForReplay)
            {
                _recentEvents.Enqueue(evt);
                while (_recentEvents.Count > ReplayBufferSize)
                {
                    _recentEvents.Dequeue();
                }
            }

            foreach (var subscriber in _subscribers.Values)
            {
                subscriber.Writer.TryWrite(evt);
            }
        }
    }

    private static SessionTurn SnapshotTurn(SessionTurn turn) => new()
    {
        Id = turn.Id,
        ThreadId = turn.ThreadId,
        Status = turn.Status,
        Input = turn.Input,
        Items = [.. turn.Items],
        StartedAt = turn.StartedAt,
        CompletedAt = turn.CompletedAt,
        TokenUsage = turn.TokenUsage,
        Error = turn.Error,
        OriginChannel = turn.OriginChannel,
        Initiator = turn.Initiator
    };

    private async IAsyncEnumerable<SessionEvent> ReadAllAsync(
        long subscriberId,
        Channel<SessionEvent> channel,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(ct))
            {
                yield return evt;
            }
        }
        finally
        {
            _subscribers.TryRemove(subscriberId, out _);
            channel.Writer.TryComplete();
        }
    }
}
