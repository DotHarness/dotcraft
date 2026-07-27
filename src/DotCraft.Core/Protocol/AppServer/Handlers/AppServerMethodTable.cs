namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Delegate shape for an AppServer request handler: takes the incoming message and a cancellation
/// token, returns the JSON-RPC result object (or null) to serialize back to the client.
/// </summary>
internal delegate Task<object?> AppServerMethodInvoker(AppServerIncomingMessage msg, CancellationToken ct);

/// <summary>
/// Per-connection registry mapping a wire method name to its handler delegate. Built-in domain
/// handlers register their methods here at construction; the dispatcher resolves a request with a
/// single dictionary lookup. Duplicate registration throws so two handlers can never silently
/// claim the same method.
/// </summary>
internal sealed class AppServerMethodTable
{
    private readonly Dictionary<string, AppServerMethodInvoker> _map = new(StringComparer.Ordinal);

    /// <summary>Registers <paramref name="handler"/> for <paramref name="method"/>. Throws on a duplicate.</summary>
    public void Map(string method, AppServerMethodInvoker handler)
    {
        if (!_map.TryAdd(method, handler))
            throw new InvalidOperationException($"Duplicate AppServer method registration: '{method}'.");
    }

    /// <summary>Looks up the handler for <paramref name="method"/>.</summary>
    public bool TryGet(string method, out AppServerMethodInvoker handler) =>
        _map.TryGetValue(method, out handler!);

    /// <summary>All registered method names (used for handshake/route freeze assertions).</summary>
    public IReadOnlyCollection<string> Methods => _map.Keys;
}

/// <summary>
/// A built-in domain handler that owns a slice of the AppServer surface (e.g. cron/*, skills/*).
/// Implementations register their methods into the shared <see cref="AppServerMethodTable"/>.
/// Mirrors the external <see cref="IAppServerProtocolExtension"/> contract for in-process domains,
/// so built-ins and extensions ultimately dispatch through the same lookup.
/// </summary>
internal interface IAppServerDomainHandler
{
    void RegisterMethods(AppServerMethodTable table);
}
