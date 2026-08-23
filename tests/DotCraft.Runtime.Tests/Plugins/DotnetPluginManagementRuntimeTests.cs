using DotCraft.Plugins;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.PluginRuntimeHarness;

namespace DotCraft.Tests.Runtime.Plugins;

/// <summary>Covers the coordinated quiesce, mutate, reconcile protocol.</summary>
public sealed class DotnetPluginManagementRuntimeTests : IDisposable
{
    private readonly PluginRuntimeHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task RemoveAndRecovery_PreserveConsumerIntent()
    {
        _harness.WriteNoop("remove.provider");
        _harness.WriteNoop(
            "remove.consumer",
            dependencies: new Dictionary<string, string> { ["remove.provider"] = "1.0.0" });
        await using var manager = _harness.CreateManager();
        await manager.StartAsync(CancellationToken.None);

        var quiesce = await manager.QuiesceForMutationAsync("remove.provider");
        Assert.Equal(PluginRuntimeMutationOutcome.Applied, quiesce.Outcome);
        Directory.Delete(_harness.PluginRoot("remove.provider"), recursive: true);
        var removed = await manager.ReconcileAfterMutationAsync("remove.provider");

        Assert.Equal(PluginRuntimeMutationOutcome.Applied, removed.Outcome);
        Assert.DoesNotContain(manager.Snapshot.Plugins, plugin => plugin.PluginId == "remove.provider");
        AssertState(Plugin(manager, "remove.consumer"), PluginDotnetRuntimeState.Blocked);
        Assert.Contains(
            Plugin(manager, "remove.consumer").Blockers,
            blocker => blocker.Code == "PluginDependencyUnsatisfied");

        _harness.WriteNoop("remove.provider");
        var recovered = await manager.ReconcileAfterMutationAsync("remove.provider");
        Assert.Equal(PluginRuntimeMutationOutcome.Applied, recovered.Outcome);
        await manager.TrustAsync("remove.provider");
        AssertState(Plugin(manager, "remove.provider"), PluginDotnetRuntimeState.Active);
        AssertState(Plugin(manager, "remove.consumer"), PluginDotnetRuntimeState.Active);
    }

    [Fact]
    public async Task Quiesce_AppliesEvenWhenTheOldGenerationLeaked()
    {
        _harness.WriteNoop("quiesce.provider");
        _harness.WriteLeaking(
            "quiesce.consumer",
            dependencies: new Dictionary<string, string> { ["quiesce.provider"] = "1.0.0" });
        await using var manager = _harness.CreateManager(collectionTimeout: TimeSpan.FromMilliseconds(200));
        await manager.StartAsync(CancellationToken.None);
        var oldGeneration = Plugin(manager, "quiesce.provider").GenerationId;

        var quiesce = await manager.QuiesceForMutationAsync("quiesce.provider");
        _harness.WriteNoop("quiesce.provider", version: "2.0.0");
        var result = await manager.ReconcileAfterMutationAsync("quiesce.provider");

        // Functional deactivation is unconditional, so a leaking consumer cannot block the mutation.
        Assert.Equal(PluginRuntimeMutationOutcome.Applied, quiesce.Outcome);
        Assert.Equal(PluginRuntimeMutationOutcome.Applied, result.Outcome);
        Assert.Equal("2.0.0", Plugin(manager, "quiesce.provider").Version);
        Assert.NotEqual(oldGeneration, Plugin(manager, "quiesce.provider").GenerationId);
        await manager.TrustAsync("quiesce.provider");
        AssertState(Plugin(manager, "quiesce.provider"), PluginDotnetRuntimeState.Active);
        Assert.Equal(1, Plugin(manager, "quiesce.consumer").LeakedGenerations);
    }
}
