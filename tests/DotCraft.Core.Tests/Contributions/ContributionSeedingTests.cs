using DotCraft.Contributions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DotCraft.Tests.Contributions;

public sealed class ContributionSeedingTests
{
    [Fact]
    public void SeedContributions_AddsTheContributorSetInContainerOrder()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILabelContract>(new LabelContribution("first"));
        services.AddSingleton<ILabelContract>(new LabelContribution("second"));
        using var provider = services.BuildServiceProvider();
        var registry = new ContributionRegistry();

        var handles = registry.SeedContributions<ILabelContract>(provider);

        Assert.Equal(2, handles.Count);
        Assert.Equal(
            ["first", "second"],
            registry.Resolve<ILabelContract>().Select(contribution => contribution.Label));
    }

    [Fact]
    public void SeedContributions_RaisesOneCoalescedEvent()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILabelContract>(new LabelContribution("a"));
        services.AddSingleton<ILabelContract>(new LabelContribution("b"));
        using var provider = services.BuildServiceProvider();
        var registry = new ContributionRegistry();
        var events = new List<ContributionsChangedEventArgs>();
        registry.Changed += (_, args) => events.Add(args);

        registry.SeedContributions<ILabelContract>(provider);

        var raised = Assert.Single(events);
        Assert.True(raised.Includes<ILabelContract>());
    }

    [Fact]
    public void SeedContributions_LeavesContainerOwnedInstancesUndisposed()
    {
        var disposals = new List<string>();
        var contribution = new DisposableLabelContribution("container-owned", disposals);
        var services = new ServiceCollection();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILabelContract>(contribution));
        using var provider = services.BuildServiceProvider();
        var registry = new ContributionRegistry();

        var handles = registry.SeedContributions<ILabelContract>(provider);
        handles[0].Dispose();

        Assert.Empty(registry.Resolve<ILabelContract>());
        Assert.Equal(0, contribution.DisposeCount);
    }

    [Fact]
    public void SeedContributions_WithARegistrar_JoinsThatDisposalGroup()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILabelContract>(new LabelContribution("a"));
        services.AddSingleton<ILabelContract>(new LabelContribution("b"));
        using var provider = services.BuildServiceProvider();
        var registry = new ContributionRegistry();
        var registrar = registry.CreateRegistrar(ContributionOrigin.Module("automations"));

        registry.SeedContributions<ILabelContract>(provider, registrar);
        Assert.Equal(2, registry.Resolve<ILabelContract>().Count);

        registrar.Dispose();

        Assert.Empty(registry.Resolve<ILabelContract>());
    }

    [Fact]
    public void SeedContributions_WithNoRegisteredContributors_IsANoOp()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var registry = new ContributionRegistry();
        var events = new List<ContributionsChangedEventArgs>();
        registry.Changed += (_, args) => events.Add(args);

        Assert.Empty(registry.SeedContributions<ILabelContract>(provider));
        Assert.Empty(events);
        Assert.Equal(0, registry.GetRevision<ILabelContract>());
    }
}
