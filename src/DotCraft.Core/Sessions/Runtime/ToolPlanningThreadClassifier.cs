using DotCraft.Tools;

namespace DotCraft.Sessions;

/// <summary>Derives the trusted tool-planning thread kind from persisted Session state.</summary>
internal static class ToolPlanningThreadClassifier
{
    public const string InheritedToolSurfaceKey = "dotcraft.subagent.toolSurface";

    private const string AutomationsChannelName = "automations";

    /// <summary>
    /// Shapes the model-visible tool surface. A full-history SubAgent keeps its parent's surface so the
    /// cached prefix survives, while <see cref="Classify"/> still governs what it may execute.
    /// </summary>
    public static ToolPlanningThreadKind ClassifyToolSurface(SessionThread thread)
    {
        var kind = Classify(thread);
        return kind == ToolPlanningThreadKind.SubAgentChild
               && thread.Metadata.TryGetValue(InheritedToolSurfaceKey, out var inherited)
               && Enum.TryParse<ToolPlanningThreadKind>(inherited, out var parentKind)
            ? parentKind
            : kind;
    }

    public static ToolPlanningThreadKind Classify(SessionThread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        if (CacheSharingFork.PurposeOf(thread) is not null
            && thread.Metadata.TryGetValue(CacheSharingFork.ToolKindKey, out var inheritedKind)
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
