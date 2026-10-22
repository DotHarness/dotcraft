namespace DotCraft.Sessions;

internal static class ThreadWorkingSet
{
    public static bool IsTerminal(SessionTurn turn) =>
        turn.Status is TurnStatus.Completed or TurnStatus.Failed or TurnStatus.Cancelled;

    public static void Trim(SessionThread thread)
    {
        if (thread.Ephemeral)
            return;

        var turns = thread.Turns;
        var keepFrom = turns.FindLastIndex(IsTerminal);
        var firstActive = turns.FindIndex(static turn => !IsTerminal(turn));
        if (firstActive >= 0 && firstActive < keepFrom)
            keepFrom = firstActive;
        if (keepFrom <= 0)
            return;

        var usage = thread.OmittedTokenUsage;
        for (var i = 0; i < keepFrom; i++)
        {
            if (turns[i].TokenUsage is { } turnUsage)
                usage += turnUsage;
        }

        thread.OmittedTokenUsage = usage;
        thread.OmittedTurnCount += keepFrom;
        turns.RemoveRange(0, keepFrom);
    }

    public static SessionTurn? FindResident(SessionThread thread, string turnId) =>
        thread.Turns.LastOrDefault(turn => string.Equals(turn.Id, turnId, StringComparison.Ordinal));
}

internal static class ThreadHistoryTotals
{
    public static ThreadHistoryAggregates Of(IEnumerable<SessionTurn> turns) =>
        turns.Aggregate(
            new ThreadHistoryAggregates(0, new TokenUsageInfo(), 0),
            static (total, turn) => new ThreadHistoryAggregates(
                total.TurnCount + 1,
                turn.TokenUsage is { } usage ? total.TokenUsage + usage : total.TokenUsage,
                total.ToolCallCount + turn.Items.Count(static item => item.IsToolInvocation)));

    public static ThreadHistoryAggregates Add(ThreadHistoryAggregates left, ThreadHistoryAggregates right) =>
        new(left.TurnCount + right.TurnCount, left.TokenUsage + right.TokenUsage, left.ToolCallCount + right.ToolCallCount);
}
