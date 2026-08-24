using DotCraft.Contributions;
using Xunit;

namespace DotCraft.Tests.Contributions;

public sealed class ContributionRegistryOrderingTests
{
    [Fact]
    public void Resolve_UnknownContract_ReturnsEmpty()
    {
        var registry = new ContributionRegistry();

        Assert.Empty(registry.Resolve<ILabelContract>());
        Assert.Equal(0, registry.GetRevision<ILabelContract>());
    }

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
    public void Resolve_ContributionPointsAreIsolatedFromEachOther()
    {
        var registry = new ContributionRegistry();
        registry.Add<ILabelContract>(new LabelContribution("label"));
        registry.Add<INoteContract>(new NoteContribution("note"));

        Assert.Equal(["label"], registry.Resolve<ILabelContract>().Select(contribution => contribution.Label));
        Assert.Equal(["note"], registry.Resolve<INoteContract>().Select(contribution => contribution.Note));
    }

    [Fact]
    public void Resolve_ReusesTheMaterializedViewUntilTheContributionPointChanges()
    {
        var registry = new ContributionRegistry();
        registry.Add<ILabelContract>(new LabelContribution("a"));

        var first = registry.Resolve<ILabelContract>();
        var second = registry.Resolve<ILabelContract>();
        Assert.Same(first, second);

        registry.Add<ILabelContract>(new LabelContribution("b"));
        var third = registry.Resolve<ILabelContract>();

        Assert.NotSame(first, third);
        // An in-flight evaluation keeps the set it started with.
        Assert.Single(first);
        Assert.Equal(2, third.Count);
    }

    [Fact]
    public void Resolve_IsUnaffectedByMutationsOnAnotherContributionPoint()
    {
        var registry = new ContributionRegistry();
        registry.Add<ILabelContract>(new LabelContribution("a"));
        var view = registry.Resolve<ILabelContract>();

        registry.Add<INoteContract>(new NoteContribution("note"));

        Assert.Same(view, registry.Resolve<ILabelContract>());
    }

    [Fact]
    public void GetRevision_IsMonotonicPerContributionPoint()
    {
        var registry = new ContributionRegistry();
        Assert.Equal(0, registry.GetRevision<ILabelContract>());

        var handle = registry.Add<ILabelContract>(new LabelContribution("a"));
        var afterAdd = registry.GetRevision<ILabelContract>();
        Assert.True(afterAdd > 0);

        registry.Add<INoteContract>(new NoteContribution("note"));
        Assert.Equal(afterAdd, registry.GetRevision<ILabelContract>());

        handle.Dispose();
        var afterDispose = registry.GetRevision<ILabelContract>();
        Assert.True(afterDispose > afterAdd);

        // Disposal is idempotent and does not bump the revision again.
        handle.Dispose();
        Assert.Equal(afterDispose, registry.GetRevision<ILabelContract>());
    }

    [Fact]
    public void Add_NullContribution_Throws()
    {
        var registry = new ContributionRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Add<ILabelContract>(null!));
    }
}
