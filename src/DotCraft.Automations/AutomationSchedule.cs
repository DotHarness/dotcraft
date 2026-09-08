using System.ComponentModel;

namespace DotCraft.Automations;

/// <summary>Explicit interval or time-zone-aware calendar schedule.</summary>
public sealed record AutomationSchedule
{
    [Description("Schedule kind: at, every, daily, weekdays, or weekly.")]
    public string Kind { get; init; } = "at";
    [Description("Required for at: absolute UTC ISO 8601 timestamp.")]
    public DateTimeOffset? At { get; init; }
    [Description("Required for every: interval in milliseconds, positive and at most 315576000000 (ten years).")]
    public long? EveryMs { get; init; }
    [Description("Calendar hour in the specified time zone, 0 through 23.")]
    public int? Hour { get; init; }
    [Description("Calendar minute, 0 through 59.")]
    public int? Minute { get; init; }
    [Description("Required for daily/weekdays/weekly: explicit IANA or Windows time zone identifier, e.g. UTC. Never infer an unknown time zone.")]
    public string? TimeZone { get; init; }
    [Description("Required for weekly: ISO weekdays, Monday=1 through Sunday=7.")]
    public int[]? Days { get; init; }

    /// <summary>Compares the fields that determine occurrences, treating weekly days as a set.</summary>
    public bool HasSameOccurrences(AutomationSchedule? other)
    {
        if (other == null || Kind != other.Kind) return false;
        return Kind switch
        {
            "at" => At == other.At,
            "every" => EveryMs == other.EveryMs,
            "daily" or "weekdays" => Hour == other.Hour && Minute == other.Minute && TimeZone == other.TimeZone,
            "weekly" => Hour == other.Hour && Minute == other.Minute && TimeZone == other.TimeZone
                && (Days ?? []).ToHashSet().SetEquals(other.Days ?? []),
            _ => false
        };
    }

    /// <summary>Rejects incomplete and invalid schedule inputs.</summary>
    public void Validate()
    {
        if (Kind == "at") { if (At == null) throw new ArgumentException("automation.schedule.atRequired"); return; }
        if (Kind == "every") { if (EveryMs is not (> 0 and <= 315576000000L)) throw new ArgumentException("automation.schedule.invalidInterval"); return; }
        if (Kind is not ("daily" or "weekdays" or "weekly")) throw new ArgumentException("automation.schedule.invalidKind");
        if (Hour is not (>= 0 and <= 23) || Minute is not (>= 0 and <= 59)) throw new ArgumentException("automation.schedule.invalidTime");
        if (string.IsNullOrWhiteSpace(TimeZone)) throw new ArgumentException("automation.schedule.timeZoneRequired");
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(TimeZone); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw new ArgumentException("automation.schedule.invalidTimeZone", ex); }
        if (Kind == "weekly" && (Days is not { Length: > 0 } || Days.Any(d => d < 1 || d > 7)))
            throw new ArgumentException("automation.schedule.daysRequired");
    }

    /// <summary>Finds the first planned occurrence strictly after the supplied instant.</summary>
    public DateTimeOffset? Next(DateTimeOffset after, DateTimeOffset? previous = null)
    {
        Validate();
        if (Kind == "at") return At > after ? At : null;
        if (Kind == "every")
        {
            var basis = previous ?? after;
            var interval = EveryMs!.Value;
            var steps = Math.Max(1, Math.Floor((after - basis).TotalMilliseconds / interval) + 1);
            return basis.AddMilliseconds(steps * interval);
        }
        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZone!);
        var localDate = TimeZoneInfo.ConvertTime(after, zone).Date;
        for (var offset = 0; offset < 370; offset++)
        {
            var date = localDate.AddDays(offset);
            var day = ((int)date.DayOfWeek + 6) % 7 + 1;
            if (Kind == "weekdays" && day > 5 || Kind == "weekly" && !Days!.Contains(day)) continue;
            var local = DateTime.SpecifyKind(date.AddHours(Hour!.Value).AddMinutes(Minute!.Value), DateTimeKind.Unspecified);
            while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
            var utc = zone.IsAmbiguousTime(local)
                ? new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime()
                : new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
            if (utc > after) return utc;
        }
        throw new InvalidOperationException("automation.schedule.noOccurrence");
    }
}
