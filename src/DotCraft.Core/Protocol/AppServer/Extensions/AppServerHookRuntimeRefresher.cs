using DotCraft.Configuration;
using DotCraft.Hooks;

namespace DotCraft.Protocol.AppServer;

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

        var botPath = workspaceCraftPath ?? Path.Combine(Directory.GetCurrentDirectory(), ".craft");
        var workspacePath = hostWorkspacePath
                            ?? (workspaceCraftPath == null ? Directory.GetCurrentDirectory() : Directory.GetParent(workspaceCraftPath)?.FullName)
                            ?? Directory.GetCurrentDirectory();
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
