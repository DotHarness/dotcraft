using System.Text.Json.Nodes;
using DotCraft.AppServer;
using DotCraft.Common;
using DotCraft.Configuration;
using DotCraft.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Channels;

public sealed partial class ChannelRunner
{
    private bool TryRestoreOfficialChannel(ChannelEntry entry, DiscoveredPlugin? installed)
    {
        if (!IsOfficialChannel(entry) || _paths.UserData.RootPath is not { } userDataPath)
            return false;

        var seedRoots = Environment.GetEnvironmentVariable("DOTCRAFT_CHANNEL_SEEDS");
        var requested = Environment.GetEnvironmentVariable("ENABLED_CHANNELS")?.Trim() ?? "";
        var dockerRequested = !string.IsNullOrWhiteSpace(seedRoots) && Path.IsPathFullyQualified(seedRoots)
            && (requested.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? !entry.Name.Equals("weixin", StringComparison.OrdinalIgnoreCase)
                : requested.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Contains(entry.Name, StringComparer.OrdinalIgnoreCase));
        if (!entry.RestoreChannelPlugin && !dockerRequested)
            return false;

        var root = installed?.Manifest.RootPath ?? Path.Combine(userDataPath, "plugins", entry.PluginId);
        var seeded = false;
        var missingBeforeRestore = installed == null && !Directory.Exists(root);
        if (missingBeforeRestore)
        {
            if (dockerRequested)
            {
                var candidate = Path.Combine(seedRoots!, entry.PluginId);
                var manifest = PluginManifestParser.Load(candidate).Manifest;
                if (manifest?.Channel?.GetCompatibilityError(AppVersion.Informational) == null && manifest?.Channel != null)
                    seeded = BuiltInPluginDeployer.InstallChannelSeed(candidate, Path.Combine(userDataPath, "plugins"), entry.PluginId, _config.Plugins);
            }
            else
            {
                ChannelPluginInstallation.RestoreFromDefaultMarketplace(entry.PluginId, userDataPath, _config.Plugins);
            }
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
            if (AtomicConfigDocument.Key(selection, "restoreChannelPlugin") is { } pending)
                selection.Remove(pending);
            applied = true;
        });

        if (!applied)
            return false;
        entry.PluginActivationId = activationId;
        entry.RestoreChannelPlugin = false;
        UpdateChannelSelection(entry);
        if (missingBeforeRestore)
            AppServerManagedPluginRefresher.PublishPluginRevision(userDataPath);
        _sp.GetService<IAppConfigMonitor>()?.NotifyChanged("channel/restore", [ConfigChangeRegions.Plugins, ConfigChangeRegions.Channels]);
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
