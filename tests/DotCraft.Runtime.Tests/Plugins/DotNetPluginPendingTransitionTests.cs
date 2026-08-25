using DotCraft.Contributions;
using DotCraft.Plugins;
using DotCraft.Runtime;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.DotNetPluginTestBundle;
using static DotCraft.Tests.Runtime.Plugins.PluginRuntimeHarness;

namespace DotCraft.Tests.Runtime.Plugins;

/// <summary>Covers deadlines whose plugin callback keeps running after the caller stops waiting.</summary>
public sealed class DotNetPluginPendingTransitionTests : IDisposable
{
    private readonly PluginRuntimeHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ActivationTimeout_DoesNotCommitOrOverlapWhenTheCallbackIgnoresCancellation()
    {
        const string pluginId = "pending-activation";
        WritePlugin(
            _harness.PluginRoot(pluginId),
            pluginId,
            "PendingActivation.Plugin",
            """
            using System;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Contributions;
            using DotCraft.Plugins;
            namespace PendingActivation;
            public sealed class Plugin : IDotCraftPlugin, IDisposable
            {
                private string _log = "";
                public async ValueTask ActivateAsync(
                    IPluginActivationContext context,
                    CancellationToken cancellationToken)
                {
                    Directory.CreateDirectory(context.DataRoot);
                    _log = Path.Combine(context.DataRoot, "lifecycle.log");
                    File.AppendAllText(_log, "activation-start\n");
                    context.Contributions.Add<ISystemPromptSection>(new Section());
                    var release = Path.Combine(context.DataRoot, "activation-release");
                    while (!File.Exists(release))
                        await Task.Delay(10);
                    File.AppendAllText(_log, "activation-return\n");
                }
                public void Dispose() => File.AppendAllText(_log, "dispose\n");
                private sealed class Section : ISystemPromptSection
                {
                    public string Name => "pending-activation";
                    public string? GetContent(SystemPromptSectionContext context) => "active";
                }
            }
            """);
        await using var manager = _harness.CreateManager(activationTimeout: TimeSpan.FromMilliseconds(100));

        await manager.StartAsync(CancellationToken.None);

        var timedOut = Plugin(manager, pluginId);
        AssertState(timedOut, PluginDotnetRuntimeState.Faulted);
        var firstGeneration = Assert.IsType<string>(timedOut.GenerationId);
        Assert.Empty(_harness.Registry.Resolve<ISystemPromptSection>());

        await manager.SetEnabledAsync(pluginId, enabled: true);
        Assert.Equal(firstGeneration, Plugin(manager, pluginId).GenerationId);
        Assert.Equal(
            PluginRuntimeMutationOutcome.NotApplied,
            (await manager.ReconcileAfterMutationAsync(pluginId)).Outcome);
        Assert.Equal(
            ["activation-start"],
            PluginLogFile.ReadLines(_harness.DataPath(pluginId, "lifecycle.log")));

        File.WriteAllText(_harness.DataPath(pluginId, "activation-release"), "release");
        var active = await WaitForDifferentGenerationAsync(manager, pluginId, firstGeneration);

        AssertState(active, PluginDotnetRuntimeState.Active);
        var section = Assert.Single(_harness.Registry.ResolveEntries<ISystemPromptSection>());
        Assert.Equal(active.GenerationId, section.Origin.Generation);
        Assert.Equal(
            ["activation-start", "activation-return", "dispose", "activation-start", "activation-return"],
            PluginLogFile.ReadLines(_harness.DataPath(pluginId, "lifecycle.log")));
    }

    [Fact]
    public async Task CleanupTimeout_BlocksProviderStopAndFreshGenerationsUntilTheConsumerFinishes()
    {
        const string providerId = "pending-provider";
        const string consumerId = "pending-consumer";
        WritePlugin(
            _harness.PluginRoot(providerId),
            providerId,
            "PendingProvider.Plugin",
            """
            using System;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            namespace PendingProvider;
            public sealed class Plugin : IDotCraftPlugin, IDisposable
            {
                private string _log = "";
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    _log = Path.Combine(context.WorkspaceRoot, "pending-order.log");
                    File.AppendAllText(_log, "provider-activate\n");
                    return ValueTask.CompletedTask;
                }
                public void Dispose() => File.AppendAllText(_log, "provider-dispose\n");
            }
            """);
        WritePluginBundle(
            _harness.PluginRoot(consumerId),
            consumerId,
            "PendingConsumer.Plugin",
            """
            using System;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            namespace PendingConsumer;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    var log = Path.Combine(context.WorkspaceRoot, "pending-order.log");
                    var release = Path.Combine(context.DataRoot, "cleanup-release");
                    context.Lifetime.Run(async stopping =>
                    {
                        File.AppendAllText(log, "consumer-work-start\n");
                        try { await Task.Delay(Timeout.InfiniteTimeSpan, stopping); }
                        catch (OperationCanceledException)
                        {
                            File.AppendAllText(log, "consumer-cleanup-start\n");
                            while (!File.Exists(release))
                                await Task.Delay(10);
                            File.AppendAllText(log, "consumer-cleanup-finish\n");
                        }
                    });
                    return ValueTask.CompletedTask;
                }
            }
            """,
            dependencies: new Dictionary<string, string> { [providerId] = "1.0.0" });
        await using var manager = _harness.CreateManager(cleanupTimeout: TimeSpan.FromMilliseconds(100));
        await manager.StartAsync(CancellationToken.None);
        await WaitForLineAsync(Path.Combine(_harness.Workspace, "pending-order.log"), "consumer-work-start");
        var providerGeneration = Plugin(manager, providerId).GenerationId;
        var consumerGeneration = Plugin(manager, consumerId).GenerationId;

        await manager.SetEnabledAsync(providerId, enabled: false);

        var consumer = Plugin(manager, consumerId);
        AssertState(consumer, PluginDotnetRuntimeState.Deactivating);
        Assert.Equal(consumerGeneration, consumer.GenerationId);
        AssertState(Plugin(manager, providerId), PluginDotnetRuntimeState.Active);
        Assert.Equal(providerGeneration, Plugin(manager, providerId).GenerationId);
        Assert.DoesNotContain(
            "provider-dispose",
            PluginLogFile.ReadLines(Path.Combine(_harness.Workspace, "pending-order.log")));

        await manager.SetEnabledAsync(providerId, enabled: true);
        Assert.Equal(providerGeneration, Plugin(manager, providerId).GenerationId);
        Assert.Equal(consumerGeneration, Plugin(manager, consumerId).GenerationId);

        File.WriteAllText(_harness.DataPath(consumerId, "cleanup-release"), "release");
        var newProvider = await WaitForDifferentGenerationAsync(manager, providerId, providerGeneration!);
        var newConsumer = await WaitForDifferentGenerationAsync(manager, consumerId, consumerGeneration!);

        AssertState(newProvider, PluginDotnetRuntimeState.Active);
        AssertState(newConsumer, PluginDotnetRuntimeState.Active);
        var order = PluginLogFile.ReadLines(Path.Combine(_harness.Workspace, "pending-order.log"));
        Assert.True(order.IndexOf("consumer-cleanup-finish") < order.IndexOf("provider-dispose"));
    }

    [Fact]
    public async Task TimedOutQuiesce_IsNotAppliedAndRestoresTheEnabledPluginAfterCleanup()
    {
        const string pluginId = "pending-quiesce";
        WritePlugin(
            _harness.PluginRoot(pluginId),
            pluginId,
            "PendingQuiesce.Plugin",
            """
            using System;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            namespace PendingQuiesce;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    var release = Path.Combine(context.DataRoot, "cleanup-release");
                    context.Lifetime.Run(async stopping =>
                    {
                        try { await Task.Delay(Timeout.InfiniteTimeSpan, stopping); }
                        catch (OperationCanceledException)
                        {
                            while (!File.Exists(release))
                                await Task.Delay(10);
                        }
                    });
                    return ValueTask.CompletedTask;
                }
            }
            """);
        await using var manager = _harness.CreateManager(cleanupTimeout: TimeSpan.FromMilliseconds(100));
        await manager.StartAsync(CancellationToken.None);
        var firstGeneration = Plugin(manager, pluginId).GenerationId!;

        var quiesce = await manager.QuiesceForMutationAsync(pluginId);

        Assert.Equal(PluginRuntimeMutationOutcome.NotApplied, quiesce.Outcome);
        Assert.Contains(quiesce.Diagnostics, diagnostic => diagnostic.Code == "PluginOperationIncomplete");
        AssertState(Plugin(manager, pluginId), PluginDotnetRuntimeState.Deactivating);
        Assert.Equal(firstGeneration, Plugin(manager, pluginId).GenerationId);

        File.WriteAllText(_harness.DataPath(pluginId, "cleanup-release"), "release");
        var restored = await WaitForDifferentGenerationAsync(manager, pluginId, firstGeneration);
        AssertState(restored, PluginDotnetRuntimeState.Active);
    }

    private static async Task<PluginDotnetRuntimeInfo> WaitForDifferentGenerationAsync(
        DotNetPluginRuntimeManager manager,
        string pluginId,
        string previousGeneration)
    {
        for (var attempt = 0; attempt < 1500; attempt++)
        {
            var plugin = Plugin(manager, pluginId);
            if (plugin.State == PluginDotnetRuntimeState.Active
                && plugin.GenerationId != null
                && !string.Equals(plugin.GenerationId, previousGeneration, StringComparison.Ordinal))
            {
                return plugin;
            }

            await Task.Delay(20);
        }

        var observed = Plugin(manager, pluginId);
        Assert.Fail(
            $"Plugin '{pluginId}' did not advance from generation '{previousGeneration}'; "
            + $"observed {observed.State} '{observed.GenerationId}'.");
        return observed;
    }
}
