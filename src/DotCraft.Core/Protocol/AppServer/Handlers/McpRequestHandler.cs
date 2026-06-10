using DotCraft.Configuration;
using DotCraft.Mcp;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Handles the <c>mcp/*</c> wire methods. Extracted from the AppServer dispatcher as part of the
/// Core architecture refactor (M3), sharing workspace MCP persistence with plugin mutations through
/// <see cref="AppServerMcpConfigService"/>.
/// </summary>
internal sealed class McpRequestHandler(
    McpClientManager? mcpClientManager,
    AppServerMcpConfigService configService,
    IAppConfigMonitor? appConfigMonitor,
    Action<McpStatusInfoWire>? broadcastMcpStatusChanged) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(AppServerMethods.McpList, HandleMcpListAsync);
        table.Map(AppServerMethods.McpGet, HandleMcpGetAsync);
        table.Map(AppServerMethods.McpUpsert, HandleMcpUpsertAsync);
        table.Map(AppServerMethods.McpRemove, HandleMcpRemoveAsync);
        table.Map(AppServerMethods.McpStatusList, HandleMcpStatusListAsync);
        table.Map(AppServerMethods.McpTest, HandleMcpTestAsync);
    }

    private async Task<object?> HandleMcpListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = msg;
        configService.EnsureManagementAvailable();
        var servers = await mcpClientManager!.ListConfigsAsync(ct);
        return new McpListResult { Servers = servers.Select(McpWireMapper.ToWire).ToList() };
    }

    private async Task<object?> HandleMcpGetAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<McpGetParams>(msg);
        configService.EnsureManagementAvailable();
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var server = await mcpClientManager!.GetConfigAsync(p.Name, ct);
        if (server == null)
            throw AppServerErrors.McpServerNotFound(p.Name);

        return new McpGetResult { Server = McpWireMapper.ToWire(server) };
    }

    private async Task<object?> HandleMcpUpsertAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<McpUpsertParams>(msg);
        configService.EnsureManagementAvailable();
        McpWireMapper.ValidateConfig(p.Server);

        var server = McpWireMapper.FromWire(p.Server);
        server.Origin = McpServerOrigin.Workspace();

        var existing = await mcpClientManager!.GetConfigAsync(server.Name, ct);
        if (existing?.ReadOnly == true)
            throw AppServerErrors.McpServerReadOnly(server.Name);

        var workspaceServers = await configService.GetWorkspaceServersAsync(ct);
        var existingIndex = workspaceServers.FindIndex(
            candidate => string.Equals(candidate.Name, server.Name, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            workspaceServers[existingIndex] = server;
        else
            workspaceServers.Add(server);

        await configService.SaveWorkspaceServersAsync(workspaceServers, ct);
        configService.SetCurrentWorkspaceServers(workspaceServers);
        await configService.ReconnectEffectiveRuntimeAsync(workspaceServers, ct);
        appConfigMonitor?.NotifyChanged(
            AppServerMethods.McpUpsert,
            [ConfigChangeRegions.Mcp]);

        var updated = await mcpClientManager.GetConfigAsync(server.Name, ct) ?? server;
        var status = (await mcpClientManager.ListStatusesAsync(ct))
            .FirstOrDefault(s => string.Equals(s.Name, updated.Name, StringComparison.OrdinalIgnoreCase));
        if (status != null)
            broadcastMcpStatusChanged?.Invoke(McpWireMapper.ToWire(status));

        return new McpUpsertResult { Server = McpWireMapper.ToWire(updated) };
    }

    private async Task<object?> HandleMcpRemoveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<McpRemoveParams>(msg);
        configService.EnsureManagementAvailable();
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var existing = await mcpClientManager!.GetConfigAsync(p.Name, ct);
        if (existing == null)
            throw AppServerErrors.McpServerNotFound(p.Name);
        if (existing.ReadOnly)
            throw AppServerErrors.McpServerReadOnly(p.Name);

        var workspaceServers = await configService.GetWorkspaceServersAsync(ct);
        var removed = workspaceServers.RemoveAll(
            candidate => string.Equals(candidate.Name, p.Name, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed)
            throw AppServerErrors.McpServerNotFound(p.Name);

        await configService.SaveWorkspaceServersAsync(workspaceServers, ct);
        configService.SetCurrentWorkspaceServers(workspaceServers);
        await configService.ReconnectEffectiveRuntimeAsync(workspaceServers, ct);
        appConfigMonitor?.NotifyChanged(
            AppServerMethods.McpRemove,
            [ConfigChangeRegions.Mcp]);
        return new McpRemoveResult { Removed = true };
    }

    private async Task<object?> HandleMcpStatusListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = msg;
        if (mcpClientManager == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.McpStatusList);

        var statuses = await mcpClientManager.ListStatusesAsync(ct);
        return new McpStatusListResult { Servers = statuses.Select(McpWireMapper.ToWire).ToList() };
    }

    private async Task<object?> HandleMcpTestAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<McpTestParams>(msg);
        if (mcpClientManager == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.McpTest);

        McpWireMapper.ValidateConfig(p.Server);
        var status = await mcpClientManager.TestAsync(McpWireMapper.FromWire(p.Server), ct);
        return new McpTestResult
        {
            Success = string.Equals(status.StartupState, "ready", StringComparison.OrdinalIgnoreCase),
            ErrorCode = status.LastError == null ? null : "McpServerTestFailed",
            ErrorMessage = status.LastError,
            ToolCount = string.Equals(status.StartupState, "ready", StringComparison.OrdinalIgnoreCase) ? status.ToolCount : null
        };
    }
}
