using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>Creates chat clients for one or more stable provider protocols.</summary>
public interface IModelProvider
{
    /// <summary>Gets the normalized protocol identifiers owned by this provider.</summary>
    IReadOnlyCollection<string> Protocols { get; }

    /// <summary>Creates a provider client for an immutable effective runtime.</summary>
    IChatClient CreateChatClient(EffectiveModelRuntime runtime);

    /// <summary>Resolves an optional provider capability.</summary>
    object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
}

/// <summary>Raised when an executable host did not register a required model provider.</summary>
public sealed class ModelProviderNotRegisteredException(string protocol)
    : InvalidOperationException($"No model provider is registered for protocol '{protocol}'.")
{
    /// <summary>Stable machine-readable failure code.</summary>
    public const string ErrorCode = "UnsupportedModelProvider";

    /// <summary>Gets the normalized protocol that could not be resolved.</summary>
    public string Protocol { get; } = protocol;
}

/// <summary>Immutable registry of explicitly composed model providers.</summary>
public sealed class ModelProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IModelProvider> _providers;
    private readonly IReadOnlyCollection<string> _protocols;

    /// <summary>Creates a registry and validates unique protocol ownership.</summary>
    public ModelProviderRegistry(IEnumerable<IModelProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var byProtocol = new Dictionary<string, IModelProvider>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            ArgumentNullException.ThrowIfNull(provider.Protocols);
            if (provider.Protocols.Count == 0)
                throw new InvalidOperationException($"Model provider '{provider.GetType().Name}' does not declare a protocol.");

            foreach (var value in provider.Protocols)
            {
                var protocol = ModelProviderProtocols.Normalize(value);
                if (!byProtocol.TryAdd(protocol, provider))
                {
                    throw new InvalidOperationException(
                        $"Multiple model providers are registered for protocol '{protocol}'.");
                }
            }
        }

        _providers = byProtocol;
        _protocols = Array.AsReadOnly(byProtocol.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>Gets the registered normalized protocol identifiers.</summary>
    public IReadOnlyCollection<string> Protocols => _protocols;

    /// <summary>Resolves the unique provider that owns a protocol.</summary>
    public IModelProvider Resolve(string protocol)
    {
        var normalized = ModelProviderProtocols.Normalize(protocol);
        return _providers.TryGetValue(normalized, out var provider)
            ? provider
            : throw new ModelProviderNotRegisteredException(normalized);
    }

    /// <summary>Attempts to resolve a provider without throwing for missing registration.</summary>
    public bool TryResolve(string protocol, out IModelProvider? provider)
    {
        var normalized = ModelProviderProtocols.Normalize(protocol);
        return _providers.TryGetValue(normalized, out provider);
    }

    /// <summary>Creates a chat client through the provider that owns the runtime protocol.</summary>
    public IChatClient CreateChatClient(EffectiveModelRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return Resolve(runtime.Protocol).CreateChatClient(runtime)
            ?? throw new InvalidOperationException("The model provider returned a null chat client.");
    }

    /// <summary>Resolves an optional capability from the provider that owns a protocol.</summary>
    public TService? GetService<TService>(string protocol, object? serviceKey = null)
        where TService : class =>
        Resolve(protocol).GetService(typeof(TService), serviceKey) as TService;
}
