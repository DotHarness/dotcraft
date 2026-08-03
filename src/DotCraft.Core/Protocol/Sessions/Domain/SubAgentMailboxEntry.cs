using System.Text.Json.Serialization;

namespace DotCraft.Protocol;

public static class SubAgentMailboxStatus
{
    public const string Pending = "pending";
    public const string Delivered = "delivered";
}

public static class SubAgentMailboxDelivery
{
    /// <summary>Delivery mode used for internal SubAgent mailbox context messages.</summary>
    public const string DeliveryMode = "subagentMailbox";

    /// <summary>Start tag for serialized SubAgent completion notifications.</summary>
    public const string NotificationStartTag = "<subagent_notification>";

    /// <summary>End tag for serialized SubAgent completion notifications.</summary>
    public const string NotificationEndTag = "</subagent_notification>";
}

internal static class SubAgentCommunicationMessageType
{
    public const string Message = "MESSAGE";
    public const string NewTask = "NEW_TASK";
    public const string FinalAnswer = "FINAL_ANSWER";

    public static string Normalize(string? value, string payload)
    {
        if (IsLegacyCompletionPayload(payload))
            return FinalAnswer;

        return value?.Trim().ToUpperInvariant() switch
        {
            NewTask => NewTask,
            FinalAnswer => FinalAnswer,
            _ => Message
        };
    }

    private static bool IsLegacyCompletionPayload(string payload) =>
        payload.TrimStart().StartsWith(SubAgentMailboxDelivery.NotificationStartTag, StringComparison.Ordinal);
}

internal sealed record SubAgentCommunication
{
    public required string Id { get; init; }

    public required string RootThreadId { get; init; }

    public required string AuthorAgentPath { get; init; }

    public required string RecipientAgentPath { get; init; }

    public required string MessageType { get; init; }

    public required string Payload { get; init; }

    public string? ParentTurnId { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public string RenderForModel()
    {
        var messageType = SubAgentCommunicationMessageType.Normalize(MessageType, Payload);
        return $"""
Message Type: {messageType}
Task name: {RecipientAgentPath}
Sender: {AuthorAgentPath}
Payload:
{Payload.Trim()}
""";
    }
}

public sealed class SubAgentMailboxEntry
{
    public string Id { get; set; } = string.Empty;

    public string RootThreadId { get; set; } = string.Empty;

    public string SenderAgentPath { get; set; } = AgentPath.Root;

    public string TargetAgentPath { get; set; } = AgentPath.Root;

    public string Message { get; set; } = string.Empty;

    /// <summary>Structured communication type: MESSAGE, NEW_TASK, or FINAL_ANSWER.</summary>
    public string MessageType { get; set; } = SubAgentCommunicationMessageType.Message;

    /// <summary>Trusted originating Turn id when the communication was created.</summary>
    public string? ParentTurnId { get; set; }

    public string Status { get; set; } = SubAgentMailboxStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? DeliveredAt { get; set; }

    internal SubAgentCommunication ToCommunication() =>
        new()
        {
            Id = Id,
            RootThreadId = RootThreadId,
            AuthorAgentPath = SenderAgentPath,
            RecipientAgentPath = TargetAgentPath,
            MessageType = SubAgentCommunicationMessageType.Normalize(MessageType, Message),
            Payload = Message,
            ParentTurnId = ParentTurnId,
            CreatedAt = CreatedAt
        };

    internal static SubAgentMailboxEntry FromCommunication(SubAgentCommunication communication) =>
        new()
        {
            Id = communication.Id,
            RootThreadId = communication.RootThreadId,
            SenderAgentPath = communication.AuthorAgentPath,
            TargetAgentPath = communication.RecipientAgentPath,
            MessageType = SubAgentCommunicationMessageType.Normalize(
                communication.MessageType,
                communication.Payload),
            Message = communication.Payload,
            ParentTurnId = communication.ParentTurnId,
            Status = SubAgentMailboxStatus.Pending,
            CreatedAt = communication.CreatedAt
        };
}
