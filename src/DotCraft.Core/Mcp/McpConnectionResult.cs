using Microsoft.Extensions.AI;

namespace DotCraft.Mcp;

internal sealed record McpConnectionResult(
    IAsyncDisposable Client,
    IReadOnlyList<AIFunction> Tools,
    bool HasSessionId = false,
    bool RecoveredFromStaleSession = false);
