using System.Threading.Channels;

namespace DotCraft.AppServer;

internal sealed class OrderedAppServerNotificationQueue
{
    private readonly IAppServerTransport _transport;
    private readonly Action _onWriteFailed;
    private readonly Channel<QueueEntry> _notifications = Channel.CreateUnbounded<QueueEntry>(new UnboundedChannelOptions
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

    public bool Enqueue(string method, object parameters) =>
        _notifications.Writer.TryWrite(new QueueEntry(method, parameters));

    public bool Enqueue(string method, object parameters, Task prerequisite) =>
        _notifications.Writer.TryWrite(new QueueEntry(method, parameters, prerequisite));

    public Task EnqueueAsync(string method, object parameters, CancellationToken ct = default)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_notifications.Writer.TryWrite(new QueueEntry(method, parameters, Completion: completion, Cancellation: ct)))
            completion.TrySetException(new ObjectDisposedException(nameof(OrderedAppServerNotificationQueue)));
        return completion.Task;
    }

    public void Complete() => _notifications.Writer.TryComplete();

    private async Task PumpAsync()
    {
        QueueEntry? current = null;
        try
        {
            await foreach (var entry in _notifications.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                current = entry;
                if (entry.Prerequisite != null)
                    await entry.Prerequisite.ConfigureAwait(false);

                try
                {
                    entry.Cancellation.ThrowIfCancellationRequested();
                    await _transport.NotifyContractAsync(entry.Method, entry.Parameters, entry.Cancellation).ConfigureAwait(false);
                    entry.Completion?.TrySetResult();
                }
                catch (OperationCanceledException) when (entry.Cancellation.IsCancellationRequested)
                {
                    entry.Completion?.TrySetCanceled(entry.Cancellation);
                }
                current = null;
            }
        }
        catch (Exception ex)
        {
            _notifications.Writer.TryComplete(ex);
            current?.Completion?.TrySetException(ex);
            while (_notifications.Reader.TryRead(out var pending))
                pending.Completion?.TrySetException(ex);
            _onWriteFailed();
        }
    }

    private sealed record QueueEntry(
        string Method, object Parameters, Task? Prerequisite = null, TaskCompletionSource? Completion = null, CancellationToken Cancellation = default);
}
