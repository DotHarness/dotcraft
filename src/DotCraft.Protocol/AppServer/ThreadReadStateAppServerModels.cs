using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

public sealed class ThreadReadState : ExtensibleJsonObject
{
    /// <summary><c>null</c> when the thread is read.</summary>
    [JsonPropertyName("firstUnread")]
    [JsonRequired]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public ThreadFirstUnread? FirstUnread { get; init; }

    /// <summary>Opaque token that changes on every receipt write; compare it only for equality within its thread.</summary>
    [JsonPropertyName("revision")]
    public required string Revision { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ThreadFirstUnreadThreadStart), "threadStart")]
[JsonDerivedType(typeof(ThreadFirstUnreadTurn), "turn")]
public abstract class ThreadFirstUnread : ExtensibleJsonObject;

public sealed class ThreadFirstUnreadThreadStart : ThreadFirstUnread;

public sealed class ThreadFirstUnreadTurn : ThreadFirstUnread
{
    [JsonPropertyName("turnId")]
    public required string TurnId { get; init; }
}

public sealed class ThreadReadStateUpdateParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("expectedRevision")]
    public required string ExpectedRevision { get; init; }

    [JsonPropertyName("operation")]
    public required ThreadReadStateOperation Operation { get; init; }
}

public sealed class ThreadReadStateOperation : ExtensibleJsonObject
{
    /// <summary><c>read</c> or <c>unread</c>.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }
}

public sealed class ThreadReadStateUpdateResult : ExtensibleJsonObject
{
    [JsonPropertyName("readState")]
    public required ThreadReadState ReadState { get; init; }
}

public sealed class ThreadReadStateChangedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("readState")]
    public required ThreadReadState ReadState { get; init; }
}
