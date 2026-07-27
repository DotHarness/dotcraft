namespace DotCraft.Protocol;

/// <summary>
/// Maps a channel-specific user context to a Thread.
/// Used for thread discovery and creation.
/// </summary>
public sealed record SessionIdentity
{
    /// <summary>
    /// The channel requesting the operation (e.g., "qq", "acp", "cli").
    /// </summary>
    public string ChannelName { get; init; } = string.Empty;

    /// <summary>
    /// Channel-specific user identifier.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Channel-specific context key (e.g., QQ group ID, ACP workspace URI).
    /// Allows multiple threads per user within the same channel.
    /// </summary>
    public string? ChannelContext { get; init; }

    /// <summary>
    /// The workspace this identity operates in.
    /// </summary>
    public string WorkspacePath { get; init; } = string.Empty;

    // Record equality is structural by default (all properties).
    // Override to exclude ChannelContext from the key used for thread discovery
    // so that callers can match threads by workspace + channel + user regardless of context.
    // Full structural equality (including ChannelContext) is preserved for exact matching.
}
