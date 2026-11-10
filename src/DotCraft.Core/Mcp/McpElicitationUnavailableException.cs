using DotCraft.Tools;
using ModelContextProtocol;

namespace DotCraft.Mcp;

public sealed class McpElicitationUnavailableException(string reason)
    : McpProtocolException($"{ToolErrorCodes.McpElicitationUnavailable}: MCP supplementary input is unavailable ({reason}).", McpErrorCode.InvalidRequest)
{
    public string Reason { get; } = reason;
}
