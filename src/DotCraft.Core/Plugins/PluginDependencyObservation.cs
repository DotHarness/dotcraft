namespace DotCraft.Plugins;

/// <summary>Observable availability of one declared plugin dependency.</summary>
public enum PluginDependencyAvailability
{
    /// <summary>No plugin with the required identifier is installed.</summary>
    Missing,

    /// <summary>The provider has no canonical version or is outside the required compatible minimum range.</summary>
    VersionUnsatisfied,

    /// <summary>The provider is installed and satisfies the minimum, but is not enabled.</summary>
    Disabled,

    /// <summary>The provider is admissible but has no observed runtime state yet.</summary>
    Unavailable,

    /// <summary>The provider cannot be activated, and the Host knows why without running it.</summary>
    Blocked,

    /// <summary>The provider is being activated.</summary>
    Activating,

    /// <summary>The provider is active and its exports are resolvable.</summary>
    Active,

    /// <summary>The provider is being deactivated.</summary>
    Deactivating,

    /// <summary>An activation attempt was made for the provider and it failed.</summary>
    Faulted,

    /// <summary>The provider is functionally stopped and a previous generation's memory has not yet been reclaimed.</summary>
    Reclaiming
}

/// <summary>Static or runtime observation of one declared direct dependency.</summary>
public sealed record PluginDependencyObservation(
    string Id,
    string RequiredVersion,
    string? ObservedVersion,
    PluginDependencyAvailability Availability);

internal static class PluginDependencyCatalogProjection
{
    public static IReadOnlyList<DiscoveredPlugin> Attach(IReadOnlyList<DiscoveredPlugin> plugins)
    {
        var installed = plugins
            .Where(static plugin => plugin.Installed)
            .ToDictionary(static plugin => plugin.Manifest.Id, StringComparer.OrdinalIgnoreCase);

        return plugins
            .Select(plugin => plugin with
            {
                DependencyObservations = Build(plugin, installed)
            })
            .ToArray();
    }

    private static IReadOnlyList<PluginDependencyObservation> Build(
        DiscoveredPlugin consumer,
        IReadOnlyDictionary<string, DiscoveredPlugin> installed)
    {
        if (consumer.Manifest.Dotnet == null)
            return [];

        return consumer.Manifest.Dependencies
            .OrderBy(static dependency => dependency.Key, StringComparer.Ordinal)
            .Select(dependency => Observe(dependency.Key, dependency.Value, installed))
            .ToArray();
    }

    private static PluginDependencyObservation Observe(
        string providerId,
        string requiredVersion,
        IReadOnlyDictionary<string, DiscoveredPlugin> installed)
    {
        if (!installed.TryGetValue(providerId, out var provider))
        {
            return new PluginDependencyObservation(
                providerId,
                requiredVersion,
                null,
                PluginDependencyAvailability.Missing);
        }

        var observedVersion = PluginDotnetManifestAdmission.IsCanonicalVersion(provider.Manifest.Version)
            ? provider.Manifest.Version
            : null;
        if (!PluginDotnetManifestAdmission.SatisfiesMinimum(requiredVersion, observedVersion))
        {
            return new PluginDependencyObservation(
                providerId,
                requiredVersion,
                observedVersion,
                PluginDependencyAvailability.VersionUnsatisfied);
        }

        if (!provider.Enabled)
        {
            return new PluginDependencyObservation(
                providerId,
                requiredVersion,
                observedVersion,
                PluginDependencyAvailability.Disabled);
        }

        return new PluginDependencyObservation(
            providerId,
            requiredVersion,
            observedVersion,
            PluginDependencyAvailability.Unavailable);
    }
}
