using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace DotCraft.Tracing;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TraceEventType
{
    SessionMetadata,
    Request,
    Response,
    ToolCallStarted,
    ToolCallCompleted,
    ToolInjection,
    DeferredToolLoading,
    TokenUsage,
    Error,
    ResponseTerminal,
    ProviderError,
    ProviderResponseDiagnostic,
    ContextCompaction,
    Thinking,
    PromptCachePoint,
    PromptCacheDiagnostic,
    PromptCacheRequestShape,
    MaintenanceForkRequest,
    MaintenanceForkResponse,
    ThreadRollback,
    TurnCompleted,
    SkillReferenced
}

/// <summary>
/// Prompt-cache diagnostic event kinds recorded in trace metadata.
/// </summary>
public static class PromptCacheEventKinds
{
    public const string Baseline = "baseline";
    public const string Drift = "drift";
    public const string ToolExtension = "toolExtension";
}

/// <summary>
/// Stable prompt-cache fingerprint fields that can change during a session.
/// </summary>
public static class PromptCacheChangedFields
{
    public const string Prompt = "prompt";
    public const string Tools = "tools";
    public const string Reasoning = "reasoning";
}

/// <summary>
/// Prompt-cache usage diagnostic classifications.
/// </summary>
public static class PromptCacheDiagnosticKinds
{
    public const string ColdStart = "coldStart";
    public const string WarmWriteOrNewPrefix = "warmWriteOrNewPrefix";
    public const string CacheHitStable = "cacheHitStable";
    public const string CacheReadDrop = "cacheReadDrop";
    public const string TtlPossible = "ttlPossible";
    public const string PromptOrToolsChanged = "promptOrToolsChanged";
    public const string LikelyServerSide = "likelyServerSide";
}

/// <summary>
/// Tool metadata recorded when deferred tool loading activates tools.
/// </summary>
public sealed record DeferredToolLoadingTraceTool(
    string Name,
    string? Source,
    string? Namespace);

public sealed class TraceEvent
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];

    public TraceEventType Type { get; init; }

    public string SessionKey { get; init; } = string.Empty;

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public string? Content { get; init; }

    public string? ToolName { get; init; }

    public string? ToolIcon { get; init; }

    public string? ToolArguments { get; init; }

    public string? ToolResult { get; init; }

    public double? DurationMs { get; init; }

    public string? CallId { get; init; }

    public string? ResponseId { get; init; }

    public string? MessageId { get; init; }

    public string? ModelId { get; init; }

    /// <summary>
    /// Resolved reasoning effort for this LLM call (e.g. "low"/"medium"/"high"/"extrahigh"),
    /// or null when reasoning was disabled or unknown. Recorded on Response events to power
    /// the Profile "most used reasoning" metric (spec §27A.5).
    /// </summary>
    public string? ReasoningEffort { get; init; }

    public string? FinishReason { get; init; }

    public int? RequestIndex { get; init; }

    public int? LlmCallIndex { get; init; }

    public string? MetadataJson { get; init; }

    public string? FinalSystemPrompt { get; init; }

    public string[]? ToolNames { get; init; }

    public string? SystemPromptHash { get; init; }

    public string? ToolSchemaHash { get; init; }

    public bool? PromptDriftDetected { get; init; }

    public bool? PromptCacheBreakDetected { get; init; }

    public string? PromptCacheEventKind { get; init; }

    public string[]? PromptCacheChangedFields { get; init; }

    public string? PreviousSystemPromptHash { get; init; }

    public string? PreviousToolSchemaHash { get; init; }

    public string? CurrentSystemPromptHash { get; init; }

    public string? CurrentToolSchemaHash { get; init; }

    public string[]? ChangedToolNames { get; init; }

    public long? InputTokens { get; init; }

    public long? OutputTokens { get; init; }

    public long? CachedInputTokens { get; init; }

    public long? CacheWriteInputTokens { get; init; }

    public long? FreshInputTokens { get; init; }

    public long? NonCachedInputTokens { get; init; }

    public long? ReasoningOutputTokens { get; init; }

    public long? TotalTokens { get; init; }
}

public sealed record PromptCachePointTraceEntry(
    string Model,
    string Role,
    int MessageIndex,
    int ContentIndex,
    int Sequence,
    string HashPrefix,
    bool Remembered,
    bool Latest,
    string ContentKind);

internal sealed record PromptCacheRequestDiagnosticSnapshot(
    string Model,
    string MarkerStrategy,
    string? Ttl,
    int LlmCallIndex,
    int BreakpointCount,
    int CandidateCount,
    int NewSelectedCount,
    int RememberedSelectedCount,
    bool LatestSelectedPointIsNew,
    string? SystemHash,
    string? ToolSchemaHash,
    string? ReasoningHash,
    int ToolCount,
    IReadOnlyList<PromptCacheSelectedPointDiagnostic> SelectedPoints,
    IReadOnlyList<PromptCacheCandidateCountDiagnostic> CandidateCounts);

internal sealed record PromptCacheRequestShapeSnapshot(
    int SchemaVersion,
    string Protocol,
    string Model,
    string? PromptCacheKeyHash,
    string? PromptCacheKeySource,
    string? InstructionsHash,
    string ToolsHash,
    string? ReasoningHash,
    string InputHash,
    int InputItemCount,
    IReadOnlyList<string> InputItemHashes,
    int InputBytes,
    int InputItemIdEligibleCount,
    int InputItemIdPresentCount,
    int InputItemIdGeneratedCount,
    int InputItemIdMissingCount,
    int InputItemIdInvalidSourceCount,
    int? MaxOutputTokensRequested,
    bool MaxOutputTokensPresentAfterOAuthRewrite,
    bool MaxOutputTokensRemovedByOAuthRewrite,
    string? ReasoningEffort,
    string ToolChoiceKind,
    int ToolCount,
    bool StreamingEnabled);

internal sealed record PromptCacheSelectedPointDiagnostic(
    string Role,
    string ContentKind,
    int MessageIndex,
    int ContentIndex,
    int Sequence,
    string HashPrefix,
    bool Remembered,
    bool Latest);

internal sealed record PromptCacheCandidateCountDiagnostic(
    string Role,
    string ContentKind,
    int Count);

public sealed class TraceSession
{
    public string SessionKey { get; init; } = string.Empty;

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;

    private long _totalInputTokens;
    private long _totalOutputTokens;
    private long _totalCachedInputTokens;
    private long _totalCacheWriteInputTokens;
    private long _totalReasoningOutputTokens;
    private long _totalToolDurationMs;
    private long _maxToolDurationMs;
    private long _maxTurnDurationMs;

    public long TotalInputTokens => Interlocked.Read(ref _totalInputTokens);

    public long TotalOutputTokens => Interlocked.Read(ref _totalOutputTokens);

    public long TotalCachedInputTokens => Interlocked.Read(ref _totalCachedInputTokens);

    public long TotalCacheWriteInputTokens => Interlocked.Read(ref _totalCacheWriteInputTokens);

    public long TotalFreshInputTokens => Math.Max(0, TotalInputTokens - TotalCachedInputTokens - TotalCacheWriteInputTokens);

    public long TotalNonCachedInputTokens => Math.Max(0, TotalInputTokens - TotalCachedInputTokens);

    public long TotalReasoningOutputTokens => Interlocked.Read(ref _totalReasoningOutputTokens);

    public double CacheHitRate => TotalInputTokens > 0
        ? TotalCachedInputTokens / (double)TotalInputTokens
        : 0;

    public long TotalToolDurationMs => Interlocked.Read(ref _totalToolDurationMs);

    public long MaxToolDurationMs => Interlocked.Read(ref _maxToolDurationMs);

    /// <summary>Longest single Turn (one unit of agent work) in this session, in milliseconds.</summary>
    public long MaxTurnDurationMs => Interlocked.Read(ref _maxTurnDurationMs);

    public double AvgToolDurationMs => ToolCallCount > 0
        ? TotalToolDurationMs / (double)ToolCallCount
        : 0;

    public void AddInputTokens(long value) => Interlocked.Add(ref _totalInputTokens, value);

    public void AddOutputTokens(long value) => Interlocked.Add(ref _totalOutputTokens, value);

    public void AddCachedInputTokens(long value) => Interlocked.Add(ref _totalCachedInputTokens, value);

    public void AddCacheWriteInputTokens(long value) => Interlocked.Add(ref _totalCacheWriteInputTokens, value);

    public void AddReasoningOutputTokens(long value) => Interlocked.Add(ref _totalReasoningOutputTokens, value);

    public void AddToolDuration(long value)
    {
        Interlocked.Add(ref _totalToolDurationMs, value);

        long current;
        do
        {
            current = Interlocked.Read(ref _maxToolDurationMs);
            if (value <= current)
                break;
        } while (Interlocked.CompareExchange(ref _maxToolDurationMs, value, current) != current);
    }

    /// <summary>Records the duration of one completed Turn, retaining the maximum seen.</summary>
    public void RecordTurnDuration(long value)
    {
        if (value <= 0)
            return;

        long current;
        do
        {
            current = Interlocked.Read(ref _maxTurnDurationMs);
            if (value <= current)
                break;
        } while (Interlocked.CompareExchange(ref _maxTurnDurationMs, value, current) != current);
    }

    internal void LoadAggregateSnapshot(
        long totalInputTokens,
        long totalOutputTokens,
        long totalCachedInputTokens,
        long totalCacheWriteInputTokens,
        long totalReasoningOutputTokens,
        long totalToolDurationMs,
        long maxToolDurationMs,
        long maxTurnDurationMs)
    {
        Interlocked.Exchange(ref _totalInputTokens, totalInputTokens);
        Interlocked.Exchange(ref _totalOutputTokens, totalOutputTokens);
        Interlocked.Exchange(ref _totalCachedInputTokens, totalCachedInputTokens);
        Interlocked.Exchange(ref _totalCacheWriteInputTokens, totalCacheWriteInputTokens);
        Interlocked.Exchange(ref _totalReasoningOutputTokens, totalReasoningOutputTokens);
        Interlocked.Exchange(ref _totalToolDurationMs, totalToolDurationMs);
        Interlocked.Exchange(ref _maxToolDurationMs, maxToolDurationMs);
        Interlocked.Exchange(ref _maxTurnDurationMs, maxTurnDurationMs);
    }

    public int RequestCount { get; set; }

    public int MaintenanceForkRequestCount { get; set; }

    public int ToolCallCount { get; set; }

    public int ResponseCount { get; set; }

    public int MaintenanceForkResponseCount { get; set; }

    public int ErrorCount { get; set; }

    public int ContextCompactionCount { get; set; }

    public int ThinkingCount { get; set; }

    public int TokenUsageCount { get; set; }

    public string? FinalSystemPrompt { get; set; }

    public string? SystemPromptHash { get; set; }

    public string? ToolSchemaHash { get; set; }

    public int PromptDriftCount { get; set; }

    public string? FirstUserRequest { get; set; }

    public string? LastFinishReason { get; set; }

    public DateTimeOffset? SessionMetadataCapturedAt { get; set; }

    public DateTimeOffset? LastPromptCacheChangeAt { get; set; }

    public string? LastPromptCacheChangeKind { get; set; }

    public string[] LastPromptCacheChangedFields { get; set; } = [];

    private string[] _toolNames = [];

    public IReadOnlyList<string> ToolNames => _toolNames;

    public void SetToolNames(IEnumerable<string>? toolNames)
    {
        if (toolNames == null)
            return;

        var normalized = toolNames
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
            return;

        _toolNames = normalized;
    }

    public ConcurrentQueue<TraceEvent> Events { get; } = [];
}
