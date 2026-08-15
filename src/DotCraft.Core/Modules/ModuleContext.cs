using DotCraft.Workspaces;
using DotCraft.Configuration;

namespace DotCraft.Modules;

/// <summary>
/// Provides context information for module initialization and service configuration.
/// </summary>
public sealed class ModuleContext
{
    /// <summary>
    /// The application configuration.
    /// </summary>
    public required AppConfig Config { get; init; }

    /// <summary>
    /// The workspace and bot paths.
    /// </summary>
    public required WorkspacePaths Paths { get; init; }
}
