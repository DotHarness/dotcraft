using System.Globalization;
using DotCraft.AppBinding;
using DotCraft.Commands.Core;
using DotCraft.Sessions;

namespace DotCraft.Commands.Handlers;

internal static class CommandThreads
{
    private const int ConversationListLimit = 10;

    public static SessionTurn? ActiveTurn(SessionThread thread) =>
        thread.Turns.LastOrDefault(static turn =>
            turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput);

    public static bool IsBoundToChat(CommandContext context) =>
        context.AppBindings is { } bindings
        && !string.IsNullOrWhiteSpace(context.WorkspaceCraftPath)
        && Directory.Exists(context.WorkspaceCraftPath)
        && bindings.ListThreadBindings(context.WorkspaceCraftPath, context.SessionId)
            .Any(static binding => binding.ChannelTarget != null && binding.State != AppBindingStates.Revoked);

    public static SessionIdentity IdentityOf(SessionThread thread) => new()
    {
        ChannelName = thread.OriginChannel,
        UserId = thread.UserId,
        ChannelContext = thread.ChannelContext,
        WorkspacePath = thread.WorkspacePath
    };

    public static async Task<IReadOnlyList<Numbered<ThreadSummary>>> NumberConversationAsync(
        ISessionService sessions,
        SessionThread current)
    {
        var threads = await sessions.FindThreadsAsync(IdentityOf(current), includeArchived: true);
        return Number(threads, static thread => thread.CreatedAt, static thread => thread.Id);
    }

    public static IReadOnlyList<Numbered<ThreadSummary>> RecentFirst(IReadOnlyList<Numbered<ThreadSummary>> threads) =>
        threads
            .OrderByDescending(static entry => entry.Value.LastActiveAt)
            .ThenByDescending(static entry => entry.Number)
            .Take(ConversationListLimit)
            .ToArray();

    public static IReadOnlyList<Numbered<T>> Number<T>(
        IEnumerable<T> items,
        Func<T, DateTimeOffset> createdAt,
        Func<T, string> id) =>
        items
            .OrderBy(createdAt)
            .ThenBy(id, StringComparer.Ordinal)
            .Select(static (item, index) => new Numbered<T>(index + 1, item))
            .ToArray();

    public static Numbered<T>? Select<T>(IReadOnlyList<Numbered<T>> entries, string? argument, Func<T, string> id)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return null;

        var byId = entries.FirstOrDefault(entry => string.Equals(id(entry.Value), argument, StringComparison.Ordinal));
        if (byId != null)
            return byId;

        return int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? entries.FirstOrDefault(entry => entry.Number == number)
            : null;
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
}

internal sealed record Numbered<T>(int Number, T Value);
