namespace DotCraft.Workspaces;

/// <summary>
/// Identifies the workspace and its DotCraft state directory.
/// </summary>
public sealed class WorkspacePaths
{
    /// <summary>Gets the workspace root.</summary>
    public required string WorkspacePath { get; init; }

    /// <summary>Gets the workspace-local DotCraft state directory.</summary>
    public required string CraftPath { get; init; }
}
