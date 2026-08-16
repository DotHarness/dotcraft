using DotCraft.Configuration;
using DotCraft.Skills;
using Microsoft.Extensions.Logging;

namespace DotCraft.Plugins;

public static class PluginRuntimeConfigurator
{
    public static PluginDiscoveryResult ConfigureSkillsLoader(
        SkillsLoader? skillsLoader,
        AppConfig config,
        DotCraft.Workspaces.DotCraftPaths paths,
        PluginDiagnosticsStore? diagnosticsStore = null,
        IReadOnlyList<string>? builtInPluginSourceRoots = null,
        ILogger? logger = null)
    {
        var discovery = new PluginDiscoveryService(paths, builtInPluginSourceRoots)
            .DiscoverAll(config, paths.WorkspacePath, paths.Data.RootPath);
        ConfigureSkills(skillsLoader, config, discovery);
        diagnosticsStore?.Append(discovery.Diagnostics);
        PluginDiagnosticsLogger.Write(discovery.Diagnostics, logger);
        return discovery;
    }

    public static PluginDiscoveryResult ConfigureSkillsLoader(
        SkillsLoader? skillsLoader,
        AppConfig config,
        string workspacePath,
        string botPath,
        PluginDiagnosticsStore? diagnosticsStore = null,
        IReadOnlyList<string>? builtInPluginSourceRoots = null,
        ILogger? logger = null)
    {
        var userDataPath = ResolveUserDataPath(config);
        var discovery = new PluginDiscoveryService(
                userDataPath == null ? null : Path.Combine(userDataPath, "plugins"),
                builtInPluginSourceRoots,
                userDataPath)
            .DiscoverAll(config, workspacePath, botPath);
        ConfigureSkills(skillsLoader, config, discovery);

        diagnosticsStore?.Append(discovery.Diagnostics);
        PluginDiagnosticsLogger.Write(discovery.Diagnostics, logger);
        return discovery;
    }

    private static void ConfigureSkills(
        SkillsLoader? skillsLoader,
        AppConfig config,
        PluginDiscoveryResult discovery)
    {
        if (skillsLoader == null)
            return;

        var sources = discovery.Plugins
            .Where(plugin => plugin.Installed && plugin.Enabled && !string.IsNullOrWhiteSpace(plugin.Manifest.SkillsPath))
            .Select(plugin => new SkillsLoader.PluginSkillSource(
                plugin.Manifest.Id,
                plugin.Manifest.Interface?.DisplayName ?? plugin.Manifest.DisplayName,
                plugin.Manifest.SkillsPath!))
            .ToArray();
        var disabledSkillNames = ResolveDisabledPluginSkillNames(config, discovery.Plugins);
        skillsLoader.SetPluginSkillSources(sources, disabledSkillNames);
    }

    public static bool IsPluginInstalledAndEnabled(
        AppConfig config,
        string workspacePath,
        string botPath,
        string pluginId,
        IReadOnlyList<string>? builtInPluginSourceRoots = null)
    {
        var userDataPath = ResolveUserDataPath(config);
        var discovery = new PluginDiscoveryService(
                userDataPath == null ? null : Path.Combine(userDataPath, "plugins"),
                builtInPluginSourceRoots,
                userDataPath)
            .DiscoverAll(config, workspacePath, botPath);
        return discovery.Plugins.Any(plugin =>
            plugin.Installed
            && plugin.Enabled
            && PluginIds.EqualsCanonical(plugin.Manifest.Id, pluginId));
    }

    private static string? ResolveUserDataPath(AppConfig config) =>
        string.IsNullOrWhiteSpace(config.GlobalConfigPath)
            ? null
            : Path.GetDirectoryName(Path.GetFullPath(config.GlobalConfigPath));

    private static IReadOnlyList<string> ResolveDisabledPluginSkillNames(
        AppConfig config,
        IReadOnlyList<DiscoveredPlugin> plugins)
    {
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in plugins.Where(plugin => !plugin.Enabled))
        {
            AddSkillDirectoryNames(disabled, plugin.Manifest.SkillsPath);
        }

        if (!config.Plugins.IsPluginEnabled(PluginIds.Browser, defaultEnabled: true))
            disabled.Add("browser");

        return disabled.ToArray();
    }

    private static void AddSkillDirectoryNames(HashSet<string> names, string? skillsPath)
    {
        if (string.IsNullOrWhiteSpace(skillsPath) || !Directory.Exists(skillsPath))
            return;

        foreach (var dir in Directory.GetDirectories(skillsPath))
        {
            if (File.Exists(Path.Combine(dir, "SKILL.md")))
                names.Add(Path.GetFileName(dir));
        }
    }
}
