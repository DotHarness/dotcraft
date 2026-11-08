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

    public string? TurnId { get; init; }

    /// <summary>Root thread this turn rolls up into; equals <see cref="ThreadId"/> for top-level threads.</summary>
    public string? RootThreadId { get; init; }

    public string? Model { get; init; }

    public string? ReasoningEffort { get; init; }

    public string? Speed { get; init; }

    public int LlmCallCount { get; init; } = 1;

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long CachedInputTokens { get; init; }

    public long CacheWriteInputTokens { get; init; }

    public long FreshInputTokens => Math.Max(0, InputTokens - CachedInputTokens - CacheWriteInputTokens);

    public long NonCachedInputTokens => Math.Max(0, InputTokens - CachedInputTokens);

    public long ReasoningOutputTokens { get; init; }

    /// <summary>USD cost of the turn's own requests; null when it could not be priced.</summary>
    public decimal? CostUsd { get; init; }

    public double CacheHitRate => InputTokens > 0
        ? CachedInputTokens / (double)InputTokens
        : 0;

    public long TotalTokens => InputTokens + OutputTokens;
}

public abstract class UsageTotals
{
    public int RequestCount { get; init; }

    public int LlmCallCount { get; init; }

    public long TotalInputTokens { get; init; }

    public long TotalOutputTokens { get; init; }

    public long TotalCachedInputTokens { get; init; }

    public long TotalCacheWriteInputTokens { get; init; }

    public long TotalFreshInputTokens => Math.Max(0, TotalInputTokens - TotalCachedInputTokens - TotalCacheWriteInputTokens);

    public long TotalNonCachedInputTokens => Math.Max(0, TotalInputTokens - TotalCachedInputTokens);

    public long TotalReasoningOutputTokens { get; init; }

    public double? TotalCostUsd { get; init; }

    public int UnpricedRequestCount { get; init; }

    public double CacheHitRate => TotalInputTokens > 0
        ? TotalCachedInputTokens / (double)TotalInputTokens
        : 0;

    public long TotalTokens => TotalInputTokens + TotalOutputTokens;

    public DateTimeOffset LastActiveAt { get; init; }
}

public sealed class UsageSourceSummary : UsageTotals
{
    public string SourceId { get; init; } = string.Empty;

    public string SourceMode { get; init; } = string.Empty;

    public string SubjectKind { get; init; } = string.Empty;

    public string? ContextKind { get; init; }

    public int SubjectCount { get; init; }

    public int ContextCount { get; init; }
}

public sealed class UsageBreakdownEntry : UsageTotals
{
    public string Kind { get; init; } = string.Empty;

    public string Id { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public int? RelatedSubjectCount { get; init; }
}
