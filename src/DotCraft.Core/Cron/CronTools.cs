using System.ComponentModel;
using System.Text.Json;
using DotCraft.Abstractions;
using DotCraft.Tools;

namespace DotCraft.Cron;

public sealed class CronTools(CronService cronService)
{
    private static readonly string[] ValidActions = ["add", "list", "remove"];

    [Description(
        "Manage scheduled tasks. " +
        "The 'action' parameter must be one of: 'add', 'list', 'remove'. " +
        "Examples: " +
        "Cron(action: \"add\", message: \"Check server status\", everySeconds: 3600) — recurring every hour; " +
        "Cron(action: \"add\", message: \"Ping\", everySeconds: 3600, delaySeconds: 600) — first run in 10 minutes, then every hour; " +
        "Cron(action: \"add\", message: \"Tea\", dailyTime: \"15:00\", timeZone: \"Asia/Shanghai\") — every day at 15:00 local; " +
        "Cron(action: \"add\", message: \"Remind meeting\", delaySeconds: 120) — one-time after 2 minutes; " +
        "Cron(action: \"list\") — show all jobs; " +
        "Cron(action: \"remove\", jobId: \"abc123\") — delete a job.")]
    [Tool(Icon = "⏰", DisplayType = typeof(CronToolDisplays), DisplayMethod = nameof(CronToolDisplays.Cron))]
    public string Cron(
        [Description("Must be one of: 'add' (create a job), 'list' (show all jobs), 'remove' (delete a job by id). This parameter is required.")] string action,
        [Description("The prompt/message for the agent to execute when the job triggers. Required when action is 'add'.")] string? message = null,
        [Description("Interval in seconds for recurring jobs (e.g. 3600 = every hour). Used with action 'add' when not using daily schedule.")] int? everySeconds = null,
        [Description("Delay in seconds from now: for one-time jobs only, or combined with everySeconds as the delay before the first recurring run.")] long? delaySeconds = null,
        [Description("Display name for the job. Optional, only used when action is 'add'.")] string? name = null,
        [Description("The ID of the job to delete. Required when action is 'remove'. Use action 'list' first to get job IDs.")] string? jobId = null,
        [Description("Whether to deliver results after the job runs. Defaults to true. Results are sent to the task creator unless 'channel' or 'toUser' overrides the target. Only used when action is 'add'.")] bool deliver = true,
        [Description("The channel to deliver results to. Use 'qq' for QQ (group or private), 'wecom' for WeCom. Optional, auto-detected from current chat context when not specified.")] string? channel = null,
        [Description("The delivery target within the channel. For QQ: 'group:<groupId>' for group chat, or a plain user ID for private chat. For WeCom: the ChatId of the target group. Optional, auto-detected from current chat context when not specified.")] string? toUser = null,
        [Description("Local hour (0–23) for a daily job at a fixed clock time. Use with dailyMinute or dailyTime. Mutually exclusive with everySeconds/delaySeconds.")] int? dailyHour = null,
        [Description("Local minute (0–59) for a daily job. Defaults to 0 if dailyHour is set without dailyTime.")] int? dailyMinute = null,
        [Description("Local time of day as HH:mm (e.g. 15:30). Alternative to dailyHour/dailyMinute for daily jobs.")] string? dailyTime = null,
        [Description("IANA time zone id for daily jobs (e.g. Asia/Shanghai, America/New_York). Defaults to UTC if omitted.")] string? timeZone = null)
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

                var hasDaily = dailyHour.HasValue || dailyMinute.HasValue || !string.IsNullOrWhiteSpace(dailyTime);
                if (hasDaily && (everySeconds.HasValue || delaySeconds.HasValue))
                    return JsonSerializer.Serialize(new { error = "Daily schedule (dailyTime or dailyHour) cannot be combined with everySeconds or delaySeconds." });

                CronSchedule schedule;
                if (hasDaily)
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
                }
                else if (delaySeconds.HasValue && !everySeconds.HasValue)
                {
                    if (delaySeconds.Value <= 0)
                        return JsonSerializer.Serialize(new { error = "Parameter 'delaySeconds' must be a positive integer." });

                    var atMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + delaySeconds.Value * 1000L;
                    schedule = new CronSchedule { Kind = "at", AtMs = atMs };
                }
                else if (everySeconds.HasValue)
                {
                    if (everySeconds.Value <= 0)
                        return JsonSerializer.Serialize(new { error = "Parameter 'everySeconds' must be a positive integer." });

                    long? initialDelayMs = null;
                    if (delaySeconds.HasValue)
                    {
                        if (delaySeconds.Value <= 0)
                            return JsonSerializer.Serialize(new { error = "Parameter 'delaySeconds' must be positive when combined with everySeconds." });
                        initialDelayMs = delaySeconds.Value * 1000L;
                    }

                    schedule = new CronSchedule
                    {
                        Kind = "every",
                        EveryMs = everySeconds.Value * 1000L,
                        InitialDelayMs = initialDelayMs
                    };
                }
                else
                {
                    return JsonSerializer.Serialize(new { error = "When action is 'add', specify one of: dailyTime/dailyHour (daily job), delaySeconds only (one-time), or everySeconds (optionally with delaySeconds for first run)." });
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

                var deleteAfter = !hasDaily && delaySeconds.HasValue && !everySeconds.HasValue;
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
            error = "Daily schedule requires dailyTime (HH:mm) or dailyHour (and optionally dailyMinute).";
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
