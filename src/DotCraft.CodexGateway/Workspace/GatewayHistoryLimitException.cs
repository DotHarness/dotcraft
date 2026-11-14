using System.Text.Json;
using DotCraft.CodexGateway.Relay;

namespace DotCraft.CodexGateway.Workspace;

internal sealed class GatewayHistoryLimitException(long maxBytes)
    : IOException("The requested full history exceeds the gateway response limit. Use paginated history.")
{
    public long MaxBytes { get; } = maxBytes;
}

internal sealed class HistoryReadBudget
{
    private long _bytes;

    public void Add(object value)
    {
        _bytes += JsonSerializer.SerializeToUtf8Bytes(value).LongLength + 1;
        if (_bytes > RelayFrames.MaxMessageBytes)
            throw new GatewayHistoryLimitException(RelayFrames.MaxMessageBytes);
    }
}
