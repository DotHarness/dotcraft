using System.Collections.Concurrent;
using DotCraft.Protocol;
using DotCraft.Sessions;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

/// <summary>Delivers thread-scoped notifications to the open connections subscribed to a thread.</summary>
public sealed class AppServerThreadSubscribers
{
    private readonly ConcurrentDictionary<AppServerConnection, IAppServerTransport> _connections = new();

    public void Register(IAppServerTransport transport, AppServerConnection connection)
    {
        _connections[connection] = transport;
        _ = connection.Closed.ContinueWith(
            closed => _connections.TryRemove(connection, out _),
            TaskScheduler.Default);
    }

    public void NotifyOthers<TParams>(
        RpcNotification<TParams> notification,
        string threadId,
        AppServerConnection sender,
        Func<AppServerConnection, bool> accepts,
        TParams parameters)
        where TParams : class
    {
        foreach (var (connection, transport) in _connections)
        {
            if (ReferenceEquals(connection, sender)
                || connection.IsClosed
                || !connection.HasSubscription(threadId)
                || !accepts(connection)
                || !connection.ShouldSendNotification(notification.Name))
            {
                continue;
            }

            _ = NotifyAsync(transport, notification, parameters);
        }
    }

    public void NotifyThreadTokenUsageUpdated(ThreadTokenUsageUpdate update)
    {
        var notification = Contract.AppServerRpc.ThreadTokenUsageUpdated;
        var parameters = ThreadContractMapper.ToContract(update);
        foreach (var (connection, transport) in _connections)
        {
            if (connection.IsClosed
                || !(connection.HasSubscription(update.ThreadId)
                     || update.ParentThreadId is { } parentThreadId && connection.HasSubscription(parentThreadId))
                || !connection.ShouldSendNotification(notification.Name))
            {
                continue;
            }

            _ = NotifyAsync(transport, notification, parameters);
        }
    }

    private static async Task NotifyAsync<TParams>(
        IAppServerTransport transport,
        RpcNotification<TParams> notification,
        TParams parameters)
        where TParams : class
    {
        try
        {
            await transport.NotifyContractAsync(notification, parameters, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
        }
    }
}
