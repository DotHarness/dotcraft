using DotCraft.Workspaces;
using DotCraft.Modules;
using DotCraft.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModuleRegistry = DotCraft.Modules.ModuleRegistry;

namespace DotCraft.Hosting;

/// <summary>
/// Selecting a module and creating its host.
/// Handles module discovery, selection, and host creation.
/// </summary>
public sealed class HostBuilder
{
    private readonly ModuleRegistry _registry;

    private readonly HostFactoryRegistry _hostFactories;

    private readonly AppConfig _config;

    private readonly string? _preferredPrimaryModuleName;

    /// <summary>
    /// Creates a new bot launcher.
    /// </summary>
    /// <param name="registry">The module registry containing all registered modules.</param>
    /// <param name="config">The application configuration.</param>
    public HostBuilder(
        ModuleRegistry registry,
        HostFactoryRegistry hostFactories,
        AppConfig config,
        string? preferredPrimaryModuleName = null)
    {
        _registry = registry;
        _hostFactories = hostFactories;
        _config = config;
        _preferredPrimaryModuleName = preferredPrimaryModuleName;
    }

    /// <summary>
    /// Configures services for the selected module.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="module">The module to configure services for.</param>
    private void ConfigureModuleServices(
        IServiceCollection services,
        IDotCraftModule module,
        DotCraftPaths paths)
    {
        var context = new ModuleContext
        {
            Config = _config,
            Paths = paths
        };

        module.ConfigureServices(services, context);
    }

    /// <summary>
    /// Creates the host for the specified module.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="module">The module to create a host for.</param>
    /// <returns>The created host instance.</returns>
    private IDotCraftHost CreateHost(
        IServiceProvider serviceProvider,
        IDotCraftModule module,
        DotCraftPaths paths)
    {
        var factory = _hostFactories.Get(module.Name);
        if (factory != null)
        {
            var context = new ModuleContext
            {
                Config = _config,
                Paths = paths
            };
            return factory.CreateHost(serviceProvider, context);
        }

        throw new InvalidOperationException($"No host factory registered for module '{module.Name}'");
    }

    /// <summary>
    /// Builds and creates the primary host based on enabled modules.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>A tuple containing the service provider and the host.</returns>
    public (ServiceProvider Provider, IDotCraftHost Host) Build(IServiceCollection services)
    {
        var paths = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(DotCraftPaths))
            ?.ImplementationInstance as DotCraftPaths
            ?? throw new InvalidOperationException(
                "DotCraftPaths is not registered. Call AddDotCraftHarness before building the application host.");

        // Select primary module
        var primaryModule = _registry.SelectPrimaryModule(_config, _preferredPrimaryModuleName);
        if (primaryModule == null)
        {
            var enabledHosts = _registry.GetEnabledPrimaryHostModules(_config)
                .Select(m => m.Name)
                .ToArray();
            var preferredText = string.IsNullOrWhiteSpace(_preferredPrimaryModuleName)
                ? string.Empty
                : $" Preferred host: '{_preferredPrimaryModuleName}'.";
            var availableText = enabledHosts.Length == 0
                ? " No primary host modules are enabled."
                : $" Enabled primary hosts: {string.Join(", ", enabledHosts)}.";
            throw new InvalidOperationException($"No primary host module is enabled.{preferredText}{availableText}");
        }

        var composedModules = primaryModule is IModuleHostComposition composition
            ? composition.GetModules(_registry, _config)
            : [primaryModule];
        foreach (var module in composedModules.DistinctBy(module => module.Name))
            ConfigureModuleServices(services, module, paths);

        // Build service provider
        var provider = services.BuildServiceProvider();

        var startupLogger = provider.GetService<ILoggerFactory>()?.CreateLogger<HostBuilder>();
        startupLogger?.LogInformation("Primary module: {ModuleName}", primaryModule.Name);
        var additionalModuleNames = composedModules
            .Where(module => !ReferenceEquals(module, primaryModule))
            .Select(module => module.Name)
            .ToArray();
        if (additionalModuleNames.Length > 0)
            startupLogger?.LogDebug(
                "Configured sub-module services: {SubModuleNames}",
                string.Join(", ", additionalModuleNames));

        // Create host
        var host = CreateHost(provider, primaryModule, paths);

        return (provider, host);
    }
}
