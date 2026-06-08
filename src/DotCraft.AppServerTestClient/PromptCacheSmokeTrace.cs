using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace DotCraft.AppServerTestClient;

internal sealed record PromptCacheSmokeTraceEvent(
    long Id,
    string SessionKey,
    string Type,
    string EventJson);

internal static class PromptCacheSmokeTraceReader
{
    public static IReadOnlyList<PromptCacheSmokeTraceEvent> ReadThreadEvents(string traceDbPath, string threadId)
    {
        if (!File.Exists(traceDbPath))
            return [];

        var result = new List<PromptCacheSmokeTraceEvent>();
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = traceDbPath,
            Mode = SqliteOpenMode.ReadOnly
        };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, session_key, type, event_json
            FROM trace_events
            WHERE session_key = $session_key
            ORDER BY timestamp, id
            """;
        command.Parameters.AddWithValue("$session_key", threadId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new PromptCacheSmokeTraceEvent(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return result;
    }
}

internal static class PromptCacheSmokeTraceValidator
{
    public static PromptCacheSmokeValidationResult Validate(
        IReadOnlyList<PromptCacheSmokeTraceEvent> events)
    {
        var contextCompactionCount = events.Count(static e => e.Type == "ContextCompaction");
        var tokenUsage = events.Where(static e => e.Type == "TokenUsage").ToArray();

        long totalInput = 0;
        long totalCached = 0;
        long totalCacheWrite = 0;
        var sawCacheHit = false;

        foreach (var evt in tokenUsage)
        {
            var input = ReadTokenUsageInt64(evt.EventJson, "InputTokens");
            var cached = ReadTokenUsageInt64(evt.EventJson, "CachedInputTokens");
            var cacheWrite = ReadTokenUsageInt64(evt.EventJson, "CacheWriteInputTokens");

            if (input.HasValue) totalInput += input.Value;
            if (cached.HasValue) totalCached += cached.Value;
            if (cacheWrite.HasValue) totalCacheWrite += cacheWrite.Value;
            if (cached is > 0) sawCacheHit = true;
        }

        var cacheHitRate = totalInput > 0
            ? totalCached / (double)totalInput
            : 0;

        if (contextCompactionCount > 0)
        {
            return PromptCacheSmokeValidationResult.Fail(
                "prompt_cache_baseline_compaction_detected",
                cacheHitRequired: false,
                cacheHit: sawCacheHit,
                inputTokens: totalInput,
                cachedInputTokens: totalCached,
                cacheWriteInputTokens: totalCacheWrite,
                cacheHitRate: cacheHitRate,
                contextCompactionCount: contextCompactionCount);
        }

        if (tokenUsage.Length == 0)
        {
            return PromptCacheSmokeValidationResult.Fail(
                "trace_missing_token_usage",
                contextCompactionCount: contextCompactionCount);
        }

        if (totalInput <= 0)
        {
            return PromptCacheSmokeValidationResult.Fail(
                "prompt_cache_baseline_no_input_tokens",
                cacheHitRequired: false,
                cacheHit: false,
                inputTokens: totalInput,
                cachedInputTokens: totalCached,
                cacheWriteInputTokens: totalCacheWrite,
                cacheHitRate: 0,
                contextCompactionCount: contextCompactionCount);
        }

        return PromptCacheSmokeValidationResult.Pass(
            cacheHitRequired: false,
            cacheHit: sawCacheHit,
            inputTokens: totalInput,
            cachedInputTokens: totalCached,
            cacheWriteInputTokens: totalCacheWrite,
            cacheHitRate: cacheHitRate,
            contextCompactionCount: contextCompactionCount);
    }

    private static long? ReadTokenUsageInt64(string eventJson, string propertyName)
    {
        try
        {
            using var doc = JsonDocument.Parse(eventJson);
            if (!doc.RootElement.TryGetProperty(propertyName, out var value))
                return null;
            return value.TryGetInt64(out var result) ? result : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal sealed record PromptCacheSmokeValidationResult(
    bool Success,
    string? Message = null,
    bool? CacheHitRequired = null,
    bool? CacheHit = null,
    long? InputTokens = null,
    long? CachedInputTokens = null,
    long? CacheWriteInputTokens = null,
    double? CacheHitRate = null,
    int? ContextCompactionCount = null)
{
    public static PromptCacheSmokeValidationResult Pass(
        bool? cacheHitRequired = null,
        bool? cacheHit = null,
        long? inputTokens = null,
        long? cachedInputTokens = null,
        long? cacheWriteInputTokens = null,
        double? cacheHitRate = null,
        int? contextCompactionCount = null) =>
        new(
            true,
            CacheHitRequired: cacheHitRequired,
            CacheHit: cacheHit,
            InputTokens: inputTokens,
            CachedInputTokens: cachedInputTokens,
            CacheWriteInputTokens: cacheWriteInputTokens,
            CacheHitRate: cacheHitRate,
            ContextCompactionCount: contextCompactionCount);

    public static PromptCacheSmokeValidationResult Fail(
        string message,
        bool? cacheHitRequired = null,
        bool? cacheHit = null,
        long? inputTokens = null,
        long? cachedInputTokens = null,
        long? cacheWriteInputTokens = null,
        double? cacheHitRate = null,
        int? contextCompactionCount = null) =>
        new(
            false,
            message,
            CacheHitRequired: cacheHitRequired,
            CacheHit: cacheHit,
            InputTokens: inputTokens,
            CachedInputTokens: cachedInputTokens,
            CacheWriteInputTokens: cacheWriteInputTokens,
            CacheHitRate: cacheHitRate,
            ContextCompactionCount: contextCompactionCount);
}
