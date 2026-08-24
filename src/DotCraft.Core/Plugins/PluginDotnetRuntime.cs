using System.Text.Json;

namespace DotCraft.Plugins;

/// <summary>Observable lifecycle state of one structurally admitted .NET plugin.</summary>
public enum PluginDotnetRuntimeState
{
    /// <summary>Not running, and activatable when enabled.</summary>
    Stopped,

    /// <summary>Not attempted, because a Host-side precondition is unsatisfied.</summary>
    Blocked,

    /// <summary>An activation transaction is in progress.</summary>
    Activating,

    /// <summary>Activated and routing contributions.</summary>
    Active,

    /// <summary>A teardown transaction is in progress.</summary>
    Deactivating,

    /// <summary>An activation attempt was made and failed.</summary>
    Faulted,

    /// <summary>Functionally deactivated, with load-context collection still outstanding.</summary>
    Reclaiming
}

/// <summary>Host-owned explanation for a blocked or failed .NET runtime state.</summary>
public sealed record PluginRuntimeBlocker(
    string Code,
    string Message,
    IReadOnlyDictionary<string, JsonElement> Parameters);

/// <summary>Host-owned model-visible metadata for one active in-process plugin Tool.</summary>
public sealed record PluginRuntimeToolInfo(
    string Id,
    string? Namespace,
    string Name,
    string Description);

/// <summary>Immutable process-local runtime projection for one .NET plugin.</summary>
/// <param name="DependencyObservations">Declared direct dependencies projected against this runtime snapshot, or <see langword="null"/> when unavailable.</param>
public sealed record PluginDotnetRuntimeInfo(
    string PluginId,
    string Version,
    PluginDotnetRuntimeState State,
    string? GenerationId,
    IReadOnlyList<PluginRuntimeBlocker> Blockers,
    IReadOnlyList<PluginRuntimeToolInfo>? Tools = null,
    int LeakedGenerations = 0,
    bool RestartRecommended = false,
    PluginDotnetTrustStatus TrustStatus = PluginDotnetTrustStatus.Untrusted,
    IReadOnlyList<PluginDependencyObservation>? DependencyObservations = null);

/// <summary>Monotonically revised immutable .NET plugin runtime snapshot.</summary>
public sealed record PluginRuntimeSnapshot(
    long Revision,
    IReadOnlyList<PluginDotnetRuntimeInfo> Plugins,
    IReadOnlyList<PluginDiagnostic> Diagnostics);

/// <summary>Describes one semantic change to the process-local .NET plugin runtime snapshot.</summary>
public sealed class PluginRuntimeSnapshotChangedEventArgs(
    PluginRuntimeSnapshot snapshot,
    IReadOnlyList<string> pluginIds) : EventArgs
{
    /// <summary>Gets the new immutable snapshot.</summary>
    public PluginRuntimeSnapshot Snapshot { get; } = snapshot;

    /// <summary>Gets the canonical plugin ids whose runtime projection or diagnostics changed.</summary>
    public IReadOnlyList<string> PluginIds { get; } = pluginIds;
}

/// <summary>Stable outcome of one coordinated plugin runtime mutation.</summary>
public enum PluginRuntimeMutationOutcome
{
    /// <summary>The mutation ran and changed runtime state.</summary>
    Applied,

    /// <summary>The mutation ran and found nothing to change.</summary>
    NoChange,

    /// <summary>The mutation was declined because a precondition was unsatisfied.</summary>
    NotApplied
}

/// <summary>Result of a Host-coordinated .NET runtime mutation.</summary>
public sealed record PluginRuntimeMutationResult(
    PluginRuntimeMutationOutcome Outcome,
    IReadOnlyList<string> AffectedPluginIds,
    IReadOnlyList<PluginDiagnostic> Diagnostics);

/// <summary>Host lifecycle boundary consumed by AppServer without depending on the runtime implementation.</summary>
/// <remarks>Cancellation applies while waiting to enter a transition. Once admitted, lifecycle work
/// settles under Host-owned activation and cleanup deadlines.</remarks>
public interface IPluginDotnetRuntimeCoordinator
{
    /// <summary>Gets the current runtime snapshot.</summary>
    PluginRuntimeSnapshot Snapshot { get; }

    /// <summary>Occurs after the runtime publishes a semantically different snapshot.</summary>
    event EventHandler<PluginRuntimeSnapshotChangedEventArgs>? SnapshotChanged;

    /// <summary>Enables or disables one plugin and settles the resulting transitions.</summary>
    Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>Stops a plugin and its dependents so its installed bytes can be mutated.</summary>
    Task<PluginRuntimeMutationResult> QuiesceForMutationAsync(
        string pluginId,
        CancellationToken cancellationToken = default);

    /// <summary>Re-admits a mutated bundle and restores the previously stopped closure.</summary>
    Task<PluginRuntimeMutationResult> ReconcileAfterMutationAsync(
        string pluginId,
        CancellationToken cancellationToken = default);

    /// <summary>Grants trust for the plugin's currently accepted bundle and replans it. The caller cannot
    /// name a fingerprint, so a client cannot confirm one set of bytes and authorize another.</summary>
    Task<PluginRuntimeMutationResult> TrustAsync(
        string pluginId,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes a plugin's trust grant and stops it together with its consumers.</summary>
    Task<PluginRuntimeMutationResult> RevokeTrustAsync(
        string pluginId,
        CancellationToken cancellationToken = default);
}
