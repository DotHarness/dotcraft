using System.Net.WebSockets;

namespace DotCraft.RemoteTools;

/// <summary>The Host is the MCP server on this connection even though it dialled the Hub.</summary>
internal static class RemoteToolHostDataSession
{
    public static async Task RunAsync(
        Uri dataUri,
        string credential,
        string peerId,
        RemoteToolHostExecutionHost host,
        CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        socket.Options.SetRequestHeader("Authorization", "Bearer " + credential);
        await socket.ConnectAsync(dataUri, cancellationToken).ConfigureAwait(false);
        await using var handlers = host.CreateSession(peerId);

        await using var stream = WebSocketStream.Create(
            socket,
            WebSocketMessageType.Text,
            ownsWebSocket: false);
        await RemoteToolHostMcpSession.RunAsync(stream, handlers, cancellationToken).ConfigureAwait(false);
    }
}
