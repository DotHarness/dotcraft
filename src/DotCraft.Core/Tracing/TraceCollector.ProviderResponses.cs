namespace DotCraft.Tracing;

public sealed partial class TraceCollector
{
    public void RecordProviderResponseDiagnostic(
        string sessionKey,
        string providerProtocol,
        string eventType,
        string? responseId,
        string? modelId,
        string? status,
        string? incompleteReason,
        string? rawFinishReason,
        bool usagePresent,
        int? requestIndex = null,
        bool metadataExtractionFailed = false,
        DateTimeOffset? timestamp = null,
        string[]? outputTypes = null,
        int? outputTextLength = null)
    {
        store.Record(new TraceEvent
        {
            Type = TraceEventType.ProviderResponseDiagnostic,
            SessionKey = sessionKey,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Content = string.IsNullOrWhiteSpace(incompleteReason)
                ? $"{providerProtocol} {eventType}"
                : $"{providerProtocol} {eventType}: {incompleteReason}",
            ResponseId = responseId,
            ModelId = modelId,
            FinishReason = rawFinishReason,
            RequestIndex = requestIndex,
            MetadataJson = SerializeMetadata(new
            {
                providerProtocol,
                eventType,
                status,
                incompleteReason,
                rawFinishReason,
                usagePresent,
                metadataExtractionFailed,
                outputTypes,
                outputTextLength
            })
        });
    }

    public void RecordPromptCacheWarm(
        string sessionKey,
        string outcome,
        TokenUsageSnapshot? usage = null,
        string? turnId = null,
        string? failureReason = null,
        DateTimeOffset? timestamp = null)
    {
        store.Record(new TraceEvent
        {
            Type = TraceEventType.ProviderResponseDiagnostic,
            SessionKey = sessionKey,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Content = $"Prompt cache warm: {outcome}",
            MetadataJson = SerializeMetadata(new
            {
                eventType = "prompt_cache_warm",
                outcome,
                turnId,
                inputTokens = usage?.InputTokens,
                cachedInputTokens = usage?.CachedInputTokens,
                cacheWriteInputTokens = usage?.CacheWriteInputTokens,
                outputTokens = usage?.OutputTokens,
                failureReason
            })
        });
    }
}
