using DotCraft.Abstractions;
using Microsoft.Extensions.AI;

namespace DotCraft.Mcp;

/// <summary>
/// Provides MCP (Model Context Protocol) tools from external servers.
/// Only available when McpClientManager is configured and connected.
/// </summary>
public sealed class McpToolProvider : IAgentToolProvider
{
    /// <inheritdoc />
    public int Priority => 80; // External tools have lowest priority

    /// <inheritdoc />
    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        if (context.McpClientManager == null)
            return [];

        return context.McpClientManager.Tools;
    }
}
