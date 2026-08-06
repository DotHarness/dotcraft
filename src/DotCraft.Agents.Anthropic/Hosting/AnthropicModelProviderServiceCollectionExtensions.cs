using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Agents;

/// <summary>Registers the built-in Anthropic integration.</summary>
public static class AnthropicModelProviderServiceCollectionExtensions
{
    /// <summary>Adds Anthropic chat and provider capability services.</summary>
    public static IServiceCollection AddAnthropicModelProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<AnthropicClientProvider>();
        services.AddSingleton<IModelProvider>(sp => sp.GetRequiredService<AnthropicClientProvider>());
        return services;
    }
}
