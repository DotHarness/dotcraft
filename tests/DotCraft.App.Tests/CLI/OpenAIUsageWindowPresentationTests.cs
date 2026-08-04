using DotCraft.Auth.OpenAI;
using DotCraft.CLI;
using Xunit;

namespace DotCraft.Tests.CLI;

public sealed class OpenAIUsageWindowPresentationTests
{
    [Fact]
    public void ShapeOrdersKnownWindowsByDurationInsteadOfUpstreamSlot()
    {
        var weekly = Window(TimeSpan.FromDays(7));
        var fiveHour = Window(TimeSpan.FromHours(5));
        var snapshot = Snapshot(primary: weekly, secondary: fiveHour);

        var windows = OpenAIUsageWindowPresentation.Shape(snapshot);

        Assert.Equal(
            new[]
            {
                new OpenAIUsageDisplayWindow(fiveHour, OpenAIUsageWindowKind.FiveHour),
                new OpenAIUsageDisplayWindow(weekly, OpenAIUsageWindowKind.Weekly)
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

        var display = Assert.Single(OpenAIUsageWindowPresentation.Shape(snapshot));

        Assert.Equal(new OpenAIUsageDisplayWindow(weekly, OpenAIUsageWindowKind.Weekly), display);
    }

    [Theory]
    [InlineData(17_100)]
    [InlineData(18_900)]
    [InlineData(574_560)]
    [InlineData(635_040)]
    public void ClassifyAcceptsFivePercentDurationTolerance(int durationSeconds)
    {
        var expected = durationSeconds < 100_000
            ? OpenAIUsageWindowKind.FiveHour
            : OpenAIUsageWindowKind.Weekly;

        var actual = OpenAIUsageWindowPresentation.Classify(
            Window(TimeSpan.FromSeconds(durationSeconds)),
            OpenAIUsageWindowKind.Primary);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ShapeUsesGenericSlotKindsForUnknownDurations()
    {
        var primary = Window(TimeSpan.FromDays(30));
        var secondary = Window(TimeSpan.FromHours(1));

        var windows = OpenAIUsageWindowPresentation.Shape(Snapshot(primary, secondary));

        Assert.Equal(
            new[]
            {
                new OpenAIUsageDisplayWindow(primary, OpenAIUsageWindowKind.Primary),
                new OpenAIUsageDisplayWindow(secondary, OpenAIUsageWindowKind.Secondary)
            },
            windows);
    }

    private static OpenAIUsageSnapshot Snapshot(RateLimitWindow? primary, RateLimitWindow? secondary)
        => new("pro", primary, secondary, null, null, DateTimeOffset.UtcNow);

    private static RateLimitWindow Window(TimeSpan duration)
        => new(9, duration, DateTimeOffset.UtcNow.Add(duration));
}
