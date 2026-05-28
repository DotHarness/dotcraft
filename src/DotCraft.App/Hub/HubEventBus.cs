using System.Threading.Channels;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace DotCraft.Hub;

/// <summary>
/// In-memory event fan-out for Hub Protocol SSE clients.
/// </summary>
public sealed class HubEventBus
{
    private readonly Lock _lock = new();
    private readonly HashSet<Channel<HubEvent>> _subscribers = [];

    public void Publish(string kind, string? workspacePath = null, object? data = null)
    {
        var evt = new HubEvent(kind, DateTimeOffset.UtcNow, workspacePath, data);
        List<Channel<HubEvent>> subscribers;
        lock (_lock)
        {
            subscribers = _subscribers.ToList();
        }

        foreach (var subscriber in subscribers)
            subscriber.Writer.TryWrite(evt);
    }

    public ChannelReader<HubEvent> Subscribe(CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<HubEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        lock (_lock)
        {
            _subscribers.Add(channel);
        }

        cancellationToken.Register(() =>
        {
            lock (_lock)
            {
                _subscribers.Remove(channel);
            }

            channel.Writer.TryComplete();
        });

        return channel.Reader;
    }

    public static async Task WriteSseAsync(HttpResponse response, ChannelReader<HubEvent> events, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.ContentType = "text/event-stream";
        await response.WriteAsync(": connected\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);

        await foreach (var evt in events.ReadAllAsync(cancellationToken))
        {
            await response.WriteAsync($"event: {evt.Kind}\n", cancellationToken);
            await response.WriteAsync("data: ", cancellationToken);
            await response.WriteAsync(JsonSerializer.Serialize(evt, SseJsonOptions), cancellationToken);
            await response.WriteAsync("\n\n", cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }
    }

    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);
}
