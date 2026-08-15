using DotCraft.Auth.OpenAI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCraft.Agents;

/// <summary>Registers the built-in OpenAI integration and its optional capabilities.</summary>
public static class OpenAIModelProviderServiceCollectionExtensions
{
    private sealed record RegistrationOptions(string? UserDataPath);

    /// <summary>Adds OpenAI chat, OAuth, usage, image, catalog, and compaction services.</summary>
    public static IServiceCollection AddOpenAIModelProvider(
        this IServiceCollection services,
        string? userDataPath = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        MergeRegistrationOptions(services, userDataPath);

        services.TryAddSingleton(sp => new OpenAITokenStore(
            sp.GetRequiredService<RegistrationOptions>().UserDataPath));
        services.TryAddSingleton(sp => new OpenAIInstallationIdProvider(
            sp.GetRequiredService<RegistrationOptions>().UserDataPath));
        services.TryAddSingleton<IOpenAIAuthService, OpenAIAuthManager>();
        services.TryAddSingleton<OpenAIUsageClient>();
        services.TryAddSingleton<OpenAIUsagePoller>();
        services.TryAddSingleton<IOpenAIUsageService>(sp => sp.GetRequiredService<OpenAIUsagePoller>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelProvider, OpenAIClientProvider>());
        services.TryAddSingleton(sp => sp.GetServices<IModelProvider>().OfType<OpenAIClientProvider>().Single());
        return services;
    }

    private static void MergeRegistrationOptions(IServiceCollection services, string? userDataPath)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(userDataPath)
            ? null
            : Path.GetFullPath(userDataPath);
        var descriptor = services.FirstOrDefault(item => item.ServiceType == typeof(RegistrationOptions));
        if (descriptor?.ImplementationInstance is not RegistrationOptions current)
        {
            services.AddSingleton(new RegistrationOptions(normalizedPath));
            return;
        }

        if (normalizedPath is null || string.Equals(current.UserDataPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            return;

        if (current.UserDataPath is not null)
        {
            throw new InvalidOperationException(
                $"OpenAI provider user data is already configured at '{current.UserDataPath}' and cannot also use '{normalizedPath}'.");
        }

        services.Replace(ServiceDescriptor.Singleton(new RegistrationOptions(normalizedPath)));
    }
}
