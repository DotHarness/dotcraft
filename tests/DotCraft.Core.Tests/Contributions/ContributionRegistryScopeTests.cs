using DotCraft.Contributions;
using Xunit;

namespace DotCraft.Tests.Contributions;

public sealed class ContributionRegistryScopeTests
{
    [Fact]
    public void Resolve_LayersThreadScopedContributionsOverWorkspaceOnes()
    {
        var registry = new ContributionRegistry();
        registry.Add<ILabelContract>(new LabelContribution("workspace"));
        registry.Add<ILabelContract>(
            new LabelContribution("thread-early"),
            ContributionOptions.ForThread("thread-1", order: -5));
        registry.Add<ILabelContract>(
            new LabelContribution("thread-late"),
            ContributionOptions.ForThread("thread-1", order: 5));
        registry.Add<ILabelContract>(
            new LabelContribution("other-thread"),
            ContributionOptions.ForThread("thread-2"));

        Assert.Equal(
            ["thread-early", "workspace", "thread-late"],
            registry.Resolve<ILabelContract>("thread-1").Select(contribution => contribution.Label));
        Assert.Equal(
            ["workspace", "other-thread"],
            registry.Resolve<ILabelContract>("thread-2").Select(contribution => contribution.Label));
        Assert.Equal(
            ["workspace"],
            registry.Resolve<ILabelContract>().Select(contribution => contribution.Label));
    }

    [Fact]
    public void MismatchedScopeAndThreadId_Throw()
    {
        var registry = new ContributionRegistry();

        Assert.Throws<ArgumentException>(() => registry.Add<ILabelContract>(
            new LabelContribution("a"),
            new ContributionOptions(ContributionScope.Thread)));
        Assert.Throws<ArgumentException>(() => registry.Add<ILabelContract>(
            new LabelContribution("a"),
            new ContributionOptions(ContributionScope.Thread, "  ")));
        Assert.Throws<ArgumentException>(() => registry.Add<ILabelContract>(
            new LabelContribution("a"),
            new ContributionOptions(ContributionScope.Workspace, "thread-1")));
        Assert.Throws<ArgumentException>(() => registry.ReleaseThread(" "));
    }

    [Fact]
    public void ReleaseThread_DisposesOnlyThatThreadNewestFirstWithOneCoalescedEvent()
    {
        var registry = new ContributionRegistry();
        var disposals = new List<string>();
        registry.Add<ILabelContract>(new DisposableLabelContribution("workspace", disposals));
        registry.Add<ILabelContract>(
            new DisposableLabelContribution("t1-first", disposals),
            ContributionOptions.ForThread("thread-1"));
        registry.Add<ILabelContract>(
            new DisposableLabelContribution("t1-second", disposals),
            ContributionOptions.ForThread("thread-1"));
        registry.Add<INoteContract>(
            new NoteContribution("t1-note"),
            ContributionOptions.ForThread("thread-1"));
        registry.Add<ILabelContract>(
            new DisposableLabelContribution("t2", disposals),
            ContributionOptions.ForThread("thread-2"));
        var events = new List<ContributionsChangedEventArgs>();
        registry.Changed += (_, args) => events.Add(args);

        var released = registry.ReleaseThread("thread-1");

        Assert.Equal(3, released);
        Assert.Equal(["t1-second", "t1-first"], disposals);
        var raised = Assert.Single(events);
        Assert.True(raised.Includes<ILabelContract>());
        Assert.True(raised.Includes<INoteContract>());
        Assert.DoesNotContain(
            registry.Resolve<ILabelContract>("thread-1"),
            contribution => contribution.Label.StartsWith("t1", StringComparison.Ordinal));
        Assert.Empty(registry.Resolve<INoteContract>("thread-1"));
        Assert.Equal(
            ["workspace", "t2"],
            registry.Resolve<ILabelContract>("thread-2").Select(contribution => contribution.Label));
        Assert.Equal(0, registry.ReleaseThread("thread-1"));
        Assert.Equal(0, registry.ReleaseThread("never-used"));
    }
}
