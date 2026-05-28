using DotCraft.Abstractions;
using DotCraft.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using ModuleRegistry = DotCraft.Modules.ModuleRegistry;

namespace DotCraft.Hosting;

/// <summary>
/// Selecting a module and creating its host.
/// Handles module discovery, selection, and host creation.
/// </summary>
public sealed class HostBuilder
{
    private readonly ModuleRegistry _registry;
    
    private readonly AppConfig _config;
    
    private readonly DotCraftPaths _paths;

    private readonly string? _preferredPrimaryModuleName;

    /// <summary>
    /// Creates a new bot launcher.
    /// </summary>
    /// <param name="registry">The module registry containing all registered modules.</param>
    /// <param name="config">The application configuration.</param>
    /// <param name="paths">The workspace and bot paths.</param>
    public HostBuilder(
        ModuleRegistry registry,
        AppConfig config,
        DotCraftPaths paths,
        string? preferredPrimaryModuleName = null)
    {
        _registry = registry;
        _config = config;
        _paths = paths;
        _preferredPrimaryModuleName = preferredPrimaryModuleName;
    }

    /// <summary>
    /// Configures services for the selected module.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="module">The module to configure services for.</param>
    private void ConfigureModuleServices(IServiceCollection services, IDotCraftModule module)
    {
        var context = new ModuleContext
        {
            Config = _config,
            Paths = _paths
        };
        
        module.ConfigureServices(services, context);
    }

    /// <summary>
    /// Configures protocol-only services for a module.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="module">The module to configure protocol services for.</param>
    private void ConfigureModuleProtocolServices(IServiceCollection services, IDotCraftModule module)
    {
        var context = new ModuleContext
        {
            Config = _config,
            Paths = _paths
        };

        module.ConfigureProtocolServices(services, context);
    }

    /// <summary>
    /// Creates the host for the specified module.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="module">The module to create a host for.</param>
    /// <returns>The created host instance.</returns>
    private IDotCraftHost CreateHost(IServiceProvider serviceProvider, IDotCraftModule module)
    {
        var factory = _registry.GetHostFactory(module.Name);
        if (factory != null)
        {
            var context = new ModuleContext
            {
                Config = _config,
                Paths = _paths
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
    public (IServiceProvider Provider, IDotCraftHost Host) Build(IServiceCollection services)
    {
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

        AnsiConsole.MarkupLine($"[green][[Startup]][/] Using module: {primaryModule.Name}");

        // Configure services for the primary module
        ConfigureModuleServices(services, primaryModule);

        // In gateway / app-server mode, also configure all enabled sub-module services so
        // their dependencies are available in the DI container. AppServer needs this because
        // it exposes sub-module functionality (e.g. Automations) via the Wire Protocol.
        List<string>? subModuleNames = null;
        if (primaryModule.Name is "gateway" or "app-server")
        {
            subModuleNames = [];
            foreach (var subModule in _registry.GetEnabledModules(_config)
                         .Where(m => m.Name != primaryModule.Name))
            {
                subModuleNames.Add(subModule.Name);
                AnsiConsole.MarkupLine($"[grey]  Configuring sub-module services: {subModule.Name}[/]");
                ConfigureModuleServices(services, subModule);
            }

            foreach (var module in _registry.Modules)
                ConfigureModuleProtocolServices(services, module);
        }

        // Build service provider
        var provider = services.BuildServiceProvider();

        var startupLogger = provider.GetService<ILoggerFactory>()?.CreateLogger<HostBuilder>();
        startupLogger?.LogInformation("Primary module: {ModuleName}", primaryModule.Name);
        if (subModuleNames is { Count: > 0 })
        {
            foreach (var name in subModuleNames)
                startupLogger?.LogInformation("Configured sub-module services: {SubModuleName}", name);
        }

        // Create host
        var host = CreateHost(provider, primaryModule);

        return (provider, host);
    }
}
