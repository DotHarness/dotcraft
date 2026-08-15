using DotCraft.Configuration;

namespace DotCraft.Runtime;

/// <summary>Defines the workspace and configuration owned by one DotCraft runtime.</summary>
public sealed class DotCraftRuntimeOptions
{
    /// <summary>Gets or sets the effective runtime configuration.</summary>
    public required AppConfig Config { get; init; }

    /// <summary>Gets or sets the application workspace.</summary>
    public required string WorkspacePath { get; init; }

    /// <summary>Gets or sets the workspace state directory.</summary>
    public required string CraftPath { get; init; }
}
