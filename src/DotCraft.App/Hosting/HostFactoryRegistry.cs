namespace DotCraft.Hosting;

/// <summary>
/// Stores the process hosts available to the official application.
/// </summary>
public sealed class HostFactoryRegistry
{
    private readonly Dictionary<string, IHostFactory> _factories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers the host factory associated with a module identifier.
    /// </summary>
    public void Register(string moduleName, IHostFactory factory) => _factories[moduleName] = factory;

    /// <summary>
    /// Gets the factory associated with a module identifier.
    /// </summary>
    public IHostFactory? Get(string moduleName) => _factories.GetValueOrDefault(moduleName);
}
