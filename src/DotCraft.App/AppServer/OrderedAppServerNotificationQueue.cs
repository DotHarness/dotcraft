using System.Threading.Channels;
using DotCraft.Protocol.AppServer;

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

    public void Complete() => _notifications.Writer.TryComplete();

    private async Task PumpAsync()
    {
        try
        {
            await foreach (var notification in _notifications.Reader.ReadAllAsync().ConfigureAwait(false))
                await _transport.WriteMessageAsync(notification, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            _onWriteFailed();
        }
    }
}
