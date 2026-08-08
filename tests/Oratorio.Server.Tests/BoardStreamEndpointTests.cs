using System.Net.WebSockets;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Oratorio.Server.Api;
using Oratorio.Server.Domain;
using Oratorio.Server.Realtime;
using BoardTaskStatus = Oratorio.Server.Domain.TaskStatus;

namespace Oratorio.Server.Tests;

public sealed class BoardStreamEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task Stream_ForwardsTaskUpdatedEvents()
    {
        await using var app = new TestOratorioApp();
        var socket = app.Server.CreateWebSocketClient();
        using var stream = await socket.ConnectAsync(new Uri("ws://localhost/api/v1/stream"), CancellationToken.None);
        var client = app.CreateClient();

        var created = await client.PostAsJsonAsync(
            "/api/v1/local-tasks",
            new CreateLocalTaskRequest(
                "Stream this task",
                "Exercise the board stream.",
                "example-owner/oratorio",
                "operator",
                "local/m3",
                ["m3", "stream"]),
            JsonOptions);
        created.EnsureSuccessStatusCode();

        var boardEvent = await ReceiveEventAsync(stream);
        Assert.Equal(BoardEvent.TaskUpdatedType, boardEvent.Type);
        Assert.Equal("DEF-1", boardEvent.ShortId);
        Assert.Equal(BoardTaskStatus.Todo, boardEvent.TaskStatus);
        Assert.Equal(0, boardEvent.BoardSortOrder);
    }

    [Fact]
    public async Task Stream_ForwardsFocusedDrawerEvents()
    {
        await using var app = new TestOratorioApp();
        var socket = app.Server.CreateWebSocketClient();
        using var stream = await socket.ConnectAsync(new Uri("ws://localhost/api/v1/stream"), CancellationToken.None);
        await SendControlFrameAsync(stream, new { type = "focus", runId = "run-1" });
        await Task.Delay(50);

        var hub = app.Services.GetRequiredService<BoardEventHub>();
        hub.PublishDrawer(
            "run-1",
            new DrawerEvent(
                DrawerEvent.PlanUpdatedType,
                "run-1",
                null,
                null,
                JsonSerializer.SerializeToElement(new { title = "Focused plan" }, JsonOptions),
                DateTimeOffset.Parse("2026-05-09T00:00:00Z")));

        var drawerEvent = await ReceiveEventAsync<DrawerEvent>(stream);
        Assert.Equal(DrawerEvent.PlanUpdatedType, drawerEvent.Type);
        Assert.Equal("run-1", drawerEvent.RunId);
    }

    [Fact]
    public async Task Stream_ForwardsSourceSyncEvents()
    {
        await using var app = new TestOratorioApp();
        var socket = app.Server.CreateWebSocketClient();
        using var stream = await socket.ConnectAsync(new Uri("ws://localhost/api/v1/stream"), CancellationToken.None);

        var hub = app.Services.GetRequiredService<BoardEventHub>();
        hub.PublishSourceSync(new SourceSyncEvent(
            SourceSyncEvent.GitHubSyncJobUpdatedType,
            JsonSerializer.SerializeToElement(new { jobId = "job-1", status = "running" }, JsonOptions),
            DateTimeOffset.Parse("2026-05-09T00:00:00Z")));

        var sourceEvent = await ReceiveEventAsync<SourceSyncEvent>(stream);
        Assert.Equal(SourceSyncEvent.GitHubSyncJobUpdatedType, sourceEvent.Type);
        Assert.Equal("job-1", sourceEvent.Payload.GetProperty("jobId").GetString());
    }

    private static async Task<BoardEvent> ReceiveEventAsync(WebSocket socket)
    {
        return await ReceiveEventAsync<BoardEvent>(socket);
    }

    private static async Task<T> ReceiveEventAsync<T>(WebSocket socket)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[4096];
        using var payload = new MemoryStream();

        while (!cts.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(buffer, cts.Token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("Stream closed before a board event was received.");
            }

            payload.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            var json = Encoding.UTF8.GetString(payload.ToArray());
            var streamEvent = JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new InvalidOperationException("Expected a stream event.");
            if (streamEvent is BoardEvent { Type: BoardEvent.PingType })
            {
                payload.SetLength(0);
                continue;
            }

            return streamEvent;
        }

        throw new TimeoutException("Timed out waiting for a board event.");
    }

    private static async Task SendControlFrameAsync(WebSocket socket, object frame)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(frame, JsonOptions);
        await socket.SendAsync(payload, WebSocketMessageType.Text, WebSocketMessageFlags.EndOfMessage, CancellationToken.None);
    }
}
