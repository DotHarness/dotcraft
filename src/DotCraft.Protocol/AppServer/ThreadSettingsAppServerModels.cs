using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

public sealed class ThreadSettingsUpdatedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }
}
