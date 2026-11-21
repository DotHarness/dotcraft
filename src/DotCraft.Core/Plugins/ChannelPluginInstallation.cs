using DotCraft.Configuration;

namespace DotCraft.Plugins;

public static class ChannelPluginInstallation
{
    internal const string FileName = ".channel-installation";

    public static string? ReadId(string pluginRoot)
    {
        var managed = ManagedPluginMarker.TryRead(pluginRoot);
        if (managed != null)
            return NormalizeId(managed.ChannelInstallationId);
        var path = Path.Combine(pluginRoot, FileName);
        return File.Exists(path) ? NormalizeId(File.ReadAllText(path)) : null;
    }

    public static string GetOrCreateLocalId(string pluginRoot)
    {
        if (ReadId(pluginRoot) is { } existing)
            return existing;
        if (BuiltInPluginDeployer.IsManagedBuiltInPluginRoot(pluginRoot))
            throw new InvalidOperationException("Managed channel plugin is missing its installation identity.");

        var path = Path.Combine(pluginRoot, FileName);
        var id = Guid.NewGuid().ToString("N");
        AtomicConfigDocument.WithLock(path, () =>
        {
            if (ReadId(pluginRoot) is { } current)
                id = current;
            else
                AtomicConfigDocument.Write(path, id);
        });
        return id;
    }

    public static bool IsFromDefaultMarketplace(string pluginRoot, AppConfig.PluginsConfig config)
    {
        var marker = ManagedPluginMarker.TryRead(pluginRoot);
        var source = PluginSourceRegistryCatalog.ArchiveSourcesFor(config).FirstOrDefault(s => s.IsDefault);
        return marker?.SourceIdentity != null && source?.CanonicalSourceIdentity() == marker.SourceIdentity;
    }

    public static IReadOnlyList<PluginDiagnostic> RestoreFromDefaultMarketplace(
        string pluginId,
        string userDataPath,
        AppConfig.PluginsConfig config)
    {
        var source = PluginSourceRegistryCatalog.ArchiveSourcesFor(config).FirstOrDefault(s => s.IsDefault);
        if (source == null)
            return [];
        var diagnostics = new List<PluginDiagnostic>();
        var candidate = PluginSourceRegistryCatalog.Discover(config, diagnostics, userDataPath)
            .FirstOrDefault(p => p.Manifest.Channel != null && PluginIds.EqualsCanonical(p.Manifest.Id, pluginId)
                && p.SourceIdentity == source.CanonicalSourceIdentity());
        if (candidate == null)
            return diagnostics;

        return new BuiltInPluginDeployer(Path.Combine(userDataPath, "plugins"), [], config, userDataPath)
            .DeployPlugin(candidate.Key);
    }

    private static string? NormalizeId(string? value) =>
        Guid.TryParseExact(value?.Trim(), "N", out var id) ? id.ToString("N") : null;
}
