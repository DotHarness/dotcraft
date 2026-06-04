using System.Text.Json.Serialization;

namespace DotCraft.Protocol;

public static class SubAgentMailboxStatus
{
    public const string Pending = "pending";
    public const string Delivered = "delivered";
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
