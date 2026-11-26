using System.Collections.Frozen;

namespace DotCraft.Runtime;

internal sealed record DotNetExtensionRuntimeOptions
{
    public TimeSpan ActivationTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Bounds a mutation's wait for cleanup, not shutdown's functional teardown.</summary>
    public TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Bounds shutdown's wait for collection after unload.</summary>
    public TimeSpan CollectionTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan CollectionPollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    public int LeakedGenerationRestartThreshold { get; init; } = 3;

    public IReadOnlySet<string> HostSharedAssemblies { get; init; } = FrozenSet<string>.Empty;

    public ModelPluginAdmission? ModelPluginAdmission { get; init; }
}
