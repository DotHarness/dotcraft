namespace DotCraft.AppServer;

/// <summary>
/// Reports workspace activation intent and current channel activity.
/// </summary>
public interface IChannelStatusProvider
{
    /// <summary>
    /// Returns status for configured channels, including stopped or blocked selections.
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
    public string? PluginId { get; init; }
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
    public const string ChannelStartFailed = "channelStartFailed";
}
