using DotCraft.Configuration;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Refreshes process-local runtime config caches after AppServer config mutations.
/// </summary>
internal sealed class AppServerRuntimeConfigRefresher(
    ISessionService sessionService,
    IAppConfigMonitor? appConfigMonitor,
    string? workspaceCraftPath,
    WorkspaceConfigEditor workspaceConfig)
{
    public void RefreshCurrentLlmConfig()
    {
        if (appConfigMonitor == null || string.IsNullOrWhiteSpace(workspaceCraftPath))
            return;

        var configPath = Path.Combine(workspaceCraftPath, "config.json");
        var mergedConfig = AppConfig.LoadWithGlobalFallback(configPath, workspaceConfig.EffectiveGlobalConfigPath);
        appConfigMonitor.Current.ProviderId = mergedConfig.ProviderId;
        appConfigMonitor.Current.Model = mergedConfig.Model;
        appConfigMonitor.Current.NetworkTimeoutSeconds = mergedConfig.NetworkTimeoutSeconds;
        appConfigMonitor.Current.Providers = mergedConfig.Providers.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.OrdinalIgnoreCase);
    }

    public void InvalidateThreadAgents()
    {
        if (sessionService is IThreadAgentRefreshService refreshService)
            refreshService.InvalidateThreadAgents();
    }
}
