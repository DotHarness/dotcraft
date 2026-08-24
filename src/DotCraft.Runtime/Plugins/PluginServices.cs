using System.Reflection;
using DotCraft.Plugins;

namespace DotCraft.Runtime;

/// <summary>Holds the typed services one provider plugin exports to its declared consumers.</summary>
internal sealed class PluginServiceExportRegistry(
    string providerPluginId,
    IReadOnlySet<Assembly> exportedApiAssemblies) : IPluginServiceExportRegistrar
{
    private readonly Dictionary<Type, object> _services = [];
    private bool _open = true;

    public void Add<TContract>(TContract service) where TContract : class
    {
        ArgumentNullException.ThrowIfNull(service);
        EnsureOpen();
        var contract = typeof(TContract);
        if (!contract.IsInterface || !contract.IsPublic)
            throw new InvalidOperationException("A plugin service contract must be a public interface.");
        if (!exportedApiAssemblies.Contains(contract.Assembly))
        {
            throw new InvalidOperationException(
                $"Service contract '{contract.FullName}' is not defined by an API assembly exported by '{providerPluginId}'.");
        }
        if (!_services.TryAdd(contract, service))
            throw new InvalidOperationException($"Service contract '{contract.FullName}' is already exported.");
    }

    public void Seal() => _open = false;

    /// <summary>Verifies that every disposable exported service is owned by the generation lifetime.</summary>
    public void ValidateOwnership(object entryInstance, PluginLifetime lifetime)
    {
        foreach (var service in _services.Values.Distinct(ReferenceEqualityComparer.Instance))
        {
            if ((service is IDisposable || service is IAsyncDisposable)
                && !ReferenceEquals(service, entryInstance)
                && !lifetime.Owns(service))
            {
                throw new InvalidOperationException(
                    "A disposable exported service must be registered with the generation lifetime.");
            }
        }
    }

    public object GetRequired(Type contract)
    {
        if (!_services.TryGetValue(contract, out var service))
        {
            throw new InvalidOperationException(
                $"Provider '{providerPluginId}' does not export service contract '{contract.FullName}'.");
        }
        return service;
    }

    public void Clear() => _services.Clear();

    private void EnsureOpen()
    {
        if (!_open)
            throw new InvalidOperationException("Plugin service export registration is closed.");
    }
}

/// <summary>Resolves typed exports from the direct providers of one consumer plugin.</summary>
internal sealed class PluginDependencyResolver(
    string consumerPluginId,
    IReadOnlyDictionary<string, PluginGeneration> directProviders) : IPluginDependencyResolver
{
    private bool _open = true;

    public TContract GetRequired<TContract>(string providerPluginId) where TContract : class
    {
        if (!_open)
            throw new InvalidOperationException("Plugin dependency lookup is closed.");
        if (!directProviders.TryGetValue(providerPluginId, out var provider))
        {
            throw PluginServiceBindingException.Missing(
                providerPluginId,
                typeof(TContract).FullName ?? typeof(TContract).Name,
                $"Plugin '{consumerPluginId}' has no active direct dependency named '{providerPluginId}'.");
        }

        var contract = typeof(TContract);
        if (!contract.IsInterface || !contract.IsPublic)
            throw PluginServiceBindingException.Missing(
                providerPluginId,
                contract.FullName ?? contract.Name,
                "A requested plugin service contract must be a public interface.");
        return (TContract)provider.GetRequiredExport(contract);
    }

    public void Seal() => _open = false;
}

/// <summary>A cross-plugin service binding failure carrying a stable blocker code.</summary>
internal sealed class PluginServiceBindingException(
    string code,
    string message,
    IReadOnlyDictionary<string, object?> parameters) : InvalidOperationException(message)
{
    public string Code { get; } = code;

    public IReadOnlyDictionary<string, object?> Parameters { get; } = parameters;

    public static PluginServiceBindingException Missing(
        string providerId,
        string contractType,
        string message) =>
        new(
            "PluginServiceExportMissing",
            message,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["providerId"] = providerId,
                ["contractType"] = contractType
            });

    public static PluginServiceBindingException ApiConflict(
        string assemblyName,
        IReadOnlyList<string> providerIds) =>
        new(
            "PluginApiAssemblyConflict",
            $"Provider API assembly simple name '{assemblyName}' is ambiguous.",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["assemblyName"] = assemblyName,
                ["providerIds"] = providerIds
            });
}
