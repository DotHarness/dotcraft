using System.Collections.Concurrent;
using DotCraft.AppServer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.Channels;

/// <summary>
/// Routes messages from shared infrastructure to the appropriate channel service.
/// </summary>
public sealed class MessageRouter
{
    private readonly ConcurrentDictionary<string, IChannelService> _channels = new(StringComparer.OrdinalIgnoreCase);
    private readonly IChannelRuntimeRegistry _runtimeRegistry;
    private readonly ILogger<MessageRouter> _logger;

    public MessageRouter(IChannelRuntimeRegistry runtimeRegistry, ILogger<MessageRouter>? logger = null)
    {
        _runtimeRegistry = runtimeRegistry;
        _logger = logger ?? NullLogger<MessageRouter>.Instance;
    }

    public void RegisterChannel(IChannelService service)
    {
        _channels[service.Name] = service;
        _runtimeRegistry.Register(service);
    }

    public bool UnregisterChannel(string channelName)
    {
        if (!_channels.TryRemove(channelName, out _))
            return false;

        _runtimeRegistry.TryRemove(channelName);
        return true;
    }

    public async Task DeliverAsync(
        string channel,
        string target,
        ChannelDeliveryMessage message,
        object? metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (_channels.TryGetValue(channel, out var service))
        {
            try
            {
                var result = await service.DeliverAsync(target, message, metadata, cancellationToken);
                if (!result.Delivered)
                {
                    _logger.LogError(
                        "Delivery to channel {Channel} target {Target} failed: {ErrorCode} {ErrorMessage}",
                        channel,
                        target,
                        result.ErrorCode ?? "DeliveryFailed",
                        result.ErrorMessage ?? "Unknown error");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delivery to channel {Channel} target {Target} failed", channel, target);
            }
        }
        else
        {
            _logger.LogWarning("No channel registered for {Channel}; skipping delivery", channel);
        }
    }

    public async Task BroadcastToAdminsAsync(string content)
    {
        var message = new ChannelDeliveryMessage
        {
            Kind = "text",
            Text = content
        };

        var channels = _channels.Values.ToArray();
        foreach (var channel in channels)
        {
            var targets = channel.GetAdminTargets();
            foreach (var target in targets)
            {
                try
                {
                    var result = await channel.DeliverAsync(target, message);
                    if (!result.Delivered)
                    {
                        _logger.LogError(
                            "Admin notification through channel {Channel} to {Target} failed: {ErrorCode} {ErrorMessage}",
                            channel.Name,
                            target,
                            result.ErrorCode ?? "DeliveryFailed",
                            result.ErrorMessage ?? "Unknown error");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Admin notification through channel {Channel} failed", channel.Name);
                }
            }
        }
    }
}
