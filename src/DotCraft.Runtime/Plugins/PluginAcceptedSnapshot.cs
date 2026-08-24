using DotCraft.Plugins;

namespace DotCraft.Runtime;

/// <summary>An immutable fingerprinted copy of one admitted plugin bundle.</summary>
internal sealed record PluginAcceptedSnapshot(
    PluginManifest Manifest,
    string ContentRoot,
    string Fingerprint,
    IReadOnlyList<PluginDiagnostic> PreflightDiagnostics);

/// <summary>The runtime's mutable per-plugin bookkeeping.</summary>
internal sealed class PluginRuntimeNode(PluginAcceptedSnapshot snapshot, bool enabled)
{
    public PluginAcceptedSnapshot Snapshot { get; } = snapshot;

    public bool Enabled { get; set; } = enabled;

    public PluginDotnetRuntimeState State { get; set; }
        = enabled ? PluginDotnetRuntimeState.Blocked : PluginDotnetRuntimeState.Stopped;

    public string? GenerationId { get; set; }

    public IReadOnlyList<PluginRuntimeBlocker> Blockers { get; set; } = [];

    public PluginGeneration? Generation { get; set; }

    public Task<PluginActivationAttempt>? PendingActivation { get; set; }

    public Task<PluginGenerationRemnant>? PendingTeardown { get; set; }

    public PluginDotnetRuntimeState PendingTeardownCompletedState { get; set; }
        = PluginDotnetRuntimeState.Stopped;

    public bool RetryAfterPendingTeardown { get; set; }

    public PluginGenerationRemnant? PendingRemnant { get; set; }

    public PluginDotnetRuntimeState ReclaimCompletedState { get; set; }
        = PluginDotnetRuntimeState.Stopped;

    public IReadOnlyList<PluginDiagnostic> Diagnostics { get; set; } = [];
}
