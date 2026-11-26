using DotCraft.Configuration;

namespace DotCraft.Agents;

/// <summary>Opts a provider into service-side native HTTP inference without creating a chat client.</summary>
public interface IProviderNativeHttpRouter
{
    /// <summary>Resolves the selected model without I/O. Throws an argument exception for an unsupported model.</summary>
    ProviderHttpRoute ResolveRoute(EffectiveModelRuntime runtime);

    /// <summary>Supplies upstream request headers for the selected route, or throws if credentials are missing.</summary>
    Task<IReadOnlyDictionary<string, string>> AuthenticateAsync(EffectiveModelRuntime runtime, CancellationToken cancellationToken);
}

/// <summary>The canonical model and upstream base URI to which the service appends the native operation.</summary>
public sealed record ProviderHttpRoute(string Protocol, string EndPoint, string Model);
