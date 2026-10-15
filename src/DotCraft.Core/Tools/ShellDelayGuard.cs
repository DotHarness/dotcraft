using System.Globalization;
using System.Text.RegularExpressions;

namespace DotCraft.Tools;

internal static partial class ShellDelayGuard
{
    private const double MinBlockedSeconds = 2;

    public static bool TryDetect(string command, out string delay)
    {
        delay = FirstSegment(command);
        var seconds = ParseSeconds(delay);
        return seconds >= MinBlockedSeconds;
    }

    private static string FirstSegment(string command)
    {
        var text = command.Trim();
        var end = text.Length;
        foreach (var separator in new[] { "&&", "||", ";", "|", "\n", "\r" })
        {
            var index = text.IndexOf(separator, StringComparison.Ordinal);
            if (index >= 0 && index < end)
                end = index;
        }
        return text[..end].Trim();
    }

    private static double ParseSeconds(string segment)
    {
        if (SleepPattern().Match(segment) is { Success: true } sleep)
            return Number(sleep.Groups["n"].Value);
        if (StartSleepSecondsPattern().Match(segment) is { Success: true } seconds)
            return Number(seconds.Groups["n"].Value);
        if (StartSleepMillisecondsPattern().Match(segment) is { Success: true } milliseconds)
            return Number(milliseconds.Groups["n"].Value) / 1000;
        if (TimeoutPattern().Match(segment) is { Success: true } timeout)
            return Number(timeout.Groups["n"].Value);
        return 0;
    }

    private static double Number(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0;

    [GeneratedRegex(@"^sleep\s+(?<n>\d+(?:\.\d+)?)s?$", RegexOptions.IgnoreCase)]
    private static partial Regex SleepPattern();

    [GeneratedRegex(@"^start-sleep(?:\s+-(?:s|seconds))?\s+(?<n>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex StartSleepSecondsPattern();

    [GeneratedRegex(@"^start-sleep\s+-(?:m|milliseconds)\s+(?<n>\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex StartSleepMillisecondsPattern();

    [GeneratedRegex(@"^timeout(?:\.exe)?\s+(?:/t\s+)?(?<n>\d+)(?:\s+/nobreak)?(?:\s*>\s*nul)?$", RegexOptions.IgnoreCase)]
    private static partial Regex TimeoutPattern();
}
