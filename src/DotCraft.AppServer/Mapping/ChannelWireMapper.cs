using System.Text.Json;
using System.Text.Json.Nodes;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions.Wire;

namespace DotCraft.AppServer;

public static class ChannelWireMapper
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
        DeliveryId = value.DeliveryId,
        Message = new Contract.ChannelOutboundMessage
        {
            Kind = value.Message.Kind,
            Text = OmitIfNull(value.Message.Text),
            Caption = OmitIfNull(value.Message.Caption),
            FileName = OmitIfNull(value.Message.FileName),
            MediaType = OmitIfNull(value.Message.MediaType),
            Source = value.Message.Source is null
                ? default
                : Protocol.Optional<Contract.ChannelMediaSource?>.FromValue(
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
            : Protocol.Optional<JsonElement?>.FromValue(
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

    public static Contract.ExtChannelHistoryParams ToContract(ChannelHistoryRequest value) => new()
    {
        Target = value.Target,
        Limit = value.Limit,
        Before = OmitIfNull(value.Before)
    };

    public static ChannelHistoryResult FromContract(Contract.ExtChannelHistoryResult value) => new()
    {
        Messages = Read(value.Messages)?.Select(static message => new ChannelHistoryEntry(
            Read(message.Text) ?? string.Empty,
            Read(message.MessageId),
            Read(message.SenderId),
            Read(message.SenderName),
            Read(message.SentAt),
            Read(message.FromBot))).ToList() ?? [],
        Before = Read(value.Before),
        ErrorCode = Read(value.ErrorCode),
        ErrorMessage = Read(value.ErrorMessage)
    };

    private static T? Read<T>(Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

    private static Protocol.Optional<T?> OmitIfNull<T>(T? value) =>
        value is null ? default : Protocol.Optional<T?>.FromValue(value);

}
