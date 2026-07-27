using DotCraft.Configuration;

namespace DotCraft.Protocol.AppServer;

internal static class ExternalChannelWireMapper
{
    public static ExternalChannelConfigWire ToWire(ExternalChannelEntry config) => new()
    {
        Name = config.Name,
        Enabled = config.Enabled,
        Transport = TransportToWire(config.Transport),
        Command = string.IsNullOrWhiteSpace(config.Command) ? null : config.Command,
        BuiltinModule = string.IsNullOrWhiteSpace(config.BuiltinModule) ? null : config.BuiltinModule,
        Args = config.Args is { Count: > 0 } ? [.. config.Args] : null,
        WorkingDirectory = string.IsNullOrWhiteSpace(config.WorkingDirectory) ? null : config.WorkingDirectory,
        Env = config.Env is { Count: > 0 } ? new Dictionary<string, string>(config.Env, StringComparer.Ordinal) : null
    };

    public static ExternalChannelEntry FromWire(ExternalChannelConfigWire wire) => new()
    {
        Name = wire.Name.Trim(),
        Enabled = wire.Enabled,
        Transport = NormalizeTransport(wire.Transport),
        Command = string.IsNullOrWhiteSpace(wire.Command) ? null : wire.Command.Trim(),
        BuiltinModule = string.IsNullOrWhiteSpace(wire.BuiltinModule) ? null : wire.BuiltinModule.Trim(),
        Args = wire.Args is { Count: > 0 } ? [.. wire.Args.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim())] : null,
        WorkingDirectory = string.IsNullOrWhiteSpace(wire.WorkingDirectory) ? null : wire.WorkingDirectory.Trim(),
        Env = wire.Env is { Count: > 0 } ? new Dictionary<string, string>(wire.Env, StringComparer.Ordinal) : null
    };

    public static void ValidateConfig(ExternalChannelConfigWire channel)
    {
        if (string.IsNullOrWhiteSpace(channel.Name))
            throw AppServerErrors.ExternalChannelValidationFailed("'channel.name' is required.");

        var transport = NormalizeTransport(channel.Transport);

        if (transport is ExternalChannelTransport.Subprocess or ExternalChannelTransport.ManagedWebsocket)
        {
            if (string.IsNullOrWhiteSpace(channel.Command) && string.IsNullOrWhiteSpace(channel.BuiltinModule))
                throw AppServerErrors.ExternalChannelValidationFailed("'channel.command' or 'channel.builtinModule' is required for subprocess or managedWebsocket transport.");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(channel.Command) ||
                !string.IsNullOrWhiteSpace(channel.BuiltinModule) ||
                channel.Args is { Count: > 0 } ||
                !string.IsNullOrWhiteSpace(channel.WorkingDirectory) ||
                channel.Env is { Count: > 0 })
            {
                throw AppServerErrors.ExternalChannelValidationFailed("process-launch fields are not supported for websocket transport.");
            }
        }
    }

    public static string TransportToWire(ExternalChannelTransport transport) =>
        transport switch
        {
            ExternalChannelTransport.Websocket => "websocket",
            ExternalChannelTransport.ManagedWebsocket => "managedWebsocket",
            _ => "subprocess"
        };

    private static ExternalChannelTransport NormalizeTransport(string? transport)
    {
        if (transport?.Equals("websocket", StringComparison.OrdinalIgnoreCase) == true)
            return ExternalChannelTransport.Websocket;
        if (transport?.Equals("managedWebsocket", StringComparison.OrdinalIgnoreCase) == true
            || transport?.Equals("managed-websocket", StringComparison.OrdinalIgnoreCase) == true)
            return ExternalChannelTransport.ManagedWebsocket;
        return ExternalChannelTransport.Subprocess;
    }
}
