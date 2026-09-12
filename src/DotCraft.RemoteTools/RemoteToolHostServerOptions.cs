using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotCraft.RemoteTools;

/// <summary>Single construction point for the MCP server exposed on every data connection.</summary>
internal static class RemoteToolHostServerOptions
{
    public const string ServerName = "dotcraft.remote-tool-host";

    public static McpServerOptions Create(RemoteToolHostMcpHandlers handlers) => new()
    {
        ServerInfo = new Implementation
        {
            Name = ServerName,
            Version = RemoteToolHostProtocol.ProfileVersion
        },
        ServerInstructions =
            "Pure DotCraft Remote Tool Host. It exposes paired workspace execution tools only.",
        Handlers = new McpServerHandlers
        {
            ListToolsHandler = (request, ct) => handlers.ExecuteAsync(
                token => handlers.ListToolsAsync(request, token, handlers.PeerId), ct),
            CallToolHandler = (request, cancellationToken) =>
                handlers.ExecuteAsync(token => handlers.CallToolAsync(request, handlers.PeerId, token), cancellationToken)
        },
        RequestHandlers = [.. handlers.CreateExtensionHandlers(handlers.PeerId)]
    };
}
