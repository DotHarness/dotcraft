using DotCraft.Configuration;

namespace DotCraft.Plugins;

/// <summary>
/// Provides manifest metadata for desktop-bundled plugins without installing them into a workspace.
/// </summary>
public sealed class BuiltInPluginCatalog(
    IReadOnlyList<string>? sourceRoots = null,
    AppConfig.PluginsConfig? pluginsConfig = null)
{
    /// <summary>
    /// Discovers installable built-in plugins from bundled roots and configured plugin registries.
    /// </summary>
    public PluginDiscoveryResult Discover()
    {
        var diagnostics = new List<PluginDiagnostic>();
        var sources = BuiltInPluginSourceResolver.Discover(sourceRoots, diagnostics, pluginsConfig);
        var plugins = sources
            .Select(source => new DiscoveredPlugin(
                source.Manifest,
                PluginDiscoverySourceKind.BuiltIn,
                source.ContainerRoot,
                Enabled: false,
                Installed: false,
                Installable: true,
                Removable: false))
            .ToArray();

        return new PluginDiscoveryResult(plugins, diagnostics);
    }
}
