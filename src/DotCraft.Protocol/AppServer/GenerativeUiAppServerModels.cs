using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

public sealed class GenerativeUiStateReadParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")] public required string ThreadId { get; init; }
}

public sealed class GenerativeUiStateReadResult : ExtensibleJsonObject
{
    [JsonPropertyName("states")] public required IReadOnlyList<GenerativeUiBlockState> States { get; init; }
}

public sealed class GenerativeUiBlockState : ExtensibleJsonObject
{
    [JsonPropertyName("turnId")] public required string TurnId { get; init; }

    [JsonPropertyName("itemId")] public required string ItemId { get; init; }

    [JsonPropertyName("blockIndex")] public required int BlockIndex { get; init; }

    [JsonPropertyName("state")] public required JsonElement State { get; init; }
}

public sealed class GenerativeUiStateUpdateParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")] public required string ThreadId { get; init; }

    [JsonPropertyName("turnId")] public required string TurnId { get; init; }

    [JsonPropertyName("itemId")] public required string ItemId { get; init; }

    [JsonPropertyName("blockIndex")] public required int BlockIndex { get; init; }

    /// <summary>An omitted or <c>null</c> state removes the stored state.</summary>
    [JsonPropertyName("state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> State { get; init; }
}

public sealed class GenerativeUiStateUpdatedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")] public required string ThreadId { get; init; }

    [JsonPropertyName("turnId")] public required string TurnId { get; init; }

    [JsonPropertyName("itemId")] public required string ItemId { get; init; }

    [JsonPropertyName("blockIndex")] public required int BlockIndex { get; init; }

    /// <summary><c>null</c> when the update removed the stored state.</summary>
    [JsonPropertyName("state")]
    [JsonRequired]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public JsonElement? State { get; init; }
}
