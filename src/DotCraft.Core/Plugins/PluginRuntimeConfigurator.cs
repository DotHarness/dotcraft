using DotCraft.Configuration;
using DotCraft.Skills;
using Microsoft.Extensions.Logging;

namespace DotCraft.Plugins;

public static class PluginRuntimeConfigurator
{
    public static PluginDiscoveryResult ConfigureSkillsLoader(
        SkillsLoader? skillsLoader,
        AppConfig config,
        string workspacePath,
        string botPath,
        PluginDiagnosticsStore? diagnosticsStore = null,
        IReadOnlyList<string>? builtInPluginSourceRoots = null,
        ILogger? logger = null)
    {
        var discovery = new PluginDiscoveryService(builtInPluginSourceRoots: builtInPluginSourceRoots)
            .DiscoverAll(config, workspacePath, botPath);
        if (skillsLoader != null)
        {
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

        diagnosticsStore?.Append(discovery.Diagnostics);
        PluginDiagnosticsLogger.Write(discovery.Diagnostics, logger);
        return discovery;
    }

    public static bool IsPluginInstalledAndEnabled(
        AppConfig config,
        string workspacePath,
        string botPath,
        string pluginId,
        IReadOnlyList<string>? builtInPluginSourceRoots = null)
    {
        var discovery = new PluginDiscoveryService(builtInPluginSourceRoots: builtInPluginSourceRoots)
            .DiscoverAll(config, workspacePath, botPath);
        return discovery.Plugins.Any(plugin =>
            plugin.Installed
            && plugin.Enabled
            && PluginIds.EqualsCanonical(plugin.Manifest.Id, pluginId));
    }

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
