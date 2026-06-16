using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;


// ───── usage/summary (spec Section 27A.2) ─────

/// <summary>
/// Aggregate usage telemetry across all traced sessions. Mirrors the shape the hosted
/// Dashboard serves at <c>GET /dashboard/api/summary</c>. All numeric fields are <c>0</c>
/// when no sessions have been traced yet.
/// </summary>
public sealed class UsageSummaryResult
{
    public int SessionCount { get; set; }

    public int TotalRequests { get; set; }

    public int TotalResponses { get; set; }

    public int TotalToolCalls { get; set; }

    public int TotalErrors { get; set; }

    public int TotalContextCompactions { get; set; }

    public long TotalInputTokens { get; set; }

    public long TotalOutputTokens { get; set; }

    public long TotalCachedInputTokens { get; set; }

    public long TotalCacheWriteInputTokens { get; set; }

    public long TotalFreshInputTokens { get; set; }

    public long TotalNonCachedInputTokens { get; set; }

    public long TotalReasoningOutputTokens { get; set; }

    public long TotalToolDurationMs { get; set; }

    public double AvgToolDurationMs { get; set; }

    public long MaxToolDurationMs { get; set; }

    /// <summary>Cached input tokens / total input tokens, in [0, 1]. 0 when no input tokens.</summary>
    public double CacheHitRate { get; set; }

    public long TotalTokens { get; set; }
}

// ───── usage/timeseries (spec Section 27A.3) ─────

/// <summary>
/// Params for <c>usage/timeseries</c>. All fields optional; an absent bound means
/// "no bound" and an absent <see cref="TzOffsetMinutes"/> means UTC bucketing.
/// </summary>
public sealed class UsageTimeseriesParams
{
    /// <summary>Inclusive lower bound, <c>YYYY-MM-DD</c> in the client's local frame.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? From { get; set; }

    /// <summary>Inclusive upper bound, <c>YYYY-MM-DD</c> in the client's local frame.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? To { get; set; }

    /// <summary>Minutes to add to UTC to obtain the client's local time (<c>-getTimezoneOffset()</c>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TzOffsetMinutes { get; set; }
}

/// <summary>
/// Per-day token usage across all traced sessions, for activity charts (spec Section 27A.3).
/// The <see cref="Days"/> series is sparse (only days with activity) and ascending by date.
/// </summary>
public sealed class UsageTimeseriesResult
{
    /// <summary>The (clamped) offset in minutes the server used to bucket sessions by local day.</summary>
    public int TzOffsetMinutes { get; set; }

    /// <summary>
    /// Longest single Turn (one unit of agent work) across the workspace, in milliseconds.
    /// Lifetime maximum, independent of the requested date range. 0 when none recorded.
    /// </summary>
    public long LongestTaskMs { get; set; }

    public List<UsageTimeseriesDay> Days { get; set; } = [];
}

public sealed class UsageTimeseriesDay
{
    /// <summary><c>YYYY-MM-DD</c> in the client's local frame.</summary>
    public string Date { get; set; } = string.Empty;

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    public long TotalTokens { get; set; }

    public int SessionCount { get; set; }
}

// ───── profile/insights (spec Section 27A.5) ─────

/// <summary>Params for <c>profile/insights</c>. All fields optional.</summary>
public sealed class ProfileInsightsParams
{
    /// <summary>Max number of skills to return in <see cref="ProfileInsightsResult.Skills"/> (default 5, clamped 1..20).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TopSkills { get; set; }
}

/// <summary>
/// Profile "activity insights" for the current workspace (spec Section 27A.5). Model usage
/// reflects all persisted history; reasoning and skill metrics are forward-only and may be
/// empty until new activity accrues. <see cref="TopModel"/>/<see cref="TopReasoning"/> are
/// null when no data exists.
/// </summary>
public sealed class ProfileInsightsResult
{
    /// <summary>Most-used model id (ranked by LLM-response count), or null when none recorded.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RankedMetric? TopModel { get; set; }

    /// <summary>Most-used reasoning effort (e.g. "high"), or null when reasoning was never used.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RankedMetric? TopReasoning { get; set; }

    /// <summary>Distinct skills referenced at least once ("skills explored").</summary>
    public int SkillsExplored { get; set; }

    /// <summary>Total skill references across all turns ("total skills used").</summary>
    public long TotalSkillsUsed { get; set; }

    /// <summary>Total non-internal threads in this workspace (active + archived).</summary>
    public int TotalThreads { get; set; }

    /// <summary>Top referenced skills, descending by run count.</summary>
    public List<SkillUsageWire> Skills { get; set; } = [];
}

/// <summary>A leading value with its observation count and the total it was drawn from (for share%).</summary>
public sealed class RankedMetric
{
    public string Key { get; set; } = string.Empty;

    public long Count { get; set; }

    /// <summary>Total observations the leader was drawn from; <c>Count / Total</c> yields the share.</summary>
    public long Total { get; set; }
}

/// <summary>One referenced skill, its run count, and (when it came from a plugin) its plugin attribution.</summary>
public sealed class SkillUsageWire
{
    public string Name { get; set; } = string.Empty;

    public long Count { get; set; }

    /// <summary>Owning plugin id when the skill currently resolves to a plugin source; otherwise null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PluginId { get; set; }

    /// <summary>Human-readable plugin name for the badge, when from a plugin; otherwise null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PluginDisplayName { get; set; }
}
