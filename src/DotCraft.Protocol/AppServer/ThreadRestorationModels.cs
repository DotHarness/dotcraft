using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

public sealed class ThreadResumeInitialTurnsPageParams : ExtensibleJsonObject
{
    [JsonPropertyName("limit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Limit { get; init; }

    [JsonPropertyName("itemsView")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ItemsView { get; init; }

    [JsonPropertyName("summaryTools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ThreadSummaryTool>? SummaryTools { get; init; }
}

public sealed class ThreadResumeCurrentState : ExtensibleJsonObject
{
    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TurnId { get; init; }

    [JsonPropertyName("turnStatus")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TurnStatus { get; init; }

    [JsonPropertyName("activeItems")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SessionItem>? ActiveItems { get; init; }

    [JsonPropertyName("itemCount")]
    public int ItemCount { get; init; }

    [JsonPropertyName("planTurnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlanTurnId { get; init; }

    [JsonPropertyName("toolArguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? ToolArguments { get; init; }

    [JsonPropertyName("toolProgress")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, ThreadToolProgressState>? ToolProgress { get; init; }

    [JsonPropertyName("diff")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Diff { get; init; }

    [JsonPropertyName("pendingRequests")]
    public required IReadOnlyList<ThreadPendingRequestState> PendingRequests { get; init; }
}

public sealed class ThreadToolProgressState : ExtensibleJsonObject
{
    [JsonPropertyName("callId")]
    public required string CallId { get; init; }

    [JsonPropertyName("toolName")]
    public required string ToolName { get; init; }

    [JsonPropertyName("progress")]
    public required JsonElement Progress { get; init; }
}

public sealed class ThreadPendingRequestState : ExtensibleJsonObject
{
    [JsonPropertyName("method")]
    public required string Method { get; init; }

    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    [JsonPropertyName("itemId")]
    public required string ItemId { get; init; }

    [JsonPropertyName("turnId")]
    public required string TurnId { get; init; }

    [JsonPropertyName("isBlocking")]
    public bool IsBlocking { get; init; }
}
