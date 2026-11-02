using System.Data.Common;
using DotCraft.Persistence;
using Microsoft.Data.Sqlite;

namespace DotCraft.Tracing;

public sealed partial class TokenUsageStore
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

    private readonly WorkspaceStateDatabase? _stateRuntime;

    public TokenUsageStore()
    {
    }

    /// <summary>
    /// Creates a token usage store backed by the workspace state database.
    /// </summary>
    /// <param name="stateRuntime">Workspace state database used for durable usage reads and writes.</param>
    public TokenUsageStore(WorkspaceStateDatabase stateRuntime)
    {
        _stateRuntime = stateRuntime ?? throw new ArgumentNullException(nameof(stateRuntime));
    }

    public void Record(TokenUsageRecord record)
    {
        if (!IsValidRecord(record))
            return;

        if (_stateRuntime != null)
            PersistRecordToDb(record);
        else
            RecordInMemory(record);
    }

    public IReadOnlyList<UsageSourceSummary> GetSourceSummaries()
    {
        return _stateRuntime != null
            ? GetSourceSummariesFromDb()
            : GetSourceSummariesFromMemory();
    }

    public IReadOnlyList<UsageBreakdownEntry> GetSubjectBreakdown(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            return [];

        return _stateRuntime != null
            ? GetSubjectBreakdownFromDb(sourceId)
            : GetSubjectBreakdownFromMemory(sourceId);
    }

    public IReadOnlyList<UsageBreakdownEntry> GetContextBreakdown(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            return [];

        return _stateRuntime != null
            ? GetContextBreakdownFromDb(sourceId)
            : GetContextBreakdownFromMemory(sourceId);
    }

    /// <summary>
    /// Deletes dashboard usage records associated with a server-managed thread.
    /// </summary>
    public int DeleteDashboardUsageForThread(string threadId)
        => DeleteDashboardUsageRecords([threadId], []);

    /// <summary>
    /// Deletes dashboard usage records associated with a trace session.
    /// </summary>
    public int DeleteDashboardUsageForSession(string sessionKey)
        => DeleteDashboardUsageRecords([], [sessionKey]);

    /// <summary>
    /// Deletes dashboard usage records associated with the supplied threads or trace sessions.
    /// </summary>
    public int DeleteDashboardUsageRecords(
        IEnumerable<string> threadIds,
        IEnumerable<string> sessionKeys)
    {
        if (_stateRuntime == null)
            return 0;

        var normalizedThreadIds = NormalizeKeys(threadIds);
        var normalizedSessionKeys = NormalizeKeys(sessionKeys);
        if (normalizedThreadIds.Length == 0 && normalizedSessionKeys.Length == 0)
            return 0;

        using var connection = _stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        var predicates = new List<string>(2);

        if (normalizedThreadIds.Length > 0)
            predicates.Add(BuildInPredicate(command, "thread_id", "thread", normalizedThreadIds));

        if (normalizedSessionKeys.Length > 0)
            predicates.Add(BuildInPredicate(command, "session_key", "session", normalizedSessionKeys));

        command.CommandText = $"DELETE FROM dashboard_usage_records WHERE {string.Join(" OR ", predicates)}";
        return command.ExecuteNonQuery();
    }

    private IReadOnlyList<UsageSourceSummary> GetSourceSummariesFromDb()
    {
        using var connection = _stateRuntime!.OpenConnection();
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
            FROM dashboard_usage_records
            GROUP BY LOWER(source_id)
            ORDER BY total_tokens DESC, source_id COLLATE NOCASE ASC
            """;
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

    private IReadOnlyList<UsageBreakdownEntry> GetSubjectBreakdownFromDb(string sourceId)
    {
        using var connection = _stateRuntime!.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                subject_kind,
                subject_id,
                COALESCE(MAX(NULLIF(TRIM(subject_label), '')), subject_id) AS label,
                NULL AS related_subject_count,
                {TotalsColumns}
            FROM dashboard_usage_records
            WHERE source_id = $source_id COLLATE NOCASE
            GROUP BY subject_kind, subject_id
            ORDER BY total_tokens DESC, label COLLATE NOCASE ASC
            """;
        command.Parameters.AddWithValue("$source_id", sourceId);
        return ReadBreakdown(command);
    }

    private IReadOnlyList<UsageBreakdownEntry> GetContextBreakdownFromDb(string sourceId)
    {
        using var connection = _stateRuntime!.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                context_kind,
                context_id,
                COALESCE(MAX(NULLIF(TRIM(context_label), '')), context_id) AS label,
                COUNT(DISTINCT subject_id) AS related_subject_count,
                {TotalsColumns}
            FROM dashboard_usage_records
            WHERE source_id = $source_id COLLATE NOCASE
              AND NULLIF(TRIM(context_id), '') IS NOT NULL
            GROUP BY context_kind, context_id
            ORDER BY total_tokens DESC, label COLLATE NOCASE ASC
            """;
        command.Parameters.AddWithValue("$source_id", sourceId);
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

    private void PersistRecordToDb(TokenUsageRecord record)
    {
        try
        {
            using var connection = _stateRuntime!.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO dashboard_usage_records (
                    timestamp,
                    source_id,
                    source_mode,
                    subject_kind,
                    subject_id,
                    subject_label,
                    context_kind,
                    context_id,
                    context_label,
                    thread_id,
                    session_key,
                    root_thread_id,
                    model,
                    reasoning_effort,
                    speed,
                    llm_call_count,
                    input_tokens,
                    output_tokens,
                    cached_input_tokens,
                    cache_write_input_tokens,
                    reasoning_output_tokens,
                    cost_usd
                ) VALUES (
                    $timestamp,
                    $source_id,
                    $source_mode,
                    $subject_kind,
                    $subject_id,
                    $subject_label,
                    $context_kind,
                    $context_id,
                    $context_label,
                    $thread_id,
                    $session_key,
                    $root_thread_id,
                    $model,
                    $reasoning_effort,
                    $speed,
                    $llm_call_count,
                    $input_tokens,
                    $output_tokens,
                    $cached_input_tokens,
                    $cache_write_input_tokens,
                    $reasoning_output_tokens,
                    $cost_usd
                )
                """;
            command.Parameters.AddWithValue("$root_thread_id", (object?)record.RootThreadId ?? DBNull.Value);
            command.Parameters.AddWithValue("$model", (object?)record.Model ?? DBNull.Value);
            command.Parameters.AddWithValue("$reasoning_effort", (object?)record.ReasoningEffort ?? DBNull.Value);
            command.Parameters.AddWithValue("$speed", (object?)record.Speed ?? DBNull.Value);
            command.Parameters.AddWithValue("$timestamp", record.Timestamp.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$source_id", record.SourceId);
            command.Parameters.AddWithValue("$source_mode", record.SourceMode);
            command.Parameters.AddWithValue("$subject_kind", record.SubjectKind);
            command.Parameters.AddWithValue("$subject_id", record.SubjectId);
            command.Parameters.AddWithValue("$subject_label", record.SubjectLabel);
            command.Parameters.AddWithValue("$context_kind", (object?)record.ContextKind ?? DBNull.Value);
            command.Parameters.AddWithValue("$context_id", (object?)record.ContextId ?? DBNull.Value);
            command.Parameters.AddWithValue("$context_label", (object?)record.ContextLabel ?? DBNull.Value);
            command.Parameters.AddWithValue("$thread_id", (object?)record.ThreadId ?? DBNull.Value);
            command.Parameters.AddWithValue("$session_key", (object?)record.SessionKey ?? DBNull.Value);
            command.Parameters.AddWithValue("$llm_call_count", Math.Max(1, record.LlmCallCount));
            command.Parameters.AddWithValue("$input_tokens", record.InputTokens);
            command.Parameters.AddWithValue("$output_tokens", record.OutputTokens);
            command.Parameters.AddWithValue("$cached_input_tokens", record.CachedInputTokens);
            command.Parameters.AddWithValue("$cache_write_input_tokens", record.CacheWriteInputTokens);
            command.Parameters.AddWithValue("$reasoning_output_tokens", record.ReasoningOutputTokens);
            command.Parameters.AddWithValue("$cost_usd", record.CostUsd is { } cost ? (double)cost : DBNull.Value);
            command.ExecuteNonQuery();
        }
        catch
        {
            // Preserve current best-effort persistence semantics.
        }
    }

    private static string[] NormalizeKeys(IEnumerable<string> keys)
        => keys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string BuildInPredicate(
        SqliteCommand command,
        string columnName,
        string parameterPrefix,
        IReadOnlyList<string> values)
    {
        var placeholders = new List<string>(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            var parameterName = $"${parameterPrefix}{i}";
            placeholders.Add(parameterName);
            command.Parameters.AddWithValue(parameterName, values[i]);
        }

        return $"{columnName} IN ({string.Join(", ", placeholders)})";
    }

    private static bool IsValidRecord(TokenUsageRecord record)
    {
        return !string.IsNullOrWhiteSpace(record.SourceId)
            && !string.IsNullOrWhiteSpace(record.SourceMode)
            && !string.IsNullOrWhiteSpace(record.SubjectKind)
            && !string.IsNullOrWhiteSpace(record.SubjectId);
    }
}
