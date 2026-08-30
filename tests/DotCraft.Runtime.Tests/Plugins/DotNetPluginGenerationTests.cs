using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DotCraft.Contributions;
using DotCraft.Plugins;
using DotCraft.Runtime;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.DotNetPluginTestBundle;

namespace DotCraft.Tests.Runtime.Plugins;

/// <summary>Covers the activation transaction and the teardown ordering of one generation.</summary>
public sealed class DotNetPluginGenerationTests : IDisposable
{
    private readonly PluginGenerationHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Generation_ClosesCallAdmissionBeforeSignalingStopping()
    {
        WritePlugin(
            _harness.PluginRoot("stop-order"),
            "stop-order",
            "StopOrder.Plugin",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            namespace StopOrder;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(
                    IPluginActivationContext context,
                    CancellationToken cancellationToken) => ValueTask.CompletedTask;
            }
            """);
        var attempt = await _harness.ActivateAsync("stop-order");
        var generation = Assert.IsType<PluginGeneration>(attempt.Generation);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var calls = Assert.IsType<PluginCallGate>(
            typeof(PluginGeneration).GetField("_calls", flags)!.GetValue(generation));
        var lifetime = Assert.IsType<PluginLifetime>(
            typeof(PluginGeneration).GetField("_lifetime", flags)!.GetValue(generation));
        bool? openWhenStoppingWasSignaled = null;
        using var registration = lifetime.Stopping.Register(
            () => openWhenStoppingWasSignaled = calls.IsOpen);

        await generation.BeginCleanup();

        Assert.False(openWhenStoppingWasSignaled);
    }

    [Fact]
    public async Task Generation_ContributesToSeveralContributionPointsAndRevokesThemOnTeardown()
    {
        WritePlugin(
            _harness.PluginRoot("contribution-points"),
            "contribution-points",
            "ContributionPoints.Plugin",
            """
            using System.Text.Json.Nodes;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Contributions;
            using DotCraft.Plugins;
            using DotCraft.Tests.Bundle;
            using DotCraft.Tools;
            namespace ContributionPoints;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    context.Contributions.Add<ISystemPromptSection>(new Section());
                    context.Contributions.Add<IToolSource>(new Tool());
                    return ValueTask.CompletedTask;
                }
                private sealed class Section : ISystemPromptSection
                {
                    public string Name => "contribution-points-section";
                    public string? GetContent(SystemPromptSectionContext context) => "from-plugin";
                }
                private sealed class Tool() : TestTool("echo", null, "echo", "Echoes its input.")
                {
                    public override ValueTask<ToolExecutionResult> InvokeAsync(
                        ToolInvocationContext context,
                        JsonObject arguments,
                        CancellationToken cancellationToken = default) =>
                        ValueTask.FromResult(ToolExecutionResult.Succeeded("ok"));
                }
            }
            """);

        var attempt = await _harness.ActivateAsync("contribution-points");

        var generation = Assert.IsType<PluginGeneration>(attempt.Generation);
        Assert.Null(attempt.Error);
        var section = Assert.Single(_harness.Registry.ResolveEntries<ISystemPromptSection>());
        Assert.Equal(ContributionOriginKind.Plugin, section.Origin.Kind);
        Assert.Equal("contribution-points", section.Origin.Name);
        Assert.Equal(generation.GenerationId, section.Origin.Generation);
        Assert.Single(_harness.Registry.Resolve<IToolSource>());
        Assert.True(_harness.CallGates.IsCallable("contribution-points", generation.GenerationId));

        var remnant = await generation.BeginCleanup();

        Assert.Empty(_harness.Registry.Resolve<ISystemPromptSection>());
        Assert.Empty(_harness.Registry.Resolve<IToolSource>());
        Assert.False(_harness.CallGates.IsCallable("contribution-points", generation.GenerationId));
        Assert.Equal("contribution-points", remnant.PluginId);
        Assert.Empty(remnant.CleanupErrors);
    }

    [Fact]
    public async Task Generation_CapturesOnlyItsOwnSettingsBagAtActivation()
    {
        WritePluginBundle(
            _harness.PluginRoot("settings-reader"),
            "settings-reader",
            "SettingsReader.Plugin",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Contributions;
            using DotCraft.Plugins;
            namespace SettingsReader;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    context.Contributions.Add<ISystemPromptSection>(new Section(context));
                    return ValueTask.CompletedTask;
                }
                private sealed class Section(IPluginActivationContext plugin) : ISystemPromptSection
                {
                    public string Name => "settings-reader";
                    public string? GetContent(SystemPromptSectionContext context) =>
                        plugin.Settings.TryGetProperty("label", out var label) ? label.GetString() : "<none>";
                }
            }
            """);
        var first = Bag("first");
        var attempt = await _harness.ActivateAsync("settings-reader", first);

        var generation = Assert.IsType<PluginGeneration>(attempt.Generation);
        Assert.Equal("first", ReadSection());

        first = Bag("second");
        Assert.Equal("first", ReadSection());

        await generation.BeginCleanup();
    }

    private string? ReadSection() =>
        Assert.Single(_harness.Registry.Resolve<ISystemPromptSection>())
            .GetContent(new SystemPromptSectionContext(null, _harness.WorkspaceRoot, _harness.Root));

    private static JsonElement Bag(string label) =>
        JsonSerializer.Deserialize<JsonElement>($$"""{"label":"{{label}}"}""");

    [Fact]
    public async Task Generation_TearsDownWorkEntryAndResourcesInReverseOrder()
    {
        WritePlugin(
            _harness.PluginRoot("lifecycle"),
            "lifecycle",
            "Lifecycle.Plugin",
            LifecyclePluginSource);

        var attempt = await _harness.ActivateAsync("lifecycle");
        var generation = Assert.IsType<PluginGeneration>(attempt.Generation);
        generation.StartWork(static _ => { });
        var log = _harness.DataFile("lifecycle", "lifecycle.log");
        await PluginGenerationHarness.WaitForLineAsync(log, "work-start");

        var remnant = await generation.BeginCleanup();

        Assert.Equal(
            ["activate", "work-start", "work-stop", "entry", "async", "sync"],
            PluginLogFile.ReadLines(log));
        Assert.Equal("Plugin operation failed.", Assert.Single(remnant.CleanupErrors));
        Assert.NotNull(remnant.LoadContext);
    }

    [Fact]
    public async Task Generation_CleansStagedEntryAndResourcesWhenActivationFails()
    {
        WritePlugin(
            _harness.PluginRoot("activation-failure"),
            "activation-failure",
            "Failure.Plugin",
            """
            using System;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Contributions;
            using DotCraft.Plugins;
            namespace Failure;
            public sealed class Plugin : IDotCraftPlugin, IDisposable
            {
                private string _log = "";
                private CancellationToken _stopping;
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    _log = Path.Combine(context.DataRoot, "failure.log");
                    _stopping = context.Lifetime.Stopping;
                    Directory.CreateDirectory(context.DataRoot);
                    context.Contributions.Add<ISystemPromptSection>(new Section());
                    context.Lifetime.Own(new Resource(_log));
                    throw new InvalidOperationException("activation exploded");
                }
                public void Dispose() => File.AppendAllText(
                    _log,
                    _stopping.IsCancellationRequested ? "entry-stopping\n" : "entry-running\n");
                private sealed class Resource(string log) : IDisposable
                {
                    public void Dispose() => File.AppendAllText(log, "resource\n");
                }
                private sealed class Section : DotCraft.Contributions.ISystemPromptSection
                {
                    public string Name => "doomed";
                    public string? GetContent(DotCraft.Contributions.SystemPromptSectionContext context) => "never";
                }
            }
            """);

        var attempt = await _harness.ActivateAsync("activation-failure");

        Assert.Null(attempt.Generation);
        Assert.Equal("Plugin operation failed.", attempt.Error);
        Assert.False(attempt.DeterministicFailure);
        Assert.Equal("PluginActivationFailed", attempt.FailureBlocker?.Code);
        Assert.Equal(
            ["entry-stopping", "resource"],
            PluginLogFile.ReadLines(_harness.DataFile("activation-failure", "failure.log")));

        Assert.Empty(_harness.Registry.Resolve<ISystemPromptSection>());
        Assert.NotNull(attempt.Remnant);
        Assert.NotNull(attempt.Remnant!.LoadContext);
    }

    [Fact]
    public async Task Generation_ReportsBackgroundWorkFailureThroughItsFailureCallback()
    {
        WritePlugin(
            _harness.PluginRoot("work-failure"),
            "work-failure",
            "WorkFailure.Plugin",
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            namespace WorkFailure;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    context.Lifetime.Run(async stopping =>
                    {
                        await Task.Delay(20, stopping);
                        throw new InvalidOperationException("background exploded");
                    });
                    return ValueTask.CompletedTask;
                }
            }
            """);

        var attempt = await _harness.ActivateAsync("work-failure");
        var generation = Assert.IsType<PluginGeneration>(attempt.Generation);
        var observed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        generation.StartWork(message => observed.TrySetResult(message));

        Assert.Equal("Plugin operation failed.", await observed.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        await generation.BeginCleanup();
    }

    [Fact]
    public async Task Generation_GivesBundledHostAssemblyCopiesTheHostTypeIdentity()
    {
        var pluginRoot = _harness.PluginRoot("meai-identity");
        // The bundle ships and declares its own Microsoft.Extensions.AI.Abstractions.dll, so only
        // simple-name sharing can keep the plugin's ChatMessage the Host's type.
        var bundledAbstractions = CopyHostAssemblyIntoBundle(pluginRoot, typeof(ChatMessage).Assembly);
        WritePluginBundle(
            pluginRoot,
            "meai-identity",
            "MeaiIdentity.Plugin",
            """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            using Microsoft.Extensions.AI;
            namespace MeaiIdentity;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    var sink = (IList<object>?)context.Services.GetService(typeof(IList<object>))
                        ?? throw new InvalidOperationException("Host sink service is missing.");
                    sink.Add(new ChatMessage(ChatRole.Assistant, "constructed-inside-the-plugin"));
                    return ValueTask.CompletedTask;
                }
            }
            """,
            runtimeReferences: [bundledAbstractions]);
        var sink = new List<object>();
        _harness.Services = CreateServiceProvider((typeof(IList<object>), sink));

        var attempt = await _harness.ActivateAsync("meai-identity");

        Assert.NotNull(attempt.Generation);
        Assert.True(File.Exists(bundledAbstractions), "The bundle must actually carry its own copy.");
        var message = Assert.IsType<ChatMessage>(Assert.Single(sink));
        Assert.Same(typeof(ChatMessage).Assembly, message.GetType().Assembly);
        Assert.Equal("constructed-inside-the-plugin", message.Text);
        Assert.Equal(ChatRole.Assistant, message.Role);
    }

    [Fact]
    public async Task Generation_ThatLeaksIsTornDownAndDoesNotBlockTheNextActivation()
    {
        WritePlugin(
            _harness.PluginRoot("leaking"),
            "leaking",
            "Leaking.Plugin",
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            using DotCraft.Contributions;
            namespace Leaking;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    // A static event in the default context pins this collectible one for the process.
                    AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
                    context.Contributions.Add<ISystemPromptSection>(new Section());
                    return ValueTask.CompletedTask;
                }
                private static void OnProcessExit(object? sender, EventArgs args) { }
                private sealed class Section : ISystemPromptSection
                {
                    public string Name => "leaking-section";
                    public string? GetContent(SystemPromptSectionContext context) => "leaking";
                }
            }
            """);

        var first = await _harness.ActivateAsync("leaking", "generation-one", null, CancellationToken.None);
        var firstGeneration = Assert.IsType<PluginGeneration>(first.Generation);
        var remnant = await firstGeneration.BeginCleanup();

        // Functional deactivation is unconditional; memory reclaim is not.
        Assert.Empty(_harness.Registry.Resolve<ISystemPromptSection>());
        Assert.False(_harness.CallGates.IsCallable("leaking", "generation-one"));

        Collect();
        Assert.NotNull(remnant.LoadContext);
        Assert.True(
            remnant.LoadContext!.IsAlive,
            "The deliberately leaked load context must stay alive so the reclaim path is exercised.");

        var second = await _harness.ActivateAsync("leaking", "generation-two", null, CancellationToken.None);

        var secondGeneration = Assert.IsType<PluginGeneration>(second.Generation);
        Assert.Equal("generation-two", secondGeneration.GenerationId);
        var section = Assert.Single(_harness.Registry.ResolveEntries<ISystemPromptSection>());
        Assert.Equal("generation-two", section.Origin.Generation);
        Assert.True(_harness.CallGates.IsCallable("leaking", "generation-two"));
        Assert.True(remnant.LoadContext.IsAlive, "The first generation must still be leaked.");

        await secondGeneration.BeginCleanup();
    }

    [Fact]
    public async Task Generation_ThatHoldsNothingBecomesCollectibleAfterTeardown()
    {
        // The negative control for the leak test above: proves collection is observable at all.
        WritePlugin(
            _harness.PluginRoot("well-behaved"),
            "well-behaved",
            "WellBehaved.Plugin",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Contributions;
            using DotCraft.Plugins;
            namespace WellBehaved;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    context.Contributions.Add<ISystemPromptSection>(new Section());
                    return ValueTask.CompletedTask;
                }
                private sealed class Section : ISystemPromptSection
                {
                    public string Name => "well-behaved-section";
                    public string? GetContent(SystemPromptSectionContext context) => "tidy";
                }
            }
            """);

        var remnant = await TearDownAsync("well-behaved");

        Assert.NotNull(remnant.LoadContext);
        Assert.True(
            await WaitForCollectionAsync(remnant.LoadContext!),
            "A generation that retains nothing must become collectible once its load context unloads.");
    }

    /// <remarks>Its own frame, so the caller's locals cannot keep the generation reachable while
    /// collection is being observed.</remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<PluginGenerationRemnant> TearDownAsync(string pluginId)
    {
        var attempt = await _harness.ActivateAsync(pluginId);
        var generation = Assert.IsType<PluginGeneration>(attempt.Generation);
        return await generation.BeginCleanup();
    }

    private static async Task<bool> WaitForCollectionAsync(WeakReference loadContext)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            Collect();
            if (!loadContext.IsAlive)
                return true;
            await Task.Delay(25);
        }

        return false;
    }

    private static void Collect()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
