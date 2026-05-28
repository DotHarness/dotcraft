namespace DotCraft.Cron;

public sealed class CronSchedule
{
    public string Kind { get; set; } = "every";
    public long? AtMs { get; set; }
    public long? EveryMs { get; set; }
    /// <summary>When <see cref="Kind"/> is <c>every</c>, optional delay (ms) before the first run only.</summary>
    public long? InitialDelayMs { get; set; }
    /// <summary>When <see cref="Kind"/> is <c>daily</c>, local hour 0–23.</summary>
    public int? DailyHour { get; set; }
    /// <summary>When <see cref="Kind"/> is <c>daily</c>, local minute 0–59.</summary>
    public int? DailyMinute { get; set; }
    public string? Expr { get; set; }
    /// <summary>IANA time zone id for <c>daily</c> schedules (e.g. Asia/Shanghai).</summary>
    public string? Tz { get; set; }
}

public sealed class CronPayload
{
    public string Message { get; set; } = string.Empty;
    public bool Deliver { get; set; }
    public string? Channel { get; set; }
    public string? To { get; set; }
    public string? CreatorId { get; set; }
    public string? CreatorSource { get; set; }
    /// <summary>
    /// The group ID the creator was in when the job was scheduled.
    /// Separate from Channel (delivery target) so approval requests go to the right group.
    /// </summary>
    public string? CreatorGroupId { get; set; }
}

public sealed class CronJobState
{
    public long? NextRunAtMs { get; set; }
    public long? LastRunAtMs { get; set; }
    public string? LastStatus { get; set; }
    public string? LastError { get; set; }

    /// <summary>Thread ID from the most recent execution (wire clients use with thread/read).</summary>
    public string? LastThreadId { get; set; }

    /// <summary>Truncated agent text result from the most recent run (≤500 chars).</summary>
    public string? LastResult { get; set; }
}

public sealed class CronJob
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public CronSchedule Schedule { get; set; } = new();
    public CronPayload Payload { get; set; } = new();
    public CronJobState State { get; set; } = new();
    public long CreatedAtMs { get; set; }
    public bool DeleteAfterRun { get; set; }
}

public sealed class CronStore
{
    public int Version { get; set; } = 1;
    public List<CronJob> Jobs { get; set; } = [];
}

/// <summary>
/// Outcome of <see cref="CronService.OnJob"/> for persisting execution metadata (spec §16.2).
/// </summary>
public sealed record CronOnJobResult(
    string? LastThreadId,
    string? LastResult,
    string? LastError,
    bool Ok,
    int? InputTokens,
    int? OutputTokens);
