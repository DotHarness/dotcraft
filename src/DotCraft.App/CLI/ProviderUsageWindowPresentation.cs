using DotCraft.Agents;

namespace DotCraft.CLI;

internal enum ProviderUsageWindowKind
{
    FiveHour,
    Weekly,
    Primary,
    Secondary
}

internal sealed record ProviderUsageDisplayWindow(
    ProviderRateLimitWindow Window,
    ProviderUsageWindowKind Kind);

internal static class ProviderUsageWindowPresentation
{
    private const double DurationTolerance = 0.05;
    private static readonly TimeSpan FiveHours = TimeSpan.FromHours(5);
    private static readonly TimeSpan OneWeek = TimeSpan.FromDays(7);

    internal static IReadOnlyList<ProviderUsageDisplayWindow> Shape(ProviderUsageSnapshot snapshot)
    {
        var windows = new List<ProviderUsageDisplayWindow>(2);
        AddWindow(windows, snapshot.Primary, ProviderUsageWindowKind.Primary);
        AddWindow(windows, snapshot.Secondary, ProviderUsageWindowKind.Secondary);
        windows.Sort(static (left, right) => Rank(left.Kind).CompareTo(Rank(right.Kind)));
        return windows;
    }

    internal static ProviderUsageWindowKind Classify(
        ProviderRateLimitWindow window,
        ProviderUsageWindowKind fallback)
    {
        if (IsApproximate(window.WindowDuration, FiveHours))
            return ProviderUsageWindowKind.FiveHour;
        if (IsApproximate(window.WindowDuration, OneWeek))
            return ProviderUsageWindowKind.Weekly;
        return fallback;
    }

    private static void AddWindow(
        List<ProviderUsageDisplayWindow> windows,
        ProviderRateLimitWindow? window,
        ProviderUsageWindowKind fallback)
    {
        if (window is not null)
            windows.Add(new ProviderUsageDisplayWindow(window, Classify(window, fallback)));
    }

    private static bool IsApproximate(TimeSpan actual, TimeSpan expected)
    {
        var ratio = actual.TotalSeconds / expected.TotalSeconds;
        return ratio >= 1 - DurationTolerance && ratio <= 1 + DurationTolerance;
    }

    private static int Rank(ProviderUsageWindowKind kind) => kind switch
    {
        ProviderUsageWindowKind.FiveHour => 0,
        ProviderUsageWindowKind.Weekly => 1,
        ProviderUsageWindowKind.Primary => 2,
        ProviderUsageWindowKind.Secondary => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
