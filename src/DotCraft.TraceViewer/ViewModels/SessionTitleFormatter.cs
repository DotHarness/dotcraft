using System.Globalization;

namespace DotCraft.TraceViewer.ViewModels;

internal static class SessionTitleFormatter
{
    private const string ReminderStart = "<system-reminder>";
    private const string ReminderEnd = "</system-reminder>";

    private static readonly string[] RuntimeContextSignature =
    [
        "## Environment",
        "CurrentDate:",
        "TimeZone:",
        "## Mode",
        "CurrentMode:",
        "## Mode Action",
    ];

    public static SessionTitle Format(string? firstUserRequest, DateTimeOffset startedAt)
    {
        var displayText = CollapseWhitespace(Split(firstUserRequest).UserContent);
        if (displayText.Length == 0)
        {
            displayText = string.Format(
                CultureInfo.CurrentCulture,
                "Session on {0:g}",
                startedAt.ToLocalTime());
        }

        return new SessionTitle(displayText, Truncate(displayText, 80));
    }

    public static ModelFacingRequest Split(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new(string.Empty, string.Empty);

        var trimmed = value.TrimEnd();
        var start = trimmed.LastIndexOf(ReminderStart, StringComparison.Ordinal);
        if (start < 0)
            return new(trimmed, string.Empty);

        var end = trimmed.IndexOf(ReminderEnd, start + ReminderStart.Length, StringComparison.Ordinal);
        if (end < 0 || end + ReminderEnd.Length != trimmed.Length)
            return new(trimmed, string.Empty);

        var reminder = trimmed[start..];
        if (RuntimeContextSignature.Any(signature =>
                !reminder.Contains(signature, StringComparison.Ordinal)))
        {
            return new(trimmed, string.Empty);
        }

        return new(trimmed[..start].TrimEnd(), reminder);
    }

    private static string CollapseWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..(maximumLength - 3)] + "...";
}

internal sealed record SessionTitle(string FullText, string CompactText);

internal sealed record ModelFacingRequest(string UserContent, string RuntimeContext);
