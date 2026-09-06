using System.Buffers;
using System.Net.WebSockets;

namespace DotCraft.Hub;

/// <summary>
/// Relays a Remote Tool Host session between a local AppServer socket and a paired peer socket,
/// never parsing, rewriting, logging, or persisting the payload.
/// </summary>
internal static class SatelliteWebSocketBridge
{
    private const int BufferBytes = 32 * 1024;

    /// <summary>Cancelling a receive aborts its socket, so the opposite pump gets time to see the close first.</summary>
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(2);

    public static async Task RelayAsync(WebSocket agent, WebSocket peer, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await Task.WhenAll(PumpAsync(agent, peer, linked), PumpAsync(peer, agent, linked))
            .ConfigureAwait(false);
    }

    private static async Task PumpAsync(WebSocket from, WebSocket to, CancellationTokenSource linked)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferBytes);
        try
        {
            while (true)
            {
                ValueWebSocketReceiveResult received;
                try
                {
                    received = await from.ReceiveAsync(buffer.AsMemory(), linked.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    await CloseQuietlyAsync(to, WebSocketCloseStatus.EndpointUnavailable, "peerClosed")
                        .ConfigureAwait(false);
                    break;
                }

                if (received.MessageType == WebSocketMessageType.Close)
                {
                    await CloseQuietlyAsync(
                        to,
                        from.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                        from.CloseStatusDescription).ConfigureAwait(false);
                    break;
                }

                try
                {
                    await to.SendAsync(
                        buffer.AsMemory(0, received.Count),
                        received.MessageType,
                        received.EndOfMessage,
                        linked.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    await CloseQuietlyAsync(from, WebSocketCloseStatus.EndpointUnavailable, "peerClosed")
                        .ConfigureAwait(false);
                    break;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            linked.CancelAfter(ShutdownGrace);
        }
    }

    private static async Task CloseQuietlyAsync(
        WebSocket socket,
        WebSocketCloseStatus status,
        string? description)
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await socket.CloseOutputAsync(status, description, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // The opposite side may already be gone; the pump is ending either way.
        }
    }
}
