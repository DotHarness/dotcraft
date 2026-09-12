using ModelContextProtocol.Server;

namespace DotCraft.RemoteTools;

internal static class RemoteToolHostMcpSession
{
    internal static async Task RunAsync(Stream stream, RemoteToolHostMcpHandlers handlers, CancellationToken ct)
    {
        await using var transport = new StreamServerTransport(stream, stream, RemoteToolHostServerOptions.ServerName, loggerFactory: null);
        await using var server = McpServer.Create(transport, RemoteToolHostServerOptions.Create(handlers), loggerFactory: null, serviceProvider: null);
        var running = server.RunAsync(ct);
        try
        {
            await Task.WhenAny(running, transport.MessageReader.Completion).ConfigureAwait(false);
        }
        finally
        {
            // Transport closure must stop calls before MCP disposal waits for their handlers.
            await handlers.CloseResourcesAsync().ConfigureAwait(false);
        }
        await running.ConfigureAwait(false);
    }
}
