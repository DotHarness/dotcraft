using DotCraft.Configuration;
using DotCraft.Hosting;

namespace DotCraft.Abstractions;

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
    public required DotCraftPaths Paths { get; init; }
}
