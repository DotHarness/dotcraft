using DotCraft.Tools;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private RemoteRouteNoticeCoordinator? _remoteRouteNoticeCoordinator;

    /// <summary>
    /// Starts persisting <c>remoteRoute</c> system notices for route changes a person or the model
    /// caused. Idempotent, and a no-op when this workspace has no Remote Tool Host client.
    /// </summary>
    internal void AttachRemoteRouteNotices()
    {
        if (_remoteRouteNoticeCoordinator is not null || AgentFactory.RemoteToolHostClient is not { } client)
            return;
        _remoteRouteNoticeCoordinator = new RemoteRouteNoticeCoordinator(this, client);
    }

    /// <summary>Awaits every route notice queued so far. Test seam for the asynchronous append.</summary>
    internal Task DrainRemoteRouteNoticesAsync() =>
        _remoteRouteNoticeCoordinator?.DrainAsync() ?? Task.CompletedTask;

    /// <summary>
    /// Appends the route notices queued for this thread to <paramref name="turn"/> at the caller's
    /// Item boundary. The caller already owns the Turn's Items, so it also owns the Item events and
    /// persistence for what this returns.
    /// </summary>
    internal IReadOnlyList<SessionItem> DrainRemoteRouteNoticesIntoTurn(
        string threadId,
        SessionTurn turn,
        Func<int> nextItemSequence) =>
        _remoteRouteNoticeCoordinator?.DrainIntoTurn(threadId, turn, nextItemSequence) ?? [];

    /// <summary>
    /// Records one persistent timeline notice per Remote Tool Host route change, so a thread's
    /// history shows where its tools started and stopped running. Teardown disconnects
    /// (<see cref="RemoteToolRouteInitiator.System"/>) are not history and are skipped, while a lost
    /// lease is recorded whatever raised it.
    /// </summary>
    /// <remarks>
    /// A change raised inside a running Turn cannot take the session gate that Turn holds for its
    /// whole duration, so changes queue here and are drained by whichever comes first: the Turn, at
    /// its next Item boundary, or the fallback append once the Turn releases the gate.
    /// </remarks>
    private sealed class RemoteRouteNoticeCoordinator
    {
        private readonly SessionService _owner;
        private readonly Lock _gate = new();
        private readonly Dictionary<string, Queue<RemoteToolRouteChange>> _queued =
            new(StringComparer.Ordinal);
        private Task _pending = Task.CompletedTask;

        public RemoteRouteNoticeCoordinator(SessionService owner, IRemoteToolHostClient client)
        {
            _owner = owner;
            client.RouteChanged += OnRouteChanged;
        }

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
            var changes = Take(threadId);
            if (changes.Length == 0)
                return [];

            var items = new List<SessionItem>(changes.Length);
            foreach (var change in changes)
            {
                var item = CreateNoticeItem(turn, nextItemSequence(), change);
                turn.Items.Add(item);
                items.Add(item);
            }

            return items;
        }

        private void OnRouteChanged(RemoteToolRouteChange change)
        {
            if (change.Initiator == RemoteToolRouteInitiator.System
                && change.Reason != RemoteToolRouteChangeReason.LeaseLost)
            {
                return;
            }

            // Route changes arrive synchronously from the client; the queue preserves their order and
            // the serialized fallback keeps two drains on one thread from interleaving their writes.
            lock (_gate)
            {
                if (!_queued.TryGetValue(change.ThreadId, out var queue))
                    _queued[change.ThreadId] = queue = new Queue<RemoteToolRouteChange>();
                queue.Enqueue(change);
                _pending = _pending.ContinueWith(
                    _ => AppendAsync(change.ThreadId),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default).Unwrap();
            }
        }

        /// <summary>Empties one thread's queue atomically, so exactly one drainer records it.</summary>
        private RemoteToolRouteChange[] Take(string threadId)
        {
            lock (_gate)
                return _queued.Remove(threadId, out var queue) ? [.. queue] : [];
        }

        private async Task AppendAsync(string threadId)
        {
            try
            {
                if (_owner.IsPendingPermanentDeletion(threadId)
                    || !_owner._runtimeRegistry.TryGetThread(threadId, out var thread))
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

                using var gateLock = await _owner.Gate.AcquireAsync(threadId, CancellationToken.None);
                if (_owner.IsPendingPermanentDeletion(threadId))
                    return;

                // The Turn may already have drained these at one of its own Item boundaries.
                var changes = Take(threadId);
                if (changes.Length == 0)
                    return;

                var sequence = SessionIdGenerator.LastItemSequence(turn.Items);
                var broker = _owner.GetOrCreateBroker(threadId);
                foreach (var change in changes)
                {
                    var item = CreateNoticeItem(turn, ++sequence, change);
                    turn.Items.Add(item);
                    broker.PublishItemEvent(SessionEventType.ItemStarted, turn.Id, item);
                    broker.PublishItemEvent(SessionEventType.ItemCompleted, turn.Id, item);
                }

                await _owner.PersistThreadWithMaterializationAsync(thread, CancellationToken.None);
            }
            catch (Exception exception)
            {
                _owner.Logger?.LogWarning(
                    exception,
                    "Failed to record a Remote Tool Host route notice for thread {ThreadId}.",
                    threadId);
            }
        }

        private static SessionItem CreateNoticeItem(
            SessionTurn turn,
            int sequence,
            RemoteToolRouteChange change)
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
                Payload = new SystemNoticePayload
                {
                    Kind = "remoteRoute",
                    Reason = Wire(change.Reason),
                    Initiator = Wire(change.Initiator),
                    HostId = change.Route?.HostId,
                    HostName = change.HostDisplayName,
                    WorkspaceId = change.Route?.WorkspaceId,
                    WorkspaceName = change.WorkspaceDisplayName
                }
            };
        }

        private static string Wire(RemoteToolRouteChangeReason reason) => reason switch
        {
            RemoteToolRouteChangeReason.Connected => "connected",
            RemoteToolRouteChangeReason.Disconnected => "disconnected",
            _ => "leaseLost"
        };

        private static string Wire(RemoteToolRouteInitiator initiator) => initiator switch
        {
            RemoteToolRouteInitiator.Agent => "agent",
            RemoteToolRouteInitiator.System => "system",
            _ => "client"
        };
    }
}
