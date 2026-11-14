using DotCraft.Sessions;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed partial class ThreadRequestHandler
{
    private async Task<AppServerTypedResult<Contract.ThreadSearchOccurrencesResult>> HandleThreadSearchOccurrencesAsync(
        AppServerTypedRequest<Contract.ThreadSearchOccurrencesParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        if (string.IsNullOrWhiteSpace(p.ThreadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");
        var searchTerm = p.SearchTerm?.Trim();
        if (string.IsNullOrEmpty(searchTerm))
            throw AppServerErrors.InvalidParams("'searchTerm' must not be empty.");
        var limit = NormalizePageLimit(p.Limit, 20, 100, "limit");
        var cursor = ThreadMessageSearchCursorCodec.Decode(p.Cursor, p.ThreadId, searchTerm);
        ThreadMessageSearchPage page;
        try
        {
            page = await sessionService.SearchThreadMessageOccurrencesAsync(
                p.ThreadId, searchTerm, cursor, limit, ct);
        }
        catch (NotSupportedException ex)
        {
            throw AppServerErrors.Unsupported(ex.Message);
        }

        return AppServerTypedResult<Contract.ThreadSearchOccurrencesResult>.FromResult(new()
        {
            Data = page.Data.Select(hit => new Contract.ThreadSearchOccurrence
            {
                TurnId = hit.TurnId,
                ItemId = hit.ItemId,
                Snippet = hit.Snippet,
                SnippetMatchRange = new Contract.ThreadSearchTextRange
                {
                    Start = hit.SnippetMatchStart,
                    End = hit.SnippetMatchEnd
                },
                TurnCursor = ThreadHistoryCursorCodec.EncodeBackwards(p.ThreadId, "turns", hit.TurnOrdinal)
            }).ToArray(),
            NextCursor = page.NextCursor is { } next
                ? ThreadMessageSearchCursorCodec.Encode(p.ThreadId, searchTerm, next)
                : null
        });
    }

    private async Task<AppServerTypedResult<Contract.ThreadItemsReadResult>> HandleThreadItemsReadAsync(
        AppServerTypedRequest<Contract.ThreadItemsReadParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        if (string.IsNullOrWhiteSpace(p.ThreadId) || string.IsNullOrWhiteSpace(p.TurnId))
            throw AppServerErrors.InvalidParams("'threadId' and 'turnId' are required.");
        if ((p.ItemIds is null) == (p.CallIds is null))
            throw AppServerErrors.InvalidParams("Specify exactly one of 'itemIds' or 'callIds'.");
        var ids = p.ItemIds ?? p.CallIds!;
        if (ids.Count is < 1 or > 100 || ids.Any(string.IsNullOrWhiteSpace))
            throw AppServerErrors.InvalidParams("The selector must contain 1 to 100 non-empty IDs.");
        var currentResponseWritten = false;
        await WithCurrentThreadViewAsync(p.ThreadId, ThreadObservationView.Full, [], async snapshot =>
        {
            if (snapshot.Turn is not { } turn || turn.Id != p.TurnId)
                return;
            snapshot.EnsureCurrent();
            await responseWriter.WriteResponseAsync(request.Message.Id, new Contract.ThreadItemsReadResult
            {
                Data = threadProjector.ProjectHistoryItems(
                    turn.Items.Select(item => new ThreadHistoryItem(turn.Id, item)).ToArray())
                    .Select(entry => new Contract.ThreadItemListEntry
                    {
                        TurnId = entry.TurnId,
                        Item = AppServerContractMapper.ToContract(entry.Item)
                    }).ToArray()
            }, ct);
            currentResponseWritten = true;
        }, ct, new ThreadObservationItemSelection
        {
            ItemIds = p.ItemIds?.ToHashSet(StringComparer.Ordinal),
            CallIds = p.CallIds?.ToHashSet(StringComparer.Ordinal),
            IncludeToolExecutions = connection.SupportsToolExecutionLifecycle
        });
        if (currentResponseWritten)
            return AppServerTypedResult<Contract.ThreadItemsReadResult>.Written;

        IReadOnlyList<ThreadHistoryItem> items;
        try
        {
            items = p.CallIds is { } calls
                ? await sessionService.ReadThreadCallItemsAsync(p.ThreadId, p.TurnId, calls, ct)
                : await sessionService.ReadThreadItemsAsync(p.ThreadId, p.TurnId, p.ItemIds!, ct);
        }
        catch (NotSupportedException ex)
        {
            throw AppServerErrors.Unsupported(ex.Message);
        }

        return AppServerTypedResult<Contract.ThreadItemsReadResult>.FromResult(new()
        {
            Data = threadProjector.ProjectHistoryItems(items).Select(entry => new Contract.ThreadItemListEntry
            {
                TurnId = entry.TurnId,
                Item = AppServerContractMapper.ToContract(entry.Item)
            }).ToArray()
        });
    }
}
