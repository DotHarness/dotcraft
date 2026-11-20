using DotCraft.Configuration;
using DotCraft.Hooks;
using DotCraft.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotCraft.AppServer;

public sealed partial class AppServerHost
{
    private Task StartPluginStartupSync(CancellationToken cancellationToken)
    {
        var craftHome = runtime.Paths.UserData.RootPath;
        if (string.IsNullOrWhiteSpace(craftHome))
            return Task.CompletedTask;

        var refresher = CreateManagedPluginRefresher();
        var sync = new PluginRegistrySyncService(
            craftHome,
            diagnostic => _logger.LogWarning(
                "Plugin registry sync: {Code}: {Message}",
                diagnostic.Code,
                diagnostic.Message));

        return Task.Run(
            () => refresher.RunStartupSyncAsync(craftHome, sync, BroadcastPluginSnapshotUpdated, cancellationToken),
            cancellationToken);
    }

    private AppServerManagedPluginRefresher CreateManagedPluginRefresher()
    {
        var appConfigMonitor = _services.GetService<IAppConfigMonitor>();
        var workspaceCraftPath = runtime.Paths.Data.RootPath;
        var hostWorkspacePath = runtime.Paths.WorkspacePath;
        var pluginsLogger = _loggerFactory.CreateLogger("DotCraft.Plugins");
        var workspaceConfig = new WorkspaceConfigEditor(appConfigMonitor, workspaceCraftPath);
        var mcpConfig = new AppServerMcpConfigService(
            appConfigMonitor,
            runtime.McpClientManager,
            hostWorkspacePath,
            workspaceCraftPath,
            pluginsLogger);
        var runtimeConfig = new AppServerRuntimeConfigRefresher(
            runtime.SessionService,
            appConfigMonitor,
            workspaceCraftPath,
            workspaceConfig);

        return new AppServerManagedPluginRefresher(
            runtime.SkillsLoader,
            appConfigMonitor,
            workspaceCraftPath,
            hostWorkspacePath,
            builtInPluginSourceRoots: null,
            mcpConfig,
            runtime.LspServerManager,
            runtime.ContextPageManager,
            workspaceConfig,
            runtimeConfig,
            _services.GetService<HookRunner>(),
            _services.GetRequiredService<AppServerPluginManagementState>(),
            pluginsLogger);
    }
}
