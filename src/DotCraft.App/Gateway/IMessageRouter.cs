using DotCraft.Abstractions;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Gateway;

/// <summary>
/// Routes messages from shared infrastructure (Cron, Heartbeat) to the appropriate channel.
/// </summary>
public interface IMessageRouter
{
    /// <summary>
    /// Delivers a structured message to a specific target within the named channel.
    /// </summary>
    Task DeliverAsync(
        string channel,
        string target,
        ChannelOutboundMessage message,
        object? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delivers a message to all admin-capable channels (for Heartbeat notifications).
    /// </summary>
    Task BroadcastToAdminsAsync(string content);

    /// <summary>
    /// Registers a channel service with the router.
    /// </summary>
    void RegisterChannel(IChannelService service);
}
