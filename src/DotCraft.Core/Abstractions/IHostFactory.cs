using DotCraft.Hosting;

namespace DotCraft.Abstractions;

/// <summary>
/// Factory for creating DotCraft hosts.
/// Each module can provide a host factory to create its specific host implementation.
/// </summary>
public interface IHostFactory
{
    /// <summary>
    /// Creates a host instance.
    /// </summary>
    /// <param name="serviceProvider">The service provider for resolving dependencies.</param>
    /// <param name="context">The module context containing configuration and paths.</param>
    /// <returns>A host instance.</returns>
    IDotCraftHost CreateHost(IServiceProvider serviceProvider, ModuleContext context);
}
