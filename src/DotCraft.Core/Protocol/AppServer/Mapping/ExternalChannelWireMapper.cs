using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions.Wire;

namespace DotCraft.AppServer;

public static class ExternalChannelWireMapper
{
    public static Contract.ExtChannelToolCallParams ToContract(ChannelToolInvocationRequest value) => new()
    {
        ThreadId = value.ThreadId,
        TurnId = value.TurnId,
        CallId = value.CallId,
        Tool = value.Tool,
        Arguments = JsonSerializer.SerializeToElement(value.Arguments, SessionWireJsonOptions.Default),
        Context = new Contract.ExtChannelToolCallContext
        {
            ChannelName = value.Context.ChannelName,
            ChannelContext = OmitIfNull(value.Context.ChannelContext),
            SenderId = OmitIfNull(value.Context.SenderId),
            GroupId = OmitIfNull(value.Context.GroupId)
        }
    };

    public static ChannelToolInvocationResult FromContract(Contract.ExtChannelToolCallResult value) => new()
    {
        Success = Read(value.Success),
        ContentItems = Read(value.ContentItems)?.Select(static item => new ChannelToolInvocationContentItem
        {
            Type = Read(item.Type) ?? "text",
            Text = Read(item.Text),
            Url = Read(item.Url),
            DataBase64 = Read(item.DataBase64),
            MediaType = Read(item.MediaType)
        }).ToList(),
        StructuredResult = Read(value.StructuredResult) is { } structured
            ? JsonNode.Parse(structured.GetRawText())
            : null,
        ErrorCode = Read(value.ErrorCode),
        ErrorMessage = Read(value.ErrorMessage)
    };

    public static Contract.ExtChannelSendParams ToContract(ChannelDeliveryRequest value) => new()
    {
        Target = value.Target,
        Message = new Contract.ChannelOutboundMessage
        {
            Kind = value.Message.Kind,
            Text = OmitIfNull(value.Message.Text),
            Caption = OmitIfNull(value.Message.Caption),
            FileName = OmitIfNull(value.Message.FileName),
            MediaType = OmitIfNull(value.Message.MediaType),
            Source = value.Message.Source is null
                ? default
                : DotCraft.Protocol.Optional<Contract.ChannelMediaSource?>.FromValue(
                    new Contract.ChannelMediaSource
                    {
                        Kind = value.Message.Source.Kind,
                        HostPath = OmitIfNull(value.Message.Source.HostPath),
                        Url = OmitIfNull(value.Message.Source.Url),
                        DataBase64 = OmitIfNull(value.Message.Source.DataBase64),
                        ArtifactId = OmitIfNull(value.Message.Source.ArtifactId)
                    })
        },
        Metadata = value.Metadata is null
            ? default
            : DotCraft.Protocol.Optional<JsonElement?>.FromValue(
                JsonSerializer.SerializeToElement(value.Metadata, value.Metadata.GetType(), SessionWireJsonOptions.Default))
    };

    public static ChannelDeliveryResult FromContract(Contract.ExtChannelSendResult value) => new()
    {
        Delivered = Read(value.Delivered),
        RemoteMessageId = Read(value.RemoteMessageId),
        RemoteMediaId = Read(value.RemoteMediaId),
        ErrorCode = Read(value.ErrorCode),
        ErrorMessage = Read(value.ErrorMessage)
    };

    public static Contract.ExternalChannelConfig ToContract(ExternalChannelEntry config) => new()
    {
        Name = config.Name,
        Enabled = config.Enabled,
        Transport = TransportToWire(config.Transport),
        Command = string.IsNullOrWhiteSpace(config.Command) ? null : config.Command,
        BuiltinModule = string.IsNullOrWhiteSpace(config.BuiltinModule) ? null : config.BuiltinModule,
        Args = config.Args is { Count: > 0 } ? new List<string>(config.Args) : null,
        WorkingDirectory = string.IsNullOrWhiteSpace(config.WorkingDirectory) ? null : config.WorkingDirectory,
        Env = config.Env is { Count: > 0 } ? new Dictionary<string, string>(config.Env, StringComparer.Ordinal) : null
    };

    public static ExternalChannelEntry FromContract(Contract.ExternalChannelConfig wire)
    {
        var args = wire.Args.IsSet ? wire.Args.Value : null;
        var env = wire.Env.IsSet ? wire.Env.Value : null;
        return new ExternalChannelEntry
        {
            Name = Read(wire.Name)?.Trim() ?? string.Empty,
            Enabled = wire.Enabled.IsSet ? wire.Enabled.Value : true,
            Transport = NormalizeTransport(Read(wire.Transport)),
            Command = TrimOrNull(Read(wire.Command)),
            BuiltinModule = TrimOrNull(Read(wire.BuiltinModule)),
            Args = args is { Count: > 0 } ? [.. args.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim())] : null,
            WorkingDirectory = TrimOrNull(Read(wire.WorkingDirectory)),
            Env = env is { Count: > 0 } ? new Dictionary<string, string>(env, StringComparer.Ordinal) : null
        };
    }

    public static void ValidateContract(Contract.ExternalChannelConfig channel)
    {
        var name = Read(channel.Name);
        var command = Read(channel.Command);
        var builtinModule = Read(channel.BuiltinModule);
        var args = channel.Args.IsSet ? channel.Args.Value : null;
        var workingDirectory = Read(channel.WorkingDirectory);
        var env = channel.Env.IsSet ? channel.Env.Value : null;
        if (string.IsNullOrWhiteSpace(name))
            throw AppServerErrors.ExternalChannelValidationFailed("'channel.name' is required.");

        var transport = NormalizeTransport(Read(channel.Transport));

        if (transport is ExternalChannelTransport.Subprocess or ExternalChannelTransport.ManagedWebsocket)
        {
            if (string.IsNullOrWhiteSpace(command) && string.IsNullOrWhiteSpace(builtinModule))
                throw AppServerErrors.ExternalChannelValidationFailed("'channel.command' or 'channel.builtinModule' is required for subprocess or managedWebsocket transport.");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(command) ||
                !string.IsNullOrWhiteSpace(builtinModule) ||
                args is { Count: > 0 } ||
                !string.IsNullOrWhiteSpace(workingDirectory) ||
                env is { Count: > 0 })
            {
                throw AppServerErrors.ExternalChannelValidationFailed("process-launch fields are not supported for websocket transport.");
            }
        }
    }

    private static T? Read<T>(DotCraft.Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

    private static DotCraft.Protocol.Optional<T?> OmitIfNull<T>(T? value) =>
        value is null ? default : DotCraft.Protocol.Optional<T?>.FromValue(value);

    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
