using System.Text.Json;

namespace DotCraft.Sdk.Wire;

/// <summary>
/// Client-side transport for complete JSON-RPC messages.
/// </summary>
public interface IJsonRpcTransport : IAsyncDisposable
{
    /// <summary>
    /// Reads one complete JSON-RPC message. Returns null when the transport is closed.
    /// </summary>
    Task<JsonDocument?> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes one complete JSON-RPC message.
    /// </summary>
    Task WriteAsync(object message, CancellationToken cancellationToken = default);
}
