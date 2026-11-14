using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

public sealed class ThreadTimelineListParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("cursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cursor { get; init; }

    [JsonPropertyName("limit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Limit { get; init; }
}

public sealed class ThreadTimelineEntry : ExtensibleJsonObject
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("position")]
    [JsonSafeInteger]
    public required long Position { get; init; }

    [JsonPropertyName("turnId")]
    public required string TurnId { get; init; }

    [JsonPropertyName("startedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? StartedAt { get; init; }

    [JsonPropertyName("completedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? CompletedAt { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }

    [JsonPropertyName("providerError")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderError { get; init; }

    [JsonPropertyName("item")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionItem? Item { get; init; }
}

public sealed class ThreadTimelineListResult : ExtensibleJsonObject
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<ThreadTimelineEntry> Data { get; init; }

    [JsonPropertyName("nextCursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? NextCursor { get; init; }
}
