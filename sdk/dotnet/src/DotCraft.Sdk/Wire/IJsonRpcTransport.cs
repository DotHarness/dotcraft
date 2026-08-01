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

/// <summary>A transport that can replace its underlying connection after an unexpected close.</summary>
public interface IReconnectableJsonRpcTransport : IJsonRpcTransport
{
    /// <summary>Opens a fresh connection using the original endpoint and authentication.</summary>
    Task ReconnectAsync(CancellationToken cancellationToken = default);
}
