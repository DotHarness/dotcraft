namespace DotCraft.Abstractions;

/// <summary>
/// Channel-agnostic session metadata set by each channel adapter at the start
/// of a request. Consumed by shared infrastructure (e.g. CronTools) so it does
/// not need to reference channel-specific types.
/// </summary>
public sealed class ChannelSessionInfo
{
    public string Channel { get; init; } = "";

    public string UserId { get; init; } = "";

    public string? GroupId { get; init; }

    /// <summary>
    /// Pre-formatted delivery target for the current session context.
    /// For QQ group: "group:{groupId}". For WeCom: the ChatId. Null for private/unknown.
    /// </summary>
    public string? DefaultDeliveryTarget { get; init; }
}

public static class ChannelSessionScope
{
    private static readonly AsyncLocal<ChannelSessionInfo?> CurrentContext = new();

    public static ChannelSessionInfo? Current => CurrentContext.Value;

    public static IDisposable Set(ChannelSessionInfo info)
    {
        CurrentContext.Value = info;
        return new ScopeHandle();
    }

    private sealed class ScopeHandle : IDisposable
    {
        public void Dispose() => CurrentContext.Value = null;
    }
}
