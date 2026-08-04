using DotCraft.Tools;

namespace DotCraft.Sessions;

/// <summary>Derives the trusted tool-planning thread kind from persisted Session state.</summary>
internal static class ToolPlanningThreadClassifier
{
    private const string TeamsChannelName = "teams";
    private const string AutomationsChannelName = "automations";
    private const string CronChannelName = "cron";
    private const string HeartbeatChannelName = "heartbeat";

    public static ToolPlanningThreadKind Classify(SessionThread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        if (thread.Ephemeral || ThreadVisibility.IsInternal(thread))
            return ToolPlanningThreadKind.Internal;

        if (thread.Source?.SubAgent is not null
            || string.Equals(thread.Source?.Kind, ThreadSourceKinds.SubAgent, StringComparison.OrdinalIgnoreCase)
            || string.Equals(thread.OriginChannel, SubAgentThreadOrigin.ChannelName, StringComparison.OrdinalIgnoreCase))
        {
            return ToolPlanningThreadKind.SubAgentChild;
        }

        if (!string.IsNullOrWhiteSpace(thread.Configuration?.AutomationTaskDirectory)
            || IsOrigin(thread.OriginChannel, AutomationsChannelName)
            || IsOrigin(thread.OriginChannel, CronChannelName)
            || IsOrigin(thread.OriginChannel, HeartbeatChannelName))
        {
            return ToolPlanningThreadKind.Unattended;
        }

        if (IsOrigin(thread.OriginChannel, TeamsChannelName))
            return ToolPlanningThreadKind.ModuleManaged;

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
