using System.Text.Encodings.Web;
using System.Text.Json;
using DotCraft.State;

namespace DotCraft.Tracing;

public static class TokenUsageSourceModes
{
    public const string ServerManaged = "server-managed";
    public const string ClientManaged = "client-managed";
    public const string Mixed = "mixed";
}

public static class TokenUsageSubjectKinds
{
    public const string User = "user";
    public const string Thread = "thread";
    public const string Session = "session";
    public const string Mixed = "mixed";
}

public static class TokenUsageContextKinds
{
    public const string Group = "group";
    public const string Mixed = "mixed";
}

public sealed class TokenUsageRecord
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public string SourceId { get; init; } = string.Empty;

    public string SourceMode { get; init; } = string.Empty;

    public string SubjectKind { get; init; } = string.Empty;

    public string SubjectId { get; init; } = string.Empty;

    public string SubjectLabel { get; init; } = string.Empty;

    public string? ContextKind { get; init; }

    public string? ContextId { get; init; }

    public string? ContextLabel { get; init; }

    public string? ThreadId { get; init; }

    public string? SessionKey { get; init; }

    public int LlmCallCount { get; init; } = 1;

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long CachedInputTokens { get; init; }

    public long CacheWriteInputTokens { get; init; }

    public long FreshInputTokens => Math.Max(0, InputTokens - CachedInputTokens - CacheWriteInputTokens);

    public long NonCachedInputTokens => Math.Max(0, InputTokens - CachedInputTokens);

    public long ReasoningOutputTokens { get; init; }

    public double CacheHitRate => InputTokens > 0
        ? CachedInputTokens / (double)InputTokens
        : 0;

    public long TotalTokens => InputTokens + OutputTokens;
}

public sealed class UsageSourceSummary
{
    public string SourceId { get; init; } = string.Empty;

    public string SourceMode { get; init; } = string.Empty;

    public string SubjectKind { get; init; } = string.Empty;

    public string? ContextKind { get; init; }

    public int SubjectCount { get; init; }

    public int ContextCount { get; init; }

    public int RequestCount { get; init; }

    public int LlmCallCount { get; init; }

    public long TotalInputTokens { get; init; }

    public long TotalOutputTokens { get; init; }

    public long TotalCachedInputTokens { get; init; }

    public long TotalCacheWriteInputTokens { get; init; }

    public long TotalFreshInputTokens => Math.Max(0, TotalInputTokens - TotalCachedInputTokens - TotalCacheWriteInputTokens);

    public long TotalNonCachedInputTokens => Math.Max(0, TotalInputTokens - TotalCachedInputTokens);

    public long TotalReasoningOutputTokens { get; init; }

    public double CacheHitRate => TotalInputTokens > 0
        ? TotalCachedInputTokens / (double)TotalInputTokens
        : 0;

    public long TotalTokens => TotalInputTokens + TotalOutputTokens;

    public DateTimeOffset LastActiveAt { get; init; }
}

public sealed class UsageBreakdownEntry
{
    public string Kind { get; init; } = string.Empty;

    public string Id { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public int RequestCount { get; init; }

    public int LlmCallCount { get; init; }

    public int? RelatedSubjectCount { get; init; }

    public long TotalInputTokens { get; init; }

    public long TotalOutputTokens { get; init; }

    public long TotalCachedInputTokens { get; init; }

    public long TotalCacheWriteInputTokens { get; init; }

    public long TotalFreshInputTokens => Math.Max(0, TotalInputTokens - TotalCachedInputTokens - TotalCacheWriteInputTokens);

    public long TotalNonCachedInputTokens => Math.Max(0, TotalInputTokens - TotalCachedInputTokens);

    public long TotalReasoningOutputTokens { get; init; }

    public double CacheHitRate => TotalInputTokens > 0
        ? TotalCachedInputTokens / (double)TotalInputTokens
        : 0;

    public long TotalTokens => TotalInputTokens + TotalOutputTokens;

    public DateTimeOffset LastActiveAt { get; init; }
}

public sealed class TokenUsageStore
{
    private readonly string? _storagePath;
    private readonly StateRuntime? _stateRuntime;
    private readonly Dictionary<string, FallbackSourceAggregate> _fallbackSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _aggregateLock = new();
    private readonly object _fileLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public TokenUsageStore(string? storagePath = null)
        : this(storagePath, null)
    {
    }

    internal TokenUsageStore(string? storagePath, StateRuntime? stateRuntime)
    {
        _storagePath = storagePath;
        _stateRuntime = stateRuntime;
    }

    public void Record(TokenUsageRecord record)
    {
        if (!IsValidRecord(record))
        {
            return;
        }

        if (_stateRuntime != null)
            PersistRecordToDb(record);
        else
        {
            ApplyFallbackRecord(record);
            if (_storagePath != null)
                PersistRecordToFile(record);
        }
    }

    public IReadOnlyList<UsageSourceSummary> GetSourceSummaries()
    {
        return _stateRuntime != null
            ? GetSourceSummariesFromDb()
            : GetSourceSummariesFromFallback();
    }

    public IReadOnlyList<UsageBreakdownEntry> GetSubjectBreakdown(string sourceId)
    {
        return _stateRuntime != null
            ? GetSubjectBreakdownFromDb(sourceId)
            : GetSubjectBreakdownFromFallback(sourceId);
    }

    public IReadOnlyList<UsageBreakdownEntry> GetContextBreakdown(string sourceId)
    {
        return _stateRuntime != null
            ? GetContextBreakdownFromDb(sourceId)
            : GetContextBreakdownFromFallback(sourceId);
    }

    public void LoadFromDisk()
    {
        if (_stateRuntime != null)
            return;

        ClearFallbackAggregates();

        if (_storagePath == null)
            return;

        var filePath = Path.Combine(_storagePath, "usage_records.jsonl");
        if (!File.Exists(filePath))
            return;

        try
        {
            foreach (var line in File.ReadLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var record = JsonSerializer.Deserialize<TokenUsageRecord>(line, JsonOptions);
                    if (record != null && IsValidRecord(record))
                        ApplyFallbackRecord(record);
                }
                catch
                {
                    // Skip corrupted lines
                }
            }
        }
        catch
        {
            // Skip corrupted file
        }
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

    #region Database Queries

    private IReadOnlyList<UsageSourceSummary> GetSourceSummariesFromDb()
    {
        using var connection = _stateRuntime!.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
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
                COUNT(*) AS request_count,
                COALESCE(SUM(llm_call_count), COUNT(*)) AS llm_call_count,
                COALESCE(SUM(input_tokens), 0) AS total_input_tokens,
                COALESCE(SUM(output_tokens), 0) AS total_output_tokens,
                COALESCE(SUM(cached_input_tokens), 0) AS total_cached_input_tokens,
                COALESCE(SUM(cache_write_input_tokens), 0) AS total_cache_write_input_tokens,
                COALESCE(SUM(reasoning_output_tokens), 0) AS total_reasoning_output_tokens,
                MAX(timestamp) AS last_active_at,
                COALESCE(SUM(input_tokens), 0) + COALESCE(SUM(output_tokens), 0) AS total_tokens
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
                LastActiveAt = DateTimeOffset.Parse(reader.GetString(13))
            });
        }

        return summaries;
    }

    private IReadOnlyList<UsageBreakdownEntry> GetSubjectBreakdownFromDb(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            return [];

        using var connection = _stateRuntime!.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                subject_kind,
                subject_id,
                COALESCE(MAX(NULLIF(TRIM(subject_label), '')), subject_id) AS label,
                COUNT(*) AS request_count,
                COALESCE(SUM(llm_call_count), COUNT(*)) AS llm_call_count,
                COALESCE(SUM(input_tokens), 0) AS total_input_tokens,
                COALESCE(SUM(output_tokens), 0) AS total_output_tokens,
                COALESCE(SUM(cached_input_tokens), 0) AS total_cached_input_tokens,
                COALESCE(SUM(cache_write_input_tokens), 0) AS total_cache_write_input_tokens,
                COALESCE(SUM(reasoning_output_tokens), 0) AS total_reasoning_output_tokens,
                MAX(timestamp) AS last_active_at,
                COALESCE(SUM(input_tokens), 0) + COALESCE(SUM(output_tokens), 0) AS total_tokens
            FROM dashboard_usage_records
            WHERE source_id = $source_id COLLATE NOCASE
            GROUP BY subject_kind, subject_id
            ORDER BY total_tokens DESC, label COLLATE NOCASE ASC
            """;
        command.Parameters.AddWithValue("$source_id", sourceId);

        using var reader = command.ExecuteReader();
        var entries = new List<UsageBreakdownEntry>();
        while (reader.Read())
        {
            entries.Add(new UsageBreakdownEntry
            {
                Kind = reader.GetString(0),
                Id = reader.GetString(1),
                Label = reader.GetString(2),
                RequestCount = reader.GetInt32(3),
                LlmCallCount = reader.GetInt32(4),
                TotalInputTokens = reader.GetInt64(5),
                TotalOutputTokens = reader.GetInt64(6),
                TotalCachedInputTokens = reader.GetInt64(7),
                TotalCacheWriteInputTokens = reader.GetInt64(8),
                TotalReasoningOutputTokens = reader.GetInt64(9),
                LastActiveAt = DateTimeOffset.Parse(reader.GetString(10))
            });
        }

        return entries;
    }

    private IReadOnlyList<UsageBreakdownEntry> GetContextBreakdownFromDb(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            return [];

        using var connection = _stateRuntime!.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                context_kind,
                context_id,
                COALESCE(MAX(NULLIF(TRIM(context_label), '')), context_id) AS label,
                COUNT(*) AS request_count,
                COALESCE(SUM(llm_call_count), COUNT(*)) AS llm_call_count,
                COUNT(DISTINCT subject_id) AS related_subject_count,
                COALESCE(SUM(input_tokens), 0) AS total_input_tokens,
                COALESCE(SUM(output_tokens), 0) AS total_output_tokens,
                COALESCE(SUM(cached_input_tokens), 0) AS total_cached_input_tokens,
                COALESCE(SUM(cache_write_input_tokens), 0) AS total_cache_write_input_tokens,
                COALESCE(SUM(reasoning_output_tokens), 0) AS total_reasoning_output_tokens,
                MAX(timestamp) AS last_active_at,
                COALESCE(SUM(input_tokens), 0) + COALESCE(SUM(output_tokens), 0) AS total_tokens
            FROM dashboard_usage_records
            WHERE source_id = $source_id COLLATE NOCASE
              AND NULLIF(TRIM(context_id), '') IS NOT NULL
            GROUP BY context_kind, context_id
            ORDER BY total_tokens DESC, label COLLATE NOCASE ASC
            """;
        command.Parameters.AddWithValue("$source_id", sourceId);

        using var reader = command.ExecuteReader();
        var entries = new List<UsageBreakdownEntry>();
        while (reader.Read())
        {
            entries.Add(new UsageBreakdownEntry
            {
                Kind = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                Id = reader.GetString(1),
                Label = reader.GetString(2),
                RequestCount = reader.GetInt32(3),
                LlmCallCount = reader.GetInt32(4),
                RelatedSubjectCount = reader.GetInt32(5),
                TotalInputTokens = reader.GetInt64(6),
                TotalOutputTokens = reader.GetInt64(7),
                TotalCachedInputTokens = reader.GetInt64(8),
                TotalCacheWriteInputTokens = reader.GetInt64(9),
                TotalReasoningOutputTokens = reader.GetInt64(10),
                LastActiveAt = DateTimeOffset.Parse(reader.GetString(11))
            });
        }

        return entries;
    }

    #endregion

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
                    llm_call_count,
                    input_tokens,
                    output_tokens,
                    cached_input_tokens,
                    cache_write_input_tokens,
                    reasoning_output_tokens
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
                    $llm_call_count,
                    $input_tokens,
                    $output_tokens,
                    $cached_input_tokens,
                    $cache_write_input_tokens,
                    $reasoning_output_tokens
                )
                """;
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
        Microsoft.Data.Sqlite.SqliteCommand command,
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

    #region File Fallback Aggregation

    private IReadOnlyList<UsageSourceSummary> GetSourceSummariesFromFallback()
    {
        lock (_aggregateLock)
        {
            return _fallbackSources.Values
                .Select(source => new UsageSourceSummary
                {
                    SourceId = source.SourceId,
                    SourceMode = CollapseKinds(source.SourceModes, TokenUsageSourceModes.Mixed),
                    SubjectKind = CollapseKinds(source.SubjectKinds, TokenUsageSubjectKinds.Mixed),
                    ContextKind = CollapseOptionalKinds(source.ContextKinds, TokenUsageContextKinds.Mixed),
                    SubjectCount = source.SubjectIds.Count,
                    ContextCount = source.ContextIds.Count,
                    RequestCount = source.RequestCount,
                    LlmCallCount = source.LlmCallCount,
                    TotalInputTokens = source.TotalInputTokens,
                    TotalOutputTokens = source.TotalOutputTokens,
                    TotalCachedInputTokens = source.TotalCachedInputTokens,
                    TotalCacheWriteInputTokens = source.TotalCacheWriteInputTokens,
                    TotalReasoningOutputTokens = source.TotalReasoningOutputTokens,
                    LastActiveAt = source.LastActiveAt
                })
                .OrderByDescending(summary => summary.TotalTokens)
                .ThenBy(summary => summary.SourceId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private IReadOnlyList<UsageBreakdownEntry> GetSubjectBreakdownFromFallback(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            return [];

        lock (_aggregateLock)
        {
            if (!_fallbackSources.TryGetValue(sourceId, out var source))
                return [];

            return source.Subjects.Values
                .Select(entry => new UsageBreakdownEntry
                {
                    Kind = entry.Kind,
                    Id = entry.Id,
                    Label = string.IsNullOrWhiteSpace(entry.Label) ? entry.Id : entry.Label,
                    RequestCount = entry.RequestCount,
                    LlmCallCount = entry.LlmCallCount,
                    TotalInputTokens = entry.TotalInputTokens,
                    TotalOutputTokens = entry.TotalOutputTokens,
                    TotalCachedInputTokens = entry.TotalCachedInputTokens,
                    TotalCacheWriteInputTokens = entry.TotalCacheWriteInputTokens,
                    TotalReasoningOutputTokens = entry.TotalReasoningOutputTokens,
                    LastActiveAt = entry.LastActiveAt
                })
                .OrderByDescending(entry => entry.TotalTokens)
                .ThenBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private IReadOnlyList<UsageBreakdownEntry> GetContextBreakdownFromFallback(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            return [];

        lock (_aggregateLock)
        {
            if (!_fallbackSources.TryGetValue(sourceId, out var source))
                return [];

            return source.Contexts.Values
                .Select(entry => new UsageBreakdownEntry
                {
                    Kind = entry.Kind,
                    Id = entry.Id,
                    Label = string.IsNullOrWhiteSpace(entry.Label) ? entry.Id : entry.Label,
                    RequestCount = entry.RequestCount,
                    LlmCallCount = entry.LlmCallCount,
                    RelatedSubjectCount = entry.RelatedSubjectIds.Count,
                    TotalInputTokens = entry.TotalInputTokens,
                    TotalOutputTokens = entry.TotalOutputTokens,
                    TotalCachedInputTokens = entry.TotalCachedInputTokens,
                    TotalCacheWriteInputTokens = entry.TotalCacheWriteInputTokens,
                    TotalReasoningOutputTokens = entry.TotalReasoningOutputTokens,
                    LastActiveAt = entry.LastActiveAt
                })
                .OrderByDescending(entry => entry.TotalTokens)
                .ThenBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private void ApplyFallbackRecord(TokenUsageRecord record)
    {
        lock (_aggregateLock)
        {
            var source = _fallbackSources.GetValueOrDefault(record.SourceId);
            if (source == null)
            {
                source = new FallbackSourceAggregate(record.SourceId);
                _fallbackSources[record.SourceId] = source;
            }

            source.SourceModes.Add(record.SourceMode);
            source.SubjectKinds.Add(record.SubjectKind);
            source.SubjectIds.Add(record.SubjectId);
            source.RequestCount++;
            source.LlmCallCount += Math.Max(1, record.LlmCallCount);
            source.TotalInputTokens += record.InputTokens;
            source.TotalOutputTokens += record.OutputTokens;
            source.TotalCachedInputTokens += record.CachedInputTokens;
            source.TotalCacheWriteInputTokens += record.CacheWriteInputTokens;
            source.TotalReasoningOutputTokens += record.ReasoningOutputTokens;
            if (record.Timestamp > source.LastActiveAt)
                source.LastActiveAt = record.Timestamp;

            var subjectKey = (record.SubjectKind, record.SubjectId);
            var subject = source.Subjects.GetValueOrDefault(subjectKey);
            if (subject == null)
            {
                subject = new FallbackBreakdownAggregate(record.SubjectKind, record.SubjectId);
                source.Subjects[subjectKey] = subject;
            }

            subject.Add(
                record.SubjectLabel,
                record.Timestamp,
                record.LlmCallCount,
                record.InputTokens,
                record.OutputTokens,
                record.CachedInputTokens,
                record.CacheWriteInputTokens,
                record.ReasoningOutputTokens);

            if (string.IsNullOrWhiteSpace(record.ContextId))
                return;

            source.ContextIds.Add(record.ContextId);
            if (!string.IsNullOrWhiteSpace(record.ContextKind))
                source.ContextKinds.Add(record.ContextKind!);

            var contextKind = record.ContextKind ?? string.Empty;
            var contextKey = (contextKind, record.ContextId);
            var context = source.Contexts.GetValueOrDefault(contextKey);
            if (context == null)
            {
                context = new FallbackContextAggregate(contextKind, record.ContextId);
                source.Contexts[contextKey] = context;
            }

            context.Add(
                record.ContextLabel,
                record.SubjectId,
                record.Timestamp,
                record.LlmCallCount,
                record.InputTokens,
                record.OutputTokens,
                record.CachedInputTokens,
                record.CacheWriteInputTokens,
                record.ReasoningOutputTokens);
        }
    }

    private void ClearFallbackAggregates()
    {
        lock (_aggregateLock)
        {
            _fallbackSources.Clear();
        }
    }

    #endregion

    private void PersistRecordToFile(TokenUsageRecord record)
    {
        _ = Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(_storagePath!);
                var filePath = Path.Combine(_storagePath!, "usage_records.jsonl");
                var json = JsonSerializer.Serialize(record, JsonOptions);
                lock (_fileLock)
                {
                    File.AppendAllText(filePath, json + "\n");
                }
            }
            catch
            {
                // Silently ignore persistence errors
            }
        });
    }

    private static bool IsValidRecord(TokenUsageRecord record)
    {
        return !string.IsNullOrWhiteSpace(record.SourceId)
            && !string.IsNullOrWhiteSpace(record.SourceMode)
            && !string.IsNullOrWhiteSpace(record.SubjectKind)
            && !string.IsNullOrWhiteSpace(record.SubjectId);
    }

    private static string CollapseKinds(IEnumerable<string> values, string mixedValue)
    {
        var distinct = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return distinct.Length switch
        {
            0 => string.Empty,
            1 => distinct[0],
            _ => mixedValue
        };
    }

    private static string? CollapseOptionalKinds(IEnumerable<string?> values, string mixedValue)
    {
        var distinct = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return distinct.Length switch
        {
            0 => null,
            1 => distinct[0],
            _ => mixedValue
        };
    }

    private sealed class FallbackSourceAggregate(string sourceId)
    {
        public string SourceId { get; } = sourceId;

        public HashSet<string> SourceModes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> SubjectKinds { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> ContextKinds { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> SubjectIds { get; } = new(StringComparer.Ordinal);

        public HashSet<string> ContextIds { get; } = new(StringComparer.Ordinal);

        public Dictionary<(string Kind, string Id), FallbackBreakdownAggregate> Subjects { get; } = [];

        public Dictionary<(string Kind, string Id), FallbackContextAggregate> Contexts { get; } = [];

        public int RequestCount { get; set; }

        public int LlmCallCount { get; set; }

        public long TotalInputTokens { get; set; }

        public long TotalOutputTokens { get; set; }

        public long TotalCachedInputTokens { get; set; }

        public long TotalCacheWriteInputTokens { get; set; }

        public long TotalReasoningOutputTokens { get; set; }

        public DateTimeOffset LastActiveAt { get; set; } = DateTimeOffset.MinValue;
    }

    private class FallbackBreakdownAggregate(string kind, string id)
    {
        public string Kind { get; } = kind;

        public string Id { get; } = id;

        public string Label { get; private set; } = string.Empty;

        public int RequestCount { get; private set; }

        public int LlmCallCount { get; private set; }

        public long TotalInputTokens { get; private set; }

        public long TotalOutputTokens { get; private set; }

        public long TotalCachedInputTokens { get; private set; }

        public long TotalCacheWriteInputTokens { get; private set; }

        public long TotalReasoningOutputTokens { get; private set; }

        public DateTimeOffset LastActiveAt { get; private set; } = DateTimeOffset.MinValue;

        public void Add(
            string? label,
            DateTimeOffset timestamp,
            int llmCallCount,
            long inputTokens,
            long outputTokens,
            long cachedInputTokens,
            long cacheWriteInputTokens,
            long reasoningOutputTokens)
        {
            if (string.IsNullOrWhiteSpace(Label) && !string.IsNullOrWhiteSpace(label))
                Label = label;

            RequestCount++;
            LlmCallCount += Math.Max(1, llmCallCount);
            TotalInputTokens += inputTokens;
            TotalOutputTokens += outputTokens;
            TotalCachedInputTokens += cachedInputTokens;
            TotalCacheWriteInputTokens += cacheWriteInputTokens;
            TotalReasoningOutputTokens += reasoningOutputTokens;
            if (timestamp > LastActiveAt)
                LastActiveAt = timestamp;
        }
    }

    private sealed class FallbackContextAggregate(string kind, string id)
        : FallbackBreakdownAggregate(kind, id)
    {
        public HashSet<string> RelatedSubjectIds { get; } = new(StringComparer.Ordinal);

        public void Add(
            string? label,
            string subjectId,
            DateTimeOffset timestamp,
            int llmCallCount,
            long inputTokens,
            long outputTokens,
            long cachedInputTokens,
            long cacheWriteInputTokens,
            long reasoningOutputTokens)
        {
            base.Add(label, timestamp, llmCallCount, inputTokens, outputTokens, cachedInputTokens, cacheWriteInputTokens, reasoningOutputTokens);
            RelatedSubjectIds.Add(subjectId);
        }
    }
}
