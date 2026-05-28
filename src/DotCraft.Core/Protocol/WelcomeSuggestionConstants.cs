namespace DotCraft.Protocol;

/// <summary>
/// Channel identity and tool profile constants for ephemeral welcome-suggestion threads.
/// </summary>
public static class WelcomeSuggestionConstants
{
    public const string ToolProfileName = "welcome-suggest";

    public const string ChannelName = "welcome-suggest";

    public const string InternalUserId = "internal";

    public const string InternalMetadataKey = ThreadVisibility.InternalMetadataKey;

    public const string InternalMetadataValue = "welcome-suggest";
}
