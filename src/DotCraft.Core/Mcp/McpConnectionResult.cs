using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotCraft.Mcp;

internal sealed record McpConnectionResult(
    IAsyncDisposable Client,
    IReadOnlyList<AIFunction> Tools,
    bool HasSessionId = false,
    bool RecoveredFromStaleSession = false,
    McpClient? ProtocolClient = null,
    IReadOnlyList<Resource>? Resources = null,
    IReadOnlyList<ResourceTemplate>? ResourceTemplates = null,
    string AuthStatus = "unsupported",
    string? FailureReason = null);
