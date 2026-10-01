using DotCraft.Tools;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private SystemNoticeCoordinator? _systemNoticeCoordinator;
    private bool _remoteRouteNoticesAttached;

    private SystemNoticeCoordinator SystemNotices =>
        LazyInitializer.EnsureInitialized(ref _systemNoticeCoordinator, () => new SystemNoticeCoordinator(this));

    /// <inheritdoc />
    public void AppendSystemNotice(string threadId, SystemNoticePayload payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(payload);
        SystemNotices.Enqueue(threadId, payload);
    }

    /// <summary>
    /// Starts persisting <c>remoteRoute</c> system notices for route changes a person or the model
    /// caused. Idempotent, and a no-op when this workspace has no Remote Tool Host client.
    /// </summary>
    internal void AttachRemoteRouteNotices()
    {
        if (_remoteRouteNoticesAttached || AgentFactory.RemoteToolHostClient is not { } client)
            return;
        _remoteRouteNoticesAttached = true;
        client.RouteChanged += OnRemoteRouteChanged;
    }

    /// <summary>Awaits every system notice queued so far. Test seam for the asynchronous append.</summary>
    internal Task DrainSystemNoticesAsync() =>
        _systemNoticeCoordinator?.DrainAsync() ?? Task.CompletedTask;

    /// <summary>
    /// Appends the system notices queued for this thread to <paramref name="turn"/> at the caller's
    /// Item boundary. The caller already owns the Turn's Items, so it also owns the Item events and
    /// persistence for what this returns.
    /// </summary>
    internal IReadOnlyList<SessionItem> DrainSystemNoticesIntoTurn(
        string threadId,
        SessionTurn turn,
        Func<int> nextItemSequence) =>
        _systemNoticeCoordinator?.DrainIntoTurn(threadId, turn, nextItemSequence) ?? [];

    /// <summary>
    /// Records one persistent timeline notice per Remote Tool Host route change, so a thread's
    /// history shows where its tools started and stopped running. Teardown disconnects
    /// (<see cref="RemoteToolRouteInitiator.System"/>) are not history and are skipped, while a lost
    /// lease is recorded whatever raised it.
    /// </summary>
    private void OnRemoteRouteChanged(RemoteToolRouteChange change)
    {
        if (change.Initiator == RemoteToolRouteInitiator.System
            && change.Reason != RemoteToolRouteChangeReason.LeaseLost)
        {
            return;
        }

        SystemNotices.Enqueue(change.ThreadId, new SystemNoticePayload
        {
            Kind = "remoteRoute",
            Reason = change.Reason switch
            {
                RemoteToolRouteChangeReason.Connected => "connected",
                RemoteToolRouteChangeReason.Disconnected => "disconnected",
                _ => "leaseLost"
            },
            Initiator = change.Initiator switch
            {
                RemoteToolRouteInitiator.Agent => "agent",
                RemoteToolRouteInitiator.System => "system",
                _ => "client"
            },
            HostId = change.Route?.HostId,
            HostName = change.HostDisplayName,
            WorkspaceId = change.Route?.WorkspaceId,
            WorkspaceName = change.WorkspaceDisplayName
        });
    }

    /// <summary>
    /// Appends persistent system notices to a thread's running Turn, or to its latest completed Turn
    /// when none is running. A thread with no such Turn records nothing.
    /// </summary>
    /// <remarks>
    /// A notice raised inside a running Turn cannot take the session gate that Turn holds for its
    /// whole duration, so notices queue here and are drained by whichever comes first: the Turn, at
    /// its next Item boundary, or the fallback append once the Turn releases the gate.
    /// </remarks>
    private sealed class SystemNoticeCoordinator(SessionService owner)
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, Queue<SystemNoticePayload>> _queued =
            new(StringComparer.Ordinal);
        private Task _pending = Task.CompletedTask;

        public Task DrainAsync()
        {
            lock (_gate)
                return _pending;
        }

        public IReadOnlyList<SessionItem> DrainIntoTurn(
            string threadId,
            SessionTurn turn,
            Func<int> nextItemSequence)
        {
            var notices = Take(threadId);
            if (notices.Length == 0)
                return [];

            var items = new List<SessionItem>(notices.Length);
            foreach (var notice in notices)
            {
                var item = CreateNoticeItem(turn, nextItemSequence(), notice);
                turn.Items.Add(item);
                items.Add(item);
            }

            return items;
        }

        public void Enqueue(string threadId, SystemNoticePayload payload)
        {
            // The queue preserves arrival order and the serialized fallback keeps two drains on one
            // thread from interleaving their writes.
            lock (_gate)
            {
                if (!_queued.TryGetValue(threadId, out var queue))
                    _queued[threadId] = queue = new Queue<SystemNoticePayload>();
                queue.Enqueue(payload);
                _pending = _pending.ContinueWith(
                    _ => AppendAsync(threadId),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default).Unwrap();
            }
        }

        /// <summary>Empties one thread's queue atomically, so exactly one drainer records it.</summary>
        private SystemNoticePayload[] Take(string threadId)
        {
            lock (_gate)
                return _queued.Remove(threadId, out var queue) ? [.. queue] : [];
        }

        private async Task AppendAsync(string threadId)
        {
            try
            {
                if (owner.IsPendingPermanentDeletion(threadId)
                    || !owner._runtimeRegistry.TryGetThread(threadId, out var thread))
                {
                    Take(threadId);
                    return;
                }

                var turn = thread.Turns.LastOrDefault(static candidate =>
                               candidate.Status is TurnStatus.Running
                                   or TurnStatus.WaitingApproval
                                   or TurnStatus.WaitingInput)
                           ?? thread.Turns.LastOrDefault(static candidate =>
                               candidate.Status == TurnStatus.Completed);
                if (turn is null)
                {
                    Take(threadId);
                    return;
                }

                using var gateLock = await owner.Gate.AcquireAsync(threadId, CancellationToken.None);
                if (owner.IsPendingPermanentDeletion(threadId))
                    return;

                // The Turn may already have drained these at one of its own Item boundaries.
                var notices = Take(threadId);
                if (notices.Length == 0)
                    return;

                var sequence = SessionIdGenerator.LastItemSequence(turn.Items);
                var broker = owner.GetOrCreateBroker(threadId);
                foreach (var notice in notices)
                {
                    var item = CreateNoticeItem(turn, ++sequence, notice);
                    turn.Items.Add(item);
                    broker.PublishItemEvent(SessionEventType.ItemStarted, turn.Id, item);
                    broker.PublishItemEvent(SessionEventType.ItemCompleted, turn.Id, item);
                }

                await owner.PersistThreadWithMaterializationAsync(thread, CancellationToken.None);
            }
            catch (Exception exception)
            {
                owner.Logger?.LogWarning(
                    exception,
                    "Failed to record a system notice for thread {ThreadId}.",
                    threadId);
            }
        }

        private static SessionItem CreateNoticeItem(
            SessionTurn turn,
            int sequence,
            SystemNoticePayload payload)
        {
            var now = DateTimeOffset.UtcNow;
            return new SessionItem
            {
                Id = SessionIdGenerator.NewItemId(sequence),
                TurnId = turn.Id,
                Type = ItemType.SystemNotice,
                Status = ItemStatus.Completed,
                CreatedAt = now,
                CompletedAt = now,
                Payload = payload
            };
        }
    }
}
