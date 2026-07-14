using DotCraft.Configuration;
using DotCraft.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Abstractions;

/// <summary>
/// Base class for DotCraft modules providing common functionality.
/// </summary>
public abstract class ModuleBase : IDotCraftModule
{
    /// <inheritdoc />
    public virtual string Name => "";

    /// <inheritdoc />
    public virtual int Priority => 0;

    /// <inheritdoc />
    public virtual bool CanBePrimaryHost => false;

    /// <inheritdoc />
    public abstract bool IsEnabled(AppConfig config);

    /// <inheritdoc />
    public virtual void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        // Default implementation does nothing.
        // Derived classes can override to register module-specific services.
    }

    /// <inheritdoc />
    public virtual void ConfigureProtocolServices(IServiceCollection services, ModuleContext context)
    {
        // Default implementation does nothing.
        // Derived classes can override to register protocol-only services.
    }

    /// <inheritdoc />
    public virtual IReadOnlyList<string> ValidateConfig(AppConfig config) => [];

    /// <inheritdoc />
    public virtual IChannelService? CreateChannelService(IServiceProvider sp) => null;

    /// <inheritdoc />
    public virtual IEnumerable<IToolSource> GetToolSources(IServiceProvider services) => [];

    /// <inheritdoc />
    public virtual IReadOnlyList<SessionChannelListEntry> GetSessionChannelListEntries() => [];
}
