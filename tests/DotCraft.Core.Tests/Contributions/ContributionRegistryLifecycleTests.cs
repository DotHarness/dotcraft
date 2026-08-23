using DotCraft.Contributions;
using Xunit;

namespace DotCraft.Tests.Contributions;

public sealed class ContributionRegistryLifecycleTests
{
    [Fact]
    public void Registrar_DisposesItsGroupInReverseRegistrationOrder()
    {
        var registry = new ContributionRegistry();
        var disposals = new List<string>();
        var registrar = registry.CreateRegistrar(ContributionOrigin.Plugin("acme.review", "3"));
        registrar.Add<ILabelContract>(new DisposableLabelContribution("first", disposals));
        registrar.Add<ILabelContract>(new DisposableLabelContribution("second", disposals));
        registrar.Add<INoteContract>(new DisposableNoteContribution("note", disposals));
        registry.Add<ILabelContract>(new LabelContribution("builtin"));

        registrar.Dispose();

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
    public void Registrar_DisposalIsIdempotentAndRaisesOneEvent()
    {
        var registry = new ContributionRegistry();
        var disposals = new List<string>();
        var registrar = registry.CreateRegistrar(ContributionOrigin.Plugin("acme.review", "1"));
        var contribution = new DisposableLabelContribution("only", disposals);
        registrar.Add<ILabelContract>(contribution);

        var events = new List<ContributionsChangedEventArgs>();
        registry.Changed += (_, args) => events.Add(args);

        registrar.Dispose();
        registrar.Dispose();

        Assert.Single(events);
        Assert.Equal(1, contribution.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => registrar.Add<ILabelContract>(new LabelContribution("late")));
    }

    [Fact]
    public void HandleDisposal_IsIdempotent()
    {
        var registry = new ContributionRegistry();
        var disposals = new List<string>();
        var contribution = new DisposableLabelContribution("only", disposals);
        var handle = registry.Add<ILabelContract>(contribution);

        handle.Dispose();
        handle.Dispose();

        Assert.Equal(1, contribution.DisposeCount);
        Assert.Empty(registry.Resolve<ILabelContract>());
    }

    [Fact]
    public void HandleDisposal_LeavesUnownedContributionsAlone()
    {
        var registry = new ContributionRegistry();
        var disposals = new List<string>();
        var contribution = new DisposableLabelContribution("borrowed", disposals);
        var handle = registry.Add<ILabelContract>(
            contribution,
            ContributionOptions.Default with { OwnsContribution = false });

        handle.Dispose();

        Assert.Equal(0, contribution.DisposeCount);
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
    public void BeginBatch_CoalescesMutationsIntoOneEvent()
    {
        var registry = new ContributionRegistry();
        var events = new List<ContributionsChangedEventArgs>();
        registry.Changed += (_, args) => events.Add(args);

        using (registry.BeginBatch())
        {
            registry.Add<ILabelContract>(new LabelContribution("a"));
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
    public void BeginBatch_Nests_AndRaisesOnTheOutermostScope()
    {
        var registry = new ContributionRegistry();
        var events = new List<ContributionsChangedEventArgs>();
        registry.Changed += (_, args) => events.Add(args);

        using (registry.BeginBatch())
        {
            using (registry.BeginBatch())
            {
                registry.Add<ILabelContract>(new LabelContribution("inner"));
            }

            Assert.Empty(events);
            registry.Add<ILabelContract>(new LabelContribution("outer"));
        }

        Assert.Single(events);
        Assert.Equal(2, registry.Resolve<ILabelContract>().Count);
    }

    [Fact]
    public void BeginBatch_WithNoMutations_RaisesNothing()
    {
        var registry = new ContributionRegistry();
        var events = new List<ContributionsChangedEventArgs>();
        registry.Changed += (_, args) => events.Add(args);

        registry.BeginBatch().Dispose();

        Assert.Empty(events);
    }

    [Fact]
    public void Changed_SubscriberFailure_IsLoggedAndDoesNotBreakRegistration()
    {
        var logLines = new List<string>();
        var registry = new ContributionRegistry(new CollectingLogger<ContributionRegistry>(logLines));
        registry.Changed += (_, _) => throw new InvalidOperationException("subscriber failed");

        registry.Add<ILabelContract>(new LabelContribution("a"));

        Assert.Single(registry.Resolve<ILabelContract>());
        Assert.Contains(ContributionDiagnosticCodes.ObserverFailed, Assert.Single(logLines), StringComparison.Ordinal);
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

    [Fact]
    public void Origin_RendersTheCanonicalForm()
    {
        Assert.Equal("builtin", ContributionOrigin.Builtin.ToString());
        Assert.Equal("module:automations", ContributionOrigin.Module("automations").ToString());
        Assert.Equal("plugin:acme.review@7", ContributionOrigin.Plugin("acme.review", "7").ToString());
        Assert.Equal(
            "plugin:acme.review@2026-08-22T10-15-00.3",
            ContributionOrigin.Plugin("acme.review", "2026-08-22T10-15-00.3").ToString());
        Assert.Throws<ArgumentException>(() => ContributionOrigin.Module(" "));
        Assert.Throws<ArgumentException>(() => ContributionOrigin.Plugin(string.Empty, "1"));
        Assert.Throws<ArgumentException>(() => ContributionOrigin.Plugin("acme.review", " "));
    }
}
