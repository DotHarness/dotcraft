using DotCraft.Contributions;
using Xunit;

namespace DotCraft.Tests.Contributions;

public sealed class ContributionRegistryEntryTests
{
    private static ContributionOptions Named(string targetName, int order = 0) =>
        new(Order: order) { TargetName = targetName };

    [Fact]
    public void ResolveEntries_UnknownContract_ReturnsEmpty()
    {
        var registry = new ContributionRegistry();

        Assert.Empty(registry.ResolveEntries<ILabelContract>());
    }

    [Fact]
    public void ResolveEntries_MatchesResolveElementForElement()
    {
        var registry = new ContributionRegistry();
        registry.Add<ILabelContract>(new LabelContribution("a"));
        registry.Add<ILabelContract>(new LabelContribution("late-high"), new ContributionOptions(Order: 10));
        registry.Add<ILabelContract>(new LabelContribution("b"));
        registry.Add<ILabelContract>(new LabelContribution("early-low"), new ContributionOptions(Order: -10));

        var contributions = registry.Resolve<ILabelContract>();
        var entries = registry.ResolveEntries<ILabelContract>();

        Assert.Equal(["early-low", "a", "b", "late-high"], entries.Select(entry => entry.Contribution.Label));
        Assert.Equal(contributions.Count, entries.Count);
        for (var index = 0; index < entries.Count; index++)
            Assert.Same(contributions[index], entries[index].Contribution);
    }

    [Fact]
    public void ResolveEntries_CarriesIdentityOriginAndOrder()
    {
        var registry = new ContributionRegistry();
        var builtin = registry.Add<ILabelContract>(new LabelContribution("builtin"), new ContributionOptions(Order: 5));
        var registrar = registry.CreateRegistrar(ContributionOrigin.Plugin("acme.review", "gen-2"));
        var plugin = registrar.Add<ILabelContract>(new LabelContribution("plugin"), new ContributionOptions(Order: 20));

        var entries = registry.ResolveEntries<ILabelContract>();

        Assert.Equal(2, entries.Count);
        Assert.Equal(builtin.Id, entries[0].Id);
        Assert.Equal(5, entries[0].Order);
        Assert.Equal(ContributionOriginKind.Builtin, entries[0].Origin.Kind);

        Assert.Equal(plugin.Id, entries[1].Id);
        Assert.Equal(20, entries[1].Order);
        Assert.Equal(ContributionOriginKind.Plugin, entries[1].Origin.Kind);
        Assert.Equal("acme.review", entries[1].Origin.Name);
        Assert.Equal("gen-2", entries[1].Origin.Generation);
    }

    [Fact]
    public void ResolveEntries_LayersThreadScopeLikeResolve()
    {
        var registry = new ContributionRegistry();
        registry.Add<ILabelContract>(new LabelContribution("workspace"));
        registry.Add<ILabelContract>(
            new LabelContribution("thread"),
            ContributionOptions.ForThread("thread-1", order: 10));

        var entries = registry.ResolveEntries<ILabelContract>("thread-1");

        Assert.Equal(
            registry.Resolve<ILabelContract>("thread-1").Select(contribution => contribution.Label),
            entries.Select(entry => entry.Contribution.Label));
        Assert.Equal(["workspace", "thread"], entries.Select(entry => entry.Contribution.Label));
        Assert.Equal(["workspace"], registry.ResolveEntries<ILabelContract>().Select(entry => entry.Contribution.Label));
    }

    [Fact]
    public void ResolveEntries_ExcludesTheLoserOfAReplacementConflict()
    {
        var registry = new ContributionRegistry();
        registry.Add<ILabelContract>(new LabelContribution("default"), Named("identity"));
        var loser = registry.Add<ILabelContract>(
            new LabelContribution("first"),
            new ContributionOptions(ReplaceTarget: "identity"));
        var winner = registry.Add<ILabelContract>(
            new LabelContribution("second"),
            new ContributionOptions(ReplaceTarget: "identity"));

        var entry = Assert.Single(registry.ResolveEntries<ILabelContract>());

        Assert.Equal(winner.Id, entry.Id);
        Assert.NotEqual(loser.Id, entry.Id);
        AssertViewsAgree(registry);
    }

    [Fact]
    public void ResolveEntries_SharesOneMaterializationWithResolve()
    {
        var registry = new ContributionRegistry();
        registry.Add<ILabelContract>(new LabelContribution("a"));

        var entries = registry.ResolveEntries<ILabelContract>();
        var contributions = registry.Resolve<ILabelContract>();
        Assert.Same(contributions[0], entries[0].Contribution);
        Assert.Same(contributions, registry.Resolve<ILabelContract>());
        Assert.Same(entries, registry.ResolveEntries<ILabelContract>());

        var handle = registry.Add<ILabelContract>(new LabelContribution("b"));
        Assert.NotSame(entries, registry.ResolveEntries<ILabelContract>());
        Assert.Single(entries);
        AssertViewsAgree(registry);

        handle.Dispose();
        AssertViewsAgree(registry);
        Assert.Equal(["a"], registry.ResolveEntries<ILabelContract>().Select(entry => entry.Contribution.Label));
    }

    private static void AssertViewsAgree(ContributionRegistry registry)
    {
        var contributions = registry.Resolve<ILabelContract>();
        var entries = registry.ResolveEntries<ILabelContract>();

        Assert.Equal(contributions.Count, entries.Count);
        for (var index = 0; index < entries.Count; index++)
            Assert.Same(contributions[index], entries[index].Contribution);
    }
}
