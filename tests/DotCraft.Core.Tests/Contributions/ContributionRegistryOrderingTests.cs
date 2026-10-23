using DotCraft.Contributions;
using Xunit;

namespace DotCraft.Tests.Contributions;

public sealed class ContributionRegistryOrderingTests
{
    [Fact]
    public void Resolve_OrdersByAscendingOrderThenRegistration()
    {
        var registry = new ContributionRegistry();
        registry.Add<ILabelContract>(new LabelContribution("a"));
        registry.Add<ILabelContract>(new LabelContribution("late-high"), new ContributionOptions(Order: 10));
        registry.Add<ILabelContract>(new LabelContribution("b"));
        registry.Add<ILabelContract>(new LabelContribution("early-low"), new ContributionOptions(Order: -10));

        Assert.Equal(
            ["early-low", "a", "b", "late-high"],
            registry.Resolve<ILabelContract>().Select(contribution => contribution.Label));
    }

    [Fact]
    public void Resolve_ReusesTheMaterializedViewUntilTheContributionPointChanges()
    {
        var registry = new ContributionRegistry();
        registry.Add<ILabelContract>(new LabelContribution("a"));

        var first = registry.Resolve<ILabelContract>();
        Assert.Same(first, registry.Resolve<ILabelContract>());
        registry.Add<INoteContract>(new NoteContribution("note"));
        Assert.Same(first, registry.Resolve<ILabelContract>());
        Assert.Equal(["note"], registry.Resolve<INoteContract>().Select(contribution => contribution.Note));

        registry.Add<ILabelContract>(new LabelContribution("b"));
        var third = registry.Resolve<ILabelContract>();

        Assert.NotSame(first, third);
        Assert.Single(first);
        Assert.Equal(2, third.Count);
    }

    [Fact]
    public void GetRevision_IsMonotonicPerContributionPoint()
    {
        var registry = new ContributionRegistry();
        Assert.Empty(registry.Resolve<ILabelContract>());
        Assert.Equal(0, registry.GetRevision<ILabelContract>());

        var handle = registry.Add<ILabelContract>(new LabelContribution("a"));
        var afterAdd = registry.GetRevision<ILabelContract>();
        Assert.True(afterAdd > 0);

        registry.Add<INoteContract>(new NoteContribution("note"));
        Assert.Equal(afterAdd, registry.GetRevision<ILabelContract>());

        handle.Dispose();
        var afterDispose = registry.GetRevision<ILabelContract>();
        Assert.True(afterDispose > afterAdd);

        handle.Dispose();
        Assert.Equal(afterDispose, registry.GetRevision<ILabelContract>());
    }
}
