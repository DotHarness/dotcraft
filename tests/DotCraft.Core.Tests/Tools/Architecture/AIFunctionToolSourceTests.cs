using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Modules;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Core.Tests.Tools.Architecture;

public sealed class AIFunctionToolSourceTests
{
    [Fact]
    public async Task CommitSuggest_ExecutesThroughSnapshotDispatcherWithProviderCallIdentity()
    {
        var source = new CommitSuggestToolSource();
        var planning = CreatePlanningContext();
        var snapshot = await new EffectiveToolSnapshotBuilder().BuildAsync([source], planning);
        var providerName = Assert.Single(snapshot.ProviderCallNameIndex.Keys);

        var result = await new ToolDispatcher().DispatchProviderCallAsync(
            snapshot,
            providerName,
            new JsonObject { ["summary"] = "Unify tool runtime" },
            new ToolInvocationRequest(
                planning.ThreadId,
                planning.TurnId,
                "call_original",
                ToolInvocationAudience.Model));

        Assert.True(result.Success);
        Assert.Equal("Recorded.", result.Content);
        Assert.Equal(new ToolName(null, CommitSuggestMethods.ToolName), snapshot.ProviderCallNameIndex[providerName]);
    }

    [Fact]
    public void ProfileRegistry_StoresQualifiedToolSources()
    {
        var registry = new ToolProfileRegistry();
        var source = new CommitSuggestToolSource();

        registry.Register("commit", [source]);

        Assert.True(registry.TryGet("commit", out var sources));
        Assert.Same(source, Assert.Single(sources!));
    }

    [Fact]
    public void ToolSourceCollector_CombinesDiAndEnabledModulesDeterministically()
    {
        var first = new EmptySource("first", priority: 20);
        var second = new EmptySource("second", priority: 10);
        var services = new ServiceCollection()
            .AddSingleton<IToolSource>(first)
            .BuildServiceProvider();
        var modules = new ModuleRegistry();
        modules.RegisterModule(new SourceModule(second));

        var sources = new ToolSourceCollector(modules, services, new AppConfig()).Collect();

        Assert.Equal(["second", "first"], sources.Select(source => source.SourceId));
    }

    private static ToolPlanningContext CreatePlanningContext() => new(
        "thread_test",
        "turn_test",
        Path.GetTempPath(),
        "agent",
        "commit",
        providerCapabilities: [],
        revision: 1);

    private sealed class EmptySource(string sourceId, int priority) : AIFunctionToolSource
    {
        public override string SourceId => sourceId;
        public override int Priority => priority;
        protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context) => [];
    }

    private sealed class SourceModule(IToolSource source) : ModuleBase
    {
        public override string Name => "test-source";
        public override bool IsEnabled(AppConfig config) => true;
        public override IEnumerable<IToolSource> GetToolSources(IServiceProvider services) => [source];
    }
}
