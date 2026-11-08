using System.Data.Common;
using System.Globalization;
using System.Text;
using DotCraft.Persistence;
using Microsoft.Data.Sqlite;

namespace DotCraft.Tracing;

/// <summary>
/// Queries user-level statistics independently of workspace conversation and trace retention.
/// </summary>
public sealed partial class UsageAnalyticsService(UsageStatisticsDatabase stateRuntime)
{
    private readonly UsageStatisticsDatabase _stateRuntime =
        stateRuntime ?? throw new ArgumentNullException(nameof(stateRuntime));

    public UsageAnalyticsSummary GetSummary(UsageDateRange range)
    {
        using var connection = _stateRuntime.OpenConnection();

        int threadCount, requests, unpricedTurns;
        long input, output, cached, cacheWrite, reasoning;
        double cost;
        using (var command = connection.CreateCommand())
        {
            var sql = new StringBuilder("""
                SELECT
                    COUNT(DISTINCT workspace_key || char(0) || root_thread_id),
                    COALESCE(SUM(llm_call_count), 0),
                    COALESCE(SUM(input_tokens), 0),
                    COALESCE(SUM(output_tokens), 0),
                    COALESCE(SUM(cached_input_tokens), 0),
                    COALESCE(SUM(cache_write_input_tokens), 0),
                    COALESCE(SUM(reasoning_output_tokens), 0),
                    COALESCE(SUM(cost_usd), 0),
                    COALESCE(SUM(CASE WHEN cost_usd IS NULL THEN 1 ELSE 0 END), 0)
                FROM usage_turns
                """);
            AppendRange(sql, command, range, "timestamp", hasWhere: false);
            command.CommandText = sql.ToString();

            using var reader = command.ExecuteReader();
            reader.Read();
            threadCount = ReadInt32(reader, 0);
            requests = ReadInt32(reader, 1);
            input = ReadInt64(reader, 2);
            output = ReadInt64(reader, 3);
            cached = ReadInt64(reader, 4);
            cacheWrite = ReadInt64(reader, 5);
            reasoning = ReadInt64(reader, 6);
            cost = ReadDouble(reader, 7);
            unpricedTurns = ReadInt32(reader, 8);
        }

        int responses, toolCalls, errors, compactions;
        long toolDuration, maxToolDuration;
        using (var command = connection.CreateCommand())
        {
            var sql = new StringBuilder("""
                SELECT
                    SUM(CASE WHEN type = 'Response' THEN 1 ELSE 0 END),
                    SUM(CASE WHEN type = 'ToolCallCompleted' THEN 1 ELSE 0 END),
                    SUM(CASE WHEN type = 'ToolCallCompleted' THEN duration_ms END),
                    MAX(CASE WHEN type = 'ToolCallCompleted' THEN duration_ms END),
                    SUM(CASE WHEN type IN ('Error', 'ProviderError') THEN 1 ELSE 0 END),
                    SUM(CASE WHEN type = 'ContextCompaction' THEN 1 ELSE 0 END)
                FROM usage_events
                """);
            AppendRange(sql, command, range, "timestamp", hasWhere: false);
            command.CommandText = sql.ToString();

            using var reader = command.ExecuteReader();
            reader.Read();
            responses = ReadInt32(reader, 0);
            toolCalls = ReadInt32(reader, 1);
            toolDuration = ReadInt64(reader, 2);
            maxToolDuration = ReadInt64(reader, 3);
            errors = ReadInt32(reader, 4);
            compactions = ReadInt32(reader, 5);
        }

        return new UsageAnalyticsSummary
        {
            ThreadCount = threadCount,
            TotalRequests = requests,
            TotalResponses = responses,
            TotalToolCalls = toolCalls,
            TotalErrors = errors,
            TotalContextCompactions = compactions,
            TotalToolDurationMs = toolDuration,
            MaxToolDurationMs = maxToolDuration,
            TotalInputTokens = input,
            TotalOutputTokens = output,
            TotalCachedInputTokens = cached,
            TotalCacheWriteInputTokens = cacheWrite,
            TotalReasoningOutputTokens = reasoning,
            TotalCostUsd = cost,
            UnpricedTurns = unpricedTurns
        };
    }

    /// <exception cref="ArgumentException">The metric does not support the requested dimension.</exception>
    public UsageHistory GetHistory(UsageHistoryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!UsageAnalyticsNames.IsSupported(query.Metric, query.GroupBy))
            throw new ArgumentException($"Metric '{query.Metric}' cannot be grouped by '{query.GroupBy}'.", nameof(query));

        var facts = query.Metric is UsageMetric.Tokens or UsageMetric.Turns or UsageMetric.Requests or UsageMetric.Cost
            ? ReadTurnFacts(query)
            : ReadTraceFacts(query);

        return Fold(query, facts);
    }

    private static UsageHistory Fold(UsageHistoryQuery query, List<(DateOnly Day, string Key, double Value)> facts)
    {
        var unit = query.Metric switch
        {
            UsageMetric.Tokens => UsageAnalyticsNames.UnitTokens,
            UsageMetric.Cost => UsageAnalyticsNames.UnitUsd,
            _ => UsageAnalyticsNames.UnitCount
        };
        if (query.GroupBy == UsageDimension.None)
        {
            var plainDays = facts
                .GroupBy(fact => fact.Day)
                .OrderBy(group => group.Key)
                .Select(group => new UsageHistoryDay(group.Key, group.Sum(fact => fact.Value), []))
                .ToList();
            return new UsageHistory(unit, query.GroupBy, plainDays, []);
        }

        var totals = facts
            .GroupBy(fact => fact.Key, StringComparer.Ordinal)
            .Select(group => (Key: group.Key, Total: group.Sum(fact => fact.Value)))
            .OrderByDescending(entry => entry.Total)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .ToList();

        var keptCount = query.GroupBy == UsageDimension.TokenType
            ? totals.Count
            : Math.Clamp(query.TopLimit, 1, UsageHistoryQuery.MaxTopLimit);
        var kept = totals.Take(keptCount).Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);

        string Bucket(string key) => kept.Contains(key) ? key : UsageAnalyticsNames.OtherKey;

        var days = facts
            .GroupBy(fact => fact.Day)
            .OrderBy(group => group.Key)
            .Select(group => new UsageHistoryDay(
                group.Key,
                group.Sum(fact => fact.Value),
                group
                    .GroupBy(fact => Bucket(fact.Key), StringComparer.Ordinal)
                    .Select(bucket => new UsageHistoryValue(bucket.Key, bucket.Sum(fact => fact.Value)))
                    .OrderByDescending(value => value.Value)
                    .ThenBy(value => value.Key, StringComparer.Ordinal)
                    .ToList()))
            .ToList();

        var series = totals
            .GroupBy(entry => Bucket(entry.Key), StringComparer.Ordinal)
            .Select(bucket => new UsageHistorySeriesTotal(bucket.Key, bucket.Sum(entry => entry.Total)))
            .OrderByDescending(entry => entry.Total)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .ToList();

        return new UsageHistory(unit, query.GroupBy, days, series);
    }

    private static void AppendRange(
        StringBuilder sql,
        SqliteCommand command,
        UsageDateRange range,
        string column,
        bool hasWhere,
        string keyword = "WHERE")
    {
        var first = !hasWhere;
        if (range.StartUtc is { } start)
        {
            sql.Append(first ? $"\n{keyword} " : "\nAND ").Append(column).Append(" >= $range_start");
            command.Parameters.AddWithValue("$range_start", UsageDateRange.FormatTimestamp(start));
            first = false;
        }

        if (range.EndUtcExclusive is { } end)
        {
            sql.Append(first ? $"\n{keyword} " : "\nAND ").Append(column).Append(" < $range_end");
            command.Parameters.AddWithValue("$range_end", UsageDateRange.FormatTimestamp(end));
        }
    }

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static int ReadInt32(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static double ReadDouble(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : Convert.ToDouble(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static double? ReadOptionalDouble(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToDouble(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static long ReadInt64(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : (long)Math.Round(Convert.ToDouble(reader.GetValue(ordinal), CultureInfo.InvariantCulture));
}
