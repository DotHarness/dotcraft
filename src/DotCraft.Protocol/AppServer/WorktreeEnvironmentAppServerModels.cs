using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

public sealed class WorktreeSetupResult : ExtensibleJsonObject
{
    [JsonPropertyName("status")] public required string Status { get; init; }

    [JsonPropertyName("exitCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> ExitCode { get; init; }

    [JsonPropertyName("outputTail")] public required string OutputTail { get; init; }
}

public sealed class WorktreeCreateProgressNotification : ExtensibleJsonObject
{
    [JsonPropertyName("operationId")] public required string OperationId { get; init; }

    [JsonPropertyName("step")] public required string Step { get; init; }

    [JsonPropertyName("status")] public required string Status { get; init; }
}

public sealed class WorktreeSetupOutputNotification : ExtensibleJsonObject
{
    [JsonPropertyName("operationId")] public required string OperationId { get; init; }

    [JsonPropertyName("delta")] public required string Delta { get; init; }
}

public sealed class WorktreeCancelCreateParams : ExtensibleJsonObject
{
    [JsonPropertyName("operationId")] public required string OperationId { get; init; }
}

public sealed class WorktreeRunSetupParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")] public required string ThreadId { get; init; }

    [JsonPropertyName("operationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> OperationId { get; init; }
}

public sealed class WorktreeRunSetupResult : ExtensibleJsonObject
{
    [JsonPropertyName("setup")] public required WorktreeSetupResult Setup { get; init; }
}

public sealed class WorktreeSetEnvironmentParams : ExtensibleJsonObject
{
    [JsonPropertyName("path")] public required string Path { get; init; }

    [JsonPropertyName("environmentPath")]
    [JsonRequired]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? EnvironmentPath { get; init; }
}

public sealed class WorktreeSetEnvironmentResult : ExtensibleJsonObject
{
    [JsonPropertyName("worktree")] public required ThreadWorktreeInfo Worktree { get; init; }
}
