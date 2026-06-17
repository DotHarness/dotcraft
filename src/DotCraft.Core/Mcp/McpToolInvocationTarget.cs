using Microsoft.Extensions.AI;

namespace DotCraft.Mcp;

internal sealed record McpToolInvocationTarget(
    string ServerName,
    string ToolName,
    string Transport,
    long Generation,
    AIFunction Tool,
    bool HasSessionId = false,
    TimeSpan? ToolTimeout = null);
