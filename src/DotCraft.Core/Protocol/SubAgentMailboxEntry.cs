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

public sealed class SubAgentMailboxEntry
{
    public string Id { get; set; } = string.Empty;

    public string RootThreadId { get; set; } = string.Empty;

    public string SenderAgentPath { get; set; } = AgentPath.Root;

    public string TargetAgentPath { get; set; } = AgentPath.Root;

    public string Message { get; set; } = string.Empty;

    public string Status { get; set; } = SubAgentMailboxStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? DeliveredAt { get; set; }
}
