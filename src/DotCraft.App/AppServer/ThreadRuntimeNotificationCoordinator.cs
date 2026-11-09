using System.Collections.Concurrent;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed class ThreadRuntimeNotificationCoordinator
{
    private readonly ConcurrentDictionary<Task, byte> _pendingDeliveries = new();
    private readonly object _publicationGate = new();
    private readonly Dictionary<string, ThreadSummaryRuntime> _lastSnapshots = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<IAppServerTransport, AppServerConnection> _connections = new();

    public bool ApplySnapshot(
        string threadId,
        ThreadSummaryRuntime runtime,
        ConcurrentDictionary<IAppServerTransport, AppServerConnection> activeTransports,
        Func<AppServerConnection, bool> accepts)
    {
        lock (_publicationGate)
        {
            if (_lastSnapshots.TryGetValue(threadId, out var previous) && SameRuntime(previous, runtime))
                return false;
            _lastSnapshots[threadId] = runtime;
            foreach (var (transport, connection) in activeTransports)
            {
                if (!connection.ShouldSendNotification(Contract.AppServerRpc.ThreadStatusChanged.Name) || !accepts(connection))
                    continue;
                Deliver(transport, connection, activeTransports, () => connection.RunThreadNotificationAsync(threadId, snapshot =>
                {
                    snapshot?.EnsureCurrent();
                    return connection.SendThreadNotificationAsync(transport, Contract.AppServerRpc.ThreadStatusChanged.Name,
                        new Contract.ThreadStatusChangedNotification
                        {
                            ThreadId = threadId,
                            Runtime = AppServerContractMapper.ToContract((snapshot?.Runtime ?? runtime).ToWireRuntimeState())
                        });
                }));
            }
            return true;
        }
    }

    public void BroadcastThreadStarted(
        Contract.ThreadNotification notification,
        ConcurrentDictionary<IAppServerTransport, AppServerConnection> activeTransports,
        Func<IAppServerTransport, AppServerConnection, bool> accepts)
    {
        lock (_publicationGate)
        {
            foreach (var (transport, connection) in activeTransports)
            {
                if (!connection.ShouldSendNotification(Contract.AppServerRpc.ThreadStarted.Name) || !accepts(transport, connection))
                    continue;
                Deliver(transport, connection, activeTransports,
                    () => connection.SendThreadNotificationAsync(transport, Contract.AppServerRpc.ThreadStarted.Name, notification));
            }
        }
    }

    public void RemoveThread(string threadId)
    {
        lock (_publicationGate)
            _lastSnapshots.Remove(threadId);
    }

    public void RemoveTransport(IAppServerTransport transport)
    {
        if (_connections.TryRemove(transport, out var connection))
            _ = connection.CompleteThreadNotificationsAsync();
    }

    public async Task CompleteAsync()
    {
        var completions = _connections.Values.Select(connection => connection.CompleteThreadNotificationsAsync()).ToArray();
        await Task.WhenAll(completions.Concat(_pendingDeliveries.Keys)).ConfigureAwait(false);
        _connections.Clear();
    }

    private void Deliver(
        IAppServerTransport transport,
        AppServerConnection connection,
        ConcurrentDictionary<IAppServerTransport, AppServerConnection> activeTransports,
        Func<Task> send)
    {
        _connections.TryAdd(transport, connection);
        var delivery = SendAsync();
        _pendingDeliveries.TryAdd(delivery, 0);
        _ = delivery.ContinueWith(completed => _pendingDeliveries.TryRemove(completed, out _),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        async Task SendAsync()
        {
            try { await send().ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch
            {
                activeTransports.TryRemove(transport, out _);
                connection.MarkClosed();
                RemoveTransport(transport);
            }
        }
    }

    private static bool SameRuntime(ThreadSummaryRuntime left, ThreadSummaryRuntime right) =>
        left.Running == right.Running
        && left.ActiveTurnId == right.ActiveTurnId
        && left.ActiveTurnStartedAt == right.ActiveTurnStartedAt
        && left.WaitingOnApproval == right.WaitingOnApproval
        && left.WaitingOnInput == right.WaitingOnInput
        && left.WaitingOnPlanConfirmation == right.WaitingOnPlanConfirmation
        && left.Busy == right.Busy
        && left.MaintenanceKind == right.MaintenanceKind
        && left.QueuePaused == right.QueuePaused;
}
