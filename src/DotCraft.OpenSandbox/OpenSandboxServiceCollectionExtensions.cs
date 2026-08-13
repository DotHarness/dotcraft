using DotCraft.Configuration;
using DotCraft.Tools.Sandbox;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.OpenSandbox;

/// <summary>Registers the built-in OpenSandbox infrastructure adapter.</summary>
public static class OpenSandboxServiceCollectionExtensions
{
    /// <summary>Adds the fixed OpenSandbox implementation for Core sandbox execution.</summary>
    public static IServiceCollection AddOpenSandboxProvider(
        this IServiceCollection services,
        AppConfig.SandboxConfig config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);
        services.AddSingleton<ISandboxProvider>(new OpenSandboxProvider(config));
        return services;
    }
}
