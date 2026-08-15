using DotCraft.Configuration;
using DotCraft.Modules;

namespace DotCraft.Hosting;

/// <summary>
/// Selects the compiled modules composed by an official application host.
/// </summary>
public interface IModuleHostComposition
{
    /// <summary>
    /// Gets the modules whose services belong to the host service graph.
    /// </summary>
    IReadOnlyList<IDotCraftModule> GetModules(ModuleRegistry registry, AppConfig config);
}
