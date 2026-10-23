using DotCraft.Agents;
using DotCraft.CLI;
using Xunit;

namespace DotCraft.Tests.CLI;

public sealed class ProviderUsageWindowPresentationTests
{
    [Fact]
    public void ShapeOrdersKnownWindowsByDurationInsteadOfUpstreamSlot()
    {
        var weekly = Window(TimeSpan.FromDays(7));
        var fiveHour = Window(TimeSpan.FromHours(5));
        var snapshot = Snapshot(primary: weekly, secondary: fiveHour);

        var windows = ProviderUsageWindowPresentation.Shape(snapshot);

        Assert.Equal(
            new[]
            {
                new ProviderUsageDisplayWindow(fiveHour, ProviderUsageWindowKind.FiveHour),
                new ProviderUsageDisplayWindow(weekly, ProviderUsageWindowKind.Weekly)
            },
            windows);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShapeRecognizesWeeklyOnlyWindowInEitherSlot(bool usePrimarySlot)
    {
        var weekly = Window(TimeSpan.FromDays(7));
        var snapshot = usePrimarySlot
            ? Snapshot(primary: weekly, secondary: null)
            : Snapshot(primary: null, secondary: weekly);

        var display = Assert.Single(ProviderUsageWindowPresentation.Shape(snapshot));

        Assert.Equal(new ProviderUsageDisplayWindow(weekly, ProviderUsageWindowKind.Weekly), display);
    }

    [Theory]
    [InlineData(17_100)]
    [InlineData(635_040)]
    public void ClassifyAcceptsFivePercentDurationTolerance(int durationSeconds)
    {
        var expected = durationSeconds < 100_000
            ? ProviderUsageWindowKind.FiveHour
            : ProviderUsageWindowKind.Weekly;

        var actual = ProviderUsageWindowPresentation.Classify(
            Window(TimeSpan.FromSeconds(durationSeconds)),
            ProviderUsageWindowKind.Primary);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ShapeUsesGenericSlotKindsForUnknownDurations()
    {
        var primary = Window(TimeSpan.FromDays(30));
        var secondary = Window(TimeSpan.FromHours(1));

        var windows = ProviderUsageWindowPresentation.Shape(Snapshot(primary, secondary));

        Assert.Equal(
            new[]
            {
                new ProviderUsageDisplayWindow(primary, ProviderUsageWindowKind.Primary),
                new ProviderUsageDisplayWindow(secondary, ProviderUsageWindowKind.Secondary)
            },
            windows);
    }

    private static ProviderUsageSnapshot Snapshot(ProviderRateLimitWindow? primary, ProviderRateLimitWindow? secondary)
        => new(DateTimeOffset.UtcNow, new Dictionary<string, double>(), PlanType: "pro", Primary: primary, Secondary: secondary);

    private static ProviderRateLimitWindow Window(TimeSpan duration)
        => new(9, duration, DateTimeOffset.UtcNow.Add(duration));
}
