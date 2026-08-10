using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Mcp;
using DotCraft.Plugins;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using McpServerOrigin = DotCraft.Mcp.McpServerOrigin;
using Microsoft.Extensions.Logging;

namespace DotCraft.AppServer;

/// <summary>
/// Shared AppServer MCP configuration/runtime helper. It owns the workspace MCP persistence and
/// effective-runtime reconnect logic that is used by both <c>mcp/*</c> and plugin mutations.
/// </summary>
internal sealed class AppServerMcpConfigService(
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
            .Select(CloneAsWorkspaceServer)
            .ToList();
    }

    public List<McpServerConfig> GetWorkspaceServersSnapshot()
    {
        return (appConfigMonitor?.Current.McpServers ?? [])
            .Where(server => !server.ReadOnly)
            .Select(CloneAsWorkspaceServer)
            .ToList();
    }

    public void SetCurrentWorkspaceServers(IReadOnlyList<McpServerConfig> servers)
    {
        if (appConfigMonitor == null)
            return;

        appConfigMonitor.Current.McpServers = servers
            .Select(CloneAsWorkspaceServer)
            .ToList();
    }

    public async Task SaveWorkspaceServersAsync(
        IReadOnlyList<McpServerConfig> servers,
        CancellationToken ct)
    {
        _ = ct;
        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound("mcp/*");

        var configPath = Path.Combine(workspaceCraftPath, "config.json");
        Directory.CreateDirectory(workspaceCraftPath);
        var root = WorkspaceConfigEditor.LoadObject(configPath);

        var key = WorkspaceConfigEditor.FindCaseInsensitiveKey(root, "McpServers") ?? "McpServers";
        var serverObject = new JsonObject();
        foreach (var server in servers
                     .Where(server => !server.ReadOnly && server.Origin.IsWorkspace)
                     .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(server.Name))
                continue;

            var workspaceServer = CloneAsWorkspaceServer(server);
            var serverNode = JsonSerializer.SerializeToNode(workspaceServer, AppConfig.SerializerOptions);
            if (serverNode != null)
                serverObject[workspaceServer.Name] = serverNode;
        }

        root[key] = serverObject;
        WorkspaceConfigEditor.WriteObject(configPath, root);
    }

    public async Task ReconnectEffectiveRuntimeAsync(
        IReadOnlyList<McpServerConfig> workspaceServers,
        CancellationToken ct)
    {
        if (mcpClientManager == null)
            return;

        var current = appConfigMonitor?.Current ?? new AppConfig();
        current.McpServers = workspaceServers
            .Select(CloneAsWorkspaceServer)
            .ToList();

        var effective = PluginMcpServerResolver.LoadEffectiveServers(
            current,
            ResolveHostWorkspacePath(),
            workspaceCraftPath ?? Path.Combine(ResolveHostWorkspacePath(), ".craft"),
            out var diagnostics);
        PluginDiagnosticsStore.Shared.Append(diagnostics);
        PluginDiagnosticsLogger.Write(diagnostics, logger);

        await mcpClientManager.ConnectAsync(effective, ct);
    }

    public string ResolveHostWorkspacePath() =>
        hostWorkspacePath
        ?? (workspaceCraftPath == null ? Directory.GetCurrentDirectory() : Directory.GetParent(workspaceCraftPath)?.FullName)
        ?? Directory.GetCurrentDirectory();

    public static McpServerConfig CloneAsWorkspaceServer(McpServerConfig server)
    {
        var clone = server.Clone();
        clone.Origin = McpServerOrigin.Workspace();
        return clone;
    }
}
