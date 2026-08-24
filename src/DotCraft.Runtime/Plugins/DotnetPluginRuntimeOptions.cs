namespace DotCraft.Runtime;

/// <summary>Controls bounded in-process plugin lifecycle operations.</summary>
internal sealed record DotnetPluginRuntimeOptions
{
    /// <summary>Gets the construction and activation deadline.</summary>
    public TimeSpan ActivationTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets how long an ordinary mutation waits for cleanup before leaving it pending.</summary>
    /// <remarks>Shutdown still waits for functional teardown; only the outer process can impose a safe hard deadline.</remarks>
    public TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets how long shutdown waits for the generations it has just unloaded to be collected.</summary>
    public TimeSpan CollectionTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets how often the reclaim poller re-checks an outstanding generation.</summary>
    public TimeSpan CollectionPollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Gets how many uncollected generations of one plugin make a process restart worth recommending.</summary>
    public int LeakedGenerationRestartThreshold { get; init; } = 3;
}
