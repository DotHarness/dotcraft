using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

public sealed class DynamicWorkflowCapabilities : ExtensibleJsonObject
{
    [JsonPropertyName("version")] public required int Version { get; init; }
    [JsonPropertyName("list")] public required bool List { get; init; }
    [JsonPropertyName("read")] public required bool Read { get; init; }
    [JsonPropertyName("pause")] public required bool Pause { get; init; }
    [JsonPropertyName("stop")] public required bool Stop { get; init; }
    [JsonPropertyName("resume")] public required bool Resume { get; init; }
    [JsonPropertyName("notifications")] public required bool Notifications { get; init; }
}

public sealed class WorkflowRunControls : ExtensibleJsonObject
{
    [JsonPropertyName("canPause")] public required bool CanPause { get; init; }
    [JsonPropertyName("canStop")] public required bool CanStop { get; init; }
    [JsonPropertyName("canResume")] public required bool CanResume { get; init; }
}

public sealed class WorkflowRunTotals : ExtensibleJsonObject
{
    [JsonPropertyName("agentCount")] public required int AgentCount { get; init; }
    [JsonPropertyName("queuedCount")] public required int QueuedCount { get; init; }
    [JsonPropertyName("runningCount")] public required int RunningCount { get; init; }
    [JsonPropertyName("completedCount")] public required int CompletedCount { get; init; }
    [JsonPropertyName("failedCount")] public required int FailedCount { get; init; }
    [JsonPropertyName("stoppedCount")] public required int StoppedCount { get; init; }
    [JsonPropertyName("replayedCount")] public required int ReplayedCount { get; init; }
    [JsonPropertyName("inputTokens")] [JsonSafeInteger] public required long InputTokens { get; init; }
    [JsonPropertyName("outputTokens")] [JsonSafeInteger] public required long OutputTokens { get; init; }
    [JsonPropertyName("toolCallCount")] public required int ToolCallCount { get; init; }
}

public sealed class WorkflowAgentView : ExtensibleJsonObject
{
    [JsonPropertyName("operationId")] public required string OperationId { get; init; }
    [JsonPropertyName("label")] public required string Label { get; init; }
    [JsonPropertyName("phase")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<string> Phase { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("childThreadId")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<string> ChildThreadId { get; init; }
    [JsonPropertyName("inputTokens")] [JsonSafeInteger] public required long InputTokens { get; init; }
    [JsonPropertyName("outputTokens")] [JsonSafeInteger] public required long OutputTokens { get; init; }
    [JsonPropertyName("toolCallCount")] public required int ToolCallCount { get; init; }
    [JsonPropertyName("requestedAt")] public required DateTimeOffset RequestedAt { get; init; }
    [JsonPropertyName("startedAt")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<DateTimeOffset> StartedAt { get; init; }
    [JsonPropertyName("completedAt")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<DateTimeOffset> CompletedAt { get; init; }
    [JsonPropertyName("replayed")] public required bool Replayed { get; init; }
}

public sealed class WorkflowPhaseView : ExtensibleJsonObject
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("detail")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<string> Detail { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("agents")] public required IReadOnlyList<WorkflowAgentView> Agents { get; init; }
}

public class WorkflowRunSummary : ExtensibleJsonObject
{
    [JsonPropertyName("runId")] public required string RunId { get; init; }
    [JsonPropertyName("threadId")] public required string ThreadId { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("description")] public required string Description { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("createdAt")] public required DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("startedAt")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<DateTimeOffset> StartedAt { get; init; }
    [JsonPropertyName("completedAt")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<DateTimeOffset> CompletedAt { get; init; }
    [JsonPropertyName("resumedFromRunId")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<string> ResumedFromRunId { get; init; }
    [JsonPropertyName("totals")] public required WorkflowRunTotals Totals { get; init; }
    [JsonPropertyName("controls")] public required WorkflowRunControls Controls { get; init; }
}

public sealed class WorkflowRunView : WorkflowRunSummary
{
    [JsonPropertyName("phases")] public required IReadOnlyList<WorkflowPhaseView> Phases { get; init; }
    [JsonPropertyName("unphasedAgents")] public required IReadOnlyList<WorkflowAgentView> UnphasedAgents { get; init; }
    [JsonPropertyName("result")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<JsonElement?> Result { get; init; }
    [JsonPropertyName("error")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<string> Error { get; init; }
}

public sealed class WorkflowRunListParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")] public required string ThreadId { get; init; }
    [JsonPropertyName("limit")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<int> Limit { get; init; }
    [JsonPropertyName("cursor")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<string> Cursor { get; init; }
}

public sealed class WorkflowRunListResult : ExtensibleJsonObject
{
    [JsonPropertyName("runs")] public required IReadOnlyList<WorkflowRunSummary> Runs { get; init; }
    [JsonPropertyName("nextCursor")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<string> NextCursor { get; init; }
}

public sealed class WorkflowRunParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")] public required string ThreadId { get; init; }
    [JsonPropertyName("runId")] public required string RunId { get; init; }
}

public sealed class WorkflowRunReadResult : ExtensibleJsonObject
{
    [JsonPropertyName("run")] public required WorkflowRunView Run { get; init; }
}

public sealed class WorkflowRunResumeParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")] public required string ThreadId { get; init; }
    [JsonPropertyName("runId")] public required string RunId { get; init; }
    [JsonPropertyName("args")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<JsonElement?> Args { get; init; }
}

public sealed class WorkflowRunResumeResult : ExtensibleJsonObject
{
    [JsonPropertyName("sourceRunId")] public required string SourceRunId { get; init; }
    [JsonPropertyName("run")] public required WorkflowRunView Run { get; init; }
}

public sealed class WorkflowRunUpdatedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")] public required string ThreadId { get; init; }
    [JsonPropertyName("runId")] public required string RunId { get; init; }
    [JsonPropertyName("reason")] public required string Reason { get; init; }
}
