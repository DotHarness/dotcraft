using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Configuration;

/// <summary>
/// Extension methods for configuration services.
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Adds configuration validation services to the service collection.
    /// Requires <see cref="DotCraft.Modules.ModuleRegistry"/> to be registered beforehand.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddConfigurationValidation(this IServiceCollection services)
    {
        services.AddSingleton<ConfigValidator>();
        return services;
    }
}
