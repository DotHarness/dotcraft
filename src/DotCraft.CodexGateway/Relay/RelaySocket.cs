using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace DotCraft.CodexGateway.Relay;

internal static class RelaySocket
{
    public static async Task RunAsync(RemoteControlApi api, EnrollmentResponse enrollment,
        string installationId, string name, RelaySessions sessions, RelayOutbox outbox,
        Action connected, CancellationToken ct)
    {
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
        socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(60);
        socket.Options.SetRequestHeader("Authorization", "Bearer " + enrollment.RemoteControlToken);
        socket.Options.SetRequestHeader("x-codex-server-id", enrollment.ServerId);
        socket.Options.SetRequestHeader("x-codex-installation-id", installationId);
        socket.Options.SetRequestHeader("x-codex-name", Convert.ToBase64String(Encoding.UTF8.GetBytes(name)));
        socket.Options.SetRequestHeader("x-codex-protocol-version", "3");
        if (sessions.Cursor is { } cursor)
            socket.Options.SetRequestHeader("x-codex-subscribe-cursor", cursor);
        RelayRequestException? failure = null;
        using var invoker = RemoteControlApi.CreateWebSocketInvoker(error => failure = error);
        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connect.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await socket.ConnectAsync(api.WebSocketUri, invoker, connect.Token);
            }
            catch (WebSocketException) when (failure is not null)
            {
                throw failure;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new IOException("codex_remote_control.connect_timeout: Relay connection timed out.");
            }
        }
        connected();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var reader = ReadAsync(socket, sessions, lifetime.Token);
        var writer = outbox.WriteAsync(socket, lifetime.Token);
        try
        {
            await await Task.WhenAny(reader, writer);
        }
        finally
        {
            lifetime.Cancel();
            socket.Abort();
            try { await Task.WhenAll(reader, writer); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (WebSocketException) { }
        }
    }

    private static async Task ReadAsync(WebSocket socket, RelaySessions sessions, CancellationToken ct)
    {
        using var sweep = new PeriodicTimer(TimeSpan.FromSeconds(30));
        var tick = sweep.WaitForNextTickAsync(ct).AsTask();
        var receive = ReadEnvelopeAsync(socket, ct);
        while (true)
        {
            if (await Task.WhenAny(receive, tick) == tick)
            {
                if (!await tick)
                    return;
                await sessions.SweepAsync();
                tick = sweep.WaitForNextTickAsync(ct).AsTask();
                continue;
            }
            var payload = await receive;
            try
            {
                using var envelope = JsonDocument.Parse(payload);
                await sessions.ReceiveAsync(envelope.RootElement, ct);
            }
            catch (JsonException error)
            {
                RelayDiagnostics.Failure(null, "relay_envelope", error);
                throw new IOException("codex_remote_control.invalid_envelope: Relay sent an invalid JSON envelope.");
            }
            receive = ReadEnvelopeAsync(socket, ct);
        }
    }

    private static async Task<byte[]> ReadEnvelopeAsync(WebSocket socket, CancellationToken ct)
    {
        using var message = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await socket.ReceiveAsync(buffer.AsMemory(), ct);
            if (read.MessageType == WebSocketMessageType.Close)
                throw new IOException("codex_remote_control.disconnected: Relay closed the connection.");
            if (read.MessageType != WebSocketMessageType.Text)
                throw new IOException("codex_remote_control.invalid_frame: Expected a text envelope.");
            if (message.Length + read.Count > RelayFrames.MaxEnvelopeBytes)
                throw new IOException("codex_remote_control.envelope_too_large: Relay envelope exceeds 150 KiB.");
            message.Write(buffer, 0, read.Count);
            if (read.EndOfMessage)
                return message.ToArray();
        }
    }
}
