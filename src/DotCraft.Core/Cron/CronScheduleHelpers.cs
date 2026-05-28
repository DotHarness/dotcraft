using TimeZoneConverter;

namespace DotCraft.Cron;

/// <summary>
/// Pure helpers for computing the next cron fire time (UTC ms). Used by <see cref="CronService"/> and tests.
/// </summary>
public static class CronScheduleHelpers
{
    /// <summary>
    /// Resolves an IANA or Windows time zone id; falls back to UTC if unknown.
    /// </summary>
    public static TimeZoneInfo ResolveTimeZone(string? tzId)
    {
        if (string.IsNullOrWhiteSpace(tzId))
            return TimeZoneInfo.Utc;
        try
        {
            return TZConvert.GetTimeZoneInfo(tzId.Trim());
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>
    /// Next UTC instant (ms) at <paramref name="hour"/>:<paramref name="minute"/> local in <paramref name="tz"/>,
    /// strictly after <paramref name="nowUtcMs"/> when interpreted in that zone.
    /// </summary>
    public static long ComputeNextDailyRunUtcMs(TimeZoneInfo tz, int hour, int minute, long nowUtcMs)
    {
        hour = Math.Clamp(hour, 0, 23);
        minute = Math.Clamp(minute, 0, 59);

        var utcNow = DateTimeOffset.FromUnixTimeMilliseconds(nowUtcMs).UtcDateTime;
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);
        var today = local.Date;
        var target = new DateTime(today.Year, today.Month, today.Day, hour, minute, 0, DateTimeKind.Unspecified);
        if (target <= local)
            target = target.AddDays(1);

        try
        {
            var utcTarget = TimeZoneInfo.ConvertTimeToUtc(target, tz);
            return new DateTimeOffset(utcTarget, TimeSpan.Zero).ToUnixTimeMilliseconds();
        }
        catch (ArgumentException)
        {
            // Invalid local time (e.g. DST gap): skip to next hour boundary and retry once.
            target = target.AddHours(1);
            var utcTarget = TimeZoneInfo.ConvertTimeToUtc(target, tz);
            return new DateTimeOffset(utcTarget, TimeSpan.Zero).ToUnixTimeMilliseconds();
        }
    }

    /// <summary>
    /// Computes <see cref="CronJobState.NextRunAtMs"/> from schedule and whether the job has run before.
    /// </summary>
    public static long? ComputeNextRunMs(CronSchedule schedule, long? lastRunAtMs, long nowUtcMs)
    {
        switch (schedule.Kind)
        {
            case "at":
                return schedule.AtMs;
            case "every" when schedule.EveryMs is long ev && ev > 0:
            {
                if (lastRunAtMs == null
                    && schedule.InitialDelayMs is { } id0
                    && id0 > 0)
                    return nowUtcMs + id0;
                return nowUtcMs + ev;
            }
            case "daily":
            {
                if (!schedule.DailyHour.HasValue || !schedule.DailyMinute.HasValue)
                    return null;
                var dh = schedule.DailyHour.Value;
                var dm = schedule.DailyMinute.Value;
                if (dh is < 0 or > 23 || dm is < 0 or > 59)
                    return null;
                var tz = ResolveTimeZone(schedule.Tz);
                return ComputeNextDailyRunUtcMs(tz, dh, dm, nowUtcMs);
            }
            default:
                return null;
        }
    }
}
