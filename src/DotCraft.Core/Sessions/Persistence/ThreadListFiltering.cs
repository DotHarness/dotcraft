namespace DotCraft.Sessions;

internal static class ThreadListFiltering
{
    public static ThreadListPage Apply(
        IEnumerable<ThreadSummary> stored,
        IReadOnlyList<ThreadSummary> live,
        ThreadListQuery query)
    {
        var summaries = new Dictionary<string, ThreadSummary>(StringComparer.Ordinal);
        foreach (var summary in stored.Concat(live).Where(summary => !summary.Ephemeral))
            summaries[summary.Id] = summary;
        var matches = summaries.Values
            .Where(summary => Matches(summary, summaries, query))
            .OrderByDescending(summary => summary.LastActiveAt)
            .ThenByDescending(summary => summary.Id, StringComparer.Ordinal)
            .ToList();
        IEnumerable<ThreadSummary> page = matches;
        if (query.Cursor is { } cursor)
            page = page.Where(summary => summary.LastActiveAt < cursor.LastActiveAt
                || summary.LastActiveAt == cursor.LastActiveAt
                    && string.CompareOrdinal(summary.Id, cursor.ThreadId) < 0);
        var items = (query.Limit is { } limit ? page.Take(limit + 1) : page).ToList();
        var hasMore = query.Limit is { } pageSize && items.Count > pageSize;
        if (hasMore)
            items.RemoveAt(items.Count - 1);
        return new ThreadListPage(items, matches.Count, hasMore);
    }

    private static bool Matches(
        ThreadSummary summary,
        IReadOnlyDictionary<string, ThreadSummary> summaries,
        ThreadListQuery query)
    {
        var child = string.Equals(summary.Source.Kind, ThreadSourceKinds.SubAgent, StringComparison.OrdinalIgnoreCase)
            || string.Equals(summary.OriginChannel, SubAgentThreadOrigin.ChannelName, StringComparison.OrdinalIgnoreCase);
        if (!query.IncludeArchived && summary.Status == ThreadStatus.Archived
            || !query.IncludeSubAgents && child
            || !query.IncludeInternal && ThreadVisibility.IsInternal(summary))
            return false;
        var parentId = summary.Source.SubAgent?.ParentThreadId?.Trim();
        if (string.IsNullOrWhiteSpace(parentId))
            parentId = summary.ChannelContext?.Trim();
        if (!query.IncludeArchived && child && parentId is not null
            && summaries.TryGetValue(parentId, out var parent) && parent.Status == ThreadStatus.Archived)
            return false;

        if (query.Scope == ThreadDiscoveryScope.Identity)
        {
            var userMatches = query.Identity.UserId is null || summary.UserId == query.Identity.UserId;
            var identityMatches = userMatches && summary.ChannelContext == query.Identity.ChannelContext;
            var childMatches = query.IncludeSubAgents && child && userMatches;
            var originMatches = query.CrossChannelOrigins?.Any(origin =>
                string.Equals(origin, summary.OriginChannel, StringComparison.OrdinalIgnoreCase)) == true;
            if (!identityMatches && !childMatches && !originMatches)
                return false;
        }
        if (!string.IsNullOrEmpty(query.ChannelName)
            && !string.Equals(summary.OriginChannel, query.ChannelName, StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrWhiteSpace(query.Query))
            return true;
        var term = query.Query.Trim();
        return new[] { summary.Id, summary.DisplayName, summary.OriginChannel, summary.ChannelContext, summary.Status.ToString() }
            .Any(value => value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true);
    }
}
