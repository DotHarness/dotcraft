using DotCraft.Configuration;

namespace DotCraft.Runtime;

/// <summary>Defines the workspace and configuration owned by one DotCraft runtime.</summary>
public sealed class DotCraftRuntimeOptions
{
    /// <summary>Gets or sets the effective runtime configuration.</summary>
    public required AppConfig Config { get; init; }

    /// <summary>Gets or sets the application workspace.</summary>
    public required string WorkspacePath { get; init; }

    /// <summary>
    /// Gets or sets the workspace state directory. Relative values are resolved from
    /// <see cref="WorkspacePath"/> and must identify one of its direct children.
    /// </summary>
    public string DataPath { get; init; } = ".craft";

    /// <summary>
    /// Gets or sets the optional user-owned DotCraft state directory. A <see langword="null"/>
    /// value disables implicit user-level discovery and persistence.
    /// </summary>
    public string? UserDataPath { get; init; }
}
