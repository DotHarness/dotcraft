using System.Text.Json;
using DotCraft.Protocol;

namespace DotCraft.AppServer;

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

    /// <summary>Registers a client request through its typed executable descriptor.</summary>
    public void Map<TParams, TResult>(
        RpcRequest<TParams, TResult> descriptor,
        Func<AppServerTypedRequest<TParams>, CancellationToken, Task<AppServerTypedResult<TResult>>> handler)
        where TParams : class
        where TResult : class
    {
        if (descriptor.Direction != RpcDirection.ClientToServer)
            throw new InvalidOperationException($"Request descriptor '{descriptor.Name}' has the wrong direction for server dispatch.");

        Map(descriptor.Name, async (message, cancellationToken) =>
        {
            var parameters = AppServerTypedParams.Deserialize<TParams>(message);
            var result = await handler(new AppServerTypedRequest<TParams>(message, parameters), cancellationToken);
            return result.ResponseAlreadyWritten ? null : result.Result;
        });
    }

    /// <summary>
    /// Registers a descriptor-bound handler that returns its Contracts result through the shared
    /// object dispatcher while enforcing the descriptor result type at runtime.
    /// </summary>
    public void Map<TParams, TResult>(
        RpcRequest<TParams, TResult> descriptor,
        Func<AppServerTypedRequest<TParams>, CancellationToken, Task<object?>> handler)
        where TParams : class
        where TResult : class
    {
        if (descriptor.Direction != RpcDirection.ClientToServer)
            throw new InvalidOperationException($"Request descriptor '{descriptor.Name}' has the wrong direction for server dispatch.");

        Map(descriptor.Name, async (message, cancellationToken) =>
        {
            var parameters = AppServerTypedParams.Deserialize<TParams>(message);
            var result = await handler(new AppServerTypedRequest<TParams>(message, parameters), cancellationToken);
            if (result is not null && !descriptor.ResultType.IsInstanceOfType(result))
            {
                throw new InvalidOperationException(
                    $"Request '{descriptor.Name}' returned {result.GetType().FullName}, " +
                    $"expected {descriptor.ResultType.FullName} from the Contracts assembly.");
            }
            return result;
        });
    }

    /// <summary>Looks up the handler for <paramref name="method"/>.</summary>
    public bool TryGet(string method, out AppServerMethodInvoker handler) =>
        _map.TryGetValue(method, out handler!);

    /// <summary>All registered method names (used for handshake/route freeze assertions).</summary>
    public IReadOnlyCollection<string> Methods => _map.Keys;

}

/// <summary>Per-connection descriptor registry for client-to-server notifications.</summary>
internal sealed class AppServerNotificationTable
{
    private readonly Dictionary<string, Action<AppServerIncomingMessage>> _map = new(StringComparer.Ordinal);

    public void Map<TParams>(RpcNotification<TParams> descriptor, Action<TParams> handler)
        where TParams : class
    {
        if (descriptor.Direction != RpcDirection.ClientToServer)
            throw new InvalidOperationException($"Notification descriptor '{descriptor.Name}' has the wrong direction for server dispatch.");
        if (!_map.TryAdd(descriptor.Name, message => handler(AppServerTypedParams.Deserialize<TParams>(message))))
            throw new InvalidOperationException($"Duplicate AppServer notification registration: '{descriptor.Name}'.");
    }

    public bool TryHandle(AppServerIncomingMessage message)
    {
        if (message.Method is null || !_map.TryGetValue(message.Method, out var handler))
            return false;
        handler(message);
        return true;
    }
}

internal static class AppServerTypedParams
{
    public static TParams Deserialize<TParams>(AppServerIncomingMessage message)
        where TParams : class
        => (TParams)Deserialize(typeof(TParams), message);

    public static object Deserialize(Type paramsType, AppServerIncomingMessage message)
    {
        try
        {
            var parameters = !message.Params.HasValue || message.Params.Value.ValueKind == JsonValueKind.Null
                ? JsonSerializer.SerializeToElement(new { })
                : message.Params.Value;
            return parameters.Deserialize(paramsType, AppServerContractJson.Options)
                   ?? throw new JsonException("Params deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw AppServerErrors.InvalidParams($"Failed to deserialize params: {exception.Message}");
        }
    }
}

/// <summary>A validated typed request paired with its original JSON-RPC envelope.</summary>
internal sealed record AppServerTypedRequest<TParams>(AppServerIncomingMessage Message, TParams Params);

/// <summary>A typed handler result or an indication that ordering logic wrote the response inline.</summary>
internal readonly record struct AppServerTypedResult<TResult>(TResult? Result, bool ResponseAlreadyWritten)
    where TResult : class
{
    public static AppServerTypedResult<TResult> FromResult(TResult result) => new(result, false);

    public static AppServerTypedResult<TResult> Written => new(null, true);
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
