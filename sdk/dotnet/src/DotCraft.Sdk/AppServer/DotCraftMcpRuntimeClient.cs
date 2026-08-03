using DotCraft.Protocol.Contracts;
using DotCraft.Protocol.Contracts.AppServer;
using DotCraft.Sdk.Wire;

namespace DotCraft.Sdk.AppServer;

/// <summary>Typed client for the MCP runtime/control methods.</summary>
public sealed class DotCraftMcpRuntimeClient(DotCraftClient client)
{
    public Task<McpServerStatusListResult> ListStatusAsync(
        McpServerStatusListParams? parameters = null,
        CancellationToken cancellationToken = default) =>
        client.Wire.McpServerStatusListAsync(parameters ?? new McpServerStatusListParams(), cancellationToken);

    public Task<McpServerResourceReadResult> ReadResourceAsync(
        McpServerResourceReadParams parameters,
        CancellationToken cancellationToken = default) =>
        client.Wire.McpServerResourceReadAsync(parameters, cancellationToken);

    public Task<McpServerToolCallResult> CallToolAsync(
        McpServerToolCallParams parameters,
        CancellationToken cancellationToken = default) =>
        client.Wire.McpServerToolCallAsync(parameters, cancellationToken);

    public Task<McpServerOAuthLoginResult> LoginOAuthAsync(
        McpServerOAuthLoginParams parameters,
        CancellationToken cancellationToken = default) =>
        client.Wire.McpServerOauthLoginAsync(parameters, cancellationToken);

    public Task<McpServerReloadResult> ReloadAsync(
        CancellationToken cancellationToken = default) =>
        client.Wire.ConfigMcpServerReloadAsync(new RpcEmpty(), cancellationToken);
}
