using DotCraft.Contributions;
using Xunit;

namespace DotCraft.Tests.Contributions;

/// <summary>The fold shape: each contribution transforms the result of the one before it, and a throwing
/// one is reported and leaves the accumulator where the step before it left it. Every contribution point read through
/// <see cref="ContributionRead.Fold"/> or <see cref="ContributionRead.FoldAsync"/> inherits these.</summary>
public sealed class ContributionReadFoldTests
{
    [Fact]
    public void EachContribution_TransformsTheResultOfTheOneBeforeIt()
    {
        var folded = ContributionRead.Fold(["a", "b", "c"], "seed", static (state, name) => $"{state}:{name}");

        Assert.Equal("seed:a:b:c", folded);
    }

    [Fact]
    public void TheSeed_IsReturnedForAnEmptyOrAbsentList()
    {
        Assert.Equal("seed", ContributionRead.Fold<string, string>([], "seed", static (state, _) => state + "!"));
        Assert.Equal("seed", ContributionRead.Fold<string, string>(null, "seed", static (state, _) => state + "!"));
    }

    [Fact]
    public void Reverse_WalksTheListFromTheLastContributionToTheFirst()
    {
        // A wrapping fold runs innermost first, so the lowest-order contribution ends up outermost.
        var folded = ContributionRead.Fold(
            ["outer", "middle", "inner"],
            "core",
            static (state, name) => $"{name}({state})",
            reverse: true);

        Assert.Equal("outer(middle(inner(core)))", folded);
    }

    [Fact]
    public void AThrowingStep_IsReported_AndLeavesTheAccumulatorWhereTheStepBeforeItLeftIt()
    {
        var failures = new List<(string Contribution, string Message)>();

        var folded = ContributionRead.Fold(
            ["ahead", "faulty", "behind"],
            "seed",
            static (state, name) => name == "faulty"
                ? throw new InvalidOperationException("boom")
                : $"{state}:{name}",
            (name, exception) => failures.Add((name, exception.Message)));

        Assert.Equal("seed:ahead:behind", folded);
        Assert.Equal([("faulty", "boom")], failures);
    }

    [Fact]
    public void WithoutAFailureReport_AThrowingStepPropagates()
    {
        Assert.Throws<InvalidOperationException>(() => ContributionRead.Fold<string, string>(
            ["faulty"],
            "seed",
            static (_, _) => throw new InvalidOperationException("boom")));
    }

    [Fact]
    public async Task Async_FoldsInResolvedOrder_AndReturnsTheSeedForAnEmptyList()
    {
        var folded = await ContributionRead.FoldAsync(
            ["a", "b", "c"],
            "seed",
            static async (state, name, _) =>
            {
                await Task.Yield();
                return $"{state}:{name}";
            });

        Assert.Equal("seed:a:b:c", folded);
        Assert.Equal(
            "seed",
            await ContributionRead.FoldAsync<string, string>(
                null,
                "seed",
                static (state, _, _) => ValueTask.FromResult(state + "!")));
    }

    [Fact]
    public async Task Async_AThrowingStep_IsReported_AndTheOnesBehindItStillFold()
    {
        var failures = new List<string>();

        var folded = await ContributionRead.FoldAsync(
            ["ahead", "faulty", "behind"],
            "seed",
            static (state, name, _) => name == "faulty"
                ? throw new InvalidOperationException("boom")
                : ValueTask.FromResult($"{state}:{name}"),
            (name, _) => failures.Add(name));

        Assert.Equal("seed:ahead:behind", folded);
        Assert.Equal(["faulty"], failures);
    }

    [Fact]
    public async Task Async_HandsTheCallersTokenToEveryStep()
    {
        using var source = new CancellationTokenSource();
        var observed = new List<CancellationToken>();

        await ContributionRead.FoldAsync(
            ["a", "b"],
            0,
            (state, _, token) =>
            {
                observed.Add(token);
                return ValueTask.FromResult(state + 1);
            },
            cancellationToken: source.Token);

        Assert.Equal([source.Token, source.Token], observed);
    }
}
