using DotCraft.Configuration;
using DotCraft.Plugins;

namespace DotCraft.Runtime;

internal sealed record ModelPluginAdmission(IReadOnlyList<string> Roots, IReadOnlySet<string> AllowedIds)
{
    public PluginDiscoveryResult Discover(PluginDiscoveryService discovery, AppConfig config)
    {
        if (AllowedIds.Count == 0)
            return new([], []);

        var plugins = new Dictionary<string, DiscoveredPlugin>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<PluginDiagnostic>();
        foreach (var root in Roots)
        {
            var result = discovery.DiscoverDirectory(root, config);
            diagnostics.AddRange(result.Diagnostics);
            foreach (var plugin in result.Plugins.Where(plugin => plugin.Manifest.Dotnet is not null
                && AllowedIds.Contains(plugin.Manifest.Id)))
            {
                if (!plugins.TryAdd(plugin.Manifest.Id, plugin))
                {
                    diagnostics.Add(PluginDiagnostic.Warning("DuplicatePluginId",
                        $"Plugin '{plugin.Manifest.Id}' was skipped because a higher-priority root already provided it.",
                        plugin.Manifest.Id, path: plugin.Manifest.ManifestPath));
                }
            }
        }
        foreach (var pluginId in AllowedIds.Order(StringComparer.Ordinal))
        {
            if (!plugins.ContainsKey(pluginId))
                diagnostics.Add(PluginDiagnostic.Warning("ModelPluginNotFound",
                    $"Allowed model plugin '{pluginId}' was not found in the service's plugin roots.", pluginId));
        }
        return new(plugins.Values.ToArray(), diagnostics);
    }
}
