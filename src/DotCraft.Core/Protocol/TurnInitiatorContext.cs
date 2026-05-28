namespace DotCraft.Protocol;

/// <summary>
/// Durable metadata describing who initiated a turn.
/// </summary>
public sealed class TurnInitiatorContext
{
    /// <summary>
    /// Channel that initiated the turn.
    /// </summary>
    public string ChannelName { get; set; } = string.Empty;

    /// <summary>
    /// User identifier associated with the initiating request.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Display name associated with the initiating request.
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// Role associated with the initiating request.
    /// </summary>
    public string? UserRole { get; set; }

    /// <summary>
    /// Channel-specific context for the initiating request.
    /// </summary>
    public string? ChannelContext { get; set; }

    /// <summary>
    /// Group or chat identifier when the request originates from a group context.
    /// </summary>
    public string? GroupId { get; set; }
}
