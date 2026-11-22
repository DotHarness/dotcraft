using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

public sealed class TurnStartOrEnqueueParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("input")]
    public required IReadOnlyList<InputPart> Input { get; init; }

    [JsonPropertyName("kickoff")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Kickoff { get; init; }

    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; init; }

    [JsonPropertyName("reasoning")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReasoningConfig? Reasoning { get; init; }
}

public sealed class TurnStartOrEnqueueResult : ExtensibleJsonObject
{
    [JsonPropertyName("turn")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionTurn? Turn { get; init; }

    [JsonPropertyName("queuedInput")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public QueuedTurnInput? QueuedInput { get; init; }
}
