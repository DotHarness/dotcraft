namespace DotCraft.Sdk.AppServer;

/// <summary>Typed client for the MCP runtime/control methods.</summary>
public sealed class DotCraftMcpRuntimeClient(DotCraftClient client)
{
    public Task<McpServerStatusListResult> ListStatusAsync(
        McpServerStatusListParams? parameters = null,
        CancellationToken cancellationToken = default) =>
        client.RequestRawAsync<McpServerStatusListResult>(
            "mcpServerStatus/list",
            parameters ?? new McpServerStatusListParams(),
            cancellationToken);

    public Task<McpServerResourceReadResult> ReadResourceAsync(
        McpServerResourceReadParams parameters,
        CancellationToken cancellationToken = default) =>
        client.RequestRawAsync<McpServerResourceReadResult>(
            "mcpServer/resource/read", parameters, cancellationToken);

    public Task<McpServerToolCallResult> CallToolAsync(
        McpServerToolCallParams parameters,
        CancellationToken cancellationToken = default) =>
        client.RequestRawAsync<McpServerToolCallResult>(
            "mcpServer/tool/call", parameters, cancellationToken);

    public Task<McpServerOAuthLoginResult> LoginOAuthAsync(
        McpServerOAuthLoginParams parameters,
        CancellationToken cancellationToken = default) =>
        client.RequestRawAsync<McpServerOAuthLoginResult>(
            "mcpServer/oauth/login", parameters, cancellationToken);

    public Task<McpServerReloadResult> ReloadAsync(
        CancellationToken cancellationToken = default) =>
        client.RequestRawAsync<McpServerReloadResult>(
            "config/mcpServer/reload",
            null,
            cancellationToken);
}
