using DotCraft.Configuration;

namespace DotCraft.Runtime;

public sealed class DotCraftRuntimeOptions
{
    public required AppConfig Config { get; init; }

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

    /// <summary>Gets or sets extra assembly simple names that plugins resolve to the Host's instances.</summary>
    public IReadOnlyList<string> PluginSharedAssemblies { get; init; } = [];
}
