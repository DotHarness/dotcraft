using System.Globalization;
using System.Text;

namespace DotCraft.Tracing;

public sealed partial class UsageAnalyticsService
{
    private List<(DateOnly Day, string Key, double Value)> ReadTurnFacts(UsageHistoryQuery query)
    {
        using var connection = _stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        var dimension = query.GroupBy switch
        {
            UsageDimension.Model => "model",
            UsageDimension.Surface => "surface",
            UsageDimension.Reasoning => "reasoning_effort",
            UsageDimension.Speed => "speed",
            _ => null
        };
        var key = dimension is null ? "''" : $"COALESCE(NULLIF(TRIM({dimension}), ''), 'unknown')";
        var sql = new StringBuilder($"""
            SELECT date(timestamp, $shift) AS day, {key} AS bucket,
                SUM(input_tokens), SUM(output_tokens), SUM(cached_input_tokens),
                SUM(cache_write_input_tokens), SUM(llm_call_count), SUM(cost_usd), COUNT(*),
                SUM(MAX(0, input_tokens - cached_input_tokens - cache_write_input_tokens))
            FROM usage_turns
            """);
        if (query.Metric == UsageMetric.Cost)
            sql.Append(" WHERE cost_usd IS NOT NULL");
        AppendRange(sql, command, query.Range, "timestamp", hasWhere: query.Metric == UsageMetric.Cost);
        sql.Append(" GROUP BY day, bucket ORDER BY day");
        command.CommandText = sql.ToString();
        command.Parameters.AddWithValue("$shift", $"{query.Range.TzOffsetMinutes} minutes");
        var facts = new List<(DateOnly, string, double)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var day = DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var input = ReadInt64(reader, 2);
            var output = ReadInt64(reader, 3);
            var cached = ReadInt64(reader, 4);
            var cacheWrite = ReadInt64(reader, 5);
            if (query.GroupBy == UsageDimension.TokenType)
            {
                facts.Add((day, UsageAnalyticsNames.TokenTypeUncached, ReadInt64(reader, 9)));
                facts.Add((day, UsageAnalyticsNames.TokenTypeCached, cached));
                facts.Add((day, UsageAnalyticsNames.TokenTypeCacheWrite, cacheWrite));
                facts.Add((day, UsageAnalyticsNames.TokenTypeOutput, output));
                continue;
            }
            var value = query.Metric switch
            {
                UsageMetric.Tokens => input + output,
                UsageMetric.Requests => ReadInt64(reader, 6),
                UsageMetric.Cost => ReadDouble(reader, 7),
                _ => ReadInt64(reader, 8)
            };
            facts.Add((day, reader.GetString(1), value));
        }
        return facts;
    }

    private List<(DateOnly Day, string Key, double Value)> ReadTraceFacts(UsageHistoryQuery query)
    {
        using var connection = _stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        var types = query.Metric switch
        {
            UsageMetric.ToolCalls => "'ToolCallCompleted'",
            UsageMetric.SkillUses => "'SkillReferenced'",
            _ => "'Error', 'ProviderError'"
        };
        var dimension = query.GroupBy switch
        {
            UsageDimension.Tool or UsageDimension.Skill => "tool_name",
            UsageDimension.ToolSource => "tool_source",
            UsageDimension.Surface => "surface",
            _ => null
        };
        var key = dimension is null ? "''" : $"COALESCE(NULLIF(TRIM({dimension}), ''), 'unknown')";
        var sql = new StringBuilder($"""
            SELECT date(timestamp, $shift) AS day, {key} AS bucket, COUNT(*)
            FROM usage_events WHERE type IN ({types})
            """);
        AppendRange(sql, command, query.Range, "timestamp", hasWhere: true);
        sql.Append(" GROUP BY day, bucket ORDER BY day");
        command.CommandText = sql.ToString();
        command.Parameters.AddWithValue("$shift", $"{query.Range.TzOffsetMinutes} minutes");
        var facts = new List<(DateOnly, string, double)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            facts.Add((DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(1), ReadInt64(reader, 2)));
        return facts;
    }
}
