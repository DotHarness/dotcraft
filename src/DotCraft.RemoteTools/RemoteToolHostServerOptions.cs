using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotCraft.RemoteTools;

/// <summary>Single construction point for the MCP server exposed on every data connection.</summary>
internal static class RemoteToolHostServerOptions
{
    public const string ServerName = "dotcraft.remote-tool-host";

    public static McpServerOptions Create(RemoteToolHostMcpHandlers handlers, string peerId) => new()
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
            ListToolsHandler = handlers.ListToolsAsync,
            CallToolHandler = (request, cancellationToken) =>
                handlers.CallToolAsync(request, peerId, cancellationToken)
        },
        RequestHandlers = [.. handlers.CreateExtensionHandlers(peerId)]
    };
}
