using DotCraft.Configuration;

namespace DotCraft.Context.Compaction;

/// <summary>
/// Configuration for the layered context-compaction pipeline
/// (pre-flight estimation, cold-cache microcompact, partial summary, reactive retry).
/// </summary>
[ConfigSection("Compaction", DisplayName = "Compaction", Order = 12)]
public sealed class CompactionConfig
{
    /// <summary>
    /// Enables the auto-compact pipeline. When disabled, the pipeline is a no-op;
    /// the model call may still fail with prompt_too_long which the reactive
    /// fallback (if enabled) will attempt to recover from.
    /// </summary>
    [ConfigField(Hint = "Enable automatic context compaction when estimated tokens cross the threshold.")]
    public bool AutoCompactEnabled { get; set; } = true;

    /// <summary>
    /// Enables reactive compaction: on prompt_too_long-style errors mid-turn,
    /// attempt to compact and retry the turn once before failing.
    /// </summary>
    [ConfigField(Hint = "Retry compaction when the model rejects the request for being too long.")]
    public bool ReactiveCompactEnabled { get; set; } = true;

    /// <summary>
    /// Model context window in tokens (default tuned for 256K-class models).
    /// </summary>
    [ConfigField(Min = 1000, Hint = "Model context window in tokens.")]
    public int ContextWindow { get; set; } = 256_000;

    /// <summary>
    /// Upper bound applied to inferred model context-window catalog values.
    /// Explicit <see cref="ContextWindow"/> values are preserved.
    /// </summary>
    [ConfigField(Min = 1000, Hint = "Maximum inferred model context window in tokens.")]
    public int MaxContextWindow { get; set; } = 256_000;

    /// <summary>
    /// Workspace default context-window mode captured by newly created threads.
    /// </summary>
    [ConfigField(Hint = "Default per-thread context-window mode: Default or Max.")]
    [System.Text.Json.Serialization.JsonIgnore]
    public ContextWindowMode ContextWindowMode { get; set; } = ContextWindowMode.Default;

    /// <summary>
    /// Tokens reserved for the summary output so auto-compact triggers before
    /// the prefix + expected summary exceed the window.
    /// </summary>
    [ConfigField(Min = 0, Hint = "Tokens reserved for the compaction summary output.")]
    public int SummaryReserveTokens { get; set; } = 20_000;

    /// <summary>
    /// Maximum output tokens used by compact-specific non-cache summary
    /// requests, and the post-response hard cap for snapshot fork summaries.
    /// </summary>
    [ConfigField(Min = 1, Hint = "Maximum output tokens for context-compaction summaries.")]
    public int SummaryMaxOutputTokens { get; set; } = 12_000;

    /// <summary>
    /// Additional safety buffer below (ContextWindow - SummaryReserve) at which
    /// auto-compact fires.
    /// </summary>
    [ConfigField(Min = 0, Hint = "Auto-compact fires when tokens reach ContextWindow - SummaryReserve - AutoCompactBuffer.")]
    public int AutoCompactBufferTokens { get; set; } = 13_000;

    /// <summary>
    /// Warning threshold buffer: emit compactWarning event when tokens reach
    /// (autoThreshold - WarningBuffer).
    /// </summary>
    [ConfigField(Min = 0, Hint = "Emit compactWarning event this many tokens before the auto threshold.")]
    public int WarningBufferTokens { get; set; } = 20_000;

    /// <summary>
    /// Error threshold buffer: emit compactError event when tokens reach
    /// (autoThreshold - ErrorBuffer). Typically equal to WarningBuffer.
    /// </summary>
    [ConfigField(Min = 0, Hint = "Emit compactError event this many tokens before the auto threshold.")]
    public int ErrorBufferTokens { get; set; } = 10_000;

    /// <summary>
    /// Margin used when computing the context pressure limit reported to
    /// clients and maintenance fallback trimmers.
    /// </summary>
    [ConfigField(Min = 0, Hint = "Headroom kept below the effective context window.")]
    public int ManualCompactBufferTokens { get; set; } = 3_000;

    /// <summary>
    /// Minimum tokens the partial compactor must keep verbatim after summarizing
    /// the prefix. Floor prevents dropping too much recent context.
    /// </summary>
    [ConfigField(Min = 0, Hint = "Minimum tokens to preserve verbatim after compaction.")]
    public int KeepRecentMinTokens { get; set; } = 10_000;

    /// <summary>
    /// Minimum number of API-round groups to keep verbatim. Walks from newest
    /// backwards until both min-tokens and min-groups are satisfied.
    /// </summary>
    [ConfigField(Min = 1, Hint = "Minimum API-round groups preserved verbatim after compaction.")]
    public int KeepRecentMinGroups { get; set; } = 3;

    /// <summary>
    /// Hard cap on tokens preserved verbatim; once the tail exceeds this cap
    /// the compactor stops extending the tail and summarizes the rest.
    /// </summary>
    [ConfigField(Min = 1000, Hint = "Hard cap on tokens preserved verbatim; floor for summary room.")]
    public int KeepRecentMaxTokens { get; set; } = 40_000;

    /// <summary>
    /// Enables the microcompact pass for cold-cache cleanup of stale tool
    /// results. The hot auto-threshold path goes directly to summary/fork
    /// compaction so provider prompt-cache prefixes remain stable.
    /// </summary>
    [ConfigField(Hint = "Enable cold-cache trimming of stale tool results.")]
    public bool MicrocompactEnabled { get; set; } = true;

    /// <summary>
    /// Number of tool results to keep at full fidelity when microcompact fires.
    /// </summary>
    [ConfigField(Min = 1, Hint = "Number of tool results kept at full fidelity during microcompact.")]
    public int MicrocompactKeepRecent { get; set; } = 8;

    /// <summary>
    /// Minimum idle gap (in minutes) since the last assistant message before
    /// cold-cache microcompact may clear stale tool results. Provider-aware
    /// gating may extend or disable this gap when prompt-cache expiry cannot be
    /// inferred safely. Set to 0 to disable the time-based trigger.
    /// </summary>
    [ConfigField(Min = 0, Hint = "Minimum idle minutes before cold-cache microcompact may run (0 to disable).")]
    public int MicrocompactGapMinutes { get; set; } = 20;

    /// <summary>
    /// Maximum consecutive compaction failures before the pipeline trips its
    /// circuit breaker for this thread and skips future attempts until reset.
    /// </summary>
    [ConfigField(Min = 1, Hint = "Circuit-breaker threshold: consecutive failures before giving up.")]
    public int MaxConsecutiveFailures { get; set; } = 3;

    /// <summary>
    /// Creates a copy of this configuration with the same tuning values.
    /// </summary>
    public CompactionConfig Clone() => new()
    {
        AutoCompactEnabled = AutoCompactEnabled,
        ReactiveCompactEnabled = ReactiveCompactEnabled,
        ContextWindow = ContextWindow,
        MaxContextWindow = MaxContextWindow,
        ContextWindowMode = ContextWindowMode,
        SummaryReserveTokens = SummaryReserveTokens,
        SummaryMaxOutputTokens = SummaryMaxOutputTokens,
        AutoCompactBufferTokens = AutoCompactBufferTokens,
        WarningBufferTokens = WarningBufferTokens,
        ErrorBufferTokens = ErrorBufferTokens,
        ManualCompactBufferTokens = ManualCompactBufferTokens,
        KeepRecentMinTokens = KeepRecentMinTokens,
        KeepRecentMinGroups = KeepRecentMinGroups,
        KeepRecentMaxTokens = KeepRecentMaxTokens,
        MicrocompactEnabled = MicrocompactEnabled,
        MicrocompactKeepRecent = MicrocompactKeepRecent,
        MicrocompactGapMinutes = MicrocompactGapMinutes,
        MaxConsecutiveFailures = MaxConsecutiveFailures
    };

    /// <summary>
    /// Computes the effective context window: ContextWindow − SummaryReserve,
    /// floored so the auto-compact threshold cannot go negative.
    /// </summary>
    public int EffectiveContextWindow()
    {
        var effective = ContextWindow - SummaryReserveTokens;
        var floor = SummaryReserveTokens + AutoCompactBufferTokens;
        return Math.Max(effective, floor);
    }

    /// <summary>
    /// Token count at which auto-compact fires.
    /// </summary>
    public int AutoCompactThreshold()
    {
        var effective = EffectiveContextWindow();
        return Math.Max(1, effective - AutoCompactBufferTokens);
    }

    /// <summary>
    /// Token count at which the warning event is emitted.
    /// </summary>
    public int WarningThreshold()
    {
        var threshold = AutoCompactThreshold() - WarningBufferTokens;
        return Math.Max(1, threshold);
    }

    /// <summary>
    /// Token count at which the error event is emitted (always at or above the warning threshold).
    /// </summary>
    public int ErrorThreshold()
    {
        var threshold = AutoCompactThreshold() - ErrorBufferTokens;
        return Math.Max(WarningThreshold(), threshold);
    }

    /// <summary>
    /// Token pressure limit used for status reporting and trimmed fallback inputs.
    /// </summary>
    public int BlockingLimit()
    {
        var limit = EffectiveContextWindow() - ManualCompactBufferTokens;
        return Math.Max(AutoCompactThreshold(), limit);
    }
}
