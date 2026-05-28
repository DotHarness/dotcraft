using DotCraft.Dreams;

namespace DotCraft.Protocol;

/// <summary>
/// Classifies server-managed threads by user visibility.
/// </summary>
public static class ThreadVisibility
{
    /// <summary>
    /// Metadata key used by DotCraft-owned background helpers to mark an internal-only thread.
    /// </summary>
    public const string InternalMetadataKey = "dotcraft.internal";

    /// <summary>
    /// Returns whether the thread is an internal-only DotCraft helper thread.
    /// </summary>
    public static bool IsInternal(SessionThread thread) =>
        IsInternal(thread.OriginChannel, thread.Metadata);

    /// <summary>
    /// Returns whether the thread summary represents an internal-only DotCraft helper thread.
    /// </summary>
    public static bool IsInternal(ThreadSummary summary) =>
        IsInternal(summary.OriginChannel, summary.Metadata);

    /// <summary>
    /// Returns whether the origin and metadata identify an internal-only DotCraft helper thread.
    /// </summary>
    public static bool IsInternal(string? originChannel, IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata != null
            && metadata.TryGetValue(InternalMetadataKey, out var value)
            && !string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return string.Equals(originChannel, WelcomeSuggestionConstants.ChannelName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(originChannel, DreamsConstants.ChannelName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(originChannel, CommitMessageSuggestConstants.ChannelName, StringComparison.OrdinalIgnoreCase);
    }
}
