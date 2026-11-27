namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    public Action<ThreadTokenUsageUpdate>? ThreadTokenUsageUpdatedForBroadcast { get; set; }

    private void PublishThreadTokenUsage(
        SessionThread thread,
        SessionTurn turn,
        TokenUsageInfo turnUsage,
        TokenUsageInfo last,
        int? modelContextWindow)
    {
        var handler = ThreadTokenUsageUpdatedForBroadcast;
        if (handler is null)
            return;

        var total = turnUsage + thread.OmittedTokenUsage;
        foreach (var other in thread.Turns)
        {
            if (!string.Equals(other.Id, turn.Id, StringComparison.Ordinal) && other.TokenUsage is { } usage)
                total += usage;
        }

        var parentThreadId = thread.Source.SubAgent?.ParentThreadId;
        handler(new ThreadTokenUsageUpdate(
            thread.Id,
            string.IsNullOrEmpty(parentThreadId) ? null : parentThreadId,
            turn.Id,
            total,
            last,
            modelContextWindow));
    }
}
