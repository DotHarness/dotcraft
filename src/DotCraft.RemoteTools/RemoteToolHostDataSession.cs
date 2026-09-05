using System.Net.WebSockets;
using ModelContextProtocol.Server;

namespace DotCraft.RemoteTools;

/// <summary>The Host is the MCP server on this connection even though it dialled the Hub.</summary>
internal static class RemoteToolHostDataSession
{
    public static async Task RunAsync(
        Uri dataUri,
        string credential,
        string peerId,
        RemoteToolHostMcpHandlers handlers,
        CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        socket.Options.SetRequestHeader("Authorization", "Bearer " + credential);
        await socket.ConnectAsync(dataUri, cancellationToken).ConfigureAwait(false);

        await using var stream = WebSocketStream.Create(
            socket,
            WebSocketMessageType.Text,
            ownsWebSocket: false);
        await using var transport = new StreamServerTransport(
            stream,
            stream,
            RemoteToolHostServerOptions.ServerName,
            loggerFactory: null);
        await using var server = McpServer.Create(
            transport,
            RemoteToolHostServerOptions.Create(handlers, peerId),
            loggerFactory: null,
            serviceProvider: null);
        await server.RunAsync(cancellationToken).ConfigureAwait(false);
    }
}
