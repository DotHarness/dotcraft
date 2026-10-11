using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

public sealed class CommandExecParams : ExtensibleJsonObject
{
    [JsonPropertyName("command")] public required IReadOnlyList<string> Command { get; init; }

    [JsonPropertyName("cwd")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Cwd { get; init; }

    [JsonPropertyName("env")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, string?>> Env { get; init; }

    [JsonPropertyName("timeoutMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> TimeoutMs { get; init; }

    [JsonPropertyName("outputBytesCap")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> OutputBytesCap { get; init; }
}

public sealed class CommandExecResult : ExtensibleJsonObject
{
    [JsonPropertyName("exitCode")] public required int ExitCode { get; init; }

    [JsonPropertyName("stdout")] public required string Stdout { get; init; }

    [JsonPropertyName("stderr")] public required string Stderr { get; init; }
}
