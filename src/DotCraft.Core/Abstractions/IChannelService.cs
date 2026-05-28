using DotCraft.Cron;
using DotCraft.Heartbeat;
using DotCraft.Protocol.AppServer;
using DotCraft.Security;

namespace DotCraft.Abstractions;

/// <summary>
/// Represents a channel service that handles communication for a specific platform.
/// Used by GatewayHost to run multiple channels concurrently.
/// </summary>
public interface IChannelService : IAsyncDisposable, IChannelRuntime
{
    /// <summary>
    /// Gets the unique name of this channel.
    /// </summary>
    new string Name { get; }

    /// <summary>
    /// The shared HeartbeatService injected by GatewayHost before the channel starts.
    /// Allows slash commands (/heartbeat) to operate within this channel.
    /// </summary>
    HeartbeatService? HeartbeatService { get; set; }

    /// <summary>
    /// The shared CronService injected by GatewayHost before the channel starts.
    /// Allows slash commands (/cron) to operate within this channel.
    /// </summary>
    CronService? CronService { get; set; }

    /// <summary>
    /// The channel-specific approval service, if any.
    /// Used by GatewayHost to route background-task approvals back to the originating channel.
    /// </summary>
    IApprovalService? ApprovalService { get; }

    /// <summary>
    /// Starts the channel service. This is a long-running task that completes
    /// only when the channel is stopped or the cancellation token is triggered.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops the channel service gracefully.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Returns the list of delivery targets for admin notifications (e.g. Heartbeat results).
    /// Each target is passed to <see cref="IChannelRuntime.DeliverAsync(string,ChannelOutboundMessage,object?,CancellationToken)"/>
    /// with a text message when broadcasting to admins.
    /// Return an empty list if this channel does not support admin notifications.
    /// </summary>
    IReadOnlyList<string> GetAdminTargets() => [];
}
