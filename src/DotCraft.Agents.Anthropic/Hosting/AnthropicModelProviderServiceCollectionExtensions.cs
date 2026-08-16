using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCraft.Agents;

/// <summary>Registers the built-in Anthropic integration.</summary>
public static class AnthropicModelProviderServiceCollectionExtensions
{
    /// <summary>Adds Anthropic chat and provider capability services.</summary>
    public static IServiceCollection AddAnthropicModelProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelProvider, AnthropicClientProvider>());
        services.TryAddSingleton(sp => sp.GetServices<IModelProvider>().OfType<AnthropicClientProvider>().Single());
        return services;
    }
}
