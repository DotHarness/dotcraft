using System.Text.Json.Serialization;

namespace DotCraft.Protocol;

public static class ThreadSourceKinds
{
    public const string User = "user";
    public const string SubAgent = "subagent";
}

public static class SubAgentThreadOrigin
{
    public const string ChannelName = "subagent";
}

/// <summary>
/// Describes why a thread exists. User threads are top-level conversations;
/// subagent threads are child sessions spawned from another thread turn.
/// </summary>
public sealed class ThreadSource
{
    public string Kind { get; set; } = ThreadSourceKinds.User;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SubAgentThreadSource? SubAgent { get; set; }

    public static ThreadSource User() => new() { Kind = ThreadSourceKinds.User };

    public static ThreadSource ForSubAgent(SubAgentThreadSource source) =>
        new()
        {
            Kind = ThreadSourceKinds.SubAgent,
            SubAgent = source
        };
}

public sealed class SubAgentThreadSource
{
    public string ParentThreadId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentTurnId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SpawnCallId { get; set; }

    public string RootThreadId { get; set; } = string.Empty;

    public int Depth { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentNickname { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentRole { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProfileName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RuntimeType { get; set; }

    public bool SupportsSendInput { get; set; }

    public bool SupportsResume { get; set; }

    public bool SupportsClose { get; set; } = true;
}

public static class ThreadSpawnEdgeStatus
{
    public const string Open = "open";
    public const string Closed = "closed";
}

public sealed class ThreadSpawnEdge
{
    public string ParentThreadId { get; set; } = string.Empty;

    public string ChildThreadId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentTurnId { get; set; }

    public int Depth { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentNickname { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentRole { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProfileName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RuntimeType { get; set; }

    public bool SupportsSendInput { get; set; }

    public bool SupportsResume { get; set; }

    public bool SupportsClose { get; set; } = true;

    public string Status { get; set; } = ThreadSpawnEdgeStatus.Open;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
