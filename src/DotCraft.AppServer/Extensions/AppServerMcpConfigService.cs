using DotCraft.Configuration;
using DotCraft.Mcp;
using DotCraft.Plugins;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using Microsoft.Extensions.Logging;

namespace DotCraft.AppServer;

/// <summary>
/// Shared AppServer MCP configuration/runtime helper. It owns scoped MCP persistence and
/// effective-runtime reconnect logic that is used by both <c>mcp/*</c> and plugin mutations.
/// </summary>
internal sealed partial class AppServerMcpConfigService(
    IAppConfigMonitor? appConfigMonitor,
    McpClientManager? mcpClientManager,
    string? hostWorkspacePath,
    string? workspaceCraftPath,
    ILogger? logger)
{
    public void EnsureManagementAvailable()
    {
        if (mcpClientManager == null || string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound("mcp/*");
    }

    public async Task<List<McpServerConfig>> GetWorkspaceServersAsync(CancellationToken ct)
    {
        var source = appConfigMonitor?.Current.McpServers;
        if (source is not { Count: > 0 } && mcpClientManager != null)
            source = (await mcpClientManager.ListConfigsAsync(ct))
                .Where(server => !server.ReadOnly)
                .ToList();

        return (source ?? [])
            .Where(server => !server.ReadOnly)
            .Select(server => server.Clone())
            .ToList();
    }

    public List<McpServerConfig> GetWorkspaceServersSnapshot()
    {
        return (appConfigMonitor?.Current.McpServers ?? [])
            .Where(server => !server.ReadOnly)
            .Select(server => server.Clone())
            .ToList();
    }

    public async Task ReconnectEffectiveRuntimeAsync(
        IReadOnlyList<McpServerConfig> workspaceServers,
        CancellationToken ct)
    {
        if (mcpClientManager == null)
            return;
        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound("mcp/*");

        var current = appConfigMonitor?.Current ?? new AppConfig();
        current.McpServers = workspaceServers
            .Select(server => server.Clone())
            .ToList();

        var effective = PluginMcpServerResolver.LoadEffectiveServers(
            current,
            ResolveHostWorkspacePath(),
            workspaceCraftPath,
            out var diagnostics);
        PluginDiagnosticsLogger.Write(diagnostics, logger);

        await mcpClientManager.ConnectAsync(effective, ct);
    }

    public string ResolveHostWorkspacePath() =>
        hostWorkspacePath
        ?? (!string.IsNullOrWhiteSpace(workspaceCraftPath)
            ? Directory.GetParent(workspaceCraftPath)?.FullName
            : null)
        ?? throw new InvalidOperationException("The AppServer workspace path is not configured.");

}
