using DotCraft.Contributions;
using Xunit;

namespace DotCraft.Tests.Contributions;

/// <summary>The first-opinion shape: the walk stops at the first contribution that does not decline,
/// a declining one defers to the next, and a throwing one is reported and skipped. Every contribution point read
/// through <see cref="ContributionRead.FirstOpinion"/> or <see cref="ContributionRead.FirstOpinionAsync"/> inherits these.</summary>
public sealed class ContributionReadFirstOpinionTests
{
    [Fact]
    public void TheFirstOpinion_Wins_AndTheOnesBehindItAreNeverAsked()
    {
        var asked = new List<string>();

        var opinion = ContributionRead.FirstOpinion(
            ["first", "second"],
            name =>
            {
                asked.Add(name);
                return name;
            });

        Assert.Equal("first", opinion);
        Assert.Equal(["first"], asked);
    }

    [Fact]
    public void ADecliningContribution_DefersToTheNextOne()
    {
        var asked = new List<string>();

        var opinion = ContributionRead.FirstOpinion(
            ["declines", "answers", "unreached"],
            name =>
            {
                asked.Add(name);
                return name == "answers" ? name : null;
            });

        Assert.Equal("answers", opinion);
        Assert.Equal(["declines", "answers"], asked);
    }

    [Fact]
    public void EveryContributionDeclining_OrAnEmptyList_YieldsNoOpinion()
    {
        Assert.Null(ContributionRead.FirstOpinion<string, string>(["a", "b"], static _ => null));
        Assert.Null(ContributionRead.FirstOpinion<string, string>([], static _ => "never"));
        Assert.Null(ContributionRead.FirstOpinion<string, string>(null, static _ => "never"));
    }

    [Fact]
    public void AThrowingContribution_IsReportedAndSkipped_SoTheOneBehindItAnswers()
    {
        var failures = new List<(string Contribution, string Message)>();

        var opinion = ContributionRead.FirstOpinion(
            ["faulty", "behind"],
            static name => name == "faulty" ? throw new InvalidOperationException("boom") : name,
            (name, exception) => failures.Add((name, exception.Message)));

        Assert.Equal("behind", opinion);
        Assert.Equal([("faulty", "boom")], failures);
    }

    [Fact]
    public void WithoutAFailureReport_AThrowingContributionPropagates()
    {
        Assert.Throws<InvalidOperationException>(() => ContributionRead.FirstOpinion<string, string>(
            ["faulty"],
            static _ => throw new InvalidOperationException("boom")));
    }

    [Fact]
    public void AValueTypedOpinion_SeparatesANegativeAnswerFromADecline()
    {
        var asked = new List<string>();
        // A policy answering false decides; only a null answer defers to the next one.
        var decided = ContributionRead.FirstOpinion(
            ["declines", "denies", "allows"],
            name =>
            {
                asked.Add(name);
                return name switch
                {
                    "denies" => false,
                    "allows" => true,
                    _ => (bool?)null
                };
            });

        Assert.False(decided);
        Assert.Equal(["declines", "denies"], asked);
        Assert.Null(ContributionRead.FirstOpinion(["a"], static (string _) => (bool?)null));
    }

    [Fact]
    public void AValueTypedOpinion_ThatThrows_IsReportedAndSkipped()
    {
        var failures = new List<string>();

        var decided = ContributionRead.FirstOpinion(
            ["faulty", "behind"],
            static (string name) => name == "faulty" ? throw new InvalidOperationException("boom") : (bool?)true,
            (name, _) => failures.Add(name));

        Assert.True(decided);
        Assert.Equal(["faulty"], failures);
    }

    [Fact]
    public async Task Async_TheFirstOpinionWins_AndTheOnesBehindItAreNeverAsked()
    {
        var asked = new List<string>();

        var opinion = await ContributionRead.FirstOpinionAsync(
            ["declines", "answers", "unreached"],
            async (name, _) =>
            {
                await Task.Yield();
                asked.Add(name);
                return name == "answers" ? name : null;
            });

        Assert.Equal("answers", opinion);
        Assert.Equal(["declines", "answers"], asked);
    }

    [Fact]
    public async Task Async_WithoutAFailureReport_AThrowingContributionPropagates()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ContributionRead.FirstOpinionAsync<string, string>(
                ["faulty"],
                static (_, _) => throw new InvalidOperationException("boom")));
    }

    [Fact]
    public async Task Async_AThrowingContribution_IsReportedAndSkipped_AndAnEmptyListYieldsNoOpinion()
    {
        var failures = new List<string>();

        var opinion = await ContributionRead.FirstOpinionAsync(
            ["faulty", "behind"],
            static (name, _) => name == "faulty"
                ? throw new InvalidOperationException("boom")
                : ValueTask.FromResult<string?>(name),
            (name, _) => failures.Add(name));

        Assert.Equal("behind", opinion);
        Assert.Equal(["faulty"], failures);
        Assert.Null(await ContributionRead.FirstOpinionAsync<string, string>(
            null,
            static (_, _) => ValueTask.FromResult<string?>("never")));
    }
}
