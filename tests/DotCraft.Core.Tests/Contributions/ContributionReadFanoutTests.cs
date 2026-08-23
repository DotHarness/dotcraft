using DotCraft.Contributions;
using Xunit;

namespace DotCraft.Tests.Contributions;

/// <summary>The fan-out shape: every contribution is invoked for its effect, and a throwing one is
/// reported and skipped so the ones behind it still run. Every contribution point read through
/// <see cref="ContributionRead.Fanout"/> or <see cref="ContributionRead.FanoutAsync"/> inherits these.</summary>
public sealed class ContributionReadFanoutTests
{
    [Fact]
    public void EveryContribution_IsInvokedInResolvedOrder()
    {
        var calls = new List<string>();

        ContributionRead.Fanout(["a", "b", "c"], calls.Add, (_, _) => Assert.Fail("Nothing threw."));

        Assert.Equal(["a", "b", "c"], calls);
    }

    [Fact]
    public void AThrowingContribution_IsReportedAndSkipped_AndTheOnesBehindItStillRun()
    {
        var calls = new List<string>();
        var failures = new List<(string Contribution, string Message)>();

        ContributionRead.Fanout(
            ["ahead", "faulty", "behind"],
            name =>
            {
                calls.Add(name);
                if (name == "faulty")
                    throw new InvalidOperationException("boom");
            },
            (name, exception) => failures.Add((name, exception.Message)));

        Assert.Equal(["ahead", "faulty", "behind"], calls);
        Assert.Equal([("faulty", "boom")], failures);
    }

    [Fact]
    public void AnEmptyOrAbsentList_InvokesNothing()
    {
        ContributionRead.Fanout<string>([], _ => Assert.Fail("An empty list invoked a contribution."), (_, _) => { });
        ContributionRead.Fanout<string>(null, _ => Assert.Fail("A null list invoked a contribution."), (_, _) => { });
    }

    [Fact]
    public async Task Async_AwaitsEachContributionBeforeStartingTheNext()
    {
        var calls = new List<string>();

        await ContributionRead.FanoutAsync(
            ["a", "b", "c"],
            async (name, _) =>
            {
                calls.Add($"enter:{name}");
                await Task.Yield();
                calls.Add($"exit:{name}");
            },
            (_, _) => Assert.Fail("Nothing threw."));

        Assert.Equal(["enter:a", "exit:a", "enter:b", "exit:b", "enter:c", "exit:c"], calls);
    }

    [Fact]
    public async Task Async_AThrowingContribution_IsReportedAndSkipped_AndTheOnesBehindItStillRun()
    {
        var calls = new List<string>();
        var failures = new List<string>();

        await ContributionRead.FanoutAsync(
            ["ahead", "faulty", "behind"],
            async (name, _) =>
            {
                await Task.Yield();
                calls.Add(name);
                if (name == "faulty")
                    throw new InvalidOperationException("boom");
            },
            (name, _) => failures.Add(name));

        Assert.Equal(["ahead", "faulty", "behind"], calls);
        Assert.Equal(["faulty"], failures);
    }

    [Fact]
    public async Task Async_HandsTheCallersTokenToEveryContribution()
    {
        using var source = new CancellationTokenSource();
        var observed = new List<CancellationToken>();

        await ContributionRead.FanoutAsync(
            ["a", "b"],
            (_, token) =>
            {
                observed.Add(token);
                return ValueTask.CompletedTask;
            },
            (_, _) => Assert.Fail("Nothing threw."),
            source.Token);

        Assert.Equal([source.Token, source.Token], observed);
    }

    [Fact]
    public async Task Async_AnEmptyOrAbsentList_InvokesNothing()
    {
        await ContributionRead.FanoutAsync<string>(
            [],
            (_, _) => throw new InvalidOperationException("An empty list invoked a contribution."),
            (_, _) => { });
        await ContributionRead.FanoutAsync<string>(
            null,
            (_, _) => throw new InvalidOperationException("A null list invoked a contribution."),
            (_, _) => { });
    }
}
