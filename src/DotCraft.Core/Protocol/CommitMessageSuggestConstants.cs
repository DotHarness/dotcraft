namespace DotCraft.Protocol;

/// <summary>
/// Channel identity and tool profile for ephemeral commit-message suggestion threads.
/// </summary>
public static class CommitMessageSuggestConstants
{
    public const string ToolProfileName = "commit-suggest";

    public const string ChannelName = "commit-suggest";

    public const string InternalUserId = "internal";

    /// <summary>Metadata key marking an internal thread (clients may filter).</summary>
    public const string InternalMetadataKey = ThreadVisibility.InternalMetadataKey;

    public const string InternalMetadataValue = "commit-suggest";
}
