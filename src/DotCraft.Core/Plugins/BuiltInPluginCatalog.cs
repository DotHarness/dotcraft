using DotCraft.Configuration;

namespace DotCraft.Plugins;

/// <summary>
/// Provides manifest metadata for desktop-bundled plugins without installing them into a workspace.
/// </summary>
public sealed class BuiltInPluginCatalog(
    IReadOnlyList<string>? sourceRoots = null,
    AppConfig.PluginsConfig? pluginsConfig = null,
    string? craftHome = null)
{
    /// <summary>
    /// Discovers installable built-in plugins from bundled roots and configured marketplaces.
    /// </summary>
    public PluginDiscoveryResult Discover()
    {
        var diagnostics = new List<PluginDiagnostic>();
        var sources = BuiltInPluginSourceResolver.Discover(sourceRoots, diagnostics, pluginsConfig, craftHome);
        var plugins = sources
            .Select(source => new DiscoveredPlugin(
                source.Manifest,
                PluginDiscoverySourceKind.BuiltIn,
                source.ContainerRoot,
                Enabled: false,
                Installed: false,
                Installable: true,
                Removable: false,
                MarketplaceName: source.MarketplaceName))
            .ToArray();

        return new PluginDiscoveryResult(plugins, diagnostics);
    }
}
