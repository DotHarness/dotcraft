using DotCraft.Auth.OpenAI;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Agents;

/// <summary>Registers the built-in OpenAI integration and its optional capabilities.</summary>
public static class OpenAIModelProviderServiceCollectionExtensions
{
    /// <summary>Adds OpenAI chat, OAuth, usage, image, catalog, and compaction services.</summary>
    public static IServiceCollection AddOpenAIModelProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<OpenAITokenStore>();
        services.AddSingleton<OpenAIInstallationIdProvider>();
        services.AddSingleton<IOpenAIAuthService, OpenAIAuthManager>();
        services.AddSingleton<OpenAIUsageClient>();
        services.AddSingleton<OpenAIUsagePoller>();
        services.AddSingleton<IOpenAIUsageService>(sp => sp.GetRequiredService<OpenAIUsagePoller>());
        services.AddSingleton<OpenAIClientProvider>();
        services.AddSingleton<IModelProvider>(sp => sp.GetRequiredService<OpenAIClientProvider>());
        return services;
    }
}
