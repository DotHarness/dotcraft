using System.Text.Json;
using System.Threading.Channels;
using DotCraft.Sdk.Wire;

namespace DotCraft.Sdk.Tests;

internal sealed class TestJsonRpcTransport : IReconnectableJsonRpcTransport
{
    private readonly Channel<JsonDocument> _inbound = Channel.CreateUnbounded<JsonDocument>();
    private readonly Channel<JsonDocument> _outbound = Channel.CreateUnbounded<JsonDocument>();

    public Task<JsonDocument?> ReadAsync(CancellationToken cancellationToken = default) =>
        ReadNullableAsync(_inbound.Reader, cancellationToken);

    public int ReconnectCount { get; private set; }

    public Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        ReconnectCount++;
        return Task.CompletedTask;
    }

    public Task WriteAsync(object message, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message, DotCraft.Sdk.Wire.DotCraftJson.Options);
        _outbound.Writer.TryWrite(JsonDocument.Parse(json));
        return Task.CompletedTask;
    }

    public Task PushInboundAsync(object message)
    {
        var json = JsonSerializer.Serialize(message, DotCraft.Sdk.Wire.DotCraftJson.Options);
        _inbound.Writer.TryWrite(JsonDocument.Parse(json));
        return Task.CompletedTask;
    }

    public Task PushDisconnectAsync()
    {
        _inbound.Writer.TryWrite(JsonDocument.Parse("null"));
        return Task.CompletedTask;
    }

    public Task<JsonDocument> ReadOutboundAsync(CancellationToken cancellationToken = default) =>
        _outbound.Reader.ReadAsync(cancellationToken).AsTask();

    public ValueTask DisposeAsync()
    {
        _inbound.Writer.TryComplete();
        _outbound.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private static async Task<JsonDocument?> ReadNullableAsync(ChannelReader<JsonDocument> reader, CancellationToken cancellationToken)
    {
        try
        {
            var document = await reader.ReadAsync(cancellationToken);
            if (document.RootElement.ValueKind == JsonValueKind.Null)
            {
                document.Dispose();
                return null;
            }
            return document;
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }
}
