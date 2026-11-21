using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

/// <summary>Canonical payload for a persistent system notice item.</summary>
public sealed class SystemNoticePayload : ExtensibleJsonObject
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("trigger")]
    public required string Trigger { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("tokensBefore")]
    [JsonSafeInteger]
    public required long TokensBefore { get; init; }

    [JsonPropertyName("tokensAfter")]
    [JsonSafeInteger]
    public required long TokensAfter { get; init; }

    [JsonPropertyName("percentLeftAfter")]
    public required double PercentLeftAfter { get; init; }

    [JsonPropertyName("clearedToolResults")]
    public required int ClearedToolResults { get; init; }

    [JsonPropertyName("sourceThreadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceThreadId { get; init; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }

    [JsonPropertyName("initiator")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Initiator { get; init; }

    [JsonPropertyName("hostId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HostId { get; init; }

    [JsonPropertyName("hostName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HostName { get; init; }

    [JsonPropertyName("workspaceId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkspaceId { get; init; }

    [JsonPropertyName("workspaceName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkspaceName { get; init; }

    [JsonPropertyName("channelName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChannelName { get; init; }

    [JsonPropertyName("targetName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetName { get; init; }

    [JsonPropertyName("from")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContextHandoffEndpoint? From { get; init; }

    [JsonPropertyName("to")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContextHandoffEndpoint? To { get; init; }

    [JsonPropertyName("fallbackText")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FallbackText { get; init; }
}

public sealed class ContextHandoffEndpoint : ExtensibleJsonObject
{
    [JsonPropertyName("providerId")]
    public required string ProviderId { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }
}
