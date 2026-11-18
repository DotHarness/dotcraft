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
        var pluginId = PluginIds.ParseKey(pluginKey).PluginId;
        if (deployDiagnostics.Any(d => IsBlockingDeployDiagnosticForPlugin(d, pluginId)))
            throw AppServerErrors.InvalidParams($"Plugin '{pluginKey}' could not be installed.");
    }

    private static DiscoveredPlugin? FindPluginEntry(PluginDiscoveryResult discovery, string id, string? marketplaceName)
    {
        if (string.IsNullOrWhiteSpace(marketplaceName))
            return discovery.Plugins.FirstOrDefault(candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, id));

        var key = PluginIds.Key(id, marketplaceName);
        return discovery.CatalogEntries.FirstOrDefault(candidate => PluginIds.KeysEqual(candidate.Key, key));
    }

    private static bool IsBlockingDeployDiagnosticForPlugin(PluginDiagnostic diagnostic, string pluginId) =>
        (diagnostic.Severity == PluginDiagnosticSeverity.Error || diagnostic.Code == "BuiltInPluginNotFound")
        && !string.IsNullOrWhiteSpace(diagnostic.PluginId)
        && PluginIds.EqualsCanonical(diagnostic.PluginId, pluginId);
}
