using System.Collections.Concurrent;
using System.Threading.Channels;
using ContextUsageSnapshot = DotCraft.Sessions.Wire.ContextUsageSnapshot;

namespace DotCraft.Sessions;

/// <summary>
/// Thread-scoped event broker that fans out lifecycle events to multiple subscribers.
/// </summary>
internal sealed class ThreadEventBroker(string threadId)
{
    private readonly ConcurrentDictionary<long, Channel<object>> _subscribers = new();
    private readonly Lock _publicationLock = new();
    private readonly ThreadObservationState _current = new();
    private SessionThread? _thread;
    private long _runtimeGeneration;
    private long _publicationGeneration;
    private bool _closed;
    private long _subscriberSequence;
    private int _eventSequence;
    private long _lastActivityTicks = DateTimeOffset.UtcNow.UtcTicks;

    public bool HasSubscribers => !_subscribers.IsEmpty;

    public DateTimeOffset LastActivityAt => new(Interlocked.Read(ref _lastActivityTicks), TimeSpan.Zero);

    private void MarkActivity() => Interlocked.Exchange(ref _lastActivityTicks, DateTimeOffset.UtcNow.UtcTicks);

    /// <summary>
    /// Creates a turn-scoped event channel backed by this broker.
    /// </summary>
    public SessionEventChannel CreateTurnChannel(
        string turnId,
        Action<SessionEvent>? debugTap = null,
        long? runtimeGeneration = null)
    {
        lock (_publicationLock)
        {
            var generation = _publicationGeneration;
            return new(threadId, turnId, NextEventId,
                evt => Publish(evt, generation, runtimeGeneration), debugTap);
        }
    }

    public void Initialize(SessionThread thread, long runtimeGeneration)
    {
        lock (_publicationLock)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            _thread = thread;
            if (_runtimeGeneration == runtimeGeneration) return;
            _runtimeGeneration = runtimeGeneration;
            _publicationGeneration++;
            _current.Initialize(thread);
        }
    }

    public bool HasContextUsage(long runtimeGeneration)
    {
        lock (_publicationLock)
            return !_closed && _runtimeGeneration == runtimeGeneration && _current.HasContextUsage;
    }

    public void SeedContextUsageIfMissing(ContextUsageSnapshot? usage, long runtimeGeneration)
    {
        lock (_publicationLock)
        {
            if (_closed || _runtimeGeneration != runtimeGeneration) return;
            _current.SeedContextUsageIfMissing(usage);
        }
    }

    public bool HasPlan(long runtimeGeneration)
    {
        lock (_publicationLock)
            return !_closed && _runtimeGeneration == runtimeGeneration && _current.PlanPrepared;
    }

    public void UpdatePlan(DotCraft.Memory.StructuredPlan plan, long runtimeGeneration)
    {
        lock (_publicationLock)
        {
            if (_closed || _runtimeGeneration != runtimeGeneration) return;
            _current.UpdatePlan(plan);
        }
    }

    public void SeedPlanIfMissing(DotCraft.Memory.StructuredPlan? plan, long runtimeGeneration)
    {
        lock (_publicationLock)
        {
            if (_closed || _runtimeGeneration != runtimeGeneration) return;
            _current.SeedPlanIfMissing(plan);
        }
    }

    public void ResetCurrentState(SessionThread thread, long runtimeGeneration)
    {
        lock (_publicationLock)
        {
            if (_closed || _runtimeGeneration != runtimeGeneration) return;
            _publicationGeneration++;
            _thread = thread;
            _current.Initialize(thread);
        }
    }

    public ThreadSummaryRuntime GetRuntimeSnapshot(SessionThread thread, string? maintenanceKind)
    {
        lock (_publicationLock)
            return _current.GetRuntimeSnapshot(thread, maintenanceKind);
    }

    public void ReleaseCurrentState(long runtimeGeneration)
    {
        lock (_publicationLock)
        {
            if (_runtimeGeneration != runtimeGeneration) return;
            _runtimeGeneration = 0;
            _publicationGeneration++;
            _current.Clear();
            _thread = null;
            CloseSubscribers();
        }
    }

    public void Close()
    {
        lock (_publicationLock)
        {
            _closed = true;
            _publicationGeneration++;
            _current.Clear();
            _thread = null;
            CloseSubscribers();
        }
    }

    public ThreadObservation Observe(
        SessionThread header,
        string? maintenanceKind,
        ThreadObservationView view,
        IReadOnlyList<DotCraft.Tools.ToolName> summaryTools,
        CancellationToken ct = default,
        long? runtimeGeneration = null)
    {
        ct.ThrowIfCancellationRequested();
        ThreadObservation observation;
        lock (_publicationLock)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (runtimeGeneration is { } expected && expected != _runtimeGeneration)
                throw new InvalidOperationException("Thread runtime is no longer current.");
            var snapshot = Capture(header, maintenanceKind, view, summaryTools);
            var subscriberId = Interlocked.Increment(ref _subscriberSequence);
            var channel = Channel.CreateUnbounded<object>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false
            });
            _subscribers[subscriberId] = channel;
            MarkActivity();
            observation = new(snapshot, ReadAllAsync(subscriberId, channel, ct),
                () => RemoveSubscriber(subscriberId, channel));
            observation.QueueRefresh = (controls, maintenance, nextView, tools, respond, refreshCt, selection) =>
                QueueRefresh(subscriberId, controls, maintenance, nextView, tools, respond, refreshCt, selection);
            observation.Refresh = (nextView, tools, respond, refreshCt, selection) =>
                observation.QueueRefresh(header, maintenanceKind, nextView, tools, respond, refreshCt, selection);
        }
        observation.RegisterCancellation(ct);
        if (ct.IsCancellationRequested)
        {
            observation.Dispose();
            ct.ThrowIfCancellationRequested();
        }
        return observation;
    }

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
            });
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
            InvocationOrigin = item.InvocationOrigin,
            FreeformCall = item.FreeformCall,
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

    public void PublishItemDelta(string turnId, string itemId, object payload)
    {
        Publish(new SessionEvent
        {
            EventId = NextEventId(),
            EventType = SessionEventType.ItemDelta,
            ThreadId = threadId,
            TurnId = turnId,
            ItemId = itemId,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = payload
        });
    }

    /// <summary>
    /// Subscribes to thread events until the returned async sequence is cancelled or completed.
    /// </summary>
    public IAsyncEnumerable<SessionEvent> SubscribeAsync(
        CancellationToken ct = default)
    {
        var subscriberId = Interlocked.Increment(ref _subscriberSequence);
        var channel = Channel.CreateUnbounded<object>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

        lock (_publicationLock)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            MarkActivity();
            _subscribers[subscriberId] = channel;
        }

        return ReadAllAsync(subscriberId, channel, ct);
    }

    private ThreadObservationSnapshot Capture(
        SessionThread header, string? maintenanceKind, ThreadObservationView view,
        IReadOnlyList<DotCraft.Tools.ToolName> summaryTools, ThreadObservationItemSelection? selection = null)
    {
        var generation = _publicationGeneration;
        return _current.Capture(header, maintenanceKind, view, summaryTools, selection) with
        {
            IsCurrent = () => !Volatile.Read(ref _closed) && Volatile.Read(ref _publicationGeneration) == generation
        };
    }

    private Task QueueRefresh(
        long subscriberId,
        SessionThread header,
        string? maintenanceKind,
        ThreadObservationView view,
        IReadOnlyList<DotCraft.Tools.ToolName> summaryTools,
        Func<ThreadObservationSnapshot, Task> respond,
        CancellationToken ct,
        ThreadObservationItemSelection? selection)
    {
        ct.ThrowIfCancellationRequested();
        lock (_publicationLock)
        {
            ObjectDisposedException.ThrowIf(_closed || !_subscribers.ContainsKey(subscriberId), this);
            var refresh = new ThreadObservationRefresh(
                Capture(header, maintenanceKind, view, summaryTools, selection), respond, ct);
            _subscribers[subscriberId].Writer.TryWrite(refresh);
            return refresh.Completion;
        }
    }

    private string NextEventId() =>
        $"evt_{Interlocked.Increment(ref _eventSequence):D4}";

    private void Publish(SessionEvent evt, long? generation = null, long? runtimeGeneration = null)
    {
        lock (_publicationLock)
        {
            if (_closed
                || (generation is { } expected && expected != _publicationGeneration)
                || (runtimeGeneration is { } owner && owner != _runtimeGeneration)) return;
            MarkActivity();
            if (_runtimeGeneration != 0 || _publicationGeneration == 0)
                _current.Apply(evt);
            if (_thread is not null && evt.EventType is SessionEventType.TurnStarted
                or SessionEventType.TurnCompleted or SessionEventType.TurnFailed or SessionEventType.TurnCancelled)
                evt.RuntimeSnapshot = _current.GetRuntimeSnapshot(_thread);

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
        Channel<object> channel,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(ct))
            {
                if (evt is SessionEvent sessionEvent)
                    yield return sessionEvent;
                else if (evt is ThreadObservationRefresh refresh)
                    await refresh.ExecuteAsync();
            }
        }
        finally
        {
            RemoveSubscriber(subscriberId, channel);
        }
    }

    private void CloseSubscribers()
    {
        foreach (var (id, channel) in _subscribers)
        {
            _subscribers.TryRemove(id, out _);
            channel.Writer.TryComplete();
            CancelRefreshes(channel);
        }
    }

    private void RemoveSubscriber(long subscriberId, Channel<object> channel)
    {
        _subscribers.TryRemove(subscriberId, out _);
        channel.Writer.TryComplete();
        CancelRefreshes(channel);
        MarkActivity();
    }

    private static void CancelRefreshes(Channel<object> channel)
    {
        while (channel.Reader.TryRead(out var entry))
            if (entry is ThreadObservationRefresh refresh)
                refresh.Cancel();
    }
}

internal sealed class ThreadObservationRefresh(
    ThreadObservationSnapshot snapshot,
    Func<ThreadObservationSnapshot, Task> respond,
    CancellationToken ct)
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ExecutionContext? _executionContext = ExecutionContext.Capture();
    public Task Completion => _completion.Task;

    public async Task ExecuteAsync()
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            snapshot.EnsureCurrent();
            Task? response = null;
            if (_executionContext is null)
                response = respond(snapshot);
            else
                ExecutionContext.Run(_executionContext, _ => response = respond(snapshot), null);
            await response!;
            _completion.TrySetResult();
        }
        catch (OperationCanceledException)
        {
            _completion.TrySetCanceled();
        }
        catch (Exception ex)
        {
            _completion.TrySetException(ex);
        }
    }

    public void Cancel() => _completion.TrySetCanceled();
}
