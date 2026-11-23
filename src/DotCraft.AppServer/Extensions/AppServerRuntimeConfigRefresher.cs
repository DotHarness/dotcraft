using DotCraft.Configuration;
using DotCraft.Sessions;

namespace DotCraft.AppServer;

internal sealed class AppServerRuntimeConfigRefresher(
    ISessionService sessionService,
    IAppConfigMonitor? appConfigMonitor,
    string? workspaceCraftPath,
    WorkspaceConfigEditor workspaceConfig)
{
    public void RefreshCurrentHooksConfig()
    {
        if (appConfigMonitor == null)
            return;

        if (!string.IsNullOrWhiteSpace(workspaceCraftPath))
        {
            var mergedConfig = LoadMergedConfig();
            appConfigMonitor.Current.Hooks = mergedConfig.Hooks;
            return;
        }

        appConfigMonitor.Current.Hooks = new AppConfig.HooksConfig();
    }

    public Task RefreshMcpRuntimesAsync(CancellationToken ct) =>
        sessionService is IThreadMcpRuntimeService mcpRuntime
            ? mcpRuntime.RefreshMcpRuntimesAsync(ct)
            : Task.CompletedTask;

    public void InvalidateThreadAgents()
    {
        if (sessionService is IThreadAgentRefreshService refreshService)
            refreshService.InvalidateThreadAgents();
    }

    public AppConfig LoadMergedConfig() =>
        AppConfig.LoadWithGlobalFallback(
            Path.Combine(workspaceCraftPath!, "config.json"),
            workspaceConfig.EffectiveGlobalConfigPath);
}
