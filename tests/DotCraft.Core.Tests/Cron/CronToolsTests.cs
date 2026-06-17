using System.Text.Json;
using DotCraft.Cron;

namespace DotCraft.Tests.Cron;

public sealed class CronToolsTests : IDisposable
{
    private readonly string _path;

    public CronToolsTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"dotcraft_cron_tools_test_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch { /* ignore */ }
    }

    [Fact]
    public void AddAtScheduleKind_IgnoresAutoFilledDailyFields()
    {
        using var svc = new CronService(_path);
        var tools = new CronTools(svc);
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var result = tools.Cron(
            action: "add",
            scheduleKind: "at",
            message: "remind me",
            everySeconds: 60,
            delaySeconds: 10,
            dailyHour: 0,
            dailyMinute: 0);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        var schedule = root.GetProperty("schedule");
        var atMs = schedule.GetProperty("atMs").GetInt64();

        Assert.Equal("created", root.GetProperty("status").GetString());
        Assert.Equal("at", schedule.GetProperty("kind").GetString());
        Assert.True(root.GetProperty("deleteAfterRun").GetBoolean());
        Assert.InRange(atMs, before + 9_000, before + 20_000);

        var job = Assert.Single(svc.ListJobs(includeDisabled: true));
        Assert.Equal("at", job.Schedule.Kind);
        Assert.True(job.DeleteAfterRun);
    }

    [Fact]
    public void AddEveryScheduleKind_IgnoresDailyFields()
    {
        using var svc = new CronService(_path);
        var tools = new CronTools(svc);

        var result = tools.Cron(
            action: "add",
            scheduleKind: "every",
            message: "check",
            everySeconds: 60,
            delaySeconds: 5,
            dailyHour: 0,
            dailyMinute: 0,
            dailyTime: "00:00");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        var schedule = root.GetProperty("schedule");

        Assert.Equal("created", root.GetProperty("status").GetString());
        Assert.Equal("every", schedule.GetProperty("kind").GetString());
        Assert.Equal(60_000, schedule.GetProperty("everyMs").GetInt64());
        Assert.Equal(5_000, schedule.GetProperty("initialDelayMs").GetInt64());
        Assert.False(root.GetProperty("deleteAfterRun").GetBoolean());

        var job = Assert.Single(svc.ListJobs(includeDisabled: true));
        Assert.Equal("every", job.Schedule.Kind);
        Assert.Equal(60_000, job.Schedule.EveryMs);
        Assert.Equal(5_000, job.Schedule.InitialDelayMs);
        Assert.False(job.DeleteAfterRun);
    }

    [Fact]
    public void AddDailyScheduleKind_IgnoresDelayAndEveryFields()
    {
        using var svc = new CronService(_path);
        var tools = new CronTools(svc);

        var result = tools.Cron(
            action: "add",
            scheduleKind: "daily",
            message: "tea",
            everySeconds: 60,
            delaySeconds: 10,
            dailyHour: 9,
            dailyMinute: 30,
            timeZone: "UTC");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        var schedule = root.GetProperty("schedule");

        Assert.Equal("created", root.GetProperty("status").GetString());
        Assert.Equal("daily", schedule.GetProperty("kind").GetString());
        Assert.Equal(9, schedule.GetProperty("dailyHour").GetInt32());
        Assert.Equal(30, schedule.GetProperty("dailyMinute").GetInt32());
        Assert.False(root.GetProperty("deleteAfterRun").GetBoolean());

        var job = Assert.Single(svc.ListJobs(includeDisabled: true));
        Assert.Equal("daily", job.Schedule.Kind);
        Assert.Equal(9, job.Schedule.DailyHour);
        Assert.Equal(30, job.Schedule.DailyMinute);
        Assert.Equal("UTC", job.Schedule.Tz);
        Assert.False(job.DeleteAfterRun);
    }

    [Fact]
    public void AddWithoutScheduleKind_ReturnsClearError()
    {
        using var svc = new CronService(_path);
        var tools = new CronTools(svc);

        var result = tools.Cron(action: "add", message: "x", delaySeconds: 10);

        Assert.Contains("Parameter 'scheduleKind' is required", ErrorOf(result));
        Assert.Empty(svc.ListJobs(includeDisabled: true));
    }

    [Fact]
    public void AddWithInvalidKindAndMissingSelectedKindFields_ReturnsClearErrors()
    {
        using var svc = new CronService(_path);
        var tools = new CronTools(svc);

        Assert.Contains(
            "Invalid parameter 'scheduleKind'",
            ErrorOf(tools.Cron(action: "add", scheduleKind: "later", message: "x")));
        Assert.Contains(
            "Parameter 'delaySeconds' must be a positive integer when scheduleKind is 'at'",
            ErrorOf(tools.Cron(action: "add", scheduleKind: "at", message: "x")));
        Assert.Contains(
            "Parameter 'everySeconds' must be a positive integer when scheduleKind is 'every'",
            ErrorOf(tools.Cron(action: "add", scheduleKind: "every", message: "x")));
        Assert.Contains(
            "scheduleKind 'daily' requires dailyTime",
            ErrorOf(tools.Cron(action: "add", scheduleKind: "daily", message: "x")));

        Assert.Empty(svc.ListJobs(includeDisabled: true));
    }

    private static string ErrorOf(string result)
    {
        using var doc = JsonDocument.Parse(result);
        return doc.RootElement.GetProperty("error").GetString() ?? string.Empty;
    }
}
