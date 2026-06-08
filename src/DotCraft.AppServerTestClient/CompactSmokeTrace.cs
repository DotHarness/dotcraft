using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace DotCraft.AppServerTestClient;

internal sealed record CompactSmokeTraceEvent(
    long Id,
    string SessionKey,
    string Type,
    string EventJson)
{
    public string? Content => ReadStringProperty("Content");

    public string? ModelId => ReadStringProperty("ModelId");

    public string? FallbackReason => ReadMetadataString("fallbackReason");

    public string? Mode => ReadMetadataString("mode");

    public string? ProviderId => ReadMetadataString("providerId");

    public string? MetadataModelId => ReadMetadataString("modelId");

    public string? TurnId => ReadMetadataString("turnId");

    public bool? PreflightRejected => ReadMetadataBool("preflightRejected");

    public long? EstimatedInputTokens => ReadMetadataInt64("estimatedInputTokens");

    public string? SnapshotSource => ReadMetadataString("snapshotSource");

    public string? SnapshotInvalidReason => ReadMetadataString("snapshotInvalidReason");

    public bool? CacheShapeApplied => ReadMetadataBool("cacheShapeApplied");

    public string? CacheShapeKind => ReadMetadataString("cacheShapeKind");

    public bool? PromptCacheKeyPresent => ReadMetadataBool("promptCacheKeyPresent");

    public string? CacheMarkerSource => ReadMetadataString("cacheMarkerSource");

    public long? UsageInputTokens => ReadUsageInt64("inputTokens");

    public long? UsageCachedInputTokens => ReadUsageInt64("cachedInputTokens");

    public long? UsageCacheWriteInputTokens => ReadUsageInt64("cacheWriteInputTokens");

    public double? UsageCacheHitRate => ReadUsageDouble("cacheHitRate");

    private string? ReadStringProperty(string propertyName)
    {
        using var doc = JsonDocument.Parse(EventJson);
        if (doc.RootElement.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    private string? ReadMetadataString(string propertyName)
    {
        using var metadata = ParseMetadata();
        if (metadata == null)
            return null;

        return metadata.RootElement.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private bool? ReadMetadataBool(string propertyName)
    {
        using var metadata = ParseMetadata();
        if (metadata == null)
            return null;

        if (!metadata.RootElement.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private long? ReadMetadataInt64(string propertyName)
    {
        using var metadata = ParseMetadata();
        if (metadata == null)
            return null;

        return metadata.RootElement.TryGetProperty(propertyName, out var value) &&
               value.TryGetInt64(out var result)
            ? result
            : null;
    }

    private long? ReadUsageInt64(string propertyName)
    {
        using var metadata = ParseMetadata();
        if (metadata == null)
            return null;

        if (!TryGetPropertyIgnoreCase(metadata.RootElement, "usage", out var usage) ||
            usage.ValueKind != JsonValueKind.Object ||
            !TryGetPropertyIgnoreCase(usage, propertyName, out var value))
        {
            return null;
        }

        return value.TryGetInt64(out var result) ? result : null;
    }

    private double? ReadUsageDouble(string propertyName)
    {
        using var metadata = ParseMetadata();
        if (metadata == null)
            return null;

        if (!TryGetPropertyIgnoreCase(metadata.RootElement, "usage", out var usage) ||
            usage.ValueKind != JsonValueKind.Object ||
            !TryGetPropertyIgnoreCase(usage, propertyName, out var value))
        {
            return null;
        }

        return value.TryGetDouble(out var result) ? result : null;
    }

    private JsonDocument? ParseMetadata()
    {
        using var doc = JsonDocument.Parse(EventJson);
        if (!doc.RootElement.TryGetProperty("MetadataJson", out var metadataJson) ||
            metadataJson.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var raw = metadataJson.GetString();
        return string.IsNullOrWhiteSpace(raw) ? null : JsonDocument.Parse(raw);
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}

internal static class CompactSmokeTraceReader
{
    public static IReadOnlyList<CompactSmokeTraceEvent> ReadThreadEvents(string traceDbPath, string threadId)
    {
        if (!File.Exists(traceDbPath))
            return [];

        var result = new List<CompactSmokeTraceEvent>();
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
            result.Add(new CompactSmokeTraceEvent(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return result;
    }
}

internal static class CompactSmokeTraceValidator
{
    public static CompactSmokeValidationResult Validate(
        string scenario,
        CompactSmokeProviderSelection provider,
        IReadOnlyList<CompactSmokeTraceEvent> events)
    {
        var requests = events
            .Where(static e => e.Type == "MaintenanceForkRequest")
            .ToArray();
        var responses = events
            .Where(static e => e.Type == "MaintenanceForkResponse")
            .ToArray();
        var forks = PairMaintenanceForks(events);
        var contextCompactions = events
            .Where(static e => e.Type == "ContextCompaction")
            .ToArray();

        if (requests.Length == 0)
            return CompactSmokeValidationResult.Fail("trace_missing_maintenance_fork_request");

        if (responses.Length == 0)
            return CompactSmokeValidationResult.Fail("trace_missing_maintenance_fork_response");

        if (contextCompactions.Length == 0)
            return CompactSmokeValidationResult.Fail("trace_missing_context_compaction");

        var responseFallback = responses
            .Select(static r => r.FallbackReason)
            .FirstOrDefault(static reason => !string.IsNullOrWhiteSpace(reason));
        if (!string.IsNullOrWhiteSpace(responseFallback))
            return CompactSmokeValidationResult.Fail("maintenance_fork_fallback", responseFallback);

        return scenario switch
        {
            CompactSmokeScenarios.ManualLegacyPartial => ValidateLegacy(requests),
            CompactSmokeScenarios.ManualSnapshotPartial => ValidateSnapshot(
                provider,
                forks,
                static fork => string.IsNullOrWhiteSpace(fork.Request.TurnId),
                "trace_missing_manual_snapshot_compact_request"),
            CompactSmokeScenarios.AutoSnapshotFork => ValidateSnapshot(
                provider,
                forks,
                static fork => !string.IsNullOrWhiteSpace(fork.Request.TurnId),
                "trace_missing_auto_snapshot_compact_request",
                expectedSnapshotSource: "captured"),
            _ => CompactSmokeValidationResult.Fail($"unknown_scenario:{scenario}")
        };
    }

    private static IReadOnlyList<MaintenanceForkTracePair> PairMaintenanceForks(
        IReadOnlyList<CompactSmokeTraceEvent> events)
    {
        var result = new List<MaintenanceForkTracePair>();
        CompactSmokeTraceEvent? pendingRequest = null;
        foreach (var evt in events)
        {
            if (evt.Type == "MaintenanceForkRequest")
            {
                pendingRequest = evt;
                continue;
            }

            if (evt.Type == "MaintenanceForkResponse" && pendingRequest is not null)
            {
                result.Add(new MaintenanceForkTracePair(pendingRequest, evt));
                pendingRequest = null;
            }
        }

        return result;
    }

    private static CompactSmokeValidationResult ValidateLegacy(
        IReadOnlyList<CompactSmokeTraceEvent> requests)
    {
        var legacyRequest = requests.FirstOrDefault(static r =>
            string.Equals(r.Mode, "legacy", StringComparison.OrdinalIgnoreCase));
        return legacyRequest is null
            ? CompactSmokeValidationResult.Fail("trace_missing_legacy_compact_request")
            : CompactSmokeValidationResult.Pass();
    }

    private static CompactSmokeValidationResult ValidateSnapshot(
        CompactSmokeProviderSelection provider,
        IReadOnlyList<MaintenanceForkTracePair> forks,
        Func<MaintenanceForkTracePair, bool> isScenarioRequest,
        string missingMessage,
        string? expectedSnapshotSource = null)
    {
        var snapshotFork = forks.FirstOrDefault(fork =>
            !string.Equals(fork.Request.Mode, "legacy", StringComparison.OrdinalIgnoreCase) &&
            isScenarioRequest(fork));
        if (snapshotFork is null)
            return CompactSmokeValidationResult.Fail(missingMessage);

        var snapshotRequest = snapshotFork.Request;
        if (snapshotRequest.PreflightRejected == true)
            return CompactSmokeValidationResult.Fail("maintenance_snapshot_preflight_rejected");

        if (!string.IsNullOrWhiteSpace(expectedSnapshotSource)
            && !string.Equals(snapshotRequest.SnapshotSource, expectedSnapshotSource, StringComparison.OrdinalIgnoreCase))
        {
            return CompactSmokeValidationResult.Fail(
                "trace_snapshot_source_mismatch",
                snapshotSource: snapshotRequest.SnapshotSource,
                snapshotInvalidReason: snapshotRequest.SnapshotInvalidReason,
                cacheShapeApplied: snapshotRequest.CacheShapeApplied,
                cacheShapeKind: snapshotRequest.CacheShapeKind,
                promptCacheKeyPresent: snapshotRequest.PromptCacheKeyPresent,
                cacheMarkerSource: snapshotRequest.CacheMarkerSource);
        }

        if (!string.Equals(snapshotRequest.ProviderId, provider.ProviderId, StringComparison.OrdinalIgnoreCase))
            return CompactSmokeValidationResult.Fail("trace_provider_id_mismatch");

        if (!string.IsNullOrWhiteSpace(snapshotRequest.MetadataModelId) &&
            !string.Equals(snapshotRequest.MetadataModelId, provider.Model, StringComparison.OrdinalIgnoreCase))
        {
            return CompactSmokeValidationResult.Fail("trace_model_id_mismatch");
        }

        var response = snapshotFork.Response;
        var inputTokens = response.UsageInputTokens;
        var cachedTokens = response.UsageCachedInputTokens;
        var cacheWriteTokens = response.UsageCacheWriteInputTokens;
        var cacheHitRate = response.UsageCacheHitRate
            ?? (inputTokens is > 0 && cachedTokens.HasValue
                ? cachedTokens.Value / (double)inputTokens.Value
                : null);
        var cacheHit = cachedTokens is > 0 || cacheHitRate is > 0;

        if (!inputTokens.HasValue && !cachedTokens.HasValue && !cacheHitRate.HasValue)
        {
            return CompactSmokeValidationResult.Fail(
                "maintenance_snapshot_cache_usage_missing",
                cacheHitRequired: true,
                cacheHit: false,
                cacheShapeApplied: snapshotRequest.CacheShapeApplied,
                cacheShapeKind: snapshotRequest.CacheShapeKind,
                promptCacheKeyPresent: snapshotRequest.PromptCacheKeyPresent,
                cacheMarkerSource: snapshotRequest.CacheMarkerSource,
                snapshotSource: snapshotRequest.SnapshotSource,
                snapshotInvalidReason: snapshotRequest.SnapshotInvalidReason,
                inputTokens: inputTokens,
                cachedInputTokens: cachedTokens,
                cacheWriteInputTokens: cacheWriteTokens,
                cacheHitRate: cacheHitRate);
        }

        if (!cacheHit)
        {
            return CompactSmokeValidationResult.Fail(
                "maintenance_snapshot_cache_miss",
                cacheHitRequired: true,
                cacheHit: false,
                cacheShapeApplied: snapshotRequest.CacheShapeApplied,
                cacheShapeKind: snapshotRequest.CacheShapeKind,
                promptCacheKeyPresent: snapshotRequest.PromptCacheKeyPresent,
                cacheMarkerSource: snapshotRequest.CacheMarkerSource,
                snapshotSource: snapshotRequest.SnapshotSource,
                snapshotInvalidReason: snapshotRequest.SnapshotInvalidReason,
                inputTokens: inputTokens,
                cachedInputTokens: cachedTokens,
                cacheWriteInputTokens: cacheWriteTokens,
                cacheHitRate: cacheHitRate);
        }

        return CompactSmokeValidationResult.Pass(
            snapshotRequest.SnapshotSource,
            snapshotRequest.SnapshotInvalidReason,
            cacheHitRequired: true,
            cacheHit: true,
            cacheShapeApplied: snapshotRequest.CacheShapeApplied,
            cacheShapeKind: snapshotRequest.CacheShapeKind,
            promptCacheKeyPresent: snapshotRequest.PromptCacheKeyPresent,
            cacheMarkerSource: snapshotRequest.CacheMarkerSource,
            inputTokens: inputTokens,
            cachedInputTokens: cachedTokens,
            cacheWriteInputTokens: cacheWriteTokens,
            cacheHitRate: cacheHitRate);
    }
}

internal sealed record MaintenanceForkTracePair(
    CompactSmokeTraceEvent Request,
    CompactSmokeTraceEvent Response);

internal sealed record CompactSmokeValidationResult(
    bool Success,
    string? Message = null,
    string? FallbackReason = null,
    string? SnapshotSource = null,
    string? SnapshotInvalidReason = null,
    bool? CacheHitRequired = null,
    bool? CacheHit = null,
    bool? CacheShapeApplied = null,
    string? CacheShapeKind = null,
    bool? PromptCacheKeyPresent = null,
    string? CacheMarkerSource = null,
    long? InputTokens = null,
    long? CachedInputTokens = null,
    long? CacheWriteInputTokens = null,
    double? CacheHitRate = null)
{
    public static CompactSmokeValidationResult Pass(
        string? snapshotSource = null,
        string? snapshotInvalidReason = null,
        bool? cacheHitRequired = null,
        bool? cacheHit = null,
        bool? cacheShapeApplied = null,
        string? cacheShapeKind = null,
        bool? promptCacheKeyPresent = null,
        string? cacheMarkerSource = null,
        long? inputTokens = null,
        long? cachedInputTokens = null,
        long? cacheWriteInputTokens = null,
        double? cacheHitRate = null) =>
        new(
            true,
            SnapshotSource: snapshotSource,
            SnapshotInvalidReason: snapshotInvalidReason,
            CacheHitRequired: cacheHitRequired,
            CacheHit: cacheHit,
            CacheShapeApplied: cacheShapeApplied,
            CacheShapeKind: cacheShapeKind,
            PromptCacheKeyPresent: promptCacheKeyPresent,
            CacheMarkerSource: cacheMarkerSource,
            InputTokens: inputTokens,
            CachedInputTokens: cachedInputTokens,
            CacheWriteInputTokens: cacheWriteInputTokens,
            CacheHitRate: cacheHitRate);

    public static CompactSmokeValidationResult Fail(
        string message,
        string? fallbackReason = null,
        string? snapshotSource = null,
        string? snapshotInvalidReason = null,
        bool? cacheHitRequired = null,
        bool? cacheHit = null,
        bool? cacheShapeApplied = null,
        string? cacheShapeKind = null,
        bool? promptCacheKeyPresent = null,
        string? cacheMarkerSource = null,
        long? inputTokens = null,
        long? cachedInputTokens = null,
        long? cacheWriteInputTokens = null,
        double? cacheHitRate = null) =>
        new(
            false,
            message,
            fallbackReason,
            snapshotSource,
            snapshotInvalidReason,
            CacheHitRequired: cacheHitRequired,
            CacheHit: cacheHit,
            CacheShapeApplied: cacheShapeApplied,
            CacheShapeKind: cacheShapeKind,
            PromptCacheKeyPresent: promptCacheKeyPresent,
            CacheMarkerSource: cacheMarkerSource,
            InputTokens: inputTokens,
            CachedInputTokens: cachedInputTokens,
            CacheWriteInputTokens: cacheWriteInputTokens,
            CacheHitRate: cacheHitRate);
}
