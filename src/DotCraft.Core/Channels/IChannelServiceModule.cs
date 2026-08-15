using DotCraft.Modules;

namespace DotCraft.Channels;

/// <summary>
/// Contributes a managed channel service from a compiled module.
/// </summary>
public interface IChannelServiceModule : IDotCraftModule
{
    /// <summary>
    /// Creates the channel service owned by the module.
    /// </summary>
    /// <param name="services">The configured service provider.</param>
    IChannelService? CreateChannelService(IServiceProvider services);
}
