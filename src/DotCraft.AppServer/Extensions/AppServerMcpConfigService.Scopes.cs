using DotCraft.Configuration;
using DotCraft.Mcp;

namespace DotCraft.AppServer;

internal sealed partial class AppServerMcpConfigService
{
    private string ScopePath(string scope) => scope switch
    {
        "user" => appConfigMonitor?.Current.GlobalConfigPath ?? throw AppServerErrors.InvalidParams("User configuration is unavailable."),
        "workspace" => Path.Combine(workspaceCraftPath ?? throw AppServerErrors.InvalidParams("Workspace configuration is unavailable."), "config.json"),
        _ => throw AppServerErrors.InvalidParams("Unknown MCP scope.")
    };

    public void UpsertScoped(string scope, McpServerConfig server)
    {
        McpScopeStore.Upsert(ScopePath(scope), server);
        PublishScopeRevision(scope);
    }

    public bool RemoveScoped(string scope, string name)
    {
        var removed = McpScopeStore.Remove(ScopePath(scope), name);
        if (removed) PublishScopeRevision(scope);
        return removed;
    }

    public async Task ReloadAsync(CancellationToken ct)
    {
        var effective = McpScopeStore.Effective(ScopePath("workspace"), appConfigMonitor?.Current.GlobalConfigPath);
        if (appConfigMonitor != null) appConfigMonitor.Current.McpServers = effective;
        await ReconnectEffectiveRuntimeAsync(effective, ct);
    }

    private void PublishScopeRevision(string scope) => AtomicConfigDocument.Update(
        Path.Combine(Path.GetDirectoryName(ScopePath(scope))!, "imports", "revision.json"),
        root => root["revision"] = Guid.NewGuid().ToString("N"));
}
