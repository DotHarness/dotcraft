using System.Collections.Frozen;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Contributions;
using DotCraft.Plugins;
using DotCraft.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DotCraft.Runtime;

public static class ModelPluginRuntimeRegistration
{
    /// <summary>Hosts installed model plugin contributions in a service-owned scope without an Agent Session.</summary>
    public static IServiceCollection AddDotCraftModelPluginRuntime(
        this IServiceCollection services,
        AppConfig config,
        DotCraftPaths paths,
        IReadOnlyList<string> pluginRoots,
        IReadOnlyCollection<string> allowedPluginIds,
        IDotNetExtensionTrustStore? trustStore = null,
        Func<string, string, IProviderCredentialStore>? createCredentialStore = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(pluginRoots);
        ArgumentNullException.ThrowIfNull(allowedPluginIds);
        if (config.ModelService is not null)
            throw new ArgumentException("The model plugin host requires local provider configuration.", nameof(config));
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IDotNetExtensionRuntimeCoordinator)
            || descriptor.ServiceType == typeof(ModelBackendResolver)))
            throw new InvalidOperationException("A DotCraft model or plugin runtime is already registered.");

        var roots = pluginRoots.Select(path => Path.GetFullPath(path, paths.WorkspacePath))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToArray();
        var allowedIds = allowedPluginIds.Select(pluginId =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
            return PluginIds.Canonicalize(pluginId);
        }).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        var options = new DotNetExtensionRuntimeOptions { ModelPluginAdmission = new(roots, allowedIds) };

        services.AddLogging();
        services.TryAddSingleton<ContributionRegistry>();
        services.TryAddSingleton<IContributionView>(sp => sp.GetRequiredService<ContributionRegistry>());
        services.TryAddSingleton<ModelProviderRegistry>();
        services.AddSingleton(sp =>
        {
            var resolver = new ModelBackendResolver(sp.GetRequiredService<ModelProviderRegistry>(),
                sp.GetRequiredService<ContributionRegistry>(), paths.UserData.RootPath ?? paths.Data.RootPath,
                sp.GetServices<IModelBackend>(), createCredentialStore);
            config.RuntimeResolver = resolver;
            return resolver;
        });
        services.AddSingleton<IModelRuntimeResolver>(sp => sp.GetRequiredService<ModelBackendResolver>());
        services.AddSingleton<ProviderAuthenticationCoordinator>();
        services.AddDotNetExtensionRuntimeManager(sp => new DotNetExtensionRuntimeManager(
            new PluginDiscoveryService(logger: sp.GetService<ILogger<PluginDiscoveryService>>()),
            config, paths, sp, sp.GetRequiredService<ContributionRegistry>(), options,
            sp.GetService<ILogger<DotNetExtensionRuntimeManager>>(), trustStore));
        services.AddSingleton<IHostedService>(sp =>
        {
            _ = sp.GetRequiredService<ModelBackendResolver>();
            return sp.GetRequiredService<DotNetExtensionRuntimeManager>();
        });
        return services;
    }

    internal static void AddDotNetExtensionRuntimeManager(this IServiceCollection services,
        Func<IServiceProvider, DotNetExtensionRuntimeManager> create)
    {
        services.AddSingleton(create);
        services.AddSingleton<IDotNetExtensionRuntimeCoordinator>(sp => sp.GetRequiredService<DotNetExtensionRuntimeManager>());
    }
}
