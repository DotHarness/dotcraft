namespace DotCraft.AppServer;

/// <summary>
/// Abstraction over the physical transport layer for the AppServer JSON-RPC protocol.
/// Implementations: <see cref="StdioTransport"/> (stdio JSONL) and <see cref="WebSocketTransport"/> (WebSocket).
/// </summary>
public interface IAppServerTransport : IAsyncDisposable
{
    /// <summary>
    /// Reads the next client-initiated message (request or notification) from the transport.
    /// Returns <c>null</c> on EOF or when the transport is closed.
    /// Responses to server-initiated requests are routed internally and never returned here.
    /// </summary>
    Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default);

    /// <summary>
    /// Sends any outbound message to the client. Thread-safe: may be called concurrently
    /// by the main loop, event dispatchers, and subscription handlers.
    /// </summary>
    Task WriteMessageAsync(object message, CancellationToken ct = default);

    /// <summary>
    /// Sends a server-initiated JSON-RPC request to the client and awaits the response.
    /// Used for bidirectional approval flows and optional client-hosted extension runtimes.
    /// </summary>
    /// <param name="method">The JSON-RPC method name.</param>
    /// <param name="params">Request parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="timeout">Optional response timeout. Defaults to 120 seconds. Use <see cref="Timeout.InfiniteTimeSpan"/> for requests that should wait until cancellation, transport closure, or a client response.</param>
    Task<AppServerIncomingMessage> SendClientRequestAsync(
        string method,
        object? @params,
        CancellationToken ct = default,
        TimeSpan? timeout = null);
}
