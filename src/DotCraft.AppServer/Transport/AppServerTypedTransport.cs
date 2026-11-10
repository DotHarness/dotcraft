using System.Text.Json;
using DotCraft.Protocol;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

public static class AppServerTypedTransport
{
    public static Task NotifyContractAsync<TParams>(
        this IAppServerTransport transport,
        RpcNotification<TParams> descriptor,
        TParams parameters,
        CancellationToken cancellationToken = default)
        where TParams : class
    {
        if (descriptor.Direction != RpcDirection.ServerToClient)
            throw new InvalidOperationException($"Catalog method '{descriptor.Name}' is not a server notification.");

        return transport.WriteMessageAsync(new
        {
            jsonrpc = "2.0",
            method = descriptor.Name,
            @params = parameters
        }, cancellationToken);
    }

    /// <summary>Unknown methods are sent as raw extension notifications.</summary>
    public static Task NotifyContractAsync(
        this IAppServerTransport transport,
        string method,
        object? parameters,
        CancellationToken cancellationToken = default)
    {
        var descriptor = Contract.AppServerRpcCatalog.All.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, method, StringComparison.Ordinal));
        if (descriptor is null)
        {
            return transport.WriteMessageAsync(new
            {
                jsonrpc = "2.0",
                method,
                @params = parameters
            }, cancellationToken);
        }

        if (descriptor.Kind != "notification" || descriptor.Direction != RpcDirection.ServerToClient)
            throw new InvalidOperationException($"Catalog method '{method}' is not a server notification.");

        if (parameters is null)
        {
            if (descriptor.ParamsType != typeof(RpcEmpty))
                throw new InvalidOperationException($"Notification '{method}' requires params of type '{descriptor.ParamsType.Name}'.");
            parameters = new RpcEmpty();
        }
        else if (!descriptor.ParamsType.IsInstanceOfType(parameters))
        {
            throw new InvalidOperationException(
                $"Notification '{method}' received {parameters.GetType().FullName}, " +
                $"expected {descriptor.ParamsType.FullName} from the Contracts assembly.");
        }

        return transport.WriteMessageAsync(new
        {
            jsonrpc = "2.0",
            method = descriptor.Name,
            @params = parameters
        }, cancellationToken);
    }

    public static Task NotifyAsync<TParams>(
        this IAppServerTransport transport,
        RpcNotification<TParams> descriptor,
        TParams parameters,
        CancellationToken cancellationToken = default)
    {
        if (descriptor.Direction != RpcDirection.ServerToClient)
            throw new InvalidOperationException($"Notification descriptor '{descriptor.Name}' has the wrong direction for server emission.");

        return transport.WriteMessageAsync(new
        {
            jsonrpc = "2.0",
            method = descriptor.Name,
            @params = parameters
        }, cancellationToken);
    }

    public static async Task<AppServerTypedClientResponse<TResult>> RequestAsync<TParams, TResult>(
        this IAppServerTransport transport,
        RpcRequest<TParams, TResult> descriptor,
        TParams parameters,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
        where TResult : class
    {
        if (descriptor.Direction != RpcDirection.ServerToClient)
            throw new InvalidOperationException($"Request descriptor '{descriptor.Name}' has the wrong direction for client dispatch.");

        var pending = await transport.StartClientRequestAsync(
            descriptor.Name,
            parameters,
            cancellationToken,
            timeout).ConfigureAwait(false);
        var response = await pending.Response.ConfigureAwait(false);
        if (response.Error.HasValue)
            return new AppServerTypedClientResponse<TResult>(null, response.Error, null);
        if (!response.Result.HasValue)
            return new AppServerTypedClientResponse<TResult>(null, null, "Client returned no result.");

        try
        {
            var result = response.Result.Value.Deserialize<TResult>(AppServerContractJson.Options);
            return result is null
                ? new AppServerTypedClientResponse<TResult>(null, null, "Client returned a null result.")
                : new AppServerTypedClientResponse<TResult>(result, null, null);
        }
        catch (JsonException exception)
        {
            return new AppServerTypedClientResponse<TResult>(null, null, exception.Message);
        }
    }
}

public sealed record AppServerTypedClientResponse<TResult>(
    TResult? Result,
    JsonElement? Error,
    string? InvalidResult)
    where TResult : class;
