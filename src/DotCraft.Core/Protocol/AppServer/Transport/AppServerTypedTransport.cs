using System.Text.Json;
using DotCraft.Protocol.Contracts;
using Contract = DotCraft.Protocol.Contracts.AppServer;

namespace DotCraft.Protocol.AppServer;

/// <summary>Descriptor-bound transport operations for stable AppServer contract messages.</summary>
public static class AppServerTypedTransport
{
    /// <summary>Sends a bundled notification through its typed descriptor and maps runtime projections at the boundary.</summary>
    public static Task NotifyContractAsync<TParams>(
        this IAppServerTransport transport,
        RpcNotification<TParams> descriptor,
        object? parameters,
        CancellationToken cancellationToken = default)
    {
        if (descriptor.Direction != RpcDirection.ServerToClient)
            throw new InvalidOperationException($"Catalog method '{descriptor.Name}' is not a server notification.");

        object contractParameters;
        if (parameters is null)
        {
            if (typeof(TParams) != typeof(RpcEmpty))
                throw new InvalidOperationException($"Notification '{descriptor.Name}' requires params of type '{typeof(TParams).Name}'.");
            contractParameters = new RpcEmpty();
        }
        else
        {
            contractParameters = parameters is TParams
                ? parameters
                : AppServerContractMapper.ToContract(typeof(TParams), parameters);
        }

        return transport.WriteMessageAsync(new
        {
            jsonrpc = "2.0",
            method = descriptor.Name,
            @params = contractParameters
        }, cancellationToken);
    }

    /// <summary>
    /// Sends a notification through the executable catalog when the method is known, while
    /// retaining the raw extension path for methods owned by external modules.
    /// </summary>
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

        object contractParameters;
        if (parameters is null)
        {
            if (descriptor.ParamsType != typeof(RpcEmpty))
                throw new InvalidOperationException($"Notification '{method}' requires params of type '{descriptor.ParamsType.Name}'.");

            contractParameters = new RpcEmpty();
        }
        else
        {
            contractParameters = descriptor.ParamsType.IsInstanceOfType(parameters)
                ? parameters
                : AppServerContractMapper.ToContract(descriptor.ParamsType, parameters);
        }

        return transport.WriteMessageAsync(new
        {
            jsonrpc = "2.0",
            method = descriptor.Name,
            @params = contractParameters
        }, cancellationToken);
    }

    /// <summary>Sends a server notification bound to its typed descriptor.</summary>
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

    /// <summary>Sends a server request bound to its typed descriptor and validates the result DTO.</summary>
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

        var response = await transport.SendClientRequestAsync(
            descriptor.Name,
            parameters,
            cancellationToken,
            timeout).ConfigureAwait(false);
        if (response.Error.HasValue)
            return new AppServerTypedClientResponse<TResult>(null, response.Error, null);
        if (!response.Result.HasValue)
            return new AppServerTypedClientResponse<TResult>(null, null, "Client returned no result.");

        try
        {
            var result = response.Result.Value.Deserialize<TResult>(DotCraft.Protocol.Contracts.AppServerContractJson.Options);
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

/// <summary>Typed client response with protocol and result-validation failures kept distinct.</summary>
public sealed record AppServerTypedClientResponse<TResult>(
    TResult? Result,
    JsonElement? Error,
    string? InvalidResult)
    where TResult : class;
