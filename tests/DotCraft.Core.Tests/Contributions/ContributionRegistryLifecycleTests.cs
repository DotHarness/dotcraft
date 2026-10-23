using DotCraft.Contributions;
using Xunit;

namespace DotCraft.Tests.Contributions;

public sealed class ContributionRegistryLifecycleTests
{
    [Fact]
    public void Registrar_DisposesItsGroupOnceInReverseRegistrationOrderWithOneEvent()
    {
        var registry = new ContributionRegistry();
        var disposals = new List<string>();
        var registrar = registry.CreateRegistrar(ContributionOrigin.Plugin("acme.review", "3"));
        registrar.Add<ILabelContract>(new DisposableLabelContribution("first", disposals));
        registrar.Add<ILabelContract>(new DisposableLabelContribution("second", disposals));
        registrar.Add<INoteContract>(new DisposableNoteContribution("note", disposals));
        registry.Add<ILabelContract>(new LabelContribution("builtin"));
        var events = new List<ContributionsChangedEventArgs>();
        registry.Changed += (_, args) => events.Add(args);

        registrar.Dispose();
        registrar.Dispose();

        Assert.Single(events);
        Assert.Throws<ObjectDisposedException>(() => registrar.Add<ILabelContract>(new LabelContribution("late")));
        Assert.Equal(["note", "second", "first"], disposals);
        Assert.Equal(["builtin"], registry.Resolve<ILabelContract>().Select(contribution => contribution.Label));
        Assert.Empty(registry.Resolve<INoteContract>());
    }

    [Fact]
    public void Registrar_LogsTeardownFailuresAndKeepsDisposing()
    {
        var logLines = new List<string>();
        var registry = new ContributionRegistry(new CollectingLogger<ContributionRegistry>(logLines));
        var disposals = new List<string>();
        var registrar = registry.CreateRegistrar(ContributionOrigin.Module("automations"));
        registrar.Add<ILabelContract>(new DisposableLabelContribution("first", disposals));
        registrar.Add<ILabelContract>(new DisposableLabelContribution("faulty", disposals, throwOnDispose: true));
        registrar.Add<ILabelContract>(new DisposableLabelContribution("last", disposals));

        registrar.Dispose();

        Assert.Equal(["last", "faulty", "first"], disposals);
        Assert.Empty(registry.Resolve<ILabelContract>());
        var logged = Assert.Single(logLines);
        Assert.Contains(ContributionDiagnosticCodes.TeardownFailed, logged, StringComparison.Ordinal);
        Assert.Contains("module:automations", logged, StringComparison.Ordinal);
    }

    [Fact]
    public void HandleDisposal_IsIdempotentAndLeavesUnownedContributionsAlone()
    {
        var registry = new ContributionRegistry();
        var disposals = new List<string>();
        var owned = new DisposableLabelContribution("owned", disposals);
        var borrowed = new DisposableLabelContribution("borrowed", disposals);
        var ownedHandle = registry.Add<ILabelContract>(owned);
        var borrowedHandle = registry.Add<ILabelContract>(
            borrowed,
            ContributionOptions.Default with { OwnsContribution = false });

        ownedHandle.Dispose();
        ownedHandle.Dispose();
        borrowedHandle.Dispose();

        Assert.Equal(1, owned.DisposeCount);
        Assert.Equal(0, borrowed.DisposeCount);
        Assert.Empty(registry.Resolve<ILabelContract>());
    }

    [Fact]
    public void Changed_RaisesOncePerMutationOutsideABatch()
    {
        var registry = new ContributionRegistry();
        var events = new List<ContributionsChangedEventArgs>();
        registry.Changed += (_, args) => events.Add(args);

        var handle = registry.Add<ILabelContract>(new LabelContribution("a"));
        registry.Add<INoteContract>(new NoteContribution("note"));
        handle.Dispose();

        Assert.Equal(3, events.Count);
        Assert.Equal([1L, 2L, 3L], events.Select(args => args.Version));
        Assert.True(events[0].Includes<ILabelContract>());
        Assert.False(events[0].Includes<INoteContract>());
        Assert.True(events[1].Includes<INoteContract>());
    }

    [Fact]
    public void BeginBatch_CoalescesNestedMutationsIntoOneEventOnTheOutermostScope()
    {
        var registry = new ContributionRegistry();
        var events = new List<ContributionsChangedEventArgs>();
        registry.Changed += (_, args) => events.Add(args);

        registry.BeginBatch().Dispose();
        Assert.Empty(events);

        using (registry.BeginBatch())
        {
            using (registry.BeginBatch())
            {
                registry.Add<ILabelContract>(new LabelContribution("a"));
            }

            registry.Add<ILabelContract>(new LabelContribution("b"));
            registry.Add<INoteContract>(new NoteContribution("note"));
            Assert.Empty(events);
        }

        var raised = Assert.Single(events);
        Assert.Equal(2, raised.ChangedContracts.Count);
        Assert.True(raised.Includes<ILabelContract>());
        Assert.True(raised.Includes<INoteContract>());
        Assert.Equal(1, raised.Version);
    }

    [Fact]
    public void Changed_AFailingSubscriber_DoesNotStarveTheOnesRegisteredAfterIt()
    {
        var logLines = new List<string>();
        var registry = new ContributionRegistry(new CollectingLogger<ContributionRegistry>(logLines));
        var observed = new List<ContributionsChangedEventArgs>();
        registry.Changed += (_, _) => throw new InvalidOperationException("subscriber failed");
        registry.Changed += (_, args) => observed.Add(args);

        registry.Add<ILabelContract>(new LabelContribution("a"));

        var raised = Assert.Single(observed);
        Assert.True(raised.Includes<ILabelContract>());
        var logged = Assert.Single(logLines);
        Assert.Contains(ContributionDiagnosticCodes.ObserverFailed, logged, StringComparison.Ordinal);
        Assert.Contains(GetType().FullName!, logged, StringComparison.Ordinal);
    }
}
