using DotCraft.Configuration;
using DotCraft.Plugins;

namespace DotCraft.AppServer;

internal sealed partial class PluginRequestHandler
{
    private void DeployCatalogPlugin(string pluginKey)
    {
        var deployDiagnostics = new BuiltInPluginDeployer(
                Path.Combine(PluginDataPath("user"), "plugins"),
                builtInPluginSourceRoots,
                appConfigMonitor?.Current.Plugins ?? new AppConfig.PluginsConfig(),
                workspaceConfig.UserDataPath)
            .DeployPlugin(pluginKey);
        PluginDiagnosticsLogger.Write(deployDiagnostics, logger);
        var pluginId = PluginIds.IdFromKey(pluginKey);
        if (deployDiagnostics.Any(d => AppServerManagedPluginRefresher.IsBlockingDeployDiagnosticForPlugin(d, pluginId)))
            throw AppServerErrors.InvalidParams($"Plugin '{pluginKey}' could not be installed.");
    }

    private static DiscoveredPlugin? FindPluginEntry(PluginDiscoveryResult discovery, string id, string? marketplaceName)
    {
        if (string.IsNullOrWhiteSpace(marketplaceName))
            return discovery.Plugins.FirstOrDefault(candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, id));

        var key = PluginIds.Key(id, marketplaceName);
        return discovery.CatalogEntries.FirstOrDefault(candidate => PluginIds.KeysEqual(candidate.Key, key));
    }
}
