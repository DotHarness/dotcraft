using DotCraft.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Abstractions;

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
    /// Configures protocol-only services for this module.
    /// This hook is intended for services that should be available even when
    /// the module runtime itself is disabled.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="context">The module context containing configuration and paths.</param>
    void ConfigureProtocolServices(IServiceCollection services, ModuleContext context)
    {
    }

    /// <summary>
    /// Validates this module's configuration section.
    /// </summary>
    /// <param name="config">The application configuration.</param>
    /// <returns>List of validation errors, empty if valid.</returns>
    IReadOnlyList<string> ValidateConfig(AppConfig config) => [];

    /// <summary>
    /// Creates a channel service for use in Gateway mode, where multiple channels run concurrently.
    /// Returns null if the module does not support channel service mode.
    /// </summary>
    /// <param name="sp">The service provider with all shared DI services available.</param>
    /// <returns>A channel service instance, or null if not supported.</returns>
    IChannelService? CreateChannelService(IServiceProvider sp) => null;

    /// <summary>
    /// Gets the tool providers contributed by this module.
    /// Tool providers are used to register AI tools specific to this module's functionality.
    /// </summary>
    /// <returns>An enumerable of tool providers, or empty if the module provides no tools.</returns>
    IEnumerable<IAgentToolProvider> GetToolProviders() => [];

    /// <summary>
    /// Session origin channels contributed by this module for AppServer <c>channel/list</c>
    /// (cross-channel visibility). Empty for modules that do not own DotCraft-managed threads
    /// (e.g. gateway shell) or channels that should not appear in the picker (e.g. HTTP API-only).
    /// </summary>
    IReadOnlyList<SessionChannelListEntry> GetSessionChannelListEntries() => [];
}
