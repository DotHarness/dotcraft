namespace DotCraft.AppServer;

public interface IAppServerTransport : IAsyncDisposable
{
    /// <summary>Returns client requests/notifications, or null when closed; server-request responses are consumed internally.</summary>
    Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default);

    /// <summary>May be called concurrently.</summary>
    Task WriteMessageAsync(object message, CancellationToken ct = default);

    /// <summary>
    /// Preserves the registered request ID even on write failure; Response owns waiting, cancellation, and write errors.
    /// </summary>
    /// <param name="timeout">Optional response timeout. Defaults to 120 seconds. Use <see cref="Timeout.InfiniteTimeSpan"/> for requests that should wait until cancellation, transport closure, or a client response.</param>
    Task<AppServerPendingClientRequest> StartClientRequestAsync(
        string method,
        object? @params,
        CancellationToken ct = default,
        TimeSpan? timeout = null);
}
