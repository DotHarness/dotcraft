using System.Collections.Concurrent;
using DotCraft.Sessions;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

/// <summary>
/// Aggregates thread runtime signals and delivers committed snapshots to each transport in order.
/// </summary>
internal sealed class ThreadRuntimeNotificationCoordinator
{
    private readonly object _signalGate = new();
    private readonly ConcurrentDictionary<string, RuntimeFacts> _threadRuntime = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<IAppServerTransport, Lazy<OrderedAppServerNotificationQueue>> _notificationQueues = new();

    public bool ApplySignal(
        string threadId,
        SessionThreadRuntimeSignal signal,
        SessionTurn? turn,
        ConcurrentDictionary<IAppServerTransport, AppServerConnection> activeTransports)
    {
        lock (_signalGate)
        {
            _threadRuntime.TryGetValue(threadId, out var previous);
            var current = turn?.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput
                ? previous with
                {
                    Running = true,
                    ActiveTurnId = turn.Id,
                    ActiveTurnStartedAt = turn.StartedAt
                }
                : previous;
            var next = signal switch
            {
                SessionThreadRuntimeSignal.TurnStarted => current with
                {
                    Running = true,
                    ActiveTurnId = turn?.Id,
                    ActiveTurnStartedAt = turn?.StartedAt,
                    WaitingOnPlanConfirmation = false
                },
                SessionThreadRuntimeSignal.TurnCompleted => current with
                {
                    Running = false,
                    ActiveTurnId = null,
                    ActiveTurnStartedAt = null,
                    WaitingOnPlanConfirmation = false
                },
                SessionThreadRuntimeSignal.TurnCompletedAwaitingPlanConfirmation => current with
                {
                    Running = false,
                    ActiveTurnId = null,
                    ActiveTurnStartedAt = null,
                    WaitingOnPlanConfirmation = true
                },
                SessionThreadRuntimeSignal.TurnFailed => current with
                {
                    Running = false,
                    ActiveTurnId = null,
                    ActiveTurnStartedAt = null,
                    WaitingOnPlanConfirmation = false
                },
                SessionThreadRuntimeSignal.TurnCancelled => current with
                {
                    Running = false,
                    ActiveTurnId = null,
                    ActiveTurnStartedAt = null,
                    WaitingOnPlanConfirmation = false
                },
                SessionThreadRuntimeSignal.ApprovalRequested => current with
                {
                    PendingApprovals = previous.PendingApprovals + 1
                },
                SessionThreadRuntimeSignal.ApprovalResolved => current with
                {
                    PendingApprovals = Math.Max(0, previous.PendingApprovals - 1)
                },
                SessionThreadRuntimeSignal.UserInputRequested => current with
                {
                    PendingUserInputs = previous.PendingUserInputs + 1
                },
                SessionThreadRuntimeSignal.UserInputResolved => current with
                {
                    PendingUserInputs = Math.Max(0, previous.PendingUserInputs - 1)
                },
                SessionThreadRuntimeSignal.MaintenanceCompactingStarted => current with
                {
                    MaintenanceKind = "compacting"
                },
                SessionThreadRuntimeSignal.MaintenanceConsolidatingStarted => current with
                {
                    MaintenanceKind = "consolidating"
                },
                SessionThreadRuntimeSignal.MaintenanceCompleted => current with
                {
                    MaintenanceKind = null
                },
                _ => current
            };

            if (next.Equals(previous))
                return false;

            _threadRuntime[threadId] = next;
            Broadcast(threadId, next.ToContract(), activeTransports);
            return true;
        }
    }

    public void RemoveThread(string threadId)
    {
        lock (_signalGate)
            _threadRuntime.TryRemove(threadId, out _);
    }

    public void RemoveTransport(IAppServerTransport transport)
    {
        if (_notificationQueues.TryRemove(transport, out var queue) && queue.IsValueCreated)
            queue.Value.Complete();
    }

    public async Task CompleteAsync()
    {
        var queues = _notificationQueues.Keys
            .Select(transport =>
            {
                if (!_notificationQueues.TryRemove(transport, out var queue) || !queue.IsValueCreated)
                    return null;
                queue.Value.Complete();
                return queue.Value.Completion;
            })
            .OfType<Task>()
            .ToArray();

        await Task.WhenAll(queues).ConfigureAwait(false);
    }

    private void Broadcast(
        string threadId,
        Contract.ThreadRuntimeState runtime,
        ConcurrentDictionary<IAppServerTransport, AppServerConnection> activeTransports)
    {
        var parameters = new Contract.ThreadRuntimeChangedParams
        {
            ThreadId = threadId,
            Runtime = runtime
        };

        foreach (var (transport, connection) in activeTransports)
        {
            if (!connection.ShouldSendNotification(Contract.AppServerRpc.ThreadRuntimeChanged.Name))
                continue;

            var queue = _notificationQueues.GetOrAdd(
                transport,
                candidate => new Lazy<OrderedAppServerNotificationQueue>(
                    () => new OrderedAppServerNotificationQueue(
                        candidate,
                        () =>
                        {
                            activeTransports.TryRemove(candidate, out _);
                            RemoveTransport(candidate);
                        }),
                    LazyThreadSafetyMode.ExecutionAndPublication));
            queue.Value.Enqueue(Contract.AppServerRpc.ThreadRuntimeChanged.Name, parameters);
        }
    }

    private readonly record struct RuntimeFacts(
        int PendingApprovals,
        int PendingUserInputs,
        bool Running,
        string? ActiveTurnId,
        DateTimeOffset? ActiveTurnStartedAt,
        bool WaitingOnPlanConfirmation,
        string? MaintenanceKind)
    {
        public Contract.ThreadRuntimeState ToContract() => new()
        {
            Running = Running,
            ActiveTurnId = ActiveTurnId is null
                ? default
                : DotCraft.Protocol.Optional<string?>.FromValue(ActiveTurnId),
            ActiveTurnStartedAt = ActiveTurnStartedAt is null
                ? default
                : DotCraft.Protocol.Optional<DateTimeOffset?>.FromValue(ActiveTurnStartedAt),
            WaitingOnApproval = PendingApprovals > 0,
            WaitingOnInput = PendingUserInputs > 0,
            WaitingOnPlanConfirmation = WaitingOnPlanConfirmation,
            MaintenanceKind = MaintenanceKind is null
                ? default
                : DotCraft.Protocol.Optional<string?>.FromValue(MaintenanceKind),
            Busy = Running || PendingApprovals > 0 || PendingUserInputs > 0 || MaintenanceKind != null
        };
    }
}
