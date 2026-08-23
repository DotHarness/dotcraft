using System.Reflection;
using DotCraft.Plugins;
using DotCraft.Runtime;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.DotnetPluginTestBundle;
using static DotCraft.Tests.Runtime.Plugins.PluginRuntimeHarness;

namespace DotCraft.Tests.Runtime.Plugins;

/// <summary>Covers the manager-level activation and deactivation transaction.</summary>
public sealed class DotnetPluginRuntimeTests : IDisposable
{
    private readonly PluginRuntimeHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Runtime_ExposesHostServicesWithoutTakingOwnership()
    {
        WritePlugin(
            _harness.PluginRoot("host-services"),
            "host-services",
            "HostServices.Plugin",
            """
            using System;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            namespace HostServices;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    var value = (string?)context.Services.GetService(typeof(string))
                        ?? throw new InvalidOperationException("Host string service is missing.");
                    if (context.Services.GetService(typeof(IDisposable)) is null)
                        throw new InvalidOperationException("Host disposable service is missing.");
                    Directory.CreateDirectory(context.DataRoot);
                    File.WriteAllText(Path.Combine(context.DataRoot, "service.txt"), value);
                    return ValueTask.CompletedTask;
                }
            }
            """);
        var probe = new DisposableProbe();
        _harness.Services = CreateServiceProvider((typeof(string), "from-host"), (typeof(IDisposable), probe));
        await using var manager = _harness.CreateManager();

        await manager.StartAsync(CancellationToken.None);

        Assert.Equal("from-host", PluginLogFile.ReadText(_harness.DataPath("host-services", "service.txt")));
        await manager.SetEnabledAsync("host-services", enabled: false);
        Assert.False(probe.IsDisposed);
    }

    [Fact]
    public async Task Runtime_ActivatesDrainsDisposesAndCollectsGeneration()
    {
        WritePlugin(
            _harness.PluginRoot("lifecycle"),
            "lifecycle",
            "Lifecycle.Plugin",
            LifecyclePluginSource);
        await using var manager = _harness.CreateManager();

        await manager.StartAsync(CancellationToken.None);

        var active = Plugin(manager, "lifecycle");
        AssertState(active, PluginDotnetRuntimeState.Active);
        Assert.False(string.IsNullOrWhiteSpace(active.GenerationId));
        await WaitForLineAsync(_harness.DataPath("lifecycle", "lifecycle.log"), "work-start");

        await manager.SetEnabledAsync("lifecycle", enabled: false);

        var stopped = await WaitForStateAsync(manager, "lifecycle", PluginDotnetRuntimeState.Stopped);
        Assert.Null(stopped.GenerationId);
        Assert.Equal(0, stopped.LeakedGenerations);
        Assert.False(stopped.RestartRecommended);
        Assert.Contains(manager.Snapshot.Diagnostics, diagnostic => diagnostic.Code == "PluginCleanupFailed");
        Assert.Empty(GenerationAssemblies(_harness.GenerationsRoot));
    }

    [Fact]
    public async Task Runtime_ReactivationUsesAcceptedSnapshotAndPrivateDependency()
    {
        var pluginRoot = _harness.PluginRoot("private-dependency");
        var libraryPath = Path.Combine(pluginRoot, "dotnet", "Private.Library.dll");
        Compile(libraryPath, "namespace PrivateLibrary; public static class Value { public static string Get() => \"v1\"; }");
        WritePlugin(
            pluginRoot,
            "private-dependency",
            "PrivatePlugin.Plugin",
            """
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            using PrivateLibrary;
            namespace PrivatePlugin;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    Directory.CreateDirectory(context.DataRoot);
                    File.AppendAllText(Path.Combine(context.DataRoot, "values.log"), Value.Get() + "\n");
                    return ValueTask.CompletedTask;
                }
            }
            """,
            libraryPath);
        await using var manager = _harness.CreateManager();
        await manager.StartAsync(CancellationToken.None);
        var firstGeneration = Plugin(manager, "private-dependency").GenerationId;

        await manager.SetEnabledAsync("private-dependency", enabled: false);
        Compile(libraryPath, "namespace PrivateLibrary; public static class Value { public static string Get() => \"v2\"; }");
        await manager.SetEnabledAsync("private-dependency", enabled: true);

        // The library was rewritten to v2 underneath, but the accepted snapshot is runtime identity.
        var active = Plugin(manager, "private-dependency");
        AssertState(active, PluginDotnetRuntimeState.Active);
        Assert.NotEqual(firstGeneration, active.GenerationId);
        Assert.Equal(["v1", "v1"], PluginLogFile.ReadLines(_harness.DataPath("private-dependency", "values.log")));
    }

    [Fact]
    public async Task Runtime_ActivationFailureCleansStagedEntryAndResources()
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
            using DotCraft.Plugins;
            namespace Failure;
            public sealed class Plugin : IDotCraftPlugin, IDisposable
            {
                private string _log = "";
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    _log = Path.Combine(context.DataRoot, "failure.log");
                    Directory.CreateDirectory(context.DataRoot);
                    context.Lifetime.Own(new Resource(_log));
                    throw new InvalidOperationException("activation exploded");
                }
                public void Dispose() => File.AppendAllText(_log, "entry\n");
                private sealed class Resource(string log) : IDisposable
                {
                    public void Dispose() => File.AppendAllText(log, "resource\n");
                }
            }
            """);
        await using var manager = _harness.CreateManager();

        await manager.StartAsync(CancellationToken.None);

        var faulted = Plugin(manager, "activation-failure");
        AssertState(faulted, PluginDotnetRuntimeState.Faulted);
        Assert.Contains(faulted.Blockers, blocker => blocker.Code == "PluginActivationFailed");
        Assert.Equal(["entry", "resource"], PluginLogFile.ReadLines(_harness.DataPath("activation-failure", "failure.log")));
        AssertReported(manager, "activation-failure", "PluginActivationFailed");
        Assert.DoesNotContain(
            manager.Snapshot.Diagnostics,
            diagnostic => diagnostic.Message.Contains("activation exploded", StringComparison.Ordinal));
        Assert.DoesNotContain(
            _harness.LogLines,
            line => line.Contains("activation exploded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reconcile_DoesNotExposeFilesystemExceptionPaths()
    {
        const string pluginId = "private-path-candidate";
        await using var manager = _harness.CreateManager(trustInstalled: false);
        await manager.StartAsync(CancellationToken.None);
        WritePlugin(
            _harness.PluginRoot(pluginId),
            pluginId,
            "PrivatePathCandidate.Plugin",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            namespace PrivatePathCandidate;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }
            """);
        var privateFile = Path.Combine(_harness.PluginRoot(pluginId), "private-callback-sentinel.bin");
        File.WriteAllText(privateFile, "locked");
        await using var locked = new FileStream(
            privateFile,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        var result = await manager.ReconcileAfterMutationAsync(pluginId);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("PluginCandidateInvalid", diagnostic.Code);
        Assert.Equal("The plugin bundle could not be copied and validated.", diagnostic.Message);
        Assert.DoesNotContain(_harness.Root, diagnostic.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-callback-sentinel", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Runtime_BackgroundWorkFailureFaultsAndCleansGeneration()
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
        await using var manager = _harness.CreateManager();
        var changed = new TaskCompletionSource<PluginRuntimeSnapshotChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        manager.SnapshotChanged += (_, args) =>
        {
            if (args.Snapshot.Plugins.Any(plugin =>
                    plugin.PluginId == "work-failure"
                    && plugin.State == PluginDotnetRuntimeState.Faulted))
            {
                changed.TrySetResult(args);
            }
        };
        await manager.StartAsync(CancellationToken.None);

        var faulted = await WaitForStateAsync(manager, "work-failure", PluginDotnetRuntimeState.Faulted);
        var notification = await changed.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Null(faulted.GenerationId);
        Assert.Contains(faulted.Blockers, blocker => blocker.Code == "PluginActivationFailed");
        Assert.Contains("work-failure", notification.PluginIds);
        Assert.Equal(manager.Snapshot.Revision, notification.Snapshot.Revision);
    }

    [Fact]
    public async Task Runtime_IgnoresABackgroundFailureFromAReplacedGeneration()
    {
        const string pluginId = "stale-work-failure";
        _harness.WriteNoop(pluginId);
        await using var manager = _harness.CreateManager();
        await manager.StartAsync(CancellationToken.None);
        var firstGeneration = Assert.IsType<string>(Plugin(manager, pluginId).GenerationId);

        await manager.SetEnabledAsync(pluginId, enabled: false);
        await manager.SetEnabledAsync(pluginId, enabled: true);
        var replacement = Plugin(manager, pluginId);
        AssertState(replacement, PluginDotnetRuntimeState.Active);
        Assert.NotEqual(firstGeneration, replacement.GenerationId);

        var handler = typeof(DotnetPluginRuntimeManager).GetMethod(
            "HandleWorkFailureAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var handling = Assert.IsAssignableFrom<Task>(handler?.Invoke(
            manager,
            [pluginId, firstGeneration, "stale background failure"]));
        await handling;

        var current = Plugin(manager, pluginId);
        AssertState(current, PluginDotnetRuntimeState.Active);
        Assert.Equal(replacement.GenerationId, current.GenerationId);
        Assert.DoesNotContain(current.Blockers, blocker => blocker.Message == "stale background failure");
    }

    [Fact]
    public async Task Runtime_CallerCancellationAfterActivationStartsDoesNotCancelTransition()
    {
        const string pluginId = "activation-caller-cancellation";
        WritePlugin(
            _harness.PluginRoot(pluginId),
            pluginId,
            "ActivationCallerCancellation.Plugin",
            BlockingActivationPluginSource("ActivationCallerCancellation"));
        await using var manager = _harness.CreateManager(activationTimeout: TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource();
        var startedPath = _harness.DataPath(pluginId, "activation-started");
        var releasePath = _harness.DataPath(pluginId, "activation-release");

        var start = manager.StartAsync(cancellation.Token);
        await WaitForFileAsync(startedPath);
        cancellation.Cancel();

        try
        {
            var completed = await Task.WhenAny(start, Task.Delay(200));
            Assert.NotSame(start, completed);
        }
        finally
        {
            File.WriteAllText(releasePath, "release");
        }

        await start.WaitAsync(TimeSpan.FromSeconds(30));
        AssertState(Plugin(manager, pluginId), PluginDotnetRuntimeState.Active);
    }

    [Fact]
    public async Task Runtime_CallerCancellationWhileWaitingForMutationLockCancelsTheWait()
    {
        const string pluginId = "mutation-lock-cancellation";
        WritePlugin(
            _harness.PluginRoot(pluginId),
            pluginId,
            "MutationLockCancellation.Plugin",
            BlockingActivationPluginSource("MutationLockCancellation"));
        await using var manager = _harness.CreateManager(activationTimeout: TimeSpan.FromSeconds(10));
        var startedPath = _harness.DataPath(pluginId, "activation-started");
        var releasePath = _harness.DataPath(pluginId, "activation-release");

        var start = manager.StartAsync(CancellationToken.None);
        await WaitForFileAsync(startedPath);
        using var cancellation = new CancellationTokenSource();
        var waiting = manager.SetEnabledAsync(pluginId, enabled: false, cancellation.Token);
        Assert.False(waiting.IsCompleted);

        try
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        }
        finally
        {
            File.WriteAllText(releasePath, "release");
        }

        await start.WaitAsync(TimeSpan.FromSeconds(30));
        AssertState(Plugin(manager, pluginId), PluginDotnetRuntimeState.Active);
    }

    [Fact]
    public async Task Runtime_CallerCancellationAfterCleanupStartsDoesNotCancelTransition()
    {
        const string pluginId = "cleanup-caller-cancellation";
        WritePlugin(
            _harness.PluginRoot(pluginId),
            pluginId,
            "CleanupCallerCancellation.Plugin",
            """
            using System;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            namespace CleanupCallerCancellation;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    Directory.CreateDirectory(context.DataRoot);
                    context.Lifetime.Run(async stopping =>
                    {
                        File.WriteAllText(Path.Combine(context.DataRoot, "work-started"), "started");
                        try
                        {
                            await Task.Delay(Timeout.InfiniteTimeSpan, stopping);
                        }
                        catch (OperationCanceledException)
                        {
                            File.WriteAllText(Path.Combine(context.DataRoot, "cleanup-started"), "started");
                            while (!File.Exists(Path.Combine(context.DataRoot, "cleanup-release")))
                                await Task.Delay(10);
                        }
                    });
                    return ValueTask.CompletedTask;
                }
            }
            """);
        await using var manager = _harness.CreateManager(cleanupTimeout: TimeSpan.FromSeconds(10));
        await manager.StartAsync(CancellationToken.None);
        await WaitForFileAsync(_harness.DataPath(pluginId, "work-started"));
        using var cancellation = new CancellationTokenSource();
        var cleanup = manager.SetEnabledAsync(pluginId, enabled: false, cancellation.Token);
        await WaitForFileAsync(_harness.DataPath(pluginId, "cleanup-started"));

        cancellation.Cancel();
        try
        {
            var completed = await Task.WhenAny(cleanup, Task.Delay(200));
            Assert.NotSame(cleanup, completed);
        }
        finally
        {
            File.WriteAllText(_harness.DataPath(pluginId, "cleanup-release"), "release");
        }

        await cleanup.WaitAsync(TimeSpan.FromSeconds(30));
        await WaitForStateAsync(manager, pluginId, PluginDotnetRuntimeState.Stopped);
    }

    [Fact]
    public async Task Runtime_ActivationTimeoutFaultsAndReenableActivatesAFreshGeneration()
    {
        WritePlugin(
            _harness.PluginRoot("slow-once"),
            "slow-once",
            "SlowOnce.Plugin",
            """
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            namespace SlowOnce;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    Directory.CreateDirectory(context.DataRoot);
                    var marker = Path.Combine(context.DataRoot, "attempted");
                    if (!File.Exists(marker))
                    {
                        File.WriteAllText(marker, "first");
                        Thread.Sleep(3000);
                    }
                    return ValueTask.CompletedTask;
                }
            }
            """);
        await using var manager = _harness.CreateManager(activationTimeout: TimeSpan.FromSeconds(1));

        await manager.StartAsync(CancellationToken.None);

        var timedOut = Plugin(manager, "slow-once");
        AssertState(timedOut, PluginDotnetRuntimeState.Faulted);
        Assert.Contains(timedOut.Blockers, blocker => blocker.Code == "PluginActivationTimeout");
        Assert.NotNull(timedOut.GenerationId);
        AssertReported(manager, "slow-once", "PluginActivationTimeout");

        await manager.SetEnabledAsync("slow-once", enabled: true);

        var active = await WaitForStateAsync(manager, "slow-once", PluginDotnetRuntimeState.Active);
        Assert.False(string.IsNullOrWhiteSpace(active.GenerationId));
    }

    [Fact]
    public async Task Runtime_DrainTimeoutProceedsWithoutRetainingTheNode()
    {
        WritePlugin(
            _harness.PluginRoot("slow-drain"),
            "slow-drain",
            "SlowDrain.Plugin",
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            namespace SlowDrain;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    context.Lifetime.Run(async stopping =>
                    {
                        try { await Task.Delay(Timeout.InfiniteTimeSpan, stopping); }
                        catch (OperationCanceledException) { await Task.Delay(400); }
                    });
                    return ValueTask.CompletedTask;
                }
            }
            """);
        await using var manager = _harness.CreateManager(cleanupTimeout: TimeSpan.FromMilliseconds(50));
        await manager.StartAsync(CancellationToken.None);

        await manager.SetEnabledAsync("slow-drain", enabled: false);

        Assert.Contains(
            manager.Snapshot.Diagnostics,
            diagnostic => diagnostic.Code == "PluginDrainTimeout");
        var settled = await WaitForStateAsync(manager, "slow-drain", PluginDotnetRuntimeState.Stopped);
        Assert.Null(settled.GenerationId);
        Assert.Empty(GenerationAssemblies(_harness.GenerationsRoot));
    }

    /// <summary>A failed activation must leave both an operator-visible log line and a client-visible diagnostic, not only a snapshot blocker.</summary>
    private void AssertReported(DotnetPluginRuntimeManager manager, string pluginId, string blockerCode)
    {
        Assert.Contains(
            manager.Snapshot.Diagnostics,
            diagnostic => diagnostic.Code == blockerCode && diagnostic.PluginId == pluginId);
        Assert.Contains(
            _harness.LogLines,
            line => line.Contains(blockerCode, StringComparison.Ordinal)
                    && line.Contains(pluginId, StringComparison.Ordinal));
    }

    private static string BlockingActivationPluginSource(string pluginNamespace) => $$"""
        using System.IO;
        using System.Threading;
        using System.Threading.Tasks;
        using DotCraft.Plugins;
        namespace {{pluginNamespace}};
        public sealed class Plugin : IDotCraftPlugin
        {
            public async ValueTask ActivateAsync(
                IPluginActivationContext context,
                CancellationToken cancellationToken)
            {
                Directory.CreateDirectory(context.DataRoot);
                File.WriteAllText(Path.Combine(context.DataRoot, "activation-started"), "started");
                while (!File.Exists(Path.Combine(context.DataRoot, "activation-release")))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(10, cancellationToken);
                }
            }
        }
        """;

    private sealed class DisposableProbe : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
