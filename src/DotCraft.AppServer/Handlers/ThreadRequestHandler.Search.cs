using DotCraft.AppBinding;
using DotCraft.Sessions;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed partial class ThreadRequestHandler
{
    private const string ThreadSearchCursorKind = "thread-search";

    private async Task<AppServerTypedResult<Contract.ThreadSearchResult>> HandleThreadSearchAsync(
        AppServerTypedRequest<Contract.ThreadSearchParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var searchTerm = p.SearchTerm?.Trim();
        if (string.IsNullOrEmpty(searchTerm))
            throw AppServerErrors.InvalidParams("'searchTerm' must not be empty.");
        var limit = NormalizePageLimit(p.Limit, ThreadListDefaultPageLimit, ThreadListMaxPageLimit, "limit");
        var offset = DecodeCursorOffset(p.Cursor, ThreadSearchCursorKind);
        Func<ThreadSummary, DateTimeOffset> sortKey = p.SortKey switch
        {
            null or "createdAt" => summary => summary.CreatedAt,
            "lastActiveAt" => summary => summary.LastActiveAt,
            _ => throw AppServerErrors.InvalidParams("'sortKey' must be 'createdAt' or 'lastActiveAt'.")
        };
        var ascending = ParseHistoryDirection(p.SortDirection) == ThreadHistorySortDirection.Ascending;
        var archived = p.Archived == true;

        var threads = (await sessionService.FindThreadsAsync(
                new SessionIdentity { WorkspacePath = hostWorkspacePath ?? string.Empty },
                includeArchived: archived,
                ct: ct,
                scope: ThreadDiscoveryScope.Workspace))
            .Where(t => (t.Status == ThreadStatus.Archived) == archived && !ThreadVisibility.IsInternal(t));
        var candidates = (ascending ? threads.OrderBy(sortKey) : threads.OrderByDescending(sortKey)).ToList();

        var matches = new List<Contract.ThreadSearchMatch>();
        var catalogByWorkspace = new Dictionary<string, AppCatalogSnapshot?>(StringComparer.Ordinal);
        string? nextCursor = null;
        for (var position = offset; position < candidates.Count; position++)
        {
            var summary = candidates[position];
            var snippet = await sessionService.FindThreadContentMatchAsync(summary.Id, searchTerm, ct);
            if (snippet == null)
                continue;
            if (matches.Count == limit)
            {
                nextCursor = EncodeCursor(ThreadSearchCursorKind, position);
                break;
            }

            await threadProjector.EnrichSummaryAsync(summary, catalogByWorkspace, ct);
            matches.Add(new Contract.ThreadSearchMatch
            {
                Thread = ThreadContractMapper.ToContract(summary),
                Snippet = snippet
            });
        }

        return AppServerTypedResult<Contract.ThreadSearchResult>.FromResult(new Contract.ThreadSearchResult
        {
            Data = matches,
            NextCursor = nextCursor
        });
    }
}
