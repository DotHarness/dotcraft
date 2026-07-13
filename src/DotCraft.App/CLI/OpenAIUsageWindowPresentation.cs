using DotCraft.Auth.OpenAI;

namespace DotCraft.CLI;

internal enum OpenAIUsageWindowKind
{
    FiveHour,
    Weekly,
    Primary,
    Secondary
}

internal sealed record OpenAIUsageDisplayWindow(
    RateLimitWindow Window,
    OpenAIUsageWindowKind Kind);

internal static class OpenAIUsageWindowPresentation
{
    private const double DurationTolerance = 0.05;
    private static readonly TimeSpan FiveHours = TimeSpan.FromHours(5);
    private static readonly TimeSpan OneWeek = TimeSpan.FromDays(7);

    internal static IReadOnlyList<OpenAIUsageDisplayWindow> Shape(OpenAIUsageSnapshot snapshot)
    {
        var windows = new List<OpenAIUsageDisplayWindow>(2);
        AddWindow(windows, snapshot.Primary, OpenAIUsageWindowKind.Primary);
        AddWindow(windows, snapshot.Secondary, OpenAIUsageWindowKind.Secondary);
        windows.Sort(static (left, right) => Rank(left.Kind).CompareTo(Rank(right.Kind)));
        return windows;
    }

    internal static OpenAIUsageWindowKind Classify(
        RateLimitWindow window,
        OpenAIUsageWindowKind fallback)
    {
        if (IsApproximate(window.WindowDuration, FiveHours))
            return OpenAIUsageWindowKind.FiveHour;
        if (IsApproximate(window.WindowDuration, OneWeek))
            return OpenAIUsageWindowKind.Weekly;
        return fallback;
    }

    private static void AddWindow(
        List<OpenAIUsageDisplayWindow> windows,
        RateLimitWindow? window,
        OpenAIUsageWindowKind fallback)
    {
        if (window is not null)
            windows.Add(new OpenAIUsageDisplayWindow(window, Classify(window, fallback)));
    }

    private static bool IsApproximate(TimeSpan actual, TimeSpan expected)
    {
        var ratio = actual.TotalSeconds / expected.TotalSeconds;
        return ratio >= 1 - DurationTolerance && ratio <= 1 + DurationTolerance;
    }

    private static int Rank(OpenAIUsageWindowKind kind) => kind switch
    {
        OpenAIUsageWindowKind.FiveHour => 0,
        OpenAIUsageWindowKind.Weekly => 1,
        OpenAIUsageWindowKind.Primary => 2,
        OpenAIUsageWindowKind.Secondary => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
