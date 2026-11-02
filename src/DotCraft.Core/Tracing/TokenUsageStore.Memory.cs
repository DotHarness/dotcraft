namespace DotCraft.Tracing;

/// <summary>Aggregates records in memory when the store has no workspace state database.</summary>
public sealed partial class TokenUsageStore
{
    private readonly List<TokenUsageRecord> _memoryRecords = [];
    private readonly object _memoryLock = new();

    private void RecordInMemory(TokenUsageRecord record)
    {
        lock (_memoryLock)
            _memoryRecords.Add(record);
    }

    private TokenUsageRecord[] SnapshotMemory()
    {
        lock (_memoryLock)
            return _memoryRecords.ToArray();
    }

    private IReadOnlyList<UsageSourceSummary> GetSourceSummariesFromMemory() =>
        SnapshotMemory()
            .GroupBy(record => record.SourceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new UsageSourceSummary
            {
                SourceId = group.First().SourceId,
                SourceMode = CollapseKinds(group.Select(record => record.SourceMode), TokenUsageSourceModes.Mixed) ?? string.Empty,
                SubjectKind = CollapseKinds(group.Select(record => record.SubjectKind), TokenUsageSubjectKinds.Mixed) ?? string.Empty,
                ContextKind = CollapseKinds(group.Select(record => record.ContextKind), TokenUsageContextKinds.Mixed),
                SubjectCount = group.Select(record => record.SubjectId).Distinct(StringComparer.Ordinal).Count(),
                ContextCount = group.Select(record => record.ContextId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).Count(),
                RequestCount = group.Count(),
                LlmCallCount = group.Sum(record => Math.Max(1, record.LlmCallCount)),
                TotalInputTokens = group.Sum(record => record.InputTokens),
                TotalOutputTokens = group.Sum(record => record.OutputTokens),
                TotalCachedInputTokens = group.Sum(record => record.CachedInputTokens),
                TotalCacheWriteInputTokens = group.Sum(record => record.CacheWriteInputTokens),
                TotalReasoningOutputTokens = group.Sum(record => record.ReasoningOutputTokens),
                TotalCostUsd = SumCost(group),
                UnpricedRequestCount = group.Count(record => record.CostUsd is null),
                LastActiveAt = group.Max(record => record.Timestamp)
            })
            .OrderByDescending(summary => summary.TotalTokens)
            .ThenBy(summary => summary.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private IReadOnlyList<UsageBreakdownEntry> GetSubjectBreakdownFromMemory(string sourceId) =>
        Breakdown(
            SnapshotMemory()
                .Where(record => string.Equals(record.SourceId, sourceId, StringComparison.OrdinalIgnoreCase))
                .GroupBy(record => (record.SubjectKind, record.SubjectId)),
            group => group.Key.SubjectKind,
            group => group.Key.SubjectId,
            record => record.SubjectLabel,
            relatedSubjects: false);

    private IReadOnlyList<UsageBreakdownEntry> GetContextBreakdownFromMemory(string sourceId) =>
        Breakdown(
            SnapshotMemory()
                .Where(record => string.Equals(record.SourceId, sourceId, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(record.ContextId))
                .GroupBy(record => (Kind: record.ContextKind ?? string.Empty, Id: record.ContextId!)),
            group => group.Key.Kind,
            group => group.Key.Id,
            record => record.ContextLabel,
            relatedSubjects: true);

    private static List<UsageBreakdownEntry> Breakdown<TKey>(
        IEnumerable<IGrouping<TKey, TokenUsageRecord>> groups,
        Func<IGrouping<TKey, TokenUsageRecord>, string> kind,
        Func<IGrouping<TKey, TokenUsageRecord>, string> id,
        Func<TokenUsageRecord, string?> label,
        bool relatedSubjects) =>
        groups
            .Select(group => new UsageBreakdownEntry
            {
                Kind = kind(group),
                Id = id(group),
                Label = group.Select(label).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? id(group),
                RelatedSubjectCount = relatedSubjects
                    ? group.Select(record => record.SubjectId).Distinct(StringComparer.Ordinal).Count()
                    : null,
                RequestCount = group.Count(),
                LlmCallCount = group.Sum(record => Math.Max(1, record.LlmCallCount)),
                TotalInputTokens = group.Sum(record => record.InputTokens),
                TotalOutputTokens = group.Sum(record => record.OutputTokens),
                TotalCachedInputTokens = group.Sum(record => record.CachedInputTokens),
                TotalCacheWriteInputTokens = group.Sum(record => record.CacheWriteInputTokens),
                TotalReasoningOutputTokens = group.Sum(record => record.ReasoningOutputTokens),
                TotalCostUsd = SumCost(group),
                UnpricedRequestCount = group.Count(record => record.CostUsd is null),
                LastActiveAt = group.Max(record => record.Timestamp)
            })
            .OrderByDescending(entry => entry.TotalTokens)
            .ThenBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static double? SumCost(IEnumerable<TokenUsageRecord> records)
    {
        var priced = records.Where(record => record.CostUsd is not null).ToList();
        return priced.Count == 0 ? null : (double)priced.Sum(record => record.CostUsd!.Value);
    }

    private static string? CollapseKinds(IEnumerable<string?> values, string mixedValue)
    {
        var distinct = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();

        return distinct.Length switch
        {
            0 => null,
            1 => distinct[0],
            _ => mixedValue
        };
    }
}
