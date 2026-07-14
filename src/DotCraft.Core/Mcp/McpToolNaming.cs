using DotCraft.Tools;

namespace DotCraft.Mcp;

/// <summary>Canonical MCP identity helpers for normalized provider call names.</summary>
public static class McpToolNaming
{
    /// <summary>Returns the canonical MCP namespace while preserving the declared server identity.</summary>
    public static string CanonicalNamespace(string serverName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        return $"mcp__{serverName.Trim()}";
    }

    /// <summary>Creates a canonical MCP tool name from raw server and source tool identities.</summary>
    public static ToolName CanonicalToolName(string serverName, string rawToolName) =>
        new(CanonicalNamespace(serverName), rawToolName);
}
