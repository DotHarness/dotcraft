using System.Threading.Channels;

namespace DotCraft.AppServer;

/// <summary>
/// Serializes one class of server notifications for a transport without creating
/// a task per write. Used by terminal lifecycle notifications whose order matters.
/// </summary>
internal sealed class OrderedAppServerNotificationQueue
{
    private readonly IAppServerTransport _transport;
    private readonly Action _onWriteFailed;
    private readonly Channel<object> _notifications = Channel.CreateUnbounded<object>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });

    public OrderedAppServerNotificationQueue(IAppServerTransport transport, Action onWriteFailed)
    {
        _transport = transport;
        _onWriteFailed = onWriteFailed;
        Completion = PumpAsync();
    }

    public Task Completion { get; }

    public bool Enqueue(object notification) => _notifications.Writer.TryWrite(notification);

    public bool Enqueue(string method, object parameters) =>
        _notifications.Writer.TryWrite(new ContractNotification(method, parameters));

    public void Complete() => _notifications.Writer.TryComplete();

    private async Task PumpAsync()
    {
        try
        {
            await foreach (var notification in _notifications.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (notification is ContractNotification contract)
                {
                    await _transport.NotifyContractAsync(
                        contract.Method,
                        contract.Parameters,
                        CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    await _transport.WriteMessageAsync(notification, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            _onWriteFailed();
        }
    }

    private sealed record ContractNotification(string Method, object Parameters);
}
