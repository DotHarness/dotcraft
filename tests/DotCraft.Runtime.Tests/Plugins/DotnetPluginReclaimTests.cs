using DotCraft.Plugins;
using DotCraft.Runtime;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.DotnetPluginTestBundle;
using static DotCraft.Tests.Runtime.Plugins.PluginRuntimeHarness;

namespace DotCraft.Tests.Runtime.Plugins;

/// <summary>Covers the leak-tolerant half of deactivation: reclaim is best-effort and blocks nothing.
/// Each test leaks on purpose by letting the plugin hand a delegate over a static Host event.</summary>
public sealed class DotnetPluginReclaimTests : IDisposable
{
    private readonly PluginRuntimeHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ReclaimCallbackFailureIsRetriedWithoutNewTrackedWork()
    {
        var attempts = 0;
        var settled = new TaskCompletionSource<PluginGenerationRemnant>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var poller = new PluginReclaimPoller(
            new DotnetPluginRuntimeOptions
            {
                CollectionPollInterval = TimeSpan.FromMilliseconds(10),
                CollectionTimeout = TimeSpan.FromMilliseconds(100)
            },
            (remnant, _) => Interlocked.Increment(ref attempts) == 1
                ? Task.FromException(new InvalidOperationException("transient settlement failure"))
                : Complete(remnant),
            _ => true);
        var tracked = new PluginGenerationRemnant(
            null,
            "callback-retry",
            "generation-one",
            "shadow-copy",
            []);

        try
        {
            poller.Track(tracked);

            var observed = await settled.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Same(tracked, observed);
            Assert.Equal(2, Volatile.Read(ref attempts));
            Assert.Equal(0, poller.OutstandingCount(tracked.PluginId));
        }
        finally
        {
            await poller.StopAsync(CancellationToken.None);
        }

        Task Complete(PluginGenerationRemnant remnant)
        {
            settled.TrySetResult(remnant);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task LeakedGenerationEntersReclaimingAndDoesNotBlockReactivation()
    {
        _harness.WriteLeaking("leaking");
        await using var manager = _harness.CreateManager(collectionTimeout: TimeSpan.FromMilliseconds(200));
        await manager.StartAsync(CancellationToken.None);
        var firstGeneration = Plugin(manager, "leaking").GenerationId;

        await manager.SetEnabledAsync("leaking", enabled: false);

        var reclaiming = Plugin(manager, "leaking");
        AssertState(reclaiming, PluginDotnetRuntimeState.Reclaiming);
        Assert.Equal(1, reclaiming.LeakedGenerations);
        Assert.False(reclaiming.RestartRecommended);

        await manager.SetEnabledAsync("leaking", enabled: true);

        var active = Plugin(manager, "leaking");
        AssertState(active, PluginDotnetRuntimeState.Active);
        Assert.NotEqual(firstGeneration, active.GenerationId);
        Assert.Equal(1, active.LeakedGenerations);
    }

    [Fact]
    public async Task ReclaimPollerCollectsAConformingGenerationDeletesItsCopyAndStopsIt()
    {
        _harness.WriteNoop("conforming");
        await using var manager = _harness.CreateManager();
        await manager.StartAsync(CancellationToken.None);
        var generationId = Plugin(manager, "conforming").GenerationId!;

        await manager.SetEnabledAsync("conforming", enabled: false);

        var stopped = await WaitForReclaimedAsync(manager, "conforming", _harness.GenerationsRoot);
        Assert.Null(stopped.GenerationId);
        Assert.False(stopped.RestartRecommended);
        Assert.DoesNotContain(generationId, GenerationCopies(_harness.GenerationsRoot), StringComparer.Ordinal);
    }

    [Fact]
    public async Task LeakCounterReachingTheThresholdRecommendsARestart()
    {
        _harness.WriteLeaking("repeat-offender");
        await using var manager = _harness.CreateManager(
            collectionTimeout: TimeSpan.FromMilliseconds(200),
            leakedGenerationRestartThreshold: 2);
        await manager.StartAsync(CancellationToken.None);

        await manager.SetEnabledAsync("repeat-offender", enabled: false);
        Assert.False(Plugin(manager, "repeat-offender").RestartRecommended);
        await manager.SetEnabledAsync("repeat-offender", enabled: true);
        await manager.SetEnabledAsync("repeat-offender", enabled: false);

        // Crossing the threshold changes what the client is told, never what the runtime allows.
        var reclaiming = Plugin(manager, "repeat-offender");
        Assert.Equal(2, reclaiming.LeakedGenerations);
        Assert.True(reclaiming.RestartRecommended);
        await manager.SetEnabledAsync("repeat-offender", enabled: true);
        AssertState(Plugin(manager, "repeat-offender"), PluginDotnetRuntimeState.Active);
    }

    [Fact]
    public async Task LeakedGenerationDoesNotBlockWorkspaceShutdown()
    {
        _harness.WriteLeaking("shutdown.leaking");
        WritePluginBundle(
            _harness.PluginRoot("shutdown.conforming"),
            "shutdown.conforming",
            "Conforming.Plugin",
            """
            using System;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            namespace Conforming;
            public sealed class Plugin : IDotCraftPlugin, IDisposable
            {
                private string _workspace = "";
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    _workspace = context.WorkspaceRoot;
                    return ValueTask.CompletedTask;
                }
                public void Dispose() => File.WriteAllText(Path.Combine(_workspace, "conforming-disposed"), "yes");
            }
            """);
        await using var manager = _harness.CreateManager();
        await manager.StartAsync(CancellationToken.None);
        AssertState(Plugin(manager, "shutdown.leaking"), PluginDotnetRuntimeState.Active);
        AssertState(Plugin(manager, "shutdown.conforming"), PluginDotnetRuntimeState.Active);

        await manager.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(File.Exists(Path.Combine(_harness.Workspace, "conforming-disposed")));
        AssertState(Plugin(manager, "shutdown.conforming"), PluginDotnetRuntimeState.Stopped);
        AssertState(Plugin(manager, "shutdown.leaking"), PluginDotnetRuntimeState.Reclaiming);
        Assert.Equal(1, Plugin(manager, "shutdown.leaking").LeakedGenerations);
    }
}
