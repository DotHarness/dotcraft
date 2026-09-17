using System.Data.Common;
using System.Globalization;
using System.Text;
using DotCraft.Persistence;
using Microsoft.Data.Sqlite;

namespace DotCraft.Tracing;

/// <summary>
/// Aggregates completed-Turn usage facts and trace events into the summaries, daily histories,
/// and thread rankings served by the <c>usage/*</c> methods (spec §27A). Every aggregate buckets
/// a fact by its own timestamp shifted into the caller's local day.
/// </summary>
public sealed class UsageAnalyticsService(WorkspaceStateDatabase stateRuntime)
{
    private const string ToolCallCompleted = nameof(TraceEventType.ToolCallCompleted);
    private const string SkillReferenced = nameof(TraceEventType.SkillReferenced);
    private const string ErrorType = nameof(TraceEventType.Error);
    private const string ProviderErrorType = nameof(TraceEventType.ProviderError);

    private readonly WorkspaceStateDatabase _stateRuntime =
        stateRuntime ?? throw new ArgumentNullException(nameof(stateRuntime));

    public UsageAnalyticsSummary GetSummary(UsageDateRange range)
    {
        using var connection = _stateRuntime.OpenConnection();

        int threadCount, requests;
        long input, output, cached, cacheWrite, reasoning;
        using (var command = connection.CreateCommand())
        {
            var sql = new StringBuilder("""
                SELECT
                    COUNT(DISTINCT COALESCE(root_thread_id, thread_id)),
                    COALESCE(SUM(llm_call_count), 0),
                    COALESCE(SUM(input_tokens), 0),
                    COALESCE(SUM(output_tokens), 0),
                    COALESCE(SUM(cached_input_tokens), 0),
                    COALESCE(SUM(cache_write_input_tokens), 0),
                    COALESCE(SUM(reasoning_output_tokens), 0)
                FROM dashboard_usage_records
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
                FROM trace_events
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
            TotalReasoningOutputTokens = reasoning
        };
    }

    /// <exception cref="ArgumentException">The metric does not support the requested dimension.</exception>
    public UsageHistory GetHistory(UsageHistoryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!UsageAnalyticsNames.IsSupported(query.Metric, query.GroupBy))
            throw new ArgumentException($"Metric '{query.Metric}' cannot be grouped by '{query.GroupBy}'.", nameof(query));

        var facts = query.Metric is UsageMetric.Tokens or UsageMetric.Turns or UsageMetric.Requests
            ? ReadTurnFacts(query)
            : ReadTraceFacts(query);

        return Fold(query, facts);
    }

    public IReadOnlyList<UsageThreadUsage> GetTopThreads(UsageDateRange range, int limit)
    {
        limit = Math.Clamp(limit, 1, 50);
        using var connection = _stateRuntime.OpenConnection();

        var rows = new List<(string ThreadId, int Turns, long Input, long Output, long Cached, DateTimeOffset LastActiveAt, string OwnOrigin)>();
        using (var command = connection.CreateCommand())
        {
            var sql = new StringBuilder("""
                SELECT
                    COALESCE(root_thread_id, thread_id) AS root,
                    COUNT(*),
                    COALESCE(SUM(input_tokens), 0),
                    COALESCE(SUM(output_tokens), 0),
                    COALESCE(SUM(cached_input_tokens), 0),
                    MAX(timestamp),
                    COALESCE(MAX(CASE WHEN thread_id = COALESCE(root_thread_id, thread_id) THEN source_id END), MIN(source_id))
                FROM dashboard_usage_records
                WHERE COALESCE(root_thread_id, thread_id) IS NOT NULL
                GROUP BY COALESCE(root_thread_id, thread_id)
                """);
            AppendRange(sql, command, range, "MAX(timestamp)", hasWhere: false, keyword: "HAVING");
            sql.Append(" ORDER BY (SUM(input_tokens) + SUM(output_tokens)) DESC, MAX(timestamp) DESC LIMIT $limit");
            command.Parameters.AddWithValue("$limit", limit);
            command.CommandText = sql.ToString();

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((
                    reader.GetString(0),
                    ReadInt32(reader, 1),
                    ReadInt64(reader, 2),
                    ReadInt64(reader, 3),
                    ReadInt64(reader, 4),
                    ParseTimestamp(reader.GetString(5)),
                    reader.IsDBNull(6) ? string.Empty : reader.GetString(6)));
            }
        }

        var metadata = ReadThreadMetadata(connection, rows.Select(row => row.ThreadId));
        return rows
            .Select(row =>
            {
                metadata.TryGetValue(row.ThreadId, out var meta);
                return new UsageThreadUsage(
                    row.ThreadId,
                    meta.Title,
                    string.IsNullOrWhiteSpace(meta.OriginChannel) ? row.OwnOrigin : meta.OriginChannel!,
                    row.LastActiveAt,
                    row.Turns,
                    row.Input,
                    row.Output,
                    row.Cached,
                    meta.Archived);
            })
            .ToList();
    }

    public UsageThreadBreakdown GetThreadUsage(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        using var connection = _stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                model,
                reasoning_effort,
                speed,
                COUNT(*),
                COALESCE(SUM(input_tokens), 0),
                COALESCE(SUM(output_tokens), 0),
                COALESCE(SUM(cached_input_tokens), 0)
            FROM dashboard_usage_records
            WHERE COALESCE(root_thread_id, thread_id) = $thread_id
            GROUP BY model, reasoning_effort, speed
            ORDER BY (SUM(input_tokens) + SUM(output_tokens)) DESC, model, reasoning_effort, speed
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);

        var groups = new List<UsageThreadBreakdownGroup>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            groups.Add(new UsageThreadBreakdownGroup(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                ReadInt32(reader, 3),
                ReadInt64(reader, 4),
                ReadInt64(reader, 5),
                ReadInt64(reader, 6)));
        }

        return new UsageThreadBreakdown(
            threadId,
            groups.Sum(group => group.Turns),
            groups.Sum(group => group.InputTokens),
            groups.Sum(group => group.OutputTokens),
            groups.Sum(group => group.CachedInputTokens),
            groups);
    }

    private List<(DateOnly Day, string Key, long Value)> ReadTurnFacts(UsageHistoryQuery query)
    {
        using var connection = _stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        var sql = new StringBuilder("""
            SELECT
                d.timestamp,
                d.input_tokens,
                d.output_tokens,
                d.cached_input_tokens,
                d.cache_write_input_tokens,
                d.llm_call_count,
                d.model,
                COALESCE(t.origin_channel, d.source_id),
                d.reasoning_effort,
                d.speed
            FROM dashboard_usage_records d
            LEFT JOIN threads t ON t.thread_id = COALESCE(d.root_thread_id, d.thread_id)
            """);
        AppendRange(sql, command, query.Range, "d.timestamp", hasWhere: false);
        command.CommandText = sql.ToString();

        var facts = new List<(DateOnly, string, long)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var day = query.Range.LocalDayOf(ParseTimestamp(reader.GetString(0)));
            var input = ReadInt64(reader, 1);
            var output = ReadInt64(reader, 2);
            var cached = ReadInt64(reader, 3);
            var cacheWrite = ReadInt64(reader, 4);
            var llmCalls = ReadInt64(reader, 5);
            var model = reader.IsDBNull(6) ? null : reader.GetString(6);
            var surface = reader.IsDBNull(7) ? null : reader.GetString(7);
            var reasoning = reader.IsDBNull(8) ? null : reader.GetString(8);
            var speed = reader.IsDBNull(9) ? null : reader.GetString(9);

            if (query.GroupBy == UsageDimension.TokenType)
            {
                facts.Add((day, UsageAnalyticsNames.TokenTypeUncached, Math.Max(0, input - cached - cacheWrite)));
                facts.Add((day, UsageAnalyticsNames.TokenTypeCached, cached));
                facts.Add((day, UsageAnalyticsNames.TokenTypeCacheWrite, cacheWrite));
                facts.Add((day, UsageAnalyticsNames.TokenTypeOutput, output));
                continue;
            }

            var key = query.GroupBy switch
            {
                UsageDimension.Model => KeyOrUnknown(model),
                UsageDimension.Surface => KeyOrUnknown(surface),
                UsageDimension.Reasoning => KeyOrUnknown(reasoning),
                UsageDimension.Speed => KeyOrUnknown(speed),
                _ => string.Empty
            };
            var value = query.Metric switch
            {
                UsageMetric.Tokens => input + output,
                UsageMetric.Requests => llmCalls,
                _ => 1
            };
            facts.Add((day, key, value));
        }

        return facts;
    }

    private List<(DateOnly Day, string Key, long Value)> ReadTraceFacts(UsageHistoryQuery query)
    {
        using var connection = _stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        var types = query.Metric switch
        {
            UsageMetric.ToolCalls => $"'{ToolCallCompleted}'",
            UsageMetric.SkillUses => $"'{SkillReferenced}'",
            _ => $"'{ErrorType}', '{ProviderErrorType}'"
        };
        var sql = new StringBuilder($"""
            SELECT
                e.timestamp,
                e.tool_name,
                e.tool_source,
                t.origin_channel
            FROM trace_events e
            LEFT JOIN trace_session_bindings b ON b.session_key = e.session_key
            LEFT JOIN threads t ON t.thread_id = COALESCE(b.root_thread_id, e.session_key)
            WHERE e.type IN ({types})
            """);
        AppendRange(sql, command, query.Range, "e.timestamp", hasWhere: true);
        command.CommandText = sql.ToString();

        var facts = new List<(DateOnly, string, long)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var day = query.Range.LocalDayOf(ParseTimestamp(reader.GetString(0)));
            var key = query.GroupBy switch
            {
                UsageDimension.Tool or UsageDimension.Skill => KeyOrUnknown(reader.IsDBNull(1) ? null : reader.GetString(1)),
                UsageDimension.ToolSource => KeyOrUnknown(reader.IsDBNull(2) ? null : reader.GetString(2)),
                UsageDimension.Surface => KeyOrUnknown(reader.IsDBNull(3) ? null : reader.GetString(3)),
                _ => string.Empty
            };
            facts.Add((day, key, 1));
        }

        return facts;
    }

    private static UsageHistory Fold(UsageHistoryQuery query, List<(DateOnly Day, string Key, long Value)> facts)
    {
        var unit = query.Metric == UsageMetric.Tokens ? UsageAnalyticsNames.UnitTokens : UsageAnalyticsNames.UnitCount;
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

    private static Dictionary<string, (string? Title, string? OriginChannel, bool Archived)> ReadThreadMetadata(
        SqliteConnection connection,
        IEnumerable<string> threadIds)
    {
        var ids = threadIds.Distinct(StringComparer.Ordinal).ToArray();
        var result = new Dictionary<string, (string?, string?, bool)>(StringComparer.Ordinal);
        if (ids.Length == 0)
            return result;

        using var command = connection.CreateCommand();
        var placeholders = new string[ids.Length];
        for (var i = 0; i < ids.Length; i++)
        {
            placeholders[i] = $"$id{i}";
            command.Parameters.AddWithValue(placeholders[i], ids[i]);
        }

        command.CommandText = $"""
            SELECT thread_id, display_name, first_user_message, origin_channel, archived_at
            FROM threads
            WHERE thread_id IN ({string.Join(", ", placeholders)})
            """;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var displayName = reader.IsDBNull(1) ? null : reader.GetString(1);
            var firstMessage = reader.IsDBNull(2) ? null : reader.GetString(2);
            var title = !string.IsNullOrWhiteSpace(displayName)
                ? displayName.Trim()
                : string.IsNullOrWhiteSpace(firstMessage) ? null : firstMessage.Trim();
            result[reader.GetString(0)] = (
                title,
                reader.IsDBNull(3) ? null : reader.GetString(3),
                !reader.IsDBNull(4));
        }

        return result;
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

    private static string KeyOrUnknown(string? value) =>
        string.IsNullOrWhiteSpace(value) ? UsageAnalyticsNames.UnknownKey : value;

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static int ReadInt32(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static long ReadInt64(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : (long)Math.Round(Convert.ToDouble(reader.GetValue(ordinal), CultureInfo.InvariantCulture));
}
