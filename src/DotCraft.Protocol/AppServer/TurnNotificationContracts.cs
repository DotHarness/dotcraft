using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

public enum TurnNotifyStatus
{
    Completed,
    Failed,
    Stopped,
    WaitingForInput
}

public sealed class TurnNotifyParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("notificationId")]
    public required string NotificationId { get; init; }

    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("refId")]
    public required string RefId { get; init; }

    [JsonPropertyName("status")]
    public required TurnNotifyStatus Status { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("project")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Project { get; init; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Result { get; init; }
}

public sealed class TurnNotifyResult : ExtensibleJsonObject
{
    [JsonPropertyName("queued")]
    public required bool Queued { get; init; }
}
