using System.Globalization;

namespace DotCraft.Tracing;

/// <summary>Inclusive local-day bounds plus the offset that maps UTC facts onto local days (spec §27A.1).</summary>
public readonly record struct UsageDateRange(DateOnly? From, DateOnly? To, int TzOffsetMinutes)
{
    public const int MaxTzOffsetMinutes = 840;

    public static UsageDateRange Create(DateOnly? from, DateOnly? to, int? tzOffsetMinutes) =>
        new(from, to, Math.Clamp(tzOffsetMinutes ?? 0, -MaxTzOffsetMinutes, MaxTzOffsetMinutes));

    public DateTimeOffset? StartUtc => From is { } from ? ToUtc(from) : null;

    public DateTimeOffset? EndUtcExclusive => To is { } to ? ToUtc(to.AddDays(1)) : null;

    public DateOnly LocalDayOf(DateTimeOffset timestamp) =>
        DateOnly.FromDateTime(timestamp.ToUniversalTime().AddMinutes(TzOffsetMinutes).DateTime);

    private DateTimeOffset ToUtc(DateOnly localDay) =>
        new DateTimeOffset(localDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddMinutes(-TzOffsetMinutes);

    /// <summary>Formats an instant the way usage facts and trace events store their timestamps.</summary>
    public static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}

public enum UsageMetric
{
    Tokens,
    Turns,
    Requests,
    ToolCalls,
    SkillUses,
    Errors
}

public enum UsageDimension
{
    None,
    TokenType,
    Surface,
    Model,
    Reasoning,
    Speed,
    ToolSource,
    Tool,
    Skill
}

/// <summary>Wire names and the supported metric × dimension matrix of <c>usage/history</c> (spec §27A.3).</summary>
public static class UsageAnalyticsNames
{
    public const string OtherKey = "other";
    public const string UnknownKey = "unknown";

    public const string TokenTypeUncached = "uncached";
    public const string TokenTypeCached = "cached";
    public const string TokenTypeCacheWrite = "cacheWrite";
    public const string TokenTypeOutput = "output";

    public const string UnitTokens = "tokens";
    public const string UnitCount = "count";

    private static readonly (string Wire, UsageMetric Metric)[] Metrics =
    [
        ("tokens", UsageMetric.Tokens),
        ("turns", UsageMetric.Turns),
        ("requests", UsageMetric.Requests),
        ("toolCalls", UsageMetric.ToolCalls),
        ("skillUses", UsageMetric.SkillUses),
        ("errors", UsageMetric.Errors)
    ];

    private static readonly (string Wire, UsageDimension Dimension)[] Dimensions =
    [
        ("none", UsageDimension.None),
        ("tokenType", UsageDimension.TokenType),
        ("surface", UsageDimension.Surface),
        ("model", UsageDimension.Model),
        ("reasoning", UsageDimension.Reasoning),
        ("speed", UsageDimension.Speed),
        ("toolSource", UsageDimension.ToolSource),
        ("tool", UsageDimension.Tool),
        ("skill", UsageDimension.Skill)
    ];

    public static bool TryParseMetric(string? value, out UsageMetric metric)
    {
        foreach (var (wire, candidate) in Metrics)
        {
            if (string.Equals(wire, value, StringComparison.Ordinal))
            {
                metric = candidate;
                return true;
            }
        }

        metric = default;
        return false;
    }

    public static bool TryParseDimension(string? value, out UsageDimension dimension)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            dimension = UsageDimension.None;
            return true;
        }

        foreach (var (wire, candidate) in Dimensions)
        {
            if (string.Equals(wire, value, StringComparison.Ordinal))
            {
                dimension = candidate;
                return true;
            }
        }

        dimension = default;
        return false;
    }

    public static string ToWire(UsageDimension dimension) =>
        Dimensions.First(entry => entry.Dimension == dimension).Wire;

    public static bool IsSupported(UsageMetric metric, UsageDimension dimension) => dimension switch
    {
        UsageDimension.None => true,
        UsageDimension.TokenType => metric == UsageMetric.Tokens,
        UsageDimension.Surface => metric is UsageMetric.Tokens or UsageMetric.Turns or UsageMetric.Requests or UsageMetric.Errors,
        UsageDimension.Model or UsageDimension.Reasoning or UsageDimension.Speed =>
            metric is UsageMetric.Tokens or UsageMetric.Turns or UsageMetric.Requests,
        UsageDimension.ToolSource or UsageDimension.Tool => metric == UsageMetric.ToolCalls,
        UsageDimension.Skill => metric == UsageMetric.SkillUses,
        _ => false
    };
}

public sealed record UsageHistoryQuery(
    UsageMetric Metric,
    UsageDimension GroupBy,
    UsageDateRange Range,
    int TopLimit)
{
    public const int DefaultTopLimit = 10;
    public const int MaxTopLimit = 20;
}

public sealed record UsageHistoryValue(string Key, long Value);

public sealed record UsageHistoryDay(DateOnly Date, long Total, IReadOnlyList<UsageHistoryValue> Values);

public sealed record UsageHistorySeriesTotal(string Key, long Total);

public sealed record UsageHistory(
    string Unit,
    UsageDimension GroupBy,
    IReadOnlyList<UsageHistoryDay> Days,
    IReadOnlyList<UsageHistorySeriesTotal> Series);

/// <summary>One root thread's lifetime usage (spec §27A.4); subagent turns are rolled into the root.</summary>
public sealed record UsageThreadUsage(
    string ThreadId,
    string? Title,
    string OriginChannel,
    DateTimeOffset LastActiveAt,
    int Turns,
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens,
    bool Archived)
{
    public long TotalTokens => InputTokens + OutputTokens;

    public double CacheHitRate => InputTokens > 0 ? CachedInputTokens / (double)InputTokens : 0;
}

/// <summary>One model × reasoning × speed slice of a thread's lifetime usage (spec §27A.5).</summary>
public sealed record UsageThreadBreakdownGroup(
    string? Model,
    string? ReasoningEffort,
    string? Speed,
    int Turns,
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens)
{
    public long TotalTokens => InputTokens + OutputTokens;
}

public sealed record UsageThreadBreakdown(
    string ThreadId,
    int Turns,
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens,
    IReadOnlyList<UsageThreadBreakdownGroup> Groups)
{
    public long TotalTokens => InputTokens + OutputTokens;

    public double CacheHitRate => InputTokens > 0 ? CachedInputTokens / (double)InputTokens : 0;
}

/// <summary>Range-scoped workspace aggregate served by <c>usage/summary</c> (spec §27A.2).</summary>
public sealed class UsageAnalyticsSummary
{
    public int ThreadCount { get; init; }

    public int TotalRequests { get; init; }

    public int TotalResponses { get; init; }

    public int TotalToolCalls { get; init; }

    public int TotalErrors { get; init; }

    public int TotalContextCompactions { get; init; }

    public long TotalToolDurationMs { get; init; }

    public long MaxToolDurationMs { get; init; }

    public long TotalInputTokens { get; init; }

    public long TotalOutputTokens { get; init; }

    public long TotalCachedInputTokens { get; init; }

    public long TotalCacheWriteInputTokens { get; init; }

    public long TotalReasoningOutputTokens { get; init; }

    public long TotalFreshInputTokens => Math.Max(0, TotalInputTokens - TotalCachedInputTokens - TotalCacheWriteInputTokens);

    public long TotalNonCachedInputTokens => Math.Max(0, TotalInputTokens - TotalCachedInputTokens);

    public long TotalTokens => TotalInputTokens + TotalOutputTokens;

    public double AvgToolDurationMs => TotalToolCalls > 0 ? TotalToolDurationMs / (double)TotalToolCalls : 0;

    public double CacheHitRate => TotalInputTokens > 0 ? TotalCachedInputTokens / (double)TotalInputTokens : 0;
}
