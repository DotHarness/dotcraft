using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using Contract = DotCraft.Protocol.AppServer;
using SessionTurn = DotCraft.Sessions.SessionTurn;

namespace DotCraft.AppServer;

internal sealed partial class ThreadRequestHandler
{
    private const int ThreadTurnsDefaultPageLimit = 20;
    private const int ThreadTurnsMaxPageLimit = 100;
    private const int ThreadItemsDefaultPageLimit = 100;
    private const int ThreadItemsMaxPageLimit = 100;
    private const int SummaryItemReadLimit = 1000;

    private async Task<AppServerTypedResult<Contract.ThreadTurnsListResult>> HandleThreadTurnsListAsync(
        AppServerTypedRequest<Contract.ThreadTurnsListParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var summaryTools = ParseSummaryView(p.ItemsView, p.SummaryTools);
        var direction = ParseHistoryDirection(p.SortDirection);
        var directionName = HistoryDirectionName(direction);
        var cursor = ThreadHistoryCursorCodec.Decode(
            p.Cursor, p.ThreadId, "turns", null, directionName);
        ThreadHistoryPage<SessionTurn> page;
        try
        {
            page = await sessionService.ListThreadTurnsAsync(
                p.ThreadId,
                cursor,
                NormalizePageLimit(p.Limit, ThreadTurnsDefaultPageLimit, ThreadTurnsMaxPageLimit, "limit"),
                direction,
                ct);
        }
        catch (NotSupportedException ex)
        {
            throw AppServerErrors.Unsupported(ex.Message);
        }

        var turns = new Contract.SessionTurn[page.Data.Count];
        for (var i = 0; i < turns.Length; i++)
        {
            var wire = page.Data[i].ToWire(includeItems: false);
            if (summaryTools is null)
            {
                turns[i] = AppServerContractMapper.ToContract(wire);
                continue;
            }

            var items = await ReadVisibleTurnItemsAsync(p.ThreadId, wire.Id, ct);
            var summary = wire with { Items = TurnSummarySelector.Select(items, summaryTools).ToList() };
            turns[i] = AppServerContractMapper.ToContract(summary, itemsView: "summary", itemCount: items.Count);
        }

        return AppServerTypedResult<Contract.ThreadTurnsListResult>.FromResult(new()
        {
            Data = turns,
            NextCursor = page.NextCursor is { } next
                ? ThreadHistoryCursorCodec.Encode(p.ThreadId, "turns", null, directionName, next.RolloutOrdinal)
                : null,
            BackwardsCursor = page.BackwardsCursor is { } anchor
                ? ThreadHistoryCursorCodec.EncodeBackwards(p.ThreadId, "turns", anchor.RolloutOrdinal)
                : null
        });
    }

    private async Task<AppServerTypedResult<Contract.ThreadItemsListResult>> HandleThreadItemsListAsync(
        AppServerTypedRequest<Contract.ThreadItemsListParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var turnId = string.IsNullOrWhiteSpace(p.TurnId) ? null : p.TurnId;
        var scope = turnId is null ? "items" : "turn-items";
        var direction = ParseHistoryDirection(p.SortDirection);
        var directionName = HistoryDirectionName(direction);
        var cursor = ThreadHistoryCursorCodec.Decode(
            p.Cursor, p.ThreadId, scope, turnId, directionName);
        ThreadHistoryPage<ThreadHistoryItem> page;
        try
        {
            page = await sessionService.ListThreadItemsAsync(
                p.ThreadId,
                turnId,
                cursor,
                NormalizePageLimit(p.Limit, ThreadItemsDefaultPageLimit, ThreadItemsMaxPageLimit, "limit"),
                direction,
                ct);
        }
        catch (NotSupportedException ex)
        {
            throw AppServerErrors.Unsupported(ex.Message);
        }
        var projected = threadProjector.ProjectHistoryItems(page.Data);
        return AppServerTypedResult<Contract.ThreadItemsListResult>.FromResult(new()
        {
            Data = projected.Select(entry => new Contract.ThreadItemListEntry
            {
                TurnId = entry.TurnId,
                Item = AppServerContractMapper.ToContract(entry.Item)
            }).ToArray(),
            NextCursor = page.NextCursor is { } next
                ? ThreadHistoryCursorCodec.Encode(p.ThreadId, scope, turnId, directionName, next.RolloutOrdinal)
                : null
        });
    }

    private async Task<List<SessionWireItem>> ReadVisibleTurnItemsAsync(string threadId, string turnId, CancellationToken ct)
    {
        var items = new List<SessionWireItem>();
        ThreadHistoryCursor? cursor = null;
        do
        {
            var page = await sessionService.ListThreadItemsAsync(
                threadId, turnId, cursor, SummaryItemReadLimit, ThreadHistorySortDirection.Ascending, ct);
            items.AddRange(threadProjector.ProjectHistoryItems(page.Data).Select(entry => entry.Item));
            cursor = page.NextCursor;
        } while (cursor is not null);
        return items;
    }

    private static IReadOnlyList<Contract.ThreadSummaryTool>? ParseSummaryView(
        string? itemsView,
        IReadOnlyList<Contract.ThreadSummaryTool>? summaryTools)
    {
        switch (itemsView)
        {
            case null or "none":
                if (summaryTools is not null)
                    throw AppServerErrors.InvalidParams("'summaryTools' requires 'itemsView' to be 'summary'.");
                return null;
            case "summary":
                summaryTools ??= [];
                if (summaryTools.Count > TurnSummarySelector.MaxSummaryTools)
                    throw AppServerErrors.InvalidParams($"'summaryTools' must have at most {TurnSummarySelector.MaxSummaryTools} entries.");
                if (summaryTools.Any(tool => string.IsNullOrWhiteSpace(tool.Name)))
                    throw AppServerErrors.InvalidParams("'summaryTools' entries require a 'name'.");
                return summaryTools;
            default:
                throw AppServerErrors.InvalidParams("'itemsView' must be 'none' or 'summary'.");
        }
    }

    private static ThreadHistorySortDirection ParseHistoryDirection(string? value) => value switch
    {
        null or "descending" => ThreadHistorySortDirection.Descending,
        "ascending" => ThreadHistorySortDirection.Ascending,
        _ => throw AppServerErrors.InvalidParams("'sortDirection' must be 'ascending' or 'descending'.")
    };

    private static string HistoryDirectionName(ThreadHistorySortDirection direction) =>
        direction == ThreadHistorySortDirection.Ascending ? "ascending" : "descending";
}
