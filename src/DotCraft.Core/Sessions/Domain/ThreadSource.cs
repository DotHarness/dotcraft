using System.Text.Json.Serialization;

namespace DotCraft.Sessions;

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

    /// <summary>
    /// Set when a top-level (non-subagent) thread was started from another thread,
    /// e.g. via the Desktop CreateThread tool. Holds the originating thread id while
    /// <see cref="Kind"/> stays <see cref="ThreadSourceKinds.User"/>, so the thread
    /// remains an ordinary sibling chat (not a subagent). Mirrored into thread
    /// metadata (<c>spawnedFromThreadId</c>) for durable client display.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SpawnedFromThreadId { get; set; }

    public static ThreadSource User() => new() { Kind = ThreadSourceKinds.User };

    public static ThreadSource SpawnedFromThread(string parentThreadId) =>
        new()
        {
            Kind = ThreadSourceKinds.User,
            SpawnedFromThreadId = parentThreadId
        };

    public static ThreadSource ForSubAgent(SubAgentThreadSource source) =>
        new()
        {
            Kind = ThreadSourceKinds.SubAgent,
            SubAgent = source
        };
}

public sealed class SubAgentThreadSource
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Purpose { get; set; }

    public string ParentThreadId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentTurnId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SpawnCallId { get; set; }

    public string RootThreadId { get; set; } = string.Empty;

    public int Depth { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentPath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TaskName { get; set; }

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

    public bool SupportsSendMessage { get; set; }

    public bool SupportsFollowupTask { get; set; }

    public bool SupportsClose { get; set; } = true;
}

internal sealed class PersistedThreadSource
{
    public int SchemaVersion { get; init; } = PersistedThreadSourceCodec.CurrentSchemaVersion;

    public string Kind { get; init; } = ThreadSourceKinds.User;

    public string? SpawnedFromThreadId { get; init; }

    public PersistedSubAgentThreadSource? SubAgent { get; init; }
}

internal sealed class PersistedSubAgentThreadSource
{
    public string? Purpose { get; init; }
    public string ParentThreadId { get; init; } = string.Empty;
    public string? ParentTurnId { get; init; }
    public string? SpawnCallId { get; init; }
    public string RootThreadId { get; init; } = string.Empty;
    public int Depth { get; init; }
    public string? AgentPath { get; init; }
    public string? TaskName { get; init; }
    public string? AgentNickname { get; init; }
    public string? AgentRole { get; init; }
    public string? ProfileName { get; init; }
    public string? RuntimeType { get; init; }
    public bool SupportsSendInput { get; init; }
    public bool SupportsResume { get; init; }
    public bool SupportsSendMessage { get; init; }
    public bool SupportsFollowupTask { get; init; }
    public bool SupportsClose { get; init; } = true;
}

internal static class PersistedThreadSourceCodec
{
    public const int CurrentSchemaVersion = 1;

    public static PersistedThreadSource Encode(ThreadSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Kind switch
        {
            ThreadSourceKinds.User => new PersistedThreadSource
            {
                Kind = ThreadSourceKinds.User,
                SpawnedFromThreadId = source.SpawnedFromThreadId
            },
            ThreadSourceKinds.SubAgent when source.SubAgent is { } subAgent => new PersistedThreadSource
            {
                Kind = ThreadSourceKinds.SubAgent,
                SubAgent = new PersistedSubAgentThreadSource
                {
                    Purpose = subAgent.Purpose,
                    ParentThreadId = subAgent.ParentThreadId,
                    ParentTurnId = subAgent.ParentTurnId,
                    SpawnCallId = subAgent.SpawnCallId,
                    RootThreadId = subAgent.RootThreadId,
                    Depth = subAgent.Depth,
                    AgentPath = subAgent.AgentPath,
                    TaskName = subAgent.TaskName,
                    AgentNickname = subAgent.AgentNickname,
                    AgentRole = subAgent.AgentRole,
                    ProfileName = subAgent.ProfileName,
                    RuntimeType = subAgent.RuntimeType,
                    SupportsSendInput = subAgent.SupportsSendInput,
                    SupportsResume = subAgent.SupportsResume,
                    SupportsSendMessage = subAgent.SupportsSendMessage,
                    SupportsFollowupTask = subAgent.SupportsFollowupTask,
                    SupportsClose = subAgent.SupportsClose
                }
            },
            ThreadSourceKinds.SubAgent => throw new InvalidOperationException("A subagent thread source requires subagent details."),
            _ => throw new NotSupportedException($"Unsupported thread source kind '{source.Kind}'.")
        };
    }

    public static ThreadSource Decode(PersistedThreadSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.SchemaVersion != CurrentSchemaVersion)
            throw new NotSupportedException($"Unsupported thread source schema version '{source.SchemaVersion}'.");

        return source.Kind switch
        {
            ThreadSourceKinds.User when source.SubAgent == null => string.IsNullOrWhiteSpace(source.SpawnedFromThreadId)
                ? ThreadSource.User()
                : ThreadSource.SpawnedFromThread(source.SpawnedFromThreadId),
            ThreadSourceKinds.SubAgent when source.SubAgent is { } subAgent => ThreadSource.ForSubAgent(
                new SubAgentThreadSource
                {
                    Purpose = subAgent.Purpose,
                    ParentThreadId = subAgent.ParentThreadId,
                    ParentTurnId = subAgent.ParentTurnId,
                    SpawnCallId = subAgent.SpawnCallId,
                    RootThreadId = subAgent.RootThreadId,
                    Depth = subAgent.Depth,
                    AgentPath = subAgent.AgentPath,
                    TaskName = subAgent.TaskName,
                    AgentNickname = subAgent.AgentNickname,
                    AgentRole = subAgent.AgentRole,
                    ProfileName = subAgent.ProfileName,
                    RuntimeType = subAgent.RuntimeType,
                    SupportsSendInput = subAgent.SupportsSendInput,
                    SupportsResume = subAgent.SupportsResume,
                    SupportsSendMessage = subAgent.SupportsSendMessage,
                    SupportsFollowupTask = subAgent.SupportsFollowupTask,
                    SupportsClose = subAgent.SupportsClose
                }),
            ThreadSourceKinds.User => throw new InvalidOperationException("A user thread source cannot contain subagent details."),
            ThreadSourceKinds.SubAgent => throw new InvalidOperationException("A subagent thread source requires subagent details."),
            _ => throw new NotSupportedException($"Unsupported thread source kind '{source.Kind}'.")
        };
    }

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
    public string? AgentPath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TaskName { get; set; }

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

    public bool SupportsSendMessage { get; set; }

    public bool SupportsFollowupTask { get; set; }

    public bool SupportsClose { get; set; } = true;

    public string Status { get; set; } = ThreadSpawnEdgeStatus.Open;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
