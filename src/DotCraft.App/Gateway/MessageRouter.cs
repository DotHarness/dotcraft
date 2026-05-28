using DotCraft.Abstractions;
using DotCraft.Protocol.AppServer;
using Spectre.Console;
using System.Collections.Concurrent;

namespace DotCraft.Gateway;

/// <summary>
/// Routes messages from shared infrastructure (Cron, Heartbeat) to the appropriate channel service.
/// </summary>
public sealed class MessageRouter : IMessageRouter
{
    private readonly ConcurrentDictionary<string, IChannelService> _channels = new(StringComparer.OrdinalIgnoreCase);
    private readonly IChannelRuntimeRegistry _runtimeRegistry;

    public MessageRouter(IChannelRuntimeRegistry runtimeRegistry)
    {
        _runtimeRegistry = runtimeRegistry;
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
        ChannelOutboundMessage message,
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
                    AnsiConsole.MarkupLine(
                        $"[grey][[Gateway]][/] [red]Delivery to {channel}/{target} failed: {Markup.Escape(result.ErrorCode ?? "DeliveryFailed")} {Markup.Escape(result.ErrorMessage ?? "Unknown error")}[/]");
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[grey][[Gateway]][/] [red]Delivery to {channel}/{target} failed: {Markup.Escape(ex.Message)}[/]");
            }
        }
        else
        {
            AnsiConsole.MarkupLine($"[grey][[Gateway]][/] [yellow]No channel registered for '{Markup.Escape(channel)}', skipping delivery[/]");
        }
    }

    public async Task BroadcastToAdminsAsync(string content)
    {
        var message = new ChannelOutboundMessage
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
                        AnsiConsole.MarkupLine(
                            $"[grey][[Gateway]][/] [red]{Markup.Escape(channel.Name)} admin notify to {Markup.Escape(target)} failed: {Markup.Escape(result.ErrorCode ?? "DeliveryFailed")} {Markup.Escape(result.ErrorMessage ?? "Unknown error")}[/]");
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[grey][[Gateway]][/] [red]{Markup.Escape(channel.Name)} admin notify failed: {Markup.Escape(ex.Message)}[/]");
                }
            }
        }
    }
}
