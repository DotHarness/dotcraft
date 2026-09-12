using System.Net.WebSockets;
using ModelContextProtocol.Client;

namespace DotCraft.RemoteTools;

/// <summary>An authenticated transport supplied by the embedding application's connection adapter.</summary>
public sealed class RemoteToolHostConnection(
    IClientTransport transport, string endpoint, ClientWebSocket? socket = null, Stream? stream = null) : IAsyncDisposable
{
    public IClientTransport Transport => transport;
    public string Endpoint => endpoint;
    public string? CloseDescription => socket?.CloseStatusDescription;

    public async ValueTask DisposeAsync()
    {
        if (stream is not null) await stream.DisposeAsync().ConfigureAwait(false);
        socket?.Dispose();
    }
}
