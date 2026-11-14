using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using DotCraft.CodexGateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DotCraft.CodexGateway.Tests;

internal sealed class RelayFixture : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly Channel<Connection> _connections = Channel.CreateUnbounded<Connection>();
    private readonly ConcurrentBag<Connection> _allConnections = [];
    public ConcurrentQueue<Request> Requests { get; } = [];
    public Uri Backend { get; private set; } = null!;

    private RelayFixture(WebApplication app) => _app = app;

    public static async Task<RelayFixture> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var fixture = new RelayFixture(builder.Build());
        fixture._app.UseWebSockets();
        fixture._app.MapPost("/backend-api/wham/remote/control/server/{**action}", fixture.HandleHttpAsync);
        fixture._app.MapGet("/backend-api/wham/remote/control/server", fixture.HandleWebSocketAsync);
        await fixture._app.StartAsync();
        fixture.Backend = new Uri(fixture._app.Urls.Single() + "/backend-api/");
        return fixture;
    }

    private async Task HandleHttpAsync(HttpContext context)
    {
        var action = context.Request.RouteValues["action"]!.ToString()!;
        using var body = await JsonDocument.ParseAsync(context.Request.Body);
        var request = new Request(action, context.Request.Headers.ToDictionary(
            header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase),
            body.RootElement.Clone());
        Requests.Enqueue(request);
        if (action is "enroll" or "refresh")
        {
            if (request.Headers["Authorization"] == "Bearer expired-access")
            {
                context.Response.StatusCode = 401;
                return;
            }
            var account = request.Headers["chatgpt-account-id"];
            await context.Response.WriteAsJsonAsync(new
            {
                server_id = "server-" + account, environment_id = "environment-" + account,
                remote_control_token = "relay-" + account, expires_at = DateTimeOffset.UtcNow.AddHours(1)
            });
        }
        else if (action == "pair")
        {
            var account = request.Headers["Authorization"]["Bearer relay-".Length..];
            await context.Response.WriteAsJsonAsync(new
            {
                pairing_code = "pairing-secret", manual_pairing_code = "123456",
                server_id = "server-" + account, environment_id = "environment-" + account,
                expires_at = DateTimeOffset.UtcNow.AddMinutes(5)
            });
        }
        else if (action == "pair/status")
        {
            await context.Response.WriteAsJsonAsync(new { claimed = true });
        }
        else
        {
            context.Response.StatusCode = 404;
        }
    }

    private async Task HandleWebSocketAsync(HttpContext context)
    {
        var socket = await context.WebSockets.AcceptWebSocketAsync();
        var connection = new Connection(socket, context.Request.Headers.ToDictionary(
            header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase));
        _allConnections.Add(connection);
        await _connections.Writer.WriteAsync(connection);
        try
        {
            await connection.Completion.Task.WaitAsync(context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        finally
        {
            socket.Dispose();
        }
    }

    public Task<Connection> ConnectAsync(CancellationToken ct) => _connections.Reader.ReadAsync(ct).AsTask();

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _allConnections)
            connection.Abort();
        await _app.DisposeAsync();
    }

    internal sealed record Request(string Action, Dictionary<string, string> Headers, JsonElement Body);

    internal sealed class Connection(WebSocket socket, Dictionary<string, string> headers)
    {
        public Dictionary<string, string> Headers { get; } = headers;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task SendAsync(object message, CancellationToken ct) =>
            await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message).AsMemory(), WebSocketMessageType.Text, true, ct);

        public async Task<JsonElement> ReadAsync(CancellationToken ct)
        {
            using var bytes = new MemoryStream();
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var read = await socket.ReceiveAsync(buffer.AsMemory(), ct);
                if (read.MessageType == WebSocketMessageType.Close)
                    throw new IOException("Host closed the test socket.");
                bytes.Write(buffer, 0, read.Count);
                if (read.EndOfMessage)
                {
                    using var json = JsonDocument.Parse(bytes.ToArray());
                    return json.RootElement.Clone();
                }
            }
        }

        public void Abort()
        {
            socket.Abort();
            Completion.TrySetResult();
        }
    }
}

internal sealed class EchoSession(
    RemoteControlPeer peer, Func<JsonElement, CancellationToken, Task> send,
    ConcurrentQueue<(RemoteControlPeer Peer, JsonElement Message)> received,
    Action disposed) : IRemoteControlRpcSession
{
    public async Task ReceiveAsync(JsonElement message, CancellationToken ct)
    {
        received.Enqueue((peer, message.Clone()));
        if (message.TryGetProperty("id", out var id))
            await send(JsonSerializer.SerializeToElement(new
            {
                id,
                result = message.TryGetProperty("params", out var parameters) ? parameters : JsonSerializer.SerializeToElement(new { })
            }), ct);
    }

    public ValueTask DisposeAsync()
    {
        disposed();
        return ValueTask.CompletedTask;
    }
}
