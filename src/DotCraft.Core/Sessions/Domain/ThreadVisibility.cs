using DotCraft.Dreams;

namespace DotCraft.Sessions;

public static class ThreadVisibility
{
    /// <summary>Metadata key DotCraft-owned background helpers set to mark an internal-only thread.</summary>
    public const string InternalMetadataKey = "dotcraft.internal";

    /// <summary>Metadata marker value used for conversational Agent Builder threads.</summary>
    public const string AgentBuilderInternalValue = "agent-builder";

    /// <summary>Conversational Agent Builder threads, bound to a builder target, also count as internal.</summary>
    public static bool IsInternal(SessionThread thread) =>
        !string.IsNullOrWhiteSpace(thread.Configuration?.AgentBuilderTargetId)
        || IsInternal(thread.OriginChannel, thread.Metadata);

    /// <summary>Returns whether workspace-level thread notifications reach only the thread's subscribers.</summary>
    public static bool IsHidden(SessionThread thread) => thread.Ephemeral || IsInternal(thread);

    public static bool IsInternal(ThreadSummary summary) =>
        IsInternal(summary.OriginChannel, summary.Metadata);

    public static bool IsInternal(string? originChannel, IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata != null
            && metadata.TryGetValue(InternalMetadataKey, out var value)
            && !string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return string.Equals(originChannel, DreamsConstants.ChannelName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(originChannel, CommitMessageSuggestConstants.ChannelName, StringComparison.OrdinalIgnoreCase);
    }
}
