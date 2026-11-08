using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace DotCraft.Tracing;

public sealed partial class UsageStatisticsStore
{
    private const string TotalsColumns = """
        COUNT(*) AS request_count,
        COALESCE(SUM(llm_call_count), COUNT(*)) AS llm_call_count,
        COALESCE(SUM(input_tokens), 0) AS total_input_tokens,
        COALESCE(SUM(output_tokens), 0) AS total_output_tokens,
        COALESCE(SUM(cached_input_tokens), 0) AS total_cached_input_tokens,
        COALESCE(SUM(cache_write_input_tokens), 0) AS total_cache_write_input_tokens,
        COALESCE(SUM(reasoning_output_tokens), 0) AS total_reasoning_output_tokens,
        SUM(cost_usd) AS total_cost_usd,
        SUM(CASE WHEN cost_usd IS NULL THEN 1 ELSE 0 END) AS unpriced_count,
        MAX(timestamp) AS last_active_at,
        COALESCE(SUM(input_tokens), 0) + COALESCE(SUM(output_tokens), 0) AS total_tokens
        """;

    public IReadOnlyList<UsageSourceSummary> GetSourceSummaries()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                MIN(source_id) AS source_id,
                CASE
                    WHEN COUNT(DISTINCT LOWER(source_mode)) = 0 THEN ''
                    WHEN COUNT(DISTINCT LOWER(source_mode)) = 1 THEN MIN(source_mode)
                    ELSE $mixed_source_mode
                END AS source_mode,
                CASE
                    WHEN COUNT(DISTINCT LOWER(subject_kind)) = 0 THEN ''
                    WHEN COUNT(DISTINCT LOWER(subject_kind)) = 1 THEN MIN(subject_kind)
                    ELSE $mixed_subject_kind
                END AS subject_kind,
                CASE
                    WHEN COUNT(DISTINCT LOWER(NULLIF(TRIM(context_kind), ''))) = 0 THEN NULL
                    WHEN COUNT(DISTINCT LOWER(NULLIF(TRIM(context_kind), ''))) = 1 THEN MIN(NULLIF(TRIM(context_kind), ''))
                    ELSE $mixed_context_kind
                END AS context_kind,
                COUNT(DISTINCT subject_id) AS subject_count,
                COUNT(DISTINCT NULLIF(TRIM(context_id), '')) AS context_count,
                {TotalsColumns}
            FROM usage_turns
            WHERE workspace_key = $workspace
            GROUP BY LOWER(source_id)
            ORDER BY total_tokens DESC, source_id COLLATE NOCASE ASC
            """;
        command.Parameters.AddWithValue("$workspace", _workspaceKey);
        command.Parameters.AddWithValue("$mixed_source_mode", TokenUsageSourceModes.Mixed);
        command.Parameters.AddWithValue("$mixed_subject_kind", TokenUsageSubjectKinds.Mixed);
        command.Parameters.AddWithValue("$mixed_context_kind", TokenUsageContextKinds.Mixed);

        using var reader = command.ExecuteReader();
        var summaries = new List<UsageSourceSummary>();
        while (reader.Read())
        {
            summaries.Add(new UsageSourceSummary
            {
                SourceId = reader.GetString(0),
                SourceMode = reader.GetString(1),
                SubjectKind = reader.GetString(2),
                ContextKind = reader.IsDBNull(3) ? null : reader.GetString(3),
                SubjectCount = reader.GetInt32(4),
                ContextCount = reader.GetInt32(5),
                RequestCount = reader.GetInt32(6),
                LlmCallCount = reader.GetInt32(7),
                TotalInputTokens = reader.GetInt64(8),
                TotalOutputTokens = reader.GetInt64(9),
                TotalCachedInputTokens = reader.GetInt64(10),
                TotalCacheWriteInputTokens = reader.GetInt64(11),
                TotalReasoningOutputTokens = reader.GetInt64(12),
                TotalCostUsd = ReadCost(reader, 13),
                UnpricedRequestCount = reader.GetInt32(14),
                LastActiveAt = DateTimeOffset.Parse(reader.GetString(15))
            });
        }

        return summaries;
    }

    public IReadOnlyList<UsageBreakdownEntry> GetSubjectBreakdown(string sourceId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                subject_kind,
                subject_id,
                COALESCE(MAX(NULLIF(TRIM(subject_label), '')), subject_id) AS label,
                NULL AS related_subject_count,
                {TotalsColumns}
            FROM usage_turns
            WHERE workspace_key = $workspace AND source_id = $source_id COLLATE NOCASE
            GROUP BY subject_kind, subject_id
            ORDER BY total_tokens DESC, label COLLATE NOCASE ASC
            """;
        command.Parameters.AddWithValue("$source_id", sourceId);
        command.Parameters.AddWithValue("$workspace", _workspaceKey);
        return ReadBreakdown(command);
    }

    public IReadOnlyList<UsageBreakdownEntry> GetContextBreakdown(string sourceId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                context_kind,
                context_id,
                COALESCE(MAX(NULLIF(TRIM(context_label), '')), context_id) AS label,
                COUNT(DISTINCT subject_id) AS related_subject_count,
                {TotalsColumns}
            FROM usage_turns
            WHERE workspace_key = $workspace AND source_id = $source_id COLLATE NOCASE
              AND NULLIF(TRIM(context_id), '') IS NOT NULL
            GROUP BY context_kind, context_id
            ORDER BY total_tokens DESC, label COLLATE NOCASE ASC
            """;
        command.Parameters.AddWithValue("$source_id", sourceId);
        command.Parameters.AddWithValue("$workspace", _workspaceKey);
        return ReadBreakdown(command);
    }

    private static List<UsageBreakdownEntry> ReadBreakdown(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var entries = new List<UsageBreakdownEntry>();
        while (reader.Read())
        {
            entries.Add(new UsageBreakdownEntry
            {
                Kind = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                Id = reader.GetString(1),
                Label = reader.GetString(2),
                RelatedSubjectCount = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                RequestCount = reader.GetInt32(4),
                LlmCallCount = reader.GetInt32(5),
                TotalInputTokens = reader.GetInt64(6),
                TotalOutputTokens = reader.GetInt64(7),
                TotalCachedInputTokens = reader.GetInt64(8),
                TotalCacheWriteInputTokens = reader.GetInt64(9),
                TotalReasoningOutputTokens = reader.GetInt64(10),
                TotalCostUsd = ReadCost(reader, 11),
                UnpricedRequestCount = reader.GetInt32(12),
                LastActiveAt = DateTimeOffset.Parse(reader.GetString(13))
            });
        }

        return entries;
    }

    private static double? ReadCost(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

}
