using DotCraft.Contributions;
using Xunit;

namespace DotCraft.Tests.Contributions;

public sealed class ContributionReadTests
{
    [Fact]
    public void Authority_IsTheLastContributionOrTheBuiltIn()
    {
        Assert.Equal("last", ContributionRead.Authority(["first", "middle", "last"], "built-in"));
        Assert.Equal("built-in", ContributionRead.Authority([], "built-in"));
        Assert.Equal("built-in", ContributionRead.Authority(null, "built-in"));
        Assert.Null(ContributionRead.Authority<string>([]));
    }

    [Fact]
    public void Fanout_InvokesEveryContributionInOrderAndSkipsAThrowingOne()
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
        ContributionRead.Fanout<string>(null, _ => Assert.Fail("A null list invoked a contribution."), (_, _) => { });
    }

    [Fact]
    public async Task FanoutAsync_AwaitsEachInTurnWithTheCallersTokenAndSkipsAThrowingOne()
    {
        using var source = new CancellationTokenSource();
        var calls = new List<string>();
        var failures = new List<string>();

        await ContributionRead.FanoutAsync(
            ["a", "faulty", "c"],
            async (name, token) =>
            {
                Assert.Equal(source.Token, token);
                calls.Add($"enter:{name}");
                await Task.Yield();
                calls.Add($"exit:{name}");
                if (name == "faulty")
                    throw new InvalidOperationException("boom");
            },
            (name, _) => failures.Add(name),
            source.Token);

        Assert.Equal(["enter:a", "exit:a", "enter:faulty", "exit:faulty", "enter:c", "exit:c"], calls);
        Assert.Equal(["faulty"], failures);
    }

    [Fact]
    public void Fold_ChainsInOrderOrReverseAndSkipsAThrowingStep()
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
        Assert.Equal(
            "outer(middle(inner(core)))",
            ContributionRead.Fold(["outer", "middle", "inner"], "core", static (state, name) => $"{name}({state})", reverse: true));
        Assert.Equal("seed", ContributionRead.Fold<string, string>(null, "seed", static (state, _) => state + "!"));
        Assert.Throws<InvalidOperationException>(() => ContributionRead.Fold<string, string>(
            ["faulty"],
            "seed",
            static (_, _) => throw new InvalidOperationException("boom")));
    }

    [Fact]
    public async Task FoldAsync_ChainsWithTheCallersTokenAndSkipsAThrowingStep()
    {
        using var source = new CancellationTokenSource();
        var failures = new List<string>();

        var folded = await ContributionRead.FoldAsync(
            ["ahead", "faulty", "behind"],
            "seed",
            async (state, name, token) =>
            {
                Assert.Equal(source.Token, token);
                await Task.Yield();
                return name == "faulty" ? throw new InvalidOperationException("boom") : $"{state}:{name}";
            },
            (name, _) => failures.Add(name),
            source.Token);

        Assert.Equal("seed:ahead:behind", folded);
        Assert.Equal(["faulty"], failures);
    }

    [Fact]
    public void FirstOpinion_StopsAtTheFirstAnswerSkippingDeclinesAndThrowingOnes()
    {
        var asked = new List<string>();
        var failures = new List<(string Contribution, string Message)>();

        var opinion = ContributionRead.FirstOpinion(
            ["declines", "faulty", "answers", "unreached"],
            name =>
            {
                asked.Add(name);
                return name switch
                {
                    "faulty" => throw new InvalidOperationException("boom"),
                    "declines" => null,
                    _ => name
                };
            },
            (name, exception) => failures.Add((name, exception.Message)));

        Assert.Equal("answers", opinion);
        Assert.Equal(["declines", "faulty", "answers"], asked);
        Assert.Equal([("faulty", "boom")], failures);
        Assert.Null(ContributionRead.FirstOpinion<string, string>(["a", "b"], static _ => null));
        Assert.Null(ContributionRead.FirstOpinion<string, string>(null, static _ => "never"));
        Assert.Throws<InvalidOperationException>(() => ContributionRead.FirstOpinion<string, string>(
            ["faulty"],
            static _ => throw new InvalidOperationException("boom")));
        Assert.False(ContributionRead.FirstOpinion(
            ["declines", "denies", "allows"],
            static (string name) => name switch
            {
                "denies" => false,
                "allows" => true,
                _ => (bool?)null
            }));
        Assert.Null(ContributionRead.FirstOpinion(["a"], static (string _) => (bool?)null));
    }

    [Fact]
    public async Task FirstOpinionAsync_StopsAtTheFirstAnswerSkippingThrowingOnesOrPropagatesWithoutAReport()
    {
        var asked = new List<string>();
        var failures = new List<string>();

        var opinion = await ContributionRead.FirstOpinionAsync(
            ["declines", "faulty", "answers", "unreached"],
            async (name, _) =>
            {
                await Task.Yield();
                asked.Add(name);
                return name switch
                {
                    "faulty" => throw new InvalidOperationException("boom"),
                    "answers" => name,
                    _ => null
                };
            },
            (name, _) => failures.Add(name));

        Assert.Equal("answers", opinion);
        Assert.Equal(["declines", "faulty", "answers"], asked);
        Assert.Equal(["faulty"], failures);
        Assert.Null(await ContributionRead.FirstOpinionAsync<string, string>(
            null,
            static (_, _) => ValueTask.FromResult<string?>("never")));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ContributionRead.FirstOpinionAsync<string, string>(
                ["faulty"],
                static (_, _) => throw new InvalidOperationException("boom")));
    }
}
