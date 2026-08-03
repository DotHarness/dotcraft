using DotCraft.Configuration;
using PluginDiagnostic = DotCraft.Plugins.PluginDiagnostic;

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
    string? MarketplaceName = null);

/// <summary>
/// Result of a plugin discovery pass.
/// </summary>
public sealed record PluginDiscoveryResult(
    IReadOnlyList<DiscoveredPlugin> Plugins,
    IReadOnlyList<PluginDiagnostic> Diagnostics);

/// <summary>
/// Finds local DotCraft plugin manifests from workspace, configured, user-global, and bundled roots.
/// </summary>
public sealed class PluginDiscoveryService(
    string? userGlobalPluginsPath = null,
    IReadOnlyList<string>? builtInPluginSourceRoots = null,
    string? craftHome = null)
{
    private const string RemovedBrowserUsePluginId = "browser-use";

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
        var diagnostics = new List<PluginDiagnostic>();
        RefreshManagedBuiltInPlugins(config, Path.Combine(botPath, "plugins"), diagnostics);
        var candidates = EnumerateCandidates(config, workspacePath, botPath, diagnostics);
        var discovered = new List<DiscoveredPlugin>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            var parse = PluginManifestParser.Load(candidate.PluginRoot);
            diagnostics.AddRange(parse.Diagnostics);
            var manifest = parse.Manifest;
            if (manifest == null)
                continue;

            if (IsRemovedPluginId(manifest.Id))
            {
                diagnostics.Add(PluginDiagnostic.Info(
                    "RemovedPluginIgnored",
                    "Removed Browser plugin id is ignored; install the built-in 'browser' plugin instead.",
                    manifest.Id,
                    path: manifest.ManifestPath));
                continue;
            }

            if (!seen.Add(manifest.Id))
            {
                diagnostics.Add(PluginDiagnostic.Warning(
                    "DuplicatePluginId",
                    $"Plugin '{manifest.Id}' was skipped because a higher-priority root already provided it.",
                    manifest.Id,
                    path: manifest.ManifestPath));
                continue;
            }

            var enabled = config.Plugins.IsPluginEnabled(manifest.Id, defaultEnabled: true);
            if (!enabled)
            {
                diagnostics.Add(PluginDiagnostic.Info(
                    "PluginDisabled",
                    $"Plugin '{manifest.Id}' is disabled by configuration.",
                    manifest.Id,
                    path: manifest.ManifestPath));
            }

            var removable = candidate.SourceKind == PluginDiscoverySourceKind.Workspace
                            && IsStrictPathWithinDirectory(manifest.RootPath, candidate.SourceRoot);
            discovered.Add(new DiscoveredPlugin(
                manifest,
                candidate.SourceKind,
                candidate.SourceRoot,
                enabled,
                Installed: true,
                Installable: false,
                Removable: removable));
        }

        var builtInDiscovery = new BuiltInPluginCatalog(builtInPluginSourceRoots, config.Plugins, craftHome).Discover();
        diagnostics.AddRange(builtInDiscovery.Diagnostics);
        foreach (var builtIn in builtInDiscovery.Plugins)
        {
            if (!seen.Add(builtIn.Manifest.Id))
                continue;

            discovered.Add(builtIn);
        }

        return new PluginDiscoveryResult(discovered, diagnostics);
    }

    private void RefreshManagedBuiltInPlugins(
        AppConfig config,
        string workspacePluginsRoot,
        List<PluginDiagnostic> diagnostics)
    {
        if (!Directory.Exists(workspacePluginsRoot))
            return;

        foreach (var pluginRoot in Directory.GetDirectories(workspacePluginsRoot))
        {
            if (!BuiltInPluginDeployer.IsManagedBuiltInPluginRoot(pluginRoot))
                continue;

            var parse = PluginManifestParser.Load(pluginRoot);
            diagnostics.AddRange(parse.Diagnostics);
            if (parse.Manifest == null)
                continue;
            if (IsRemovedPluginId(parse.Manifest.Id))
                continue;

            diagnostics.AddRange(new BuiltInPluginDeployer(workspacePluginsRoot, builtInPluginSourceRoots, config.Plugins).DeployPlugin(parse.Manifest.Id));
        }
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

        roots.Add((
            userGlobalPluginsPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".craft",
                "plugins"),
            PluginDiscoverySourceKind.UserGlobal));

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

    private static bool IsRemovedPluginId(string pluginId) =>
        string.Equals(pluginId, RemovedBrowserUsePluginId, StringComparison.OrdinalIgnoreCase);

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
