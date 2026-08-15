namespace DotCraft.Harness;

/// <summary>Defines the paths owned by one in-process DotCraft Harness.</summary>
public sealed class DotCraftHarnessOptions
{
    /// <summary>Gets or sets the application workspace.</summary>
    public string WorkspacePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the workspace state directory. Relative values are resolved from
    /// <see cref="WorkspacePath"/> and must identify one of its direct children.
    /// </summary>
    public string DataPath { get; set; } = ".craft";

    /// <summary>
    /// Gets or sets the optional user-owned DotCraft state directory. A <see langword="null"/>
    /// value disables implicit user-level discovery and persistence.
    /// </summary>
    public string? UserDataPath { get; set; }
}
