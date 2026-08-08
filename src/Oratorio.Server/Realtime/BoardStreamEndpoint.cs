using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Oratorio.Server.Realtime;

/// <summary>
/// Maps the browser WebSocket stream used for board-level live updates.
/// </summary>
public static class BoardStreamEndpoint
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static IEndpointRouteBuilder MapBoardStream(this IEndpointRouteBuilder routes)
    {
        routes.Map("/api/v1/stream", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Expected a WebSocket request.", context.RequestAborted);
                return;
            }

            var broker = context.RequestServices.GetRequiredService<BoardEventHub>();
            using var subscription = broker.Subscribe();
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            using var sendLock = new SemaphoreSlim(1, 1);

            var boardPumpTask = PumpBoardEventsAsync(socket, subscription, sendLock, linkedCts.Token);
            var drawerPumpTask = PumpDrawerEventsAsync(socket, subscription, sendLock, linkedCts.Token);
            var sourceSyncPumpTask = PumpSourceSyncEventsAsync(socket, subscription, sendLock, linkedCts.Token);
            var heartbeatTask = SendHeartbeatsAsync(socket, sendLock, linkedCts.Token);
            var readTask = ReadControlFramesAsync(socket, subscription, linkedCts);

            await Task.WhenAny(boardPumpTask, drawerPumpTask, sourceSyncPumpTask, heartbeatTask, readTask);
            linkedCts.Cancel();

            try
            {
                await Task.WhenAll(boardPumpTask, drawerPumpTask, sourceSyncPumpTask, heartbeatTask, readTask);
            }
            catch (OperationCanceledException)
            {
                // Normal stream shutdown.
            }
            catch (WebSocketException)
            {
                // The browser disconnected without a close frame.
            }
        });

        return routes;
    }

    private static async Task PumpBoardEventsAsync(
        WebSocket socket,
        BoardEventSubscription subscription,
        SemaphoreSlim sendLock,
        CancellationToken ct)
    {
        await foreach (var boardEvent in subscription.BoardReader.ReadAllAsync(ct))
        {
            await SendAsync(socket, boardEvent, sendLock, ct);
        }
    }

    private static async Task PumpDrawerEventsAsync(
        WebSocket socket,
        BoardEventSubscription subscription,
        SemaphoreSlim sendLock,
        CancellationToken ct)
    {
        await foreach (var drawerEvent in subscription.DrawerReader.ReadAllAsync(ct))
        {
            await SendAsync(socket, drawerEvent, sendLock, ct);
        }
    }

    private static async Task PumpSourceSyncEventsAsync(
        WebSocket socket,
        BoardEventSubscription subscription,
        SemaphoreSlim sendLock,
        CancellationToken ct)
    {
        await foreach (var sourceEvent in subscription.SourceSyncReader.ReadAllAsync(ct))
        {
            await SendAsync(socket, sourceEvent, sendLock, ct);
        }
    }

    private static async Task SendHeartbeatsAsync(WebSocket socket, SemaphoreSlim sendLock, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(ct))
        {
            await SendAsync(socket, BoardEvent.Ping(DateTimeOffset.UtcNow), sendLock, ct);
        }
    }

    private static async Task ReadControlFramesAsync(WebSocket socket, BoardEventSubscription subscription, CancellationTokenSource cts)
    {
        var buffer = new byte[512];
        using var payload = new MemoryStream();
        while (!cts.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, cts.Token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                cts.Cancel();
                return;
            }

            payload.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            ApplyControlFrame(subscription, payload.ToArray());
            payload.SetLength(0);
        }
    }

    private static void ApplyControlFrame(BoardEventSubscription subscription, byte[] payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            var runId = root.TryGetProperty("runId", out var runIdElement) ? runIdElement.GetString() : null;
            if (type == "focus" && !string.IsNullOrWhiteSpace(runId))
            {
                subscription.Focus(runId);
            }
            else if (type == "unfocus" && !string.IsNullOrWhiteSpace(runId))
            {
                subscription.Unfocus(runId);
            }
        }
        catch (JsonException)
        {
            // Ignore malformed control frames; the heartbeat keeps the stream healthy.
        }
    }

    private static async Task SendAsync<T>(WebSocket socket, T boardEvent, SemaphoreSlim sendLock, CancellationToken ct)
    {
        if (socket.State != WebSocketState.Open)
        {
            return;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(boardEvent, JsonOptions);
        await sendLock.WaitAsync(ct);
        try
        {
            await socket.SendAsync(payload, WebSocketMessageType.Text, WebSocketMessageFlags.EndOfMessage, ct);
        }
        finally
        {
            sendLock.Release();
        }
    }
}
