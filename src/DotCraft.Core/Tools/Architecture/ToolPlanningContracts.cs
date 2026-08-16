using System.Collections.Frozen;

namespace DotCraft.Tools;

/// <summary>Immutable planning inputs for collecting source registrations.</summary>
public sealed class ToolPlanningContext
{
    /// <summary>Creates immutable planning inputs.</summary>
    public ToolPlanningContext(
        string threadId,
        string? turnId,
        string workspacePath,
        string dataPath,
        string mode,
        string? profile,
        IEnumerable<string>? providerCapabilities,
        long revision,
        ToolPlanningThreadKind threadKind = ToolPlanningThreadKind.Unknown,
        string? effectiveProviderId = null,
        string? effectiveMainModel = null,
        IReadOnlyList<string>? workspaceRoots = null)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            throw new ArgumentException("A thread identifier is required.", nameof(threadId));
        if (string.IsNullOrWhiteSpace(workspacePath))
            throw new ArgumentException("A workspace path is required.", nameof(workspacePath));
        if (string.IsNullOrWhiteSpace(dataPath))
            throw new ArgumentException("A data path is required.", nameof(dataPath));
        if (string.IsNullOrWhiteSpace(mode))
            throw new ArgumentException("A mode is required.", nameof(mode));
        ThreadId = threadId;
        TurnId = turnId;
        WorkspacePath = workspacePath;
        DataPath = dataPath;
        WorkspaceRoots = workspaceRoots ?? [workspacePath];
        Mode = mode;
        Profile = profile;
        ProviderCapabilities = (providerCapabilities ?? [])
            .ToFrozenSet(StringComparer.Ordinal);
        Revision = revision;
        ThreadKind = threadKind;
        EffectiveProviderId = effectiveProviderId;
        EffectiveMainModel = effectiveMainModel;
    }

    /// <summary>Gets the thread identifier.</summary>
    public string ThreadId { get; }
    /// <summary>Gets the Turn identifier when planning occurs for a Turn.</summary>
    public string? TurnId { get; }
    /// <summary>Gets the workspace root.</summary>
    public string WorkspacePath { get; }
    /// <summary>Gets the resolved workspace data directory.</summary>
    public string DataPath { get; }
    /// <summary>Gets the ordered runtime workspace boundaries.</summary>
    public IReadOnlyList<string> WorkspaceRoots { get; }
    /// <summary>Gets the agent mode.</summary>
    public string Mode { get; }
    /// <summary>Gets the optional tool profile.</summary>
    public string? Profile { get; }
    /// <summary>Gets the provider capabilities frozen for this planning operation.</summary>
    public IReadOnlySet<string> ProviderCapabilities { get; }
    /// <summary>Gets the snapshot revision to create.</summary>
    public long Revision { get; }
    /// <summary>Gets the trusted thread classification frozen for this planning operation.</summary>
    public ToolPlanningThreadKind ThreadKind { get; }
    /// <summary>Gets the provider snapshot effective for the current thread.</summary>
    public string? EffectiveProviderId { get; }
    /// <summary>Gets the MainAgent model snapshot effective for the current thread.</summary>
    public string? EffectiveMainModel { get; }
}
