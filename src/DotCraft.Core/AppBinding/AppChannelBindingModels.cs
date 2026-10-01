using System.Text.Json.Serialization;

namespace DotCraft.AppBinding;

public sealed class ChannelBoundBy
{
    public string PlatformUserId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }
}
public sealed class ChannelTarget
{
    public string ChannelName { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccountId { get; set; }

    public string ConversationKind { get; set; } = string.Empty;
    public string ConversationId { get; set; } = string.Empty;
    public string DeliveryTarget { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelBoundBy? BoundBy { get; set; }
}
