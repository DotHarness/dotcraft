namespace DotCraft.Plugins;

/// <summary>
/// Provides manifest metadata for desktop-bundled plugins without installing them into a workspace.
/// </summary>
public sealed class BuiltInPluginCatalog(IReadOnlyList<string>? sourceRoots = null)
{
    /// <summary>
    /// Discovers installable built-in plugins from DOTCRAFT_BUILTIN_PLUGIN_ROOTS or injected source roots.
    /// </summary>
    public PluginDiscoveryResult Discover()
    {
        var diagnostics = new List<PluginDiagnostic>();
        var sources = BuiltInPluginSourceResolver.Discover(sourceRoots, diagnostics);
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
