using System.Text.Json;

namespace DotCraft.Protocol.AppServer;

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
        await transport.WriteMessageAsync(new
        {
            jsonrpc = "2.0",
            method = notificationMethod,
            @params = notificationParams
        }, ct);
    }
}
