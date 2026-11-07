using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using DotCraft.Tools;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed partial class ThreadRequestHandler
{
    private PreparedThreadRestorePage? PrepareThreadRestorePage(
        string threadId, Contract.ThreadResumeInitialTurnsPageParams? options, CancellationToken ct)
    {
        if (options is null)
            return null;
        var limit = NormalizePageLimit(options.Limit, ThreadTurnsDefaultPageLimit, ThreadTurnsMaxPageLimit, "limit");
        var (view, tools) = ParseRestorationView(options);
        return new(view, tools,
            snapshot => ReadRestorationPageAsync(threadId, limit, view, tools, snapshot, ct));
    }

    private async Task<Contract.ThreadTurnsListResult> ReadRestorationPageAsync(
        string threadId, int limit, ThreadObservationView view, IReadOnlyList<ToolName> tools,
        ThreadObservationSnapshot snapshot, CancellationToken ct)
    {
        if (sessionService.TryGetLoadedThread(threadId) is { Ephemeral: true })
            return new() { Data = snapshot.Turn is null ? [] : [ProjectCurrentTurn(snapshot, view)] };

        var head = snapshot.Turn;
        if (head is null)
            return new() { Data = [] };
        var current = ProjectCurrentTurn(snapshot, view);
        var headAnchor = await sessionService.ResolveThreadTurnCursorAsync(threadId, head.Id, ct);
        ThreadHistoryCursor? olderCursor = headAnchor is { } durableHead
            ? durableHead with { Inclusive = false } : null;
        if (olderCursor is null)
        {
            var prepared = await sessionService.ListThreadTurnsAsync(
                threadId, null, limit, ThreadHistorySortDirection.Descending, ct);
            headAnchor = await sessionService.ResolveThreadTurnCursorAsync(threadId, head.Id, ct);
            olderCursor = headAnchor is { } nowDurable
                ? nowDurable with { Inclusive = false } : prepared.BackwardsCursor;
        }
        if (olderCursor is null)
            return new() { Data = [current] };

        if (limit == 1)
        {
            var omitted = await sessionService.ListThreadTurnsAsync(
                threadId, olderCursor, 1, ThreadHistorySortDirection.Descending, ct);
            return ProjectTurnsPage(threadId,
                new ThreadHistoryPage<Contract.SessionTurn>([current], omitted.BackwardsCursor, headAnchor));
        }
        var older = await ReadProjectedTurnsPageAsync(
            threadId, olderCursor, limit - 1, ThreadHistorySortDirection.Descending, view, tools, ct);
        return ProjectTurnsPage(threadId, older with
        {
            Data = new[] { current }.Concat(older.Data).ToArray(),
            BackwardsCursor = headAnchor ?? older.BackwardsCursor
        });
    }

    private sealed record PreparedThreadRestorePage(
        ThreadObservationView View,
        IReadOnlyList<ToolName> SummaryTools,
        Func<ThreadObservationSnapshot, Task<Contract.ThreadTurnsListResult>> MergeAsync);

    private async Task<ThreadHistoryPage<Contract.SessionTurn>> ReadProjectedTurnsPageAsync(
        string threadId, ThreadHistoryCursor? cursor, int limit, ThreadHistorySortDirection direction,
        ThreadObservationView view, IReadOnlyList<ToolName> summaryTools, CancellationToken ct)
    {
        try
        {
            if (view == ThreadObservationView.Summary)
            {
                var summaries = await sessionService.ListThreadSummariesAsync(
                    threadId, cursor, limit, direction, summaryTools, ct);
                return new(summaries.Data.Select(summary => AppServerContractMapper.ToContract(
                    ProjectTurnItems(summary.Turn),
                    itemsView: "summary",
                    itemCount: VisibleItemCount(summary.ItemCount, summary.ToolExecutionCount))).ToArray(),
                    summaries.NextCursor, summaries.BackwardsCursor);
            }

            var page = await sessionService.ListThreadTurnsAsync(threadId, cursor, limit, direction, ct);
            var turns = new Contract.SessionTurn[page.Data.Count];
            for (var i = 0; i < turns.Length; i++)
            {
                var turn = page.Data[i];
                var wire = turn.ToWire(includeItems: false);
                if (view == ThreadObservationView.Full)
                {
                    var items = await ReadFullTurnItemsAsync(threadId, turn.Id, ct);
                    wire = wire with { Items = items };
                    turns[i] = AppServerContractMapper.ToContract(wire, itemsView: "full", itemCount: items.Count);
                }
                else
                    turns[i] = AppServerContractMapper.ToContract(wire);
            }
            return new(turns, page.NextCursor, page.BackwardsCursor);
        }
        catch (NotSupportedException ex)
        {
            throw AppServerErrors.Unsupported(ex.Message);
        }
    }

    private SessionWireTurn ProjectTurnItems(SessionTurn turn) =>
        turn.ToWire(includeItems: false) with
        {
            Items = threadProjector.ProjectHistoryItems(
                turn.Items.Select(item => new ThreadHistoryItem(turn.Id, item)).ToArray())
                .Select(entry => entry.Item).ToList()
        };

    private Contract.SessionTurn ProjectCurrentTurn(ThreadObservationSnapshot snapshot, ThreadObservationView view)
    {
        var turn = snapshot.Turn!;
        return AppServerContractMapper.ToContract(
            view == ThreadObservationView.Metadata ? turn.ToWire(includeItems: false) : ProjectTurnItems(turn),
            itemsView: view switch
            {
                ThreadObservationView.Summary => "summary",
                ThreadObservationView.Full => "full",
                _ => null
            },
            itemCount: VisibleItemCount(snapshot.ItemCount, snapshot.ToolExecutionCount));
    }

    private int VisibleItemCount(int itemCount, int toolExecutionCount) =>
        connection.SupportsToolExecutionLifecycle ? itemCount : itemCount - toolExecutionCount;

    private static Contract.ThreadTurnsListResult ProjectTurnsPage(
        string threadId, ThreadHistoryPage<Contract.SessionTurn> page) => new()
    {
        Data = page.Data,
        NextCursor = page.NextCursor is { } next
            ? next.Inclusive
                ? ThreadHistoryCursorCodec.EncodeBackwards(threadId, "turns", next.RolloutOrdinal)
                : ThreadHistoryCursorCodec.Encode(threadId, "turns", null, "descending", next.RolloutOrdinal)
            : null,
        BackwardsCursor = page.BackwardsCursor is { } anchor
            ? ThreadHistoryCursorCodec.EncodeBackwards(threadId, "turns", anchor.RolloutOrdinal)
            : null
    };
}
