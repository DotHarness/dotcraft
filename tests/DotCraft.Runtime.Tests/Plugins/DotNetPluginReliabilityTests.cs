using System.Text.Json.Nodes;
using DotCraft.Plugins;
using DotCraft.Sessions;
using DotCraft.Tools;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.DotNetPluginTestBundle;
using static DotCraft.Tests.Runtime.Plugins.PluginRuntimeHarness;

namespace DotCraft.Tests.Runtime.Plugins;

/// <summary>The merge gate for the leak-tolerant lifecycle: a hundred generations of one plugin, once
/// with a plugin that honours the lifecycle contract and once with a plugin that breaks it.</summary>
/// <remarks>Both runs cycle without waiting for reclaim: a generation still <c>Reclaiming</c> when the
/// next cycle starts is the normal case under load.</remarks>
public sealed class DotNetPluginReliabilityTests : IDisposable
{
    private const int Cycles = 100;

    private readonly PluginRuntimeHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task OneHundredConformingCyclesMintNewGenerationsAndLeaveNothingBehind()
    {
        WriteSignalObserver("reliability.conforming");
        await using var manager = _harness.CreateManager(
            collectionPollInterval: TimeSpan.FromMilliseconds(20));
        await manager.StartAsync(CancellationToken.None);

        var generations = new HashSet<string>(StringComparer.Ordinal);
        for (var cycle = 0; cycle < Cycles; cycle++)
        {
            var active = Plugin(manager, "reliability.conforming");
            AssertState(active, PluginDotnetRuntimeState.Active);
            Assert.True(
                generations.Add(active.GenerationId!),
                $"Cycle {cycle} reused generation '{active.GenerationId}'.");
            Assert.Single(_harness.Registry.Resolve<IThreadRuntimeSignalContributor>());

            await manager.SetEnabledAsync("reliability.conforming", enabled: false);

            // Revocation must unsubscribe: a signal observer surviving its generation would be
            // dispatched into an unloaded load context.
            Assert.Empty(_harness.Registry.Resolve<IThreadRuntimeSignalContributor>());

            // Disabling promises functional deactivation by the time the call returns, not that the
            // memory has come back yet.
            var disabled = Plugin(manager, "reliability.conforming");
            Assert.True(
                disabled.State is PluginDotnetRuntimeState.Stopped
                    or PluginDotnetRuntimeState.Reclaiming,
                $"Cycle {cycle} left the plugin in {disabled.State}.");
            Assert.Null(disabled.GenerationId);

            await manager.SetEnabledAsync("reliability.conforming", enabled: true);
        }

        Assert.Equal(Cycles, generations.Count);
        await manager.SetEnabledAsync("reliability.conforming", enabled: false);
        Assert.Empty(_harness.Registry.Resolve<IThreadRuntimeSignalContributor>());

        var settled = await WaitForReclaimedAsync(
            manager,
            "reliability.conforming",
            _harness.GenerationsRoot);
        Assert.False(settled.RestartRecommended);
        Assert.Null(settled.GenerationId);
        Assert.Empty(settled.Blockers);
    }

    [Fact]
    public async Task OneHundredLeakingCyclesKeepRoutingCorrectAndOnlyGrowTheLeakCounter()
    {
        WriteLeakingTool("reliability.leaking");
        await using var manager = _harness.CreateManager(
            collectionTimeout: TimeSpan.FromMilliseconds(200),
            collectionPollInterval: TimeSpan.FromMilliseconds(20),
            leakedGenerationRestartThreshold: 3);
        await manager.StartAsync(CancellationToken.None);

        var generations = new HashSet<string>(StringComparer.Ordinal);
        for (var cycle = 0; cycle < Cycles; cycle++)
        {
            var active = Plugin(manager, "reliability.leaking");
            AssertState(active, PluginDotnetRuntimeState.Active);
            var generationId = active.GenerationId!;
            Assert.True(generations.Add(generationId), $"Cycle {cycle} reused generation '{generationId}'.");

            // The Tool answers with its own generation id, so a stale proxy answering is visible.
            var snapshot = await BuildSnapshotAsync(manager.ToolSource, cycle + 1);
            var registration = Assert.Single(snapshot.Registrations).Value;
            var live = await new ToolDispatcher().DispatchAsync(
                snapshot,
                registration.Definition.Name,
                [],
                Request($"live-{cycle}"));
            Assert.True(live.Success, $"Cycle {cycle} could not reach its live generation.");
            Assert.Equal(generationId, live.Content);

            await manager.SetEnabledAsync("reliability.leaking", enabled: false);

            var stale = await new ToolDispatcher().DispatchAsync(
                snapshot,
                registration.Definition.Name,
                [],
                Request($"stale-{cycle}"));
            Assert.False(stale.Success, $"Cycle {cycle} still routed into a revoked generation.");
            Assert.Equal(ToolErrorCodes.Unavailable, stale.Error?.Code);

            var reclaiming = Plugin(manager, "reliability.leaking");
            AssertState(reclaiming, PluginDotnetRuntimeState.Reclaiming);
            Assert.Equal(cycle + 1, reclaiming.LeakedGenerations);
            Assert.Equal(cycle + 1 >= 3, reclaiming.RestartRecommended);
            Assert.Empty(await manager.ToolSource.GetRegistrationsAsync(PlanningContext(cycle + 1)));

            await manager.SetEnabledAsync("reliability.leaking", enabled: true);
        }

        Assert.Equal(Cycles, generations.Count);
        var final = Plugin(manager, "reliability.leaking");
        AssertState(final, PluginDotnetRuntimeState.Active);
        Assert.Equal(Cycles, final.LeakedGenerations);
        Assert.True(final.RestartRecommended);
    }

    /// <summary>Writes a conforming bundle that contributes one thread runtime signal observer, so every
    /// cycle proves the contribution point is emptied when the generation is revoked.</summary>
    private void WriteSignalObserver(string pluginId) =>
        WritePluginBundle(
            _harness.PluginRoot(pluginId),
            pluginId,
            "SignalObserver.Plugin",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            using DotCraft.Sessions;
            namespace SignalObserver;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    context.Contributions.Add<IThreadRuntimeSignalContributor>(new Observer());
                    return ValueTask.CompletedTask;
                }
                private sealed class Observer : IThreadRuntimeSignalContributor
                {
                    public Task OnThreadRuntimeSignalAsync(
                        ThreadRuntimeSignalContext context,
                        CancellationToken cancellationToken = default) => Task.CompletedTask;
                }
            }
            """);

    /// <summary>Writes a bundle that contributes one Tool answering with its generation id, and pins
    /// its own load context through a Host-owned static event.</summary>
    private void WriteLeakingTool(string pluginId) =>
        WritePluginBundle(
            _harness.PluginRoot(pluginId),
            pluginId,
            "LeakingTool.Plugin",
            """
            using System;
            using System.Text.Json.Nodes;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            using DotCraft.Tests.Bundle;
            using DotCraft.Tools;
            namespace LeakingTool;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
                    context.Contributions.Add<IToolSource>(new Identify(context.Plugin.GenerationId));
                    return ValueTask.CompletedTask;
                }
                private static void OnProcessExit(object? sender, EventArgs args) { }
                private sealed class Identify(string generationId) : TestTool(
                    "identify",
                    null,
                    "identify_generation",
                    "Answers with the generation that served the call.",
                    policyHints: new ToolPolicyHints(false, true, false, false))
                {
                    public override ValueTask<ToolExecutionResult> InvokeAsync(
                        ToolInvocationContext context,
                        JsonObject arguments,
                        CancellationToken cancellationToken = default)
                        => ValueTask.FromResult(ToolExecutionResult.Succeeded(generationId));
                }
            }
            """);
}
