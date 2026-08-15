namespace DotCraft.AppServer;

/// <summary>
/// Provides runtime enabled/running status for all configured social and external channels.
/// Consumed by <see cref="AppServerRequestHandler"/> to serve the <c>channel/status</c> wire method
/// (spec Section 20).
/// </summary>
public interface IChannelStatusProvider
{
    /// <summary>
    /// Returns the current status of every configured social and external channel.
    /// Results are sorted by category (<c>social</c> before <c>external</c>), then by name.
    /// </summary>
    IReadOnlyList<ChannelStatusSnapshot> GetChannelStatuses();
}

/// <summary>Runtime channel state kept separate from the stable AppServer DTO.</summary>
public sealed record ChannelStatusSnapshot
{
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public bool Running { get; init; }
    public string? RuntimeState { get; init; }
    public string? FailureCode { get; init; }
}

/// <summary>Stable lifecycle values reported by <c>channel/status</c>.</summary>
public static class ChannelRuntimeStates
{
    public const string Stopped = "stopped";
    public const string Starting = "starting";
    public const string Running = "running";
    public const string Failed = "failed";
}

/// <summary>Stable channel failure codes reported by <c>channel/status</c>.</summary>
public static class ChannelFailureCodes
{
    public const string ExternalChannelStartFailed = "externalChannelStartFailed";
}
