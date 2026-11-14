using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

public sealed class ThreadSearchOccurrencesParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("searchTerm")]
    public required string SearchTerm { get; init; }

    [JsonPropertyName("cursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cursor { get; init; }

    [JsonPropertyName("limit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Limit { get; init; }
}

public sealed class ThreadSearchTextRange : ExtensibleJsonObject
{
    [JsonPropertyName("start")]
    public required int Start { get; init; }

    [JsonPropertyName("end")]
    public required int End { get; init; }
}

public sealed class ThreadSearchOccurrence : ExtensibleJsonObject
{
    [JsonPropertyName("turnId")]
    public required string TurnId { get; init; }

    [JsonPropertyName("itemId")]
    public required string ItemId { get; init; }

    [JsonPropertyName("snippet")]
    public required string Snippet { get; init; }

    [JsonPropertyName("snippetMatchRange")]
    public required ThreadSearchTextRange SnippetMatchRange { get; init; }

    [JsonPropertyName("turnCursor")]
    public required string TurnCursor { get; init; }
}

public sealed class ThreadSearchOccurrencesResult : ExtensibleJsonObject
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<ThreadSearchOccurrence> Data { get; init; }

    [JsonPropertyName("nextCursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextCursor { get; init; }
}

public sealed class ThreadItemsReadParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public required string TurnId { get; init; }

    [JsonPropertyName("itemIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ItemIds { get; init; }

    [JsonPropertyName("callIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? CallIds { get; init; }
}

public sealed class ThreadItemsReadResult : ExtensibleJsonObject
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<ThreadItemListEntry> Data { get; init; }
}
