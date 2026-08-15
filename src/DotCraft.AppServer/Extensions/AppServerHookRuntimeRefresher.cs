using DotCraft.Configuration;
using DotCraft.Hooks;

namespace DotCraft.AppServer;

internal static class AppServerHookRuntimeRefresher
{
    public static HookDiscoveryResult Refresh(
        HookRunner? hookRunner,
        IAppConfigMonitor? appConfigMonitor,
        string? workspaceCraftPath,
        string? hostWorkspacePath,
        WorkspaceConfigEditor workspaceConfig,
        AppServerRuntimeConfigRefresher runtimeConfig,
        IReadOnlyList<string>? builtInPluginSourceRoots)
    {
        var config = appConfigMonitor?.Current ?? workspaceConfig.LoadCurrentMergedConfig();
        if (appConfigMonitor != null)
        {
            runtimeConfig.RefreshCurrentHooksConfig();
            config = appConfigMonitor.Current;
        }

        var botPath = workspaceCraftPath
                      ?? throw new InvalidOperationException("Workspace DataPath is required to refresh hooks.");
        var workspacePath = hostWorkspacePath
                            ?? Directory.GetParent(botPath)?.FullName
                            ?? throw new InvalidOperationException("The workspace path could not be resolved from DataPath.");
        var discovery = new HooksLoader(botPath).Discover(config, workspacePath, builtInPluginSourceRoots);
        if (hookRunner == null)
            return discovery;

        var hadToolHooks = hookRunner.HasToolHooks;
        hookRunner.ReplaceSnapshot(discovery);
        if (hadToolHooks != hookRunner.HasToolHooks)
            runtimeConfig.InvalidateThreadAgents();

        return discovery;
    }
}
