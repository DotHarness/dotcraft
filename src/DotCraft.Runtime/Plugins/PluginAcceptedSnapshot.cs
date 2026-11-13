using DotCraft.Plugins;

namespace DotCraft.Runtime;

/// <summary>An immutable fingerprinted copy of one admitted plugin bundle.</summary>
internal sealed record PluginAcceptedSnapshot(
    PluginManifest Manifest,
    string ContentRoot,
    string ContentFingerprint,
    string DotnetFingerprint,
    IReadOnlyList<PluginDiagnostic> PreflightDiagnostics);

/// <summary>The runtime's mutable per-plugin bookkeeping.</summary>
internal sealed class PluginRuntimeNode(
    PluginAcceptedSnapshot snapshot,
    bool enabled,
    string workspaceRoot)
{
    public PluginAcceptedSnapshot Snapshot { get; } = snapshot;

    public string WorkspaceRoot { get; } = workspaceRoot;

    public bool Enabled { get; set; } = enabled;

    public DotNetExtensionRuntimeState State { get; set; }
        = enabled ? DotNetExtensionRuntimeState.Blocked : DotNetExtensionRuntimeState.Stopped;

    public string? GenerationId { get; set; }

    public long GenerationRevision { get; set; }
    public string? ExecutionSourceGeneration { get; set; }

    public System.Text.Json.JsonElement? Settings { get; set; }

    public IReadOnlyList<PluginRuntimeBlocker> Blockers { get; set; } = [];

    public PluginGeneration? Generation { get; set; }

    public Task<PluginActivationAttempt>? PendingActivation { get; set; }

    public Task<PluginGenerationRemnant>? PendingTeardown { get; set; }

    public DotNetExtensionRuntimeState PendingTeardownCompletedState { get; set; }
        = DotNetExtensionRuntimeState.Stopped;

    public bool RetryAfterPendingTeardown { get; set; }

    public PluginGenerationRemnant? PendingRemnant { get; set; }

    public DotNetExtensionRuntimeState ReclaimCompletedState { get; set; }
        = DotNetExtensionRuntimeState.Stopped;

    public IReadOnlyList<PluginDiagnostic> Diagnostics { get; set; } = [];
}
