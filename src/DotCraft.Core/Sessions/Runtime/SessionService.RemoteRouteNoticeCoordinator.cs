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
    /// Records one persistent timeline notice per Remote Tool Host route change, so a thread's
    /// history shows where its tools started and stopped running. Teardown disconnects
    /// (<see cref="RemoteToolRouteInitiator.System"/>) are not history and are skipped, while a lost
    /// lease is recorded whatever raised it.
    /// </summary>
    private sealed class RemoteRouteNoticeCoordinator
    {
        private readonly SessionService _owner;
        private readonly Lock _gate = new();
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

        private void OnRouteChanged(RemoteToolRouteChange change)
        {
            if (change.Initiator == RemoteToolRouteInitiator.System
                && change.Reason != RemoteToolRouteChangeReason.LeaseLost)
            {
                return;
            }

            // Route changes arrive synchronously from the client; appends are serialized so two
            // changes on one thread cannot interleave their item writes.
            lock (_gate)
                _pending = _pending.ContinueWith(
                    _ => AppendAsync(change),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default).Unwrap();
        }

        private async Task AppendAsync(RemoteToolRouteChange change)
        {
            try
            {
                if (_owner.IsPendingPermanentDeletion(change.ThreadId)
                    || !_owner._runtimeRegistry.TryGetThread(change.ThreadId, out var thread))
                {
                    return;
                }

                var turn = thread.Turns.LastOrDefault(static candidate =>
                               candidate.Status is TurnStatus.Running
                                   or TurnStatus.WaitingApproval
                                   or TurnStatus.WaitingInput)
                           ?? thread.Turns.LastOrDefault(static candidate =>
                               candidate.Status == TurnStatus.Completed);
                if (turn is null)
                    return;

                using var gateLock = await _owner.Gate.AcquireAsync(change.ThreadId, CancellationToken.None);
                if (_owner.IsPendingPermanentDeletion(change.ThreadId))
                    return;

                var item = CreateNoticeItem(turn, change);
                turn.Items.Add(item);
                var broker = _owner.GetOrCreateBroker(change.ThreadId);
                broker.PublishItemEvent(SessionEventType.ItemStarted, turn.Id, item);
                broker.PublishItemEvent(SessionEventType.ItemCompleted, turn.Id, item);
                await _owner.PersistThreadWithMaterializationAsync(thread, CancellationToken.None);
            }
            catch (Exception exception)
            {
                _owner.Logger?.LogWarning(
                    exception,
                    "Failed to record a Remote Tool Host route notice for thread {ThreadId}.",
                    change.ThreadId);
            }
        }

        private static SessionItem CreateNoticeItem(SessionTurn turn, RemoteToolRouteChange change)
        {
            var now = DateTimeOffset.UtcNow;
            return new SessionItem
            {
                Id = SessionIdGenerator.NewItemId(turn.Items.Count + 1),
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
