using DotCraft.Tools;

namespace DotCraft.Sessions;

/// <summary>Derives the trusted tool-planning thread kind from persisted Session state.</summary>
internal static class ToolPlanningThreadClassifier
{
    private const string AutomationsChannelName = "automations";

    public static ToolPlanningThreadKind Classify(SessionThread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        if (thread.Ephemeral
            && thread.Metadata.TryGetValue(ThreadVisibility.InternalMetadataKey, out var internalKind)
            && string.Equals(internalKind, PromptSuggestionThread.InternalValue, StringComparison.Ordinal)
            && thread.Metadata.TryGetValue(PromptSuggestionThread.ToolKindKey, out var inheritedKind)
            && Enum.TryParse<ToolPlanningThreadKind>(inheritedKind, out var kind))
        {
            return kind;
        }

        if (thread.Ephemeral || ThreadVisibility.IsInternal(thread))
            return ToolPlanningThreadKind.Internal;

        if (thread.Source?.SubAgent is not null
            || string.Equals(thread.Source?.Kind, ThreadSourceKinds.SubAgent, StringComparison.OrdinalIgnoreCase)
            || string.Equals(thread.OriginChannel, SubAgentThreadOrigin.ChannelName, StringComparison.OrdinalIgnoreCase))
        {
            return ToolPlanningThreadKind.SubAgentChild;
        }

        if (!string.IsNullOrWhiteSpace(thread.Configuration?.AutomationTaskDirectory)
            || IsOrigin(thread.OriginChannel, AutomationsChannelName))
        {
            return ToolPlanningThreadKind.Unattended;
        }

        if (string.Equals(thread.Source?.Kind, ThreadSourceKinds.User, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(thread.OriginChannel))
        {
            return ToolPlanningThreadKind.UserTopLevel;
        }

        return ToolPlanningThreadKind.Unknown;
    }

    private static bool IsOrigin(string? actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
}
