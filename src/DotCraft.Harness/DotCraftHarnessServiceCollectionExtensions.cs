using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Runtime;
using DotCraft.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCraft.Harness;

/// <summary>Registers the in-process DotCraft Agent Harness.</summary>
public static class DotCraftHarnessServiceCollectionExtensions
{
    /// <summary>
    /// Adds the DotCraft Runtime, built-in model providers, and built-in configuration schema.
    /// </summary>
    /// <param name="services">The host service collection.</param>
    /// <param name="appConfig">The effective configuration prepared by the host.</param>
    /// <param name="configure">Configures workspace and user-data ownership.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddDotCraftHarness(
        this IServiceCollection services,
        AppConfig appConfig,
        Action<DotCraftHarnessOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(appConfig);
        ArgumentNullException.ThrowIfNull(configure);

        var harnessOptions = new DotCraftHarnessOptions();
        configure(harnessOptions);
        var runtimeOptions = CreateRuntimeOptions(appConfig, harnessOptions);
        var paths = DotCraftPathResolver.Resolve(runtimeOptions);
        return AddDotCraftHarness(services, runtimeOptions, paths);
    }

    internal static IServiceCollection AddDotCraftHarness(
        this IServiceCollection services,
        DotCraftRuntimeOptions runtimeOptions,
        DotCraftPaths paths)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(runtimeOptions);
        ArgumentNullException.ThrowIfNull(paths);

        services.TryAddSingleton<IConfigSchemaProvider>(ConfigSchemaRegistrations.CreateSchemaProvider());
        services.AddOpenAIModelProvider(paths.UserData.RootPath);
        services.AddAnthropicModelProvider();
        services.AddDotCraftRuntime(runtimeOptions, paths);
        return services;
    }

    private static DotCraftRuntimeOptions CreateRuntimeOptions(
        AppConfig appConfig,
        DotCraftHarnessOptions options) =>
        new()
        {
            Config = appConfig,
            WorkspacePath = options.WorkspacePath,
            DataPath = options.DataPath,
            UserDataPath = options.UserDataPath
        };
}
