using System.Text.Json.Nodes;
using DotCraft.AppServer;
using DotCraft.Common;
using DotCraft.Configuration;
using DotCraft.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Channels;

public sealed partial class ChannelRunner
{
    private bool TryProvisionDockerChannel(ChannelEntry entry, DiscoveredPlugin? installed)
    {
        var seedRoot = Environment.GetEnvironmentVariable("DOTCRAFT_CHANNEL_SEEDS");
        if (!string.Equals(Environment.GetEnvironmentVariable("DOTCRAFT_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(seedRoot) || !Path.IsPathFullyQualified(seedRoot)
            || !IsOfficialChannel(entry) || _paths.UserData.RootPath is not { } userDataPath)
            return false;

        var requested = Environment.GetEnvironmentVariable("ENABLED_CHANNELS")?.Trim() ?? "";
        var dockerRequested = requested.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? !entry.Name.Equals("weixin", StringComparison.OrdinalIgnoreCase)
            : requested.Split(',', StringSplitOptions.TrimEntries)
                .Contains(entry.Name, StringComparer.OrdinalIgnoreCase);
        if (!dockerRequested)
            return false;

        var pluginsRoot = Path.Combine(userDataPath, "plugins");
        var root = installed?.Manifest.RootPath ?? Path.Combine(pluginsRoot, entry.PluginId);
        var seeded = false;
        if (installed == null && !Directory.Exists(root))
        {
            var candidate = Path.Combine(seedRoot, entry.PluginId);
            if (PluginManifestParser.Load(candidate).Manifest?.Channel is { } module
                && module.GetCompatibilityError(AppVersion.Informational) == null)
                seeded = BuiltInPluginDeployer.InstallChannelSeed(candidate, pluginsRoot, entry.PluginId, _config.Plugins);
        }

        if ((!seeded && !ChannelPluginInstallation.IsFromDefaultMarketplace(root, _config.Plugins))
            || ChannelPluginInstallation.ReadId(root) is not { } activationId)
            return false;

        var applied = false;
        AtomicConfigDocument.Update(_paths.Data.Resolve("config.json"), config =>
        {
            if (AtomicConfigDocument.Value(config, "Channels") is not JsonObject channels
                || AtomicConfigDocument.Value(channels, entry.Name) is not JsonObject selection
                || AtomicConfigDocument.Value(selection, "pluginId") is not JsonValue selected
                || !selected.TryGetValue<string>(out var pluginId)
                || !PluginIds.EqualsCanonical(pluginId, entry.PluginId)
                || AtomicConfigDocument.Value(selection, "pluginActivationId") != null
                || AtomicConfigDocument.Value(selection, "enabled") is not JsonValue enabled
                || !enabled.TryGetValue<bool>(out var isEnabled) || !isEnabled)
                return;

            selection["pluginActivationId"] = activationId;
            applied = true;
        });

        if (!applied)
            return false;
        entry.PluginActivationId = activationId;
        UpdateChannelSelection(entry);
        if (seeded)
            AppServerManagedPluginRefresher.PublishPluginRevision(userDataPath);
        _sp.GetService<IAppConfigMonitor>()?.NotifyChanged("channel/provision", [ConfigChangeRegions.Plugins, ConfigChangeRegions.Channels]);
        Interlocked.Increment(ref _pluginConfigRevision);
        return true;
    }

    private static bool IsOfficialChannel(ChannelEntry entry)
    {
        var name = entry.Name.ToLowerInvariant();
        return name is "telegram" or "weixin" or "qq" or "wecom" or "feishu"
            && string.Equals(entry.PluginId, "channel-" + name, StringComparison.Ordinal);
    }
}
