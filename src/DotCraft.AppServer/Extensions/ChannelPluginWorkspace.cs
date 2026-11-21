using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Workspaces;

namespace DotCraft.AppServer;

public sealed partial class ChannelPluginWorkspace(
    DotCraftPaths paths,
    AppConfig config,
    string hostVersion,
    Func<bool>? webSocketEnabled = null,
    Func<IReadOnlyList<ChannelEntry>>? readSelections = null)
{
    private readonly DotCraftPaths _paths = paths;

    public IReadOnlyList<DiscoveredPlugin> Discover() => new PluginDiscoveryService(_paths)
        .DiscoverAll(config, _paths.WorkspacePath, _paths.Data.RootPath).Plugins
        .Where(p => p.Installed && p.Manifest.Channel != null).ToArray();

    public string? GetAdmissionFailure(DiscoveredPlugin? plugin, ChannelEntry entry)
    {
        if (plugin?.Manifest.Channel is not { } module)
            return "ChannelPluginNotInstalled";
        if (!plugin.Enabled)
            return "ChannelPluginDisabled";
        if (!string.Equals(module.ChannelName, entry.Name, StringComparison.OrdinalIgnoreCase))
            return "ChannelPluginInvalidSelection";
        return module.GetCompatibilityError(hostVersion);
    }

    public void Authorize(ChannelEntry entry)
    {
        var installed = Discover();
        var plugin = Find(installed, entry.PluginId);
        if (plugin?.Manifest.Channel is not { } module)
            throw AppServerErrors.ChannelNotFound(entry.PluginId);
        if (!string.Equals(module.ChannelName, entry.Name, StringComparison.OrdinalIgnoreCase))
            throw AppServerErrors.ChannelValidationFailed("ChannelPluginInvalidSelection");
        if (!entry.Enabled)
            return;
        if (GetAdmissionFailure(plugin, entry) is { } failure)
            throw AppServerErrors.ChannelValidationFailed(failure);
        if (HasDescriptorConflict(installed, plugin))
            throw AppServerErrors.ChannelValidationFailed("ChannelPluginConflictingDescriptor");
        if (ReadSnapshot(module).MissingRequiredFields.Count > 0)
            throw AppServerErrors.ChannelValidationFailed("ChannelPluginConfigMissing");
        if (webSocketEnabled?.Invoke() == false)
            throw AppServerErrors.ChannelValidationFailed("ChannelPluginWebSocketRequired");
        EnsureConfigFile(module);
        try
        {
            entry.PluginActivationId = ChannelPluginInstallation.GetOrCreateLocalId(plugin.Manifest.RootPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw AppServerErrors.ChannelValidationFailed("ChannelPluginInstallationUnavailable");
        }
    }

    public static DiscoveredPlugin? Find(IEnumerable<DiscoveredPlugin> installed, string pluginId) =>
        installed.FirstOrDefault(p => PluginIds.EqualsCanonical(p.Manifest.Id, pluginId));

    public bool HasDescriptorConflict(IEnumerable<DiscoveredPlugin> installed, DiscoveredPlugin plugin)
    {
        var module = plugin.Manifest.Channel!;
        var conflicts = installed.Where(p => p.Installed && !PluginIds.EqualsCanonical(p.Manifest.Id, plugin.Manifest.Id)
            && p.Manifest.Channel is { } other
            && (string.Equals(other.ModuleId, module.ModuleId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(other.ConfigFileName, module.ConfigFileName, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (conflicts.Length == 0)
            return false;

        var selections = readSelections?.Invoke() ?? config.Channels.ToArray();
        return !HasActiveSelection(plugin) || conflicts.Any(HasActiveSelection);

        bool HasActiveSelection(DiscoveredPlugin selected) => selections.Any(e => e.Enabled && e.PluginActivationId != null
            && PluginIds.EqualsCanonical(e.PluginId, selected.Manifest.Id));
    }
}
