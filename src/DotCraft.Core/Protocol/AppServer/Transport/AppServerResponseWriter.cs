using System.Text.Json;
using DotCraft.Protocol;

namespace DotCraft.AppServer;

/// <summary>
/// Writes JSON-RPC responses and ordered response-then-notification pairs for request handlers.
/// </summary>
internal sealed class AppServerResponseWriter(IAppServerTransport transport)
{
    public Task WriteResponseAsync(JsonElement? id, object? result, CancellationToken ct) =>
        transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(id, result), ct);

    public async Task SendNotificationAfterResponseAsync(
        JsonElement? requestId,
        object responseResult,
        string notificationMethod,
        object notificationParams,
        CancellationToken ct)
    {
        await WriteResponseAsync(requestId, responseResult, ct);
        await transport.NotifyContractAsync(notificationMethod, notificationParams, ct);
    }

    public async Task SendNotificationAfterResponseAsync<TResult, TNotification>(
        JsonElement? requestId,
        TResult responseResult,
        RpcNotification<TNotification> notification,
        TNotification notificationParams,
        CancellationToken ct)
    {
        await WriteResponseAsync(requestId, responseResult, ct);
        await transport.NotifyAsync(notification, notificationParams, ct);
    }
}
