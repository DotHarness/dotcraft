using System.Diagnostics;
using DotCraft.Configuration;
using Microsoft.Extensions.Logging;

namespace DotCraft.Plugins;

/// <summary>
/// Describes where a discovered plugin came from.
/// </summary>
public enum PluginDiscoverySourceKind
{
    Workspace,
    Explicit,
    UserGlobal,
    BuiltIn
}

/// <summary>
/// A discovered plugin manifest.
/// </summary>
public sealed record DiscoveredPlugin(
    PluginManifest Manifest,
    PluginDiscoverySourceKind SourceKind,
    string SourceRoot,
    bool Enabled,
    bool Installed = true,
    bool Installable = false,
    bool Removable = false,
    string? MarketplaceName = null)
{
    /// <summary>Catalog-time availability of each declared cross-plugin dependency.</summary>
    public IReadOnlyList<PluginDependencyObservation> DependencyObservations { get; init; } = [];

    public string Key { get; init; } = Manifest.Id;
}

/// <summary>
/// Result of a plugin discovery pass. <see cref="Plugins"/> holds one entry per plugin id, preferring the installed plugin.
/// </summary>
public sealed record PluginDiscoveryResult(
    IReadOnlyList<DiscoveredPlugin> Plugins,
    IReadOnlyList<PluginDiagnostic> Diagnostics)
{
    /// <summary>Catalog entries whose id <see cref="Plugins"/> already represents under another key.</summary>
    public IReadOnlyList<DiscoveredPlugin> AdditionalCatalogEntries { get; init; } = [];

    public IEnumerable<DiscoveredPlugin> CatalogEntries => Plugins.Concat(AdditionalCatalogEntries);
}

/// <summary>
/// Finds local DotCraft plugin manifests from workspace, configured, user-global, and bundled roots.
/// </summary>
public sealed class PluginDiscoveryService(
    string? userGlobalPluginsPath = null,
    IReadOnlyList<string>? builtInPluginSourceRoots = null,
    string? craftHome = null,
    ILogger? logger = null)
{
    public PluginDiscoveryService(
        DotCraft.Workspaces.DotCraftPaths paths,
        IReadOnlyList<string>? builtInPluginSourceRoots = null,
        ILogger? logger = null)
        : this(
            paths.UserData.ResolveOrNull("plugins"),
            builtInPluginSourceRoots,
            paths.UserData.RootPath,
            logger)
    {
    }

    /// <summary>
    /// Discovers enabled local plugin manifests for the current workspace.
    /// </summary>
    public PluginDiscoveryResult Discover(AppConfig config, string workspacePath, string botPath)
    {
        var result = DiscoverAll(config, workspacePath, botPath);
        return new PluginDiscoveryResult(
            result.Plugins.Where(plugin => plugin.Installed && plugin.Enabled).ToArray(),
            result.Diagnostics);
    }

    /// <summary>
    /// Discovers all local plugin manifests for the current workspace, including disabled plugins.
    /// </summary>
    public PluginDiscoveryResult DiscoverAll(AppConfig config, string workspacePath, string botPath)
    {
        var timer = Stopwatch.StartNew();
        var diagnostics = new List<PluginDiagnostic>();
        var candidates = EnumerateCandidates(config, workspacePath, botPath, diagnostics);
        var discovered = DiscoverCandidates(config, candidates, diagnostics);
        var byId = discovered.ToDictionary(static plugin => plugin.Manifest.Id, StringComparer.OrdinalIgnoreCase);
        var additional = new List<DiscoveredPlugin>();

        LogStage("plugin-local-manifests");
        var builtInDiscovery = new BuiltInPluginCatalog(builtInPluginSourceRoots, config.Plugins, craftHome).Discover();
        diagnostics.AddRange(builtInDiscovery.Diagnostics);
        foreach (var entry in builtInDiscovery.Plugins)
        {
            if (!byId.TryGetValue(entry.Manifest.Id, out var existing))
            {
                byId[entry.Manifest.Id] = entry;
                discovered.Add(entry);
            }
            else if (!existing.Installed)
            {
                additional.Add(entry);
            }
            else if (!PluginIds.KeysEqual(existing.Key, entry.Key))
            {
                additional.Add(entry with { Installable = false });
            }
        }

        LogStage("plugin-built-in-catalog");
        var projected = PluginDependencyCatalogProjection.Attach([.. discovered, .. additional]);
        var result = new PluginDiscoveryResult(
            projected.Take(discovered.Count).ToArray(),
            PluginDiagnostic.Deduplicate(diagnostics))
        {
            AdditionalCatalogEntries = projected.Skip(discovered.Count).ToArray()
        };
        LogStage("plugin-dependency-projection");
        return result;

        void LogStage(string stage)
        {
            logger?.LogInformation("Startup stage {Stage} completed in {ElapsedMs}ms",
                stage, timer.Elapsed.TotalMilliseconds);
            timer.Restart();
        }
    }

    public PluginDiscoveryResult DiscoverDirectory(string directory, AppConfig config)
    {
        var diagnostics = new List<PluginDiagnostic>();
        var root = Path.GetFullPath(directory);
        if (!Directory.Exists(root))
        {
            diagnostics.Add(PluginDiagnostic.Info("PluginRootMissing",
                $"Plugin root '{root}' does not exist.", path: root));
            return new PluginDiscoveryResult([], diagnostics);
        }

        var candidates = ExpandRoot(root)
            .Select(pluginRoot => new PluginCandidate(pluginRoot, PluginDiscoverySourceKind.Explicit, root));
        return new PluginDiscoveryResult(
            PluginDependencyCatalogProjection.Attach(DiscoverCandidates(config, candidates, diagnostics)),
            PluginDiagnostic.Deduplicate(diagnostics));
    }

    private static List<DiscoveredPlugin> DiscoverCandidates(
        AppConfig config, IEnumerable<PluginCandidate> candidates, List<PluginDiagnostic> diagnostics)
    {
        var discovered = new List<DiscoveredPlugin>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var parse = PluginManifestParser.Load(candidate.PluginRoot);
            diagnostics.AddRange(parse.Diagnostics);
            var manifest = parse.Manifest;
            if (manifest == null)
                continue;

            if (!seen.Add(manifest.Id))
            {
                diagnostics.Add(PluginDiagnostic.Warning(
                    "DuplicatePluginId",
                    $"Plugin '{manifest.Id}' was skipped because a higher-priority root already provided it.",
                    manifest.Id,
                    path: manifest.ManifestPath));
                continue;
            }

            var enabled = config.Plugins.IsPluginEnabled(manifest.Id);
            if (!enabled)
            {
                diagnostics.Add(PluginDiagnostic.Info(
                    "PluginDisabled",
                    $"Plugin '{manifest.Id}' is disabled by configuration.",
                    manifest.Id,
                    path: manifest.ManifestPath));
            }

            var removable = candidate.SourceKind is PluginDiscoverySourceKind.Workspace or PluginDiscoverySourceKind.UserGlobal
                            && IsStrictPathWithinDirectory(manifest.RootPath, candidate.SourceRoot);
            var marker = candidate.SourceKind == PluginDiscoverySourceKind.UserGlobal
                ? ManagedPluginMarker.TryRead(manifest.RootPath)
                : null;
            discovered.Add(new DiscoveredPlugin(
                manifest,
                candidate.SourceKind,
                candidate.SourceRoot,
                enabled,
                Installed: true,
                Installable: false,
                Removable: removable,
                MarketplaceName: marker?.MarketplaceName)
            {
                Key = marker == null ? manifest.Id : PluginIds.Key(manifest.Id, marker.MarketplaceName)
            });
        }

        return discovered;
    }

    private IReadOnlyList<PluginCandidate> EnumerateCandidates(
        AppConfig config,
        string workspacePath,
        string botPath,
        List<PluginDiagnostic> diagnostics)
    {
        var roots = new List<(string Path, PluginDiscoverySourceKind SourceKind)>
        {
            (Path.Combine(botPath, "plugins"), PluginDiscoverySourceKind.Workspace)
        };

        foreach (var explicitRoot in config.Plugins.PluginRoots)
        {
            if (string.IsNullOrWhiteSpace(explicitRoot))
                continue;

            var resolved = Path.IsPathRooted(explicitRoot)
                ? explicitRoot
                : Path.Combine(workspacePath, explicitRoot);
            roots.Add((resolved, PluginDiscoverySourceKind.Explicit));
        }

        if (!string.IsNullOrWhiteSpace(userGlobalPluginsPath))
            roots.Add((userGlobalPluginsPath, PluginDiscoverySourceKind.UserGlobal));

        var candidates = new List<PluginCandidate>();
        var candidateRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (root, sourceKind) in roots)
        {
            var fullRoot = Path.GetFullPath(root);
            if (!Directory.Exists(fullRoot))
            {
                diagnostics.Add(PluginDiagnostic.Info(
                    "PluginRootMissing",
                    $"Plugin root '{fullRoot}' does not exist.",
                    path: fullRoot));
                continue;
            }

            foreach (var pluginRoot in ExpandRoot(fullRoot))
            {
                var fullPluginRoot = Path.GetFullPath(pluginRoot);
                if (!candidateRoots.Add(fullPluginRoot))
                    continue;

                candidates.Add(new PluginCandidate(fullPluginRoot, sourceKind, fullRoot));
            }
        }

        return candidates;
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

    private static bool IsStrictPathWithinDirectory(string path, string root)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return !string.IsNullOrWhiteSpace(relative)
               && !relative.Equals(".", StringComparison.Ordinal)
               && !Path.IsPathRooted(relative)
               && !relative.Equals("..", StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private sealed record PluginCandidate(
        string PluginRoot,
        PluginDiscoverySourceKind SourceKind,
        string SourceRoot);
}
