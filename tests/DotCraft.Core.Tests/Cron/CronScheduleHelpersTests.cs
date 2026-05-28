using DotCraft.Cron;

namespace DotCraft.Tests.Cron;

public class CronScheduleHelpersTests
{
    [Fact]
    public void ComputeNextRunMs_EveryFirstRun_UsesInitialDelayWhenSet()
    {
        var now = 1_000_000L;
        var schedule = new CronSchedule
        {
            Kind = "every",
            EveryMs = 60_000,
            InitialDelayMs = 10_000
        };
        var next = CronScheduleHelpers.ComputeNextRunMs(schedule, lastRunAtMs: null, now);
        Assert.Equal(now + 10_000, next);
    }

    [Fact]
    public void ComputeNextRunMs_EveryAfterRun_IgnoresInitialDelay()
    {
        var now = 2_000_000L;
        var schedule = new CronSchedule
        {
            Kind = "every",
            EveryMs = 60_000,
            InitialDelayMs = 10_000
        };
        var next = CronScheduleHelpers.ComputeNextRunMs(schedule, lastRunAtMs: 1_999_000, now);
        Assert.Equal(now + 60_000, next);
    }

    [Fact]
    public void ComputeNextDailyRunUtcMs_ReturnsNextOccurrenceInZone()
    {
        var tz = CronScheduleHelpers.ResolveTimeZone("UTC");
        // 2025-06-15 10:00 UTC -> next 15:00 same day
        var nowMs = new DateTimeOffset(2025, 6, 15, 10, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var next = CronScheduleHelpers.ComputeNextDailyRunUtcMs(tz, 15, 0, nowMs);
        var expected = new DateTimeOffset(2025, 6, 15, 15, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert.Equal(expected, next);
    }

    [Fact]
    public void ComputeNextDailyRunUtcMs_AfterTimeRollsToNextDay()
    {
        var tz = CronScheduleHelpers.ResolveTimeZone("UTC");
        var nowMs = new DateTimeOffset(2025, 6, 15, 16, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var next = CronScheduleHelpers.ComputeNextDailyRunUtcMs(tz, 15, 0, nowMs);
        var expected = new DateTimeOffset(2025, 6, 16, 15, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert.Equal(expected, next);
    }
}
