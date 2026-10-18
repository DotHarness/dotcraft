using System.Globalization;
using DotCraft.Sessions;

namespace DotCraft.Commands.Handlers;

internal static class CommandThreads
{
    private const int ConversationListLimit = 10;

    public static SessionTurn? ActiveTurn(SessionThread thread) =>
        thread.Turns.LastOrDefault(static turn =>
            turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput);

    public static SessionIdentity IdentityOf(SessionThread thread) => new()
    {
        ChannelName = thread.OriginChannel,
        UserId = thread.UserId,
        ChannelContext = thread.ChannelContext,
        WorkspacePath = thread.WorkspacePath
    };

    public static async Task<IReadOnlyList<ThreadSummary>> ListConversationAsync(
        ISessionService sessions,
        SessionThread current)
    {
        var threads = await sessions.FindThreadsAsync(IdentityOf(current), includeArchived: true);
        return threads
            .OrderByDescending(static thread => thread.LastActiveAt)
            .ThenByDescending(static thread => thread.CreatedAt)
            .Take(ConversationListLimit)
            .ToArray();
    }

    public static string Title(string? displayName, string threadId) =>
        string.IsNullOrWhiteSpace(displayName) ? threadId : displayName.Trim();

    public static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        if (elapsed.TotalMinutes < 1)
            return string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalSeconds}s");
        if (elapsed.TotalHours < 1)
            return string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s");
        if (elapsed.TotalDays < 1)
            return string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m");
        return string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalDays}d {elapsed.Hours}h");
    }

    public static bool TryParseIndex(string? value, int count, out int index)
    {
        index = -1;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number < 1
            || number > count)
        {
            return false;
        }

        index = number - 1;
        return true;
    }
}
