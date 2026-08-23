using DotCraft.Agents;
using DotCraft.Contributions;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Contributions;

/// <summary>Composition of the <see cref="IToolSource"/> contribution point, asserted through the factory that plans with it.</summary>
public sealed class ToolSourceContributionTests : IDisposable
{
    private readonly ContributionAgentHost _host = new("ToolSourceContribution");

    [Fact]
    public void EffectiveSources_ComeFromTheRegistryWhenOneIsWired()
    {
        var registry = new ContributionRegistry();
        var first = new StubToolSource("first");
        var second = new StubToolSource("second");
        registry.Add<IToolSource>(first, new ContributionOptions(Order: 10));
        registry.Add<IToolSource>(second, new ContributionOptions(Order: 20));
        var factory = CreateFactory(registry, constructorSources: []);

        Assert.Equal(
            new[] { "first", "second" },
            factory.ToolSources.OfType<StubToolSource>().Select(source => source.SourceId));
    }

    [Fact]
    public void ContributedSource_AppearsWithoutRebuildingTheFactory()
    {
        var registry = new ContributionRegistry();
        var baseline = new StubToolSource("baseline");
        registry.Add<IToolSource>(baseline, new ContributionOptions(Order: 10));
        var factory = CreateFactory(registry, constructorSources: [baseline]);

        var handle = registry.Add<IToolSource>(
            new StubToolSource("late"),
            new ContributionOptions(Order: 20) { OwnsContribution = false });

        Assert.Contains(factory.ToolSources, source => source.SourceId == "late");

        handle.Dispose();

        Assert.DoesNotContain(factory.ToolSources, source => source.SourceId == "late");
        Assert.Contains(factory.ToolSources, source => source.SourceId == "baseline");
    }

    [Fact]
    public void ThreadScopedSource_AppliesToThatThreadOnly()
    {
        var registry = new ContributionRegistry();
        registry.Add<IToolSource>(new StubToolSource("workspace"), new ContributionOptions(Order: 10));
        registry.Add<IToolSource>(
            new StubToolSource("thread-only"),
            ContributionOptions.ForThread("thread-a", order: 20));
        var factory = CreateFactory(registry, constructorSources: []);

        Assert.Contains(factory.GetToolSources("thread-a"), source => source.SourceId == "thread-only");
        Assert.DoesNotContain(factory.GetToolSources("thread-b"), source => source.SourceId == "thread-only");
    }

    [Fact]
    public void PluginOriginSource_IsWithheldFromTheGeneralResolution()
    {
        var registry = new ContributionRegistry();
        registry.Add<IToolSource>(new StubToolSource("workspace"), new ContributionOptions(Order: 10));
        var registrar = registry.CreateRegistrar(ContributionOrigin.Plugin("acme.tools", "gen-1"));
        registrar.Add<IToolSource>(new StubToolSource("plugin-raw"), new ContributionOptions(Order: 20));
        var factory = CreateFactory(registry, constructorSources: []);

        // A plugin source reaches a thread only through the aggregate that wraps it.
        Assert.Contains(factory.ToolSources, source => source.SourceId == "workspace");
        Assert.DoesNotContain(factory.ToolSources, source => source.SourceId == "plugin-raw");
        Assert.DoesNotContain(
            factory.GetToolSources("thread-a"),
            source => source.SourceId == "plugin-raw");
    }

    [Fact]
    public void WithoutARegistry_TheConstructorInjectedListStillGoverns()
    {
        var only = new StubToolSource("constructor-only");

        var factory = CreateFactory(contributions: null, constructorSources: [only]);

        Assert.Contains(factory.ToolSources, source => source.SourceId == "constructor-only");
    }

    [Fact]
    public void EmptyContributionPoint_FallsBackToTheConstructorInjectedList()
    {
        var registry = new ContributionRegistry();
        var only = new StubToolSource("constructor-only");

        var factory = CreateFactory(registry, constructorSources: [only]);

        Assert.Contains(factory.ToolSources, source => source.SourceId == "constructor-only");
    }

    private AgentFactory CreateFactory(
        IContributionView? contributions,
        IReadOnlyList<IToolSource> constructorSources) =>
        _host.CreateFactory(contributions, toolSources: constructorSources);

    public void Dispose() => _host.Dispose();

    private sealed class StubToolSource(string sourceId) : IToolSource
    {
        public string SourceId { get; } = sourceId;

        public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
            ToolPlanningContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ToolRegistration>>([]);
    }
}
