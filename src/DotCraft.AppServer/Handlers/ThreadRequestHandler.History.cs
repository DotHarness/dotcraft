using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed partial class ThreadRequestHandler
{
    private const int ThreadTurnsDefaultPageLimit = 20;
    private const int ThreadTurnsMaxPageLimit = 100;
    private const int ThreadItemsDefaultPageLimit = 100;
    private const int ThreadItemsMaxPageLimit = 100;
    private const int FullItemReadLimit = 100;
    private const int MaxSummaryTools = 32;

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
        var page = await ReadProjectedTurnsPageAsync(
            p.ThreadId, cursor,
            NormalizePageLimit(p.Limit, ThreadTurnsDefaultPageLimit, ThreadTurnsMaxPageLimit, "limit"),
            direction, summaryTools is null ? ThreadObservationView.Metadata : ThreadObservationView.Summary,
            summaryTools?.Select(tool => new DotCraft.Tools.ToolName(tool.Namespace, tool.Name)).ToArray() ?? [], ct);

        return AppServerTypedResult<Contract.ThreadTurnsListResult>.FromResult(new()
        {
            Data = page.Data,
            NextCursor = page.NextCursor is { } next
                ? ThreadHistoryCursorCodec.Encode(p.ThreadId, "turns", null, directionName, next.RolloutOrdinal, next.SnapshotOrdinal)
                : null,
            BackwardsCursor = page.BackwardsCursor is { } anchor
                ? ThreadHistoryCursorCodec.EncodeBackwards(p.ThreadId, "turns", anchor.RolloutOrdinal, anchor.SnapshotOrdinal)
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
        var limit = NormalizePageLimit(p.Limit, ThreadItemsDefaultPageLimit, ThreadItemsMaxPageLimit, "limit");
        var afterItemId = ThreadCurrentItemCursorCodec.Decode(p.Cursor, p.ThreadId, turnId, directionName);
        var currentResponseWritten = false;
        if (turnId is not null && (string.IsNullOrWhiteSpace(p.Cursor) || afterItemId is not null))
        {
            await WithCurrentThreadViewAsync(p.ThreadId, ThreadObservationView.Full, [], async snapshot =>
            {
                if (snapshot.Turn is not { } turn || turn.Id != turnId)
                    return;
                snapshot.EnsureCurrent();
                var selected = threadProjector.ProjectHistoryItems(
                    turn.Items.Select(item => new ThreadHistoryItem(turn.Id, item)).ToArray());
                await responseWriter.WriteResponseAsync(request.Message.Id, new Contract.ThreadItemsListResult
                {
                    Data = selected.Select(entry => new Contract.ThreadItemListEntry
                    {
                        TurnId = entry.TurnId,
                        Item = AppServerContractMapper.ToContract(entry.Item)
                    }).ToArray(),
                    NextCursor = snapshot.HasMoreItems && selected.Count > 0
                        ? ThreadCurrentItemCursorCodec.Encode(p.ThreadId, turnId, directionName, selected[^1].Item.Id)
                        : null
                }, ct);
                currentResponseWritten = true;
            }, ct, new ThreadObservationItemSelection
            {
                AfterItemId = afterItemId,
                Limit = limit,
                Direction = direction,
                IncludeToolExecutions = connection.SupportsToolExecutionLifecycle
            });
        }
        if (currentResponseWritten)
            return AppServerTypedResult<Contract.ThreadItemsListResult>.Written;

        var cursor = afterItemId is not null
            ? await sessionService.ResolveThreadItemCursorAsync(p.ThreadId, turnId!, afterItemId, ct)
                ?? throw AppServerErrors.InvalidParams("'cursor' no longer identifies an Item in this Turn.")
            : ThreadHistoryCursorCodec.Decode(p.Cursor, p.ThreadId, scope, turnId, directionName);
        ThreadHistoryPage<ThreadHistoryItem> page;
        try
        {
            page = await sessionService.ListThreadItemsAsync(
                p.ThreadId,
                turnId,
                cursor,
                limit,
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
                ? ThreadHistoryCursorCodec.Encode(p.ThreadId, scope, turnId, directionName, next.RolloutOrdinal, next.SnapshotOrdinal)
                : null
        });
    }

    private async Task<List<SessionWireItem>> ReadFullTurnItemsAsync(string threadId, string turnId, CancellationToken ct)
    {
        var items = new List<SessionWireItem>();
        ThreadHistoryCursor? cursor = null;
        do
        {
            var page = await sessionService.ListThreadItemsAsync(
                threadId, turnId, cursor, FullItemReadLimit, ThreadHistorySortDirection.Ascending, ct);
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
                if (summaryTools.Count > MaxSummaryTools)
                    throw AppServerErrors.InvalidParams($"'summaryTools' must have at most {MaxSummaryTools} entries.");
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
