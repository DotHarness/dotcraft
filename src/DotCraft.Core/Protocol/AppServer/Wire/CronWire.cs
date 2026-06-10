using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Cron;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol.AppServer;


// ───── cron/list ─────

public sealed class CronListParams
{
    /// <summary>When true, disabled jobs are included. Default false.</summary>
    public bool IncludeDisabled { get; set; }
}

public sealed class CronListResult
{
    public List<CronJobWireInfo> Jobs { get; set; } = [];
}

// ───── cron/remove ─────

public sealed class CronRemoveParams
{
    public string JobId { get; set; } = string.Empty;
}

public sealed class CronRemoveResult
{
    public bool Removed { get; set; }
}

// ───── cron/enable ─────

public sealed class CronEnableParams
{
    public string JobId { get; set; } = string.Empty;

    public bool Enabled { get; set; }
}

public sealed class CronEnableResult
{
    public CronJobWireInfo Job { get; set; } = new();
}

// ───── cron/run ─────

public sealed class CronRunParams
{
    public string JobId { get; set; } = string.Empty;
}

public sealed class CronRunResult
{
    public bool Queued { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CronJobWireInfo? Job { get; set; }
}

// ───── heartbeat/trigger (spec Section 17.2) ─────

public sealed class HeartbeatTriggerResult
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Result { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }
}

// ───── CronJobInfo wire DTO (spec Section 16.2) ─────

/// <summary>
/// Transport-safe projection of the internal CronJob domain model.
/// Used in cron/list and cron/enable results.
/// </summary>
public sealed class CronJobWireInfo
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public CronScheduleWireInfo Schedule { get; set; } = new();

    public bool Enabled { get; set; }

    public long CreatedAtMs { get; set; }

    public bool DeleteAfterRun { get; set; }

    public CronJobStateWireInfo State { get; set; } = new();
}

public sealed class CronScheduleWireInfo
{
    /// <summary>"every", "at", or "daily"</summary>
    public string Kind { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? EveryMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? AtMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? InitialDelayMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DailyHour { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DailyMinute { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tz { get; set; }
}

public sealed class CronJobStateWireInfo
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? NextRunAtMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? LastRunAtMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastStatus { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastError { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastThreadId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastResult { get; set; }
}

/// <summary>
/// Maps <see cref="CronJob"/> domain objects to wire DTOs (spec §16.2).
/// </summary>
public static class CronJobWireMapping
{
    public static CronJobWireInfo ToWire(CronJob job) => new()
    {
        Id = job.Id,
        Name = job.Name,
        Schedule = new CronScheduleWireInfo
        {
            Kind = job.Schedule.Kind,
            EveryMs = job.Schedule.EveryMs,
            AtMs = job.Schedule.AtMs,
            InitialDelayMs = job.Schedule.InitialDelayMs,
            DailyHour = job.Schedule.DailyHour,
            DailyMinute = job.Schedule.DailyMinute,
            Tz = job.Schedule.Tz
        },
        Enabled = job.Enabled,
        CreatedAtMs = job.CreatedAtMs,
        DeleteAfterRun = job.DeleteAfterRun,
        State = new CronJobStateWireInfo
        {
            NextRunAtMs = job.State.NextRunAtMs,
            LastRunAtMs = job.State.LastRunAtMs,
            LastStatus = job.State.LastStatus,
            LastError = job.State.LastError,
            LastThreadId = job.State.LastThreadId,
            LastResult = job.State.LastResult
        }
    };
}
