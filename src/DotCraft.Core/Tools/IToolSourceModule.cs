using DotCraft.Modules;

namespace DotCraft.Tools;

/// <summary>
/// Contributes tool sources from a compiled DotCraft module.
/// </summary>
public interface IToolSourceModule : IDotCraftModule
{
    /// <summary>
    /// Gets the tool sources contributed by this module.
    /// </summary>
    /// <param name="services">The configured service provider.</param>
    IEnumerable<IToolSource> GetToolSources(IServiceProvider services);
}
