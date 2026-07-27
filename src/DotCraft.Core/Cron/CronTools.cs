using System.ComponentModel;
using System.Text.Json;
using DotCraft.Channels;
using DotCraft.Tools;

namespace DotCraft.Cron;

public sealed class CronTools(CronService cronService)
{
    private static readonly string[] ValidActions = ["add", "list", "remove"];
    private static readonly string[] ValidScheduleKinds = ["at", "every", "daily"];

    [Description(
        "Manage Cron jobs. Use action 'add', 'list', or 'remove'. " +
        "For add, set scheduleKind: 'at' uses delaySeconds, 'every' uses everySeconds with optional delaySeconds, and 'daily' uses dailyTime or dailyHour/dailyMinute.")]
    [Tool(Icon = "⏰", DisplayType = typeof(CronToolDisplays), DisplayMethod = nameof(CronToolDisplays.Cron))]
    public string Cron(
        [Description("Required: add, list, or remove.")] string action,
        [Description("Required for add: at, every, or daily.")] string? scheduleKind = null,
        [Description("Prompt to run when the job triggers. Required for add.")] string? message = null,
        [Description("Seconds between recurring runs. Required for scheduleKind=every.")] int? everySeconds = null,
        [Description("Seconds from now. Required for at; optional first-run delay for every.")] long? delaySeconds = null,
        [Description("Optional job display name.")] string? name = null,
        [Description("Job id for remove.")] string? jobId = null,
        [Description("Deliver run results. Defaults to true.")] bool deliver = true,
        [Description("Optional result delivery channel.")] string? channel = null,
        [Description("Optional result delivery target.")] string? toUser = null,
        [Description("Daily local hour, 0-23.")] int? dailyHour = null,
        [Description("Daily local minute, 0-59. Defaults to 0.")] int? dailyMinute = null,
        [Description("Daily local time, HH:mm.")] string? dailyTime = null,
        [Description("Daily time zone id. Defaults to UTC.")] string? timeZone = null)
    {
        if (string.IsNullOrWhiteSpace(action))
            return JsonSerializer.Serialize(new { error = "Parameter 'action' is required. Must be one of: 'add', 'list', 'remove'." });

        var normalizedAction = action.Trim().ToLowerInvariant();

        if (!ValidActions.Contains(normalizedAction))
            return JsonSerializer.Serialize(new { error = $"Unknown action: '{action}'. Must be one of: 'add', 'list', 'remove'." });

        switch (normalizedAction)
        {
            case "add":
            {
                if (string.IsNullOrWhiteSpace(message))
                    return JsonSerializer.Serialize(new { error = "Parameter 'message' is required when action is 'add'. Provide the prompt for the agent to execute." });

                if (string.IsNullOrWhiteSpace(scheduleKind))
                    return JsonSerializer.Serialize(new { error = "Parameter 'scheduleKind' is required when action is 'add'. Must be one of: 'at', 'every', 'daily'." });

                var normalizedScheduleKind = scheduleKind.Trim().ToLowerInvariant();
                if (!ValidScheduleKinds.Contains(normalizedScheduleKind))
                    return JsonSerializer.Serialize(new { error = $"Invalid parameter 'scheduleKind': '{scheduleKind}'. Must be one of: 'at', 'every', 'daily'." });

                CronSchedule schedule;
                var deleteAfter = false;
                switch (normalizedScheduleKind)
                {
                    case "at":
                    {
                        if (!delaySeconds.HasValue || delaySeconds.Value <= 0)
                            return JsonSerializer.Serialize(new { error = "Parameter 'delaySeconds' must be a positive integer when scheduleKind is 'at'." });

                        var atMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + delaySeconds.Value * 1000L;
                        schedule = new CronSchedule { Kind = "at", AtMs = atMs };
                        deleteAfter = true;
                        break;
                    }

                    case "every":
                    {
                        if (!everySeconds.HasValue || everySeconds.Value <= 0)
                            return JsonSerializer.Serialize(new { error = "Parameter 'everySeconds' must be a positive integer when scheduleKind is 'every'." });

                        long? initialDelayMs = null;
                        if (delaySeconds.HasValue)
                        {
                            if (delaySeconds.Value <= 0)
                                return JsonSerializer.Serialize(new { error = "Parameter 'delaySeconds' must be positive when scheduleKind is 'every'." });
                            initialDelayMs = delaySeconds.Value * 1000L;
                        }

                        schedule = new CronSchedule
                        {
                            Kind = "every",
                            EveryMs = everySeconds.Value * 1000L,
                            InitialDelayMs = initialDelayMs
                        };
                        break;
                    }

                    case "daily":
                    {
                        if (!TryParseDailyClock(dailyTime, dailyHour, dailyMinute, out var h, out var m, out var parseErr))
                            return JsonSerializer.Serialize(new { error = parseErr });
                        schedule = new CronSchedule
                        {
                            Kind = "daily",
                            DailyHour = h,
                            DailyMinute = m,
                            Tz = string.IsNullOrWhiteSpace(timeZone) ? null : timeZone.Trim()
                        };
                        break;
                    }

                    default:
                        return JsonSerializer.Serialize(new { error = $"Invalid parameter 'scheduleKind': '{scheduleKind}'. Must be one of: 'at', 'every', 'daily'." });
                }

                var payload = new CronPayload { Message = message, Deliver = deliver, Channel = channel, To = toUser };

                var session = ChannelSessionScope.Current;
                if (session != null)
                {
                    payload.CreatorId = session.UserId;
                    payload.CreatorSource = session.Channel;
                    payload.CreatorGroupId = session.GroupId;
                    if (payload.Channel == null)
                        payload.Channel = session.Channel;
                    if (payload.To == null)
                        payload.To = session.DefaultDeliveryTarget;
                }
                else
                {
                    payload.CreatorSource = "api";
                }

                var job = cronService.AddJob(name ?? message[..Math.Min(message.Length, 30)], schedule, payload, deleteAfterRun: deleteAfter);
                return JsonSerializer.Serialize(new
                {
                    status = "created",
                    job.Id,
                    job.Name,
                    nextRun = job.State.NextRunAtMs,
                    schedule = new
                    {
                        kind = job.Schedule.Kind,
                        everyMs = job.Schedule.EveryMs,
                        initialDelayMs = job.Schedule.InitialDelayMs,
                        dailyHour = job.Schedule.DailyHour,
                        dailyMinute = job.Schedule.DailyMinute,
                        atMs = job.Schedule.AtMs,
                        tz = job.Schedule.Tz
                    },
                    deleteAfterRun = deleteAfter,
                    message,
                    deliver,
                    channel = payload.Channel,
                    toUser = payload.To
                });
            }

            case "list":
            {
                var jobs = cronService.ListJobs(includeDisabled: true);
                var result = jobs.Select(j => new
                {
                    j.Id,
                    j.Name,
                    j.Enabled,
                    Schedule = j.Schedule.Kind,
                    NextRun = j.State.NextRunAtMs,
                    LastRun = j.State.LastRunAtMs,
                    j.State.LastStatus
                });
                return JsonSerializer.Serialize(new { count = jobs.Count, jobs = result });
            }

            case "remove":
            {
                if (string.IsNullOrWhiteSpace(jobId))
                    return JsonSerializer.Serialize(new { error = "Parameter 'jobId' is required when action is 'remove'. Use Cron(action: \"list\") first to get job IDs." });

                var removed = cronService.RemoveJob(jobId);
                return JsonSerializer.Serialize(new { status = removed ? "removed" : "not_found", jobId });
            }

            default:
                return JsonSerializer.Serialize(new { error = $"Unknown action: '{action}'. Must be one of: 'add', 'list', 'remove'." });
        }
    }

    private static bool TryParseDailyClock(string? dailyTime, int? dailyHour, int? dailyMinute, out int h, out int m, out string? error)
    {
        h = 0;
        m = 0;
        error = null;

        if (!string.IsNullOrWhiteSpace(dailyTime))
        {
            var parts = dailyTime.Trim().Split(':');
            if (parts.Length != 2
                || !int.TryParse(parts[0].Trim(), out h)
                || !int.TryParse(parts[1].Trim(), out m))
            {
                error = "Parameter 'dailyTime' must be like HH:mm or H:mm.";
                return false;
            }
        }
        else if (dailyHour.HasValue)
        {
            h = dailyHour.Value;
            m = dailyMinute ?? 0;
        }
        else
        {
            error = "scheduleKind 'daily' requires dailyTime (HH:mm) or dailyHour (and optionally dailyMinute).";
            return false;
        }

        if (h is < 0 or > 23 || m is < 0 or > 59)
        {
            error = "Daily local hour must be 0–23 and minute 0–59.";
            return false;
        }

        return true;
    }
}
