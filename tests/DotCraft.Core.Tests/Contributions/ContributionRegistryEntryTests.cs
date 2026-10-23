using DotCraft.Contributions;
using Xunit;

namespace DotCraft.Tests.Contributions;

public sealed class ContributionRegistryEntryTests
{
    [Fact]
    public void ResolveEntries_SharesOneOrderedMaterializationWithResolve()
    {
        var registry = new ContributionRegistry();
        registry.Add<ILabelContract>(new LabelContribution("a"));
        registry.Add<ILabelContract>(new LabelContribution("late-high"), new ContributionOptions(Order: 10));
        registry.Add<ILabelContract>(new LabelContribution("early-low"), new ContributionOptions(Order: -10));
        registry.Add<ILabelContract>(
            new LabelContribution("thread"),
            ContributionOptions.ForThread("thread-1", order: 5));

        var entries = registry.ResolveEntries<ILabelContract>();
        Assert.Equal(["early-low", "a", "late-high"], entries.Select(entry => entry.Contribution.Label));
        Assert.Same(entries, registry.ResolveEntries<ILabelContract>());
        AssertViewsAgree(registry);
        Assert.Equal(
            ["early-low", "a", "thread", "late-high"],
            registry.ResolveEntries<ILabelContract>("thread-1").Select(entry => entry.Contribution.Label));

        var handle = registry.Add<ILabelContract>(new LabelContribution("b"));
        Assert.NotSame(entries, registry.ResolveEntries<ILabelContract>());
        Assert.Equal(3, entries.Count);
        AssertViewsAgree(registry);

        handle.Dispose();
        AssertViewsAgree(registry);
        Assert.Equal(["early-low", "a", "late-high"], registry.ResolveEntries<ILabelContract>().Select(entry => entry.Contribution.Label));
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

    private static void AssertViewsAgree(ContributionRegistry registry)
    {
        var contributions = registry.Resolve<ILabelContract>();
        var entries = registry.ResolveEntries<ILabelContract>();

        Assert.Equal(contributions.Count, entries.Count);
        for (var index = 0; index < entries.Count; index++)
            Assert.Same(contributions[index], entries[index].Contribution);
    }
}
