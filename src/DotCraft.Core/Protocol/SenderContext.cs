namespace DotCraft.Protocol;

/// <summary>
/// Identifies the sender of a message within a Turn.
/// Used for group sessions (QQ, WeCom) and permission-aware channels.
/// </summary>
public sealed record SenderContext
{
    /// <summary>
    /// Individual user ID within the channel.
    /// </summary>
    public string SenderId { get; init; } = string.Empty;

    /// <summary>
    /// Display name of the sender.
    /// </summary>
    public string SenderName { get; init; } = string.Empty;

    /// <summary>
    /// Permission role: "admin", "whitelisted", "unauthorized".
    /// </summary>
    public string SenderRole { get; init; } = string.Empty;

    /// <summary>
    /// Group or chat ID for group sessions. Null for direct/private sessions.
    /// </summary>
    public string? GroupId { get; init; }
}
