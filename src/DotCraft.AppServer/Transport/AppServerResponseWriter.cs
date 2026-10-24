using System.Text.Json;
using DotCraft.Protocol;

namespace DotCraft.AppServer;

internal sealed class AppServerResponseWriter(IAppServerTransport transport)
{
    public Task WriteResponseAsync(JsonElement? id, object? result, CancellationToken ct) =>
        transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(id, result), ct);

    public Task NotifyAsync<TNotification>(
        RpcNotification<TNotification> notification,
        TNotification notificationParams,
        CancellationToken ct) =>
        transport.NotifyAsync(notification, notificationParams, ct);

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
