namespace DotCraft.Mcp;

internal interface IMcpToolInvocationCoordinator
{
    Task<McpToolInvocationTarget?> TryGetInvocationTargetAsync(
        string serverName,
        string toolName,
        CancellationToken cancellationToken = default);

    Task<McpToolInvocationTarget?> RefreshToolAfterStaleSessionAsync(
        string serverName,
        string toolName,
        long observedGeneration,
        CancellationToken cancellationToken = default);
}
