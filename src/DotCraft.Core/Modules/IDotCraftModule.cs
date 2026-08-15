using DotCraft.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Modules;

/// <summary>
/// Defines a module that can be registered with the DotCraft application.
/// Each module represents an interaction channel.
/// </summary>
public interface IDotCraftModule
{
    /// <summary>
    /// Gets the unique name of the module.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the priority of the module for host selection.
    /// Higher priority modules are preferred when multiple modules are enabled.
    /// Default priority is 0.
    /// </summary>
    int Priority => 0;

    /// <summary>
    /// Gets whether this module can act as the primary host selected at startup.
    /// Background capability modules should return <see langword="false"/> so they
    /// can be attached to a host without taking over the main entry point.
    /// </summary>
    bool CanBePrimaryHost => false;

    /// <summary>
    /// Determines whether this module is enabled based on the current configuration.
    /// </summary>
    /// <param name="config">The application configuration.</param>
    /// <returns>True if the module should be active; otherwise, false.</returns>
    bool IsEnabled(AppConfig config);

    /// <summary>
    /// Configures services for this module.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="context">The module context containing configuration and paths.</param>
    void ConfigureServices(IServiceCollection services, ModuleContext context);

    /// <summary>
    /// Validates this module's configuration section.
    /// </summary>
    /// <param name="config">The application configuration.</param>
    /// <returns>List of validation errors, empty if valid.</returns>
    IReadOnlyList<string> ValidateConfig(AppConfig config) => [];

}
