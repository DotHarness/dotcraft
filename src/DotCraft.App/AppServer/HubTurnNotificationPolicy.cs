using DotCraft.Text;
using DotCraft.Protocol;
using DotCraft.Sessions;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.AppServer;

internal sealed record HubTurnNotificationSpec(
    string Kind,
    string TitleKey,
    string BodyKey,
    string Severity);

internal sealed record HubTurnNotificationDecision(
    bool ShouldNotify,
    string DisplayName,
    bool OpenDesktopOnClick,
    string? ThreadId);

internal static class HubTurnNotificationPolicy
{
    public static HubTurnNotificationSpec? GetSpec(SessionThreadRuntimeSignal signal) =>
        signal switch
        {
            SessionThreadRuntimeSignal.TurnCompleted => new HubTurnNotificationSpec(
                "turnCompleted",
                "hub.notification.turn_completed.title",
                "hub.notification.turn_completed.body",
                "success"),
            SessionThreadRuntimeSignal.TurnFailed => new HubTurnNotificationSpec(
                "turnFailed",
                "hub.notification.turn_failed.title",
                "hub.notification.turn_failed.body",
                "error"),
            _ => null
        };

    public static async Task<HubTurnNotificationDecision> ResolveDecisionAsync(
        ISessionService sessionService,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var thread = await sessionService.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
            if (ThreadVisibility.IsInternal(thread))
                return Suppress();

            if (IsSubAgentThread(thread))
                return Suppress();

            if (!string.IsNullOrWhiteSpace(thread.DisplayName))
                return Notify(
                    thread.DisplayName.Trim(),
                    IsDesktopOriginThread(thread),
                    thread.Id);

            return Notify(
                FallbackText.Format("hub.notification.thread.default"),
                IsDesktopOriginThread(thread),
                thread.Id);
        }
        catch
        {
            // Notifications are best-effort; falling back keeps turn completion isolated.
            // Without thread metadata, do not attach a Desktop-opening action.
        }

        return Notify(
            FallbackText.Format("hub.notification.thread.default"),
            openDesktopOnClick: false,
            threadId: null);
    }

    public static string BuildDesktopOpenActionUrl(string workspacePath, string threadId)
    {
        var url = "dotcraft://workspace/open?path=" + Uri.EscapeDataString(workspacePath);
        if (!string.IsNullOrWhiteSpace(threadId))
            url += "&threadId=" + Uri.EscapeDataString(threadId);
        return url;
    }

    private static HubTurnNotificationDecision Suppress() =>
        new(false, string.Empty, OpenDesktopOnClick: false, ThreadId: null);

    private static HubTurnNotificationDecision Notify(
        string displayName,
        bool openDesktopOnClick,
        string? threadId) =>
        new(true, displayName, openDesktopOnClick, openDesktopOnClick ? threadId : null);

    private static bool IsDesktopOriginThread(SessionThread thread) =>
        string.Equals(thread.OriginChannel, "dotcraft-desktop", StringComparison.OrdinalIgnoreCase);

    private static bool IsSubAgentThread(SessionThread thread)
    {
        if (string.Equals(thread.Source.Kind, ThreadSourceKinds.SubAgent, StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(thread.OriginChannel, SubAgentThreadOrigin.ChannelName, StringComparison.OrdinalIgnoreCase))
            return true;

        return !string.IsNullOrWhiteSpace(thread.ChannelContext)
            && thread.ChannelContext.StartsWith("thread_", StringComparison.OrdinalIgnoreCase);
    }
}
