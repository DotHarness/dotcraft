using DotCraft.Contributions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DotCraft.Tests.Contributions;

public sealed class ContributionSeedingTests
{
    [Fact]
    public void SeedContributions_AddsTheContributorSetInContainerOrderWithOneCoalescedEvent()
    {
        var registry = new ContributionRegistry();
        var events = new List<ContributionsChangedEventArgs>();
        registry.Changed += (_, args) => events.Add(args);
        using (var empty = new ServiceCollection().BuildServiceProvider())
            Assert.Empty(registry.SeedContributions<ILabelContract>(empty));
        Assert.Empty(events);
        Assert.Equal(0, registry.GetRevision<ILabelContract>());
        var services = new ServiceCollection();
        services.AddSingleton<ILabelContract>(new LabelContribution("first"));
        services.AddSingleton<ILabelContract>(new LabelContribution("second"));
        using var provider = services.BuildServiceProvider();

        var handles = registry.SeedContributions<ILabelContract>(provider);

        Assert.Equal(2, handles.Count);
        Assert.True(Assert.Single(events).Includes<ILabelContract>());
        Assert.Equal(
            ["first", "second"],
            registry.Resolve<ILabelContract>().Select(contribution => contribution.Label));
    }

    [Fact]
    public void SeedContributions_WithARegistrar_JoinsThatDisposalGroupWithoutDisposingContainerOwnedInstances()
    {
        var disposals = new List<string>();
        var containerOwned = new DisposableLabelContribution("container-owned", disposals);
        var services = new ServiceCollection();
        services.AddSingleton<ILabelContract>(new LabelContribution("a"));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILabelContract>(containerOwned));
        using var provider = services.BuildServiceProvider();
        var registry = new ContributionRegistry();
        var registrar = registry.CreateRegistrar(ContributionOrigin.Module("automations"));

        registry.SeedContributions<ILabelContract>(provider, registrar);
        Assert.Equal(2, registry.Resolve<ILabelContract>().Count);

        registrar.Dispose();

        Assert.Empty(registry.Resolve<ILabelContract>());
        Assert.Equal(0, containerOwned.DisposeCount);
    }

}
