using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Hooks;

namespace DotCraft.Plugins;

/// <summary>
/// Loads lifecycle hook declarations contributed by enabled plugins.
/// </summary>
public static class PluginHookLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static IReadOnlyList<PluginHookSource> LoadEnabledPluginHooks(
        AppConfig config,
        string workspacePath,
        string botPath,
        IReadOnlyList<string>? builtInPluginSourceRoots,
        out IReadOnlyList<PluginDiagnostic> diagnostics)
    {
        var allDiagnostics = new List<PluginDiagnostic>();
        var discovery = new PluginDiscoveryService(builtInPluginSourceRoots: builtInPluginSourceRoots).Discover(config, workspacePath, botPath);
        allDiagnostics.AddRange(discovery.Diagnostics);

        var bundledFingerprints = BuildHostBundledFingerprints(builtInPluginSourceRoots);
        var sources = new List<PluginHookSource>();
        foreach (var plugin in discovery.Plugins)
            sources.AddRange(LoadPluginHooks(plugin, allDiagnostics, isManagedBuiltIn: IsVerifiedHostBundledPlugin(plugin, bundledFingerprints)));

        diagnostics = allDiagnostics;
        return sources;
    }

    public static IReadOnlyList<PluginHookSource> LoadPluginHooks(
        DiscoveredPlugin plugin,
        List<PluginDiagnostic> diagnostics,
        string? pluginDataPath = null,
        bool isManagedBuiltIn = false)
    {
        var manifest = plugin.Manifest;
        var hooks = manifest.Hooks;
        if (hooks?.HasAny != true)
            return [];

        var sources = new List<PluginHookSource>();
        pluginDataPath ??= string.Empty;
        foreach (var path in hooks.Paths)
        {
            AppendHookFileSource(plugin, pluginDataPath, path, isManagedBuiltIn, sources, diagnostics);
        }

        for (var i = 0; i < hooks.Inline.Count; i++)
        {
            var inline = hooks.Inline[i];
            if (inline.Hooks.Count == 0)
                continue;

            sources.Add(new PluginHookSource(
                manifest.Id,
                manifest.Interface?.DisplayName ?? manifest.DisplayName,
                manifest.RootPath,
                pluginDataPath,
                manifest.ManifestPath,
                $"plugin.json#hooks[{i}]",
                NormalizeHookConfig(inline),
                isManagedBuiltIn));
        }

        return sources;
    }

    public static IReadOnlyList<PluginHookDeclaration> BuildDeclarations(IReadOnlyList<PluginHookSource> sources)
    {
        var declarations = new List<PluginHookDeclaration>();
        foreach (var source in sources)
        {
            foreach (var (eventName, groups) in EnumerateEvents(source.Hooks))
            {
                for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
                {
                    var group = groups[groupIndex];
                    for (var hookIndex = 0; hookIndex < group.Hooks.Count; hookIndex++)
                    {
                        declarations.Add(new PluginHookDeclaration(
                            HookKeys.ForPlugin(source.PluginId, source.SourceRelativePath, eventName, groupIndex, hookIndex),
                            eventName));
                    }
                }
            }
        }

        return declarations;
    }

    private static void AppendHookFileSource(
        DiscoveredPlugin plugin,
        string pluginDataPath,
        string path,
        bool isManagedBuiltIn,
        List<PluginHookSource> sources,
        List<PluginDiagnostic> diagnostics)
    {
        var manifest = plugin.Manifest;
        HooksFileConfig? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<HooksFileConfig>(
                File.ReadAllText(path),
                JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            diagnostics.Add(PluginDiagnostic.Warning(
                "InvalidPluginHooks",
                $"Failed to read or parse plugin hooks config: {ex.Message}",
                manifest.Id,
                path: path));
            return;
        }

        if (parsed == null || parsed.Hooks.Count == 0)
            return;

        sources.Add(new PluginHookSource(
            manifest.Id,
            manifest.Interface?.DisplayName ?? manifest.DisplayName,
            manifest.RootPath,
            pluginDataPath,
            path,
            ToPluginRelativePath(manifest.RootPath, path),
            NormalizeHookConfig(parsed),
            isManagedBuiltIn));
    }

    private static Dictionary<string, string> BuildHostBundledFingerprints(IReadOnlyList<string>? builtInPluginSourceRoots)
    {
        var fingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in BuiltInPluginSourceResolver.DiscoverHostBundled(builtInPluginSourceRoots))
        {
            fingerprints[source.Manifest.Id] = BuiltInPluginDeployer.BuildFingerprint(
                source.PluginRoot,
                source.Manifest.Channel != null);
        }
        return fingerprints;
    }

    private static bool IsVerifiedHostBundledPlugin(
        DiscoveredPlugin plugin,
        IReadOnlyDictionary<string, string> bundledFingerprints)
    {
        if (plugin.SourceKind != PluginDiscoverySourceKind.UserGlobal)
            return false;
        if (!bundledFingerprints.TryGetValue(plugin.Manifest.Id, out var expectedFingerprint))
            return false;

        try
        {
            var actualFingerprint = BuiltInPluginDeployer.BuildFingerprint(
                plugin.Manifest.RootPath,
                plugin.Manifest.Channel != null);
            return string.Equals(actualFingerprint, expectedFingerprint, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static HooksFileConfig NormalizeHookConfig(HooksFileConfig config)
    {
        var normalized = new HooksFileConfig();
        foreach (var (eventName, groups) in config.Hooks)
            normalized.Hooks[eventName] = groups ?? [];
        return normalized;
    }

    private static string ToPluginRelativePath(string pluginRoot, string path)
    {
        try
        {
            return Path.GetRelativePath(pluginRoot, path).Replace('\\', '/');
        }
        catch
        {
            return Path.GetFileName(path);
        }
    }

    private static IEnumerable<KeyValuePair<string, List<HookMatcherGroup>>> EnumerateEvents(HooksFileConfig config)
    {
        foreach (var eventName in HookKeys.ValidEventNames)
        {
            if (config.Hooks.TryGetValue(eventName, out var groups) && groups.Count > 0)
                yield return new KeyValuePair<string, List<HookMatcherGroup>>(eventName, groups);
        }
    }

    public static IReadOnlyList<PluginHookSource> LoadEnabledPluginHooks(
        AppConfig config,
        DotCraft.Workspaces.DotCraftPaths paths,
        IReadOnlyList<string>? builtInPluginSourceRoots,
        out IReadOnlyList<PluginDiagnostic> diagnostics)
    {
        var allDiagnostics = new List<PluginDiagnostic>();
        var discovery = new PluginDiscoveryService(paths, builtInPluginSourceRoots)
            .Discover(config, paths.WorkspacePath, paths.Data.RootPath);
        allDiagnostics.AddRange(discovery.Diagnostics);

        var bundledFingerprints = BuildHostBundledFingerprints(builtInPluginSourceRoots);
        var sources = new List<PluginHookSource>();
        foreach (var plugin in discovery.Plugins)
        {
            sources.AddRange(LoadPluginHooks(
                plugin,
                allDiagnostics,
                PluginDataPaths.Resolve(paths, plugin.Manifest.Id),
                IsVerifiedHostBundledPlugin(plugin, bundledFingerprints)));
        }

        diagnostics = allDiagnostics;
        return sources;
    }
}
