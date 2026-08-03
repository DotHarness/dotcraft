using DotCraft.Configuration;
using PluginDiagnostic = DotCraft.Plugins.PluginDiagnostic;

namespace DotCraft.Plugins;

internal sealed record BuiltInPluginSource(
    PluginManifest Manifest,
    string PluginRoot,
    string ContainerRoot,
    string? MarketplaceName = null);

internal static class BuiltInPluginSourceResolver
{
    public const string EnvironmentVariableName = "DOTCRAFT_BUILTIN_PLUGIN_ROOTS";

    public static IReadOnlyList<BuiltInPluginSource> Discover(
        IReadOnlyList<string>? sourceRoots,
        List<PluginDiagnostic> diagnostics,
        AppConfig.PluginsConfig? pluginsConfig = null,
        string? craftHome = null)
    {
        var roots = sourceRoots ?? ResolveEnvironmentRoots();
        var sources = new List<BuiltInPluginSource>();
        var seenPluginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenPluginRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            if (!Path.IsPathRooted(root))
            {
                diagnostics.Add(PluginDiagnostic.Warning(
                    "BuiltInPluginRootNotAbsolute",
                    $"Built-in plugin root '{root}' is ignored because it is not absolute.",
                    path: root));
                continue;
            }

            var fullRoot = Path.GetFullPath(root);
            if (!Directory.Exists(fullRoot))
            {
                diagnostics.Add(PluginDiagnostic.Warning(
                    "BuiltInPluginRootMissing",
                    $"Built-in plugin root '{fullRoot}' does not exist.",
                    path: fullRoot));
                continue;
            }

            foreach (var pluginRoot in ExpandRoot(fullRoot))
            {
                var fullPluginRoot = Path.GetFullPath(pluginRoot);
                if (!seenPluginRoots.Add(fullPluginRoot))
                    continue;

                var parse = PluginManifestParser.Load(fullPluginRoot);
                diagnostics.AddRange(parse.Diagnostics);
                var manifest = parse.Manifest;
                if (manifest == null)
                    continue;

                if (!seenPluginIds.Add(manifest.Id))
                {
                    diagnostics.Add(PluginDiagnostic.Warning(
                        "DuplicateBuiltInPluginId",
                        $"Built-in plugin '{manifest.Id}' was skipped because a higher-priority bundled root already provided it.",
                        manifest.Id,
                        path: manifest.ManifestPath));
                    continue;
                }

                sources.Add(new BuiltInPluginSource(manifest, fullPluginRoot, fullRoot));
            }
        }

        foreach (var registryPlugin in PluginSourceRegistryCatalog.Discover(pluginsConfig, diagnostics, craftHome))
        {
            if (!seenPluginIds.Add(registryPlugin.Manifest.Id))
            {
                diagnostics.Add(PluginDiagnostic.Warning(
                    "DuplicateBuiltInPluginId",
                    $"Marketplace plugin '{registryPlugin.Manifest.Id}' was skipped because a higher-priority source already provided it.",
                    registryPlugin.Manifest.Id,
                    path: registryPlugin.PluginRoot));
                continue;
            }

            sources.Add(registryPlugin);
        }

        return sources;
    }

    private static IReadOnlyList<string> ResolveEnvironmentRoots()
    {
        var value = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .ToArray();
    }

    private static IEnumerable<string> ExpandRoot(string root)
    {
        if (PluginManifestParser.IsValidPluginRoot(root))
        {
            yield return root;
            yield break;
        }

        foreach (var child in Directory.GetDirectories(root).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            if (PluginManifestParser.IsValidPluginRoot(child))
                yield return child;
        }
    }
}
