using DotCraft.Tools;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    public Task<ThreadHistoryCursor?> ResolveThreadTurnCursorAsync(
        string threadId, string turnId, CancellationToken ct = default) =>
        Persistence.ResolveThreadTurnCursorAsync(NormalizeRequiredThreadId(threadId), turnId, ct);

    public Task<ThreadHistoryCursor?> ResolveThreadItemCursorAsync(
        string threadId,
        string turnId,
        string itemId,
        CancellationToken ct = default)
    {
        var normalized = NormalizeRequiredThreadId(threadId);
        if (_runtimeRegistry.TryGetThread(normalized, out var thread) && thread.Ephemeral)
            throw new NotSupportedException("Paged history is not supported for ephemeral Threads.");
        return Persistence.ResolveThreadItemCursorAsync(normalized, turnId, itemId, ct);
    }

    public async Task<ThreadHistoryPage<ThreadTurnSummary>> ListThreadSummariesAsync(
        string threadId,
        ThreadHistoryCursor? cursor,
        int limit,
        ThreadHistorySortDirection direction,
        IReadOnlyList<ToolName> summaryTools,
        CancellationToken ct = default)
    {
        var normalized = NormalizeRequiredThreadId(threadId);
        if (_runtimeRegistry.TryGetThread(normalized, out var thread) && thread.Ephemeral)
            throw new NotSupportedException("Paged history is not supported for ephemeral Threads.");

        var page = await Persistence.ListThreadSummariesAsync(normalized, cursor, limit, direction, summaryTools, ct);
        if (_runtimeRegistry.TryGetRuntime(normalized, out _))
            return page;
        foreach (var summary in page.Data)
        {
            if (summary.Turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput)
                summary.Turn.Status = TurnStatus.Cancelled;
        }
        return page;
    }
}
