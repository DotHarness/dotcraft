using System.Threading.Channels;

namespace DotCraft.Tracing;

internal sealed class TraceEventStream
{
    private readonly Lock _gate = new();
    private readonly HashSet<Channel<TraceEvent>> _subscribers = [];

    public ChannelReader<TraceEvent> Subscribe(CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<TraceEvent>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        lock (_gate)
            _subscribers.Add(channel);

        cancellationToken.Register(() =>
        {
            lock (_gate)
                _subscribers.Remove(channel);
            channel.Writer.TryComplete();
        });

        return channel.Reader;
    }

    public void Publish(TraceEvent evt)
    {
        lock (_gate)
            foreach (var subscriber in _subscribers)
                subscriber.Writer.TryWrite(evt);
    }
}
