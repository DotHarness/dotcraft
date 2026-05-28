using System.Text.Json;

namespace DotCraft.Cron;

/// <summary>
/// Human-readable display formatters for <see cref="CronTools"/> calls and results.
/// </summary>
public static class CronToolDisplays
{
    /// <summary>
    /// Formats the Cron tool call arguments into a short summary line.
    /// </summary>
    public static string Cron(IDictionary<string, object?>? args)
    {
        var action = GetString(args, "action")?.ToLowerInvariant() ?? "?";

        return action switch
        {
            "add" => FormatAdd(args),
            "list" => "List scheduled jobs",
            "remove" => $"Remove job {GetString(args, "jobId") ?? "?"}",
            _ => $"Cron ({action})"
        };
    }

    private static string FormatAdd(IDictionary<string, object?>? args)
    {
        var name = GetString(args, "name");
        var message = GetString(args, "message") ?? "task";
        var label = name ?? (message.Length > 40 ? message[..40] + "…" : message);

        if (args != null && (!string.IsNullOrEmpty(GetString(args, "dailyTime")) || args.ContainsKey("dailyHour")))
        {
            var dt = GetString(args, "dailyTime");
            if (!string.IsNullOrEmpty(dt))
                return $"Schedule \"{label}\" daily at {dt} ({GetString(args, "timeZone") ?? "UTC"})";
            if (args.TryGetValue("dailyHour", out var ho) && TryGetLong(ho, out var dh))
            {
                var dm = args.TryGetValue("dailyMinute", out var mo) && TryGetLong(mo, out var dmin) ? dmin : 0L;
                return $"Schedule \"{label}\" daily at {dh:D2}:{dm:D2} ({GetString(args, "timeZone") ?? "UTC"})";
            }
        }

        if (args != null && args.TryGetValue("everySeconds", out var everyObj) && TryGetLong(everyObj, out var everySec))
        {
            if (args.TryGetValue("delaySeconds", out var delayObj) && TryGetLong(delayObj, out var delaySec))
                return $"Schedule \"{label}\" in {FormatDuration(delaySec)}, then every {FormatDuration(everySec)}";
            return $"Schedule \"{label}\" every {FormatDuration(everySec)}";
        }

        if (args != null && args.TryGetValue("delaySeconds", out var delayObj2) && TryGetLong(delayObj2, out var delaySec2))
            return $"Schedule \"{label}\" in {FormatDuration(delaySec2)}";

        return $"Schedule \"{label}\"";
    }

    /// <summary>
    /// Formats the JSON result string returned by <see cref="CronTools.Cron"/> into
    /// human-readable lines. Returns null if the result cannot be parsed.
    /// </summary>
    public static IReadOnlyList<string>? CronResult(string? result)
    {
        if (string.IsNullOrWhiteSpace(result)) return null;

        try
        {
            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errProp))
                return [$"Error: {errProp.GetString()}"];

            if (root.TryGetProperty("status", out var statusProp))
            {
                var status = statusProp.GetString();

                if (status == "created")
                {
                    var id = root.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                    var jobName = root.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;

                    string timeLabel = "—";
                    if (root.TryGetProperty("nextRun", out var nextRunProp) &&
                        nextRunProp.ValueKind == JsonValueKind.Number)
                    {
                        var nextRunMs = nextRunProp.GetInt64();
                        var nextRunTime = DateTimeOffset.FromUnixTimeMilliseconds(nextRunMs).ToLocalTime();
                        timeLabel = nextRunTime.ToString("HH:mm");
                    }

                    var nameDisplay = jobName ?? id ?? "job";
                    return [$"Created: {nameDisplay}  ·  triggers at {timeLabel}"];
                }

                if (status == "removed")
                {
                    var jobId = root.TryGetProperty("jobId", out var jid) ? jid.GetString() : null;
                    return [$"Removed job {jobId}"];
                }

                if (status == "not_found")
                {
                    var jobId = root.TryGetProperty("jobId", out var jid) ? jid.GetString() : null;
                    return [$"Job {jobId} not found"];
                }
            }

            // list result
            if (root.TryGetProperty("count", out var countProp))
            {
                var count = countProp.GetInt32();
                return count == 0
                    ? ["No scheduled jobs"]
                    : [$"{count} scheduled job{(count == 1 ? "" : "s")}"];
            }
        }
        catch
        {
            // fall through
        }

        return null;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string? GetString(IDictionary<string, object?>? args, string key)
    {
        if (args == null || !args.TryGetValue(key, out var val)) return null;
        return val?.ToString();
    }

    private static bool TryGetLong(object? value, out long result)
    {
        result = 0;
        if (value == null) return false;
        if (value is long l) { result = l; return true; }
        if (value is int i) { result = i; return true; }
        if (value is double d) { result = (long)d; return true; }
        if (long.TryParse(value.ToString(), out result)) return true;
        return false;
    }

    private static string FormatDuration(long seconds)
    {
        if (seconds < 60) return $"{seconds}s";
        if (seconds < 3600) return $"{seconds / 60}m";
        if (seconds < 86400) return $"{seconds / 3600}h";
        return $"{seconds / 86400}d";
    }
}
