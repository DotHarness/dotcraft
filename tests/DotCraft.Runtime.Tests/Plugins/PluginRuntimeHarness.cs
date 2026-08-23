using System.Runtime.CompilerServices;
using DotCraft.Configuration;
using DotCraft.Contributions;
using DotCraft.Plugins;
using DotCraft.Runtime;
using DotCraft.Tools;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DotCraft.Tests.Runtime.Plugins;

/// <summary>Builds a real runtime manager over a temporary workspace of compiled plugin bundles.
/// Reclaim is asynchronous, so the state assertions poll.</summary>
internal sealed class PluginRuntimeHarness : IDisposable
{
    public PluginRuntimeHarness()
    {
        Root = Path.Combine(Path.GetTempPath(), $"dotcraft_plugin_runtime_{Guid.NewGuid():N}");
    }

    public string Root { get; }

    public AppConfig Config { get; } = new();

    public ContributionRegistry Registry { get; } = new();

    private readonly InMemoryTrustStore _trustStore = new();

    /// <summary>Every log line those managers wrote, formatted.</summary>
    public List<string> LogLines { get; } = [];

    public IServiceProvider Services { get; set; } = DotnetPluginTestBundle.EmptyServices;

    public string Workspace => Path.Combine(Root, "workspace");

    public string PluginRoot(string pluginId) => Path.Combine(Workspace, ".craft", "plugins", pluginId);

    public string DataPath(string pluginId, string fileName) =>
        Path.Combine(Root, "user-data", "plugins", pluginId, fileName);

    public string GenerationsRoot => Path.Combine(Workspace, ".craft", "runtime");

    /// <summary>Trusts every installed bundle. Grants are fingerprint-bound, so rewriting a bundle
    /// afterwards invalidates its grant.</summary>
    public void TrustInstalled()
    {
        var pluginsRoot = Path.Combine(Workspace, ".craft", "plugins");
        if (!Directory.Exists(pluginsRoot))
            return;

        foreach (var pluginRoot in Directory.GetDirectories(pluginsRoot))
        {
            _trustStore.SetTrusted(
                Path.GetFileName(pluginRoot),
                PluginBundleFingerprint.Compute(pluginRoot),
                trusted: true);
        }
    }

    public DotnetPluginRuntimeManager CreateManager(
        TimeSpan? activationTimeout = null,
        TimeSpan? cleanupTimeout = null,
        TimeSpan? collectionTimeout = null,
        TimeSpan? collectionPollInterval = null,
        int? leakedGenerationRestartThreshold = null,
        bool trustInstalled = true,
        bool trustStoreAvailable = true,
        IReadOnlyList<string>? builtInPluginSourceRoots = null)
    {
        if (trustInstalled)
            TrustInstalled();
        var options = new DotCraftRuntimeOptions
        {
            Config = Config,
            WorkspacePath = Workspace,
            DataPath = ".craft",
            UserDataPath = Path.Combine(Root, "user-data")
        };
        var paths = DotCraftPathResolver.Resolve(options);
        return new DotnetPluginRuntimeManager(
            new PluginDiscoveryService(paths, builtInPluginSourceRoots),
            Config,
            paths,
            Services,
            Registry,
            new DotnetPluginRuntimeOptions
            {
                ActivationTimeout = activationTimeout ?? TimeSpan.FromSeconds(10),
                CleanupTimeout = cleanupTimeout ?? TimeSpan.FromSeconds(10),
                CollectionTimeout = collectionTimeout ?? TimeSpan.FromSeconds(3),
                CollectionPollInterval = collectionPollInterval ?? TimeSpan.FromMilliseconds(250),
                LeakedGenerationRestartThreshold = leakedGenerationRestartThreshold ?? 3
            },
            logger: new CollectingLogger<DotnetPluginRuntimeManager>(LogLines),
            trustStore: trustStoreAvailable && string.IsNullOrWhiteSpace(Config.GlobalConfigPath)
                ? _trustStore
                : null);
    }

    private sealed class CollectingLogger<TCategory>(List<string> lines) : ILogger<TCategory>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (lines)
            {
                lines.Add(
                    $"{logLevel}: {formatter(state, exception)}"
                    + (exception is null ? string.Empty : $" | {exception}"));
            }
        }
    }

    private sealed class InMemoryTrustStore : IPluginDotnetTrustStore
    {
        private readonly Dictionary<string, HashSet<string>> _grants = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, IReadOnlySet<string>> Read() => _grants.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlySet<string>)new HashSet<string>(pair.Value, StringComparer.Ordinal),
            StringComparer.OrdinalIgnoreCase);

        public void SetTrusted(string pluginId, string fingerprint, bool trusted)
        {
            if (!_grants.TryGetValue(pluginId, out var fingerprints))
                _grants[pluginId] = fingerprints = new HashSet<string>(StringComparer.Ordinal);
            if (trusted)
                fingerprints.Add(fingerprint);
            else
                fingerprints.Remove(fingerprint);
            if (fingerprints.Count == 0)
                _grants.Remove(pluginId);
        }
    }

    /// <summary>Writes a bundle whose entry does nothing but activate.</summary>
    public void WriteNoop(
        string pluginId,
        string version = "1.0.0",
        IReadOnlyDictionary<string, string>? dependencies = null) =>
        DotnetPluginTestBundle.WritePluginBundle(
            PluginRoot(pluginId),
            pluginId,
            "Noop.Plugin",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            namespace Noop;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }
            """,
            version,
            dependencies: dependencies);

    /// <summary>Writes a bundle that deliberately leaks its load context through a Host-owned static
    /// event.</summary>
    public void WriteLeaking(
        string pluginId,
        string version = "1.0.0",
        IReadOnlyDictionary<string, string>? dependencies = null) =>
        DotnetPluginTestBundle.WritePluginBundle(
            PluginRoot(pluginId),
            pluginId,
            "Leaking.Plugin",
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            namespace Leaking;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
                    return ValueTask.CompletedTask;
                }
                private static void OnProcessExit(object? sender, EventArgs args) { }
            }
            """,
            version,
            dependencies: dependencies);

    /// <summary>Every assembly mapped from a generation shadow copy; empty means none was.</summary>
    public static IEnumerable<string> GenerationAssemblies(string runtimeRoot) =>
        Directory.Exists(runtimeRoot)
            ? Directory.EnumerateDirectories(runtimeRoot, "generations", SearchOption.AllDirectories)
                .SelectMany(static root => Directory.EnumerateFiles(root, "*.dll", SearchOption.AllDirectories))
            : [];

    public static PluginDotnetRuntimeInfo Plugin(DotnetPluginRuntimeManager manager, string pluginId) =>
        manager.Snapshot.Plugins.Single(plugin => plugin.PluginId == pluginId);

    public static async Task<PluginDotnetRuntimeInfo> WaitForStateAsync(
        DotnetPluginRuntimeManager manager,
        string pluginId,
        PluginDotnetRuntimeState expected,
        int attempts = 1500)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (expected == PluginDotnetRuntimeState.Stopped)
                OfferCollection();
            var plugin = Plugin(manager, pluginId);
            if (plugin.State == expected)
                return plugin;
            await Task.Delay(20);
        }

        var observed = Plugin(manager, pluginId);
        AssertState(observed, expected);
        return observed;
    }

    /// <summary>Every generation shadow copy still on disk; a copy is deleted once its load context
    /// is observed collected.</summary>
    public static IReadOnlyList<string> GenerationCopies(string runtimeRoot) =>
        Directory.Exists(runtimeRoot)
            ? Directory.EnumerateDirectories(runtimeRoot, "generations", SearchOption.AllDirectories)
                // generations/<pluginId>/<generationId>: the per-plugin directory is not a copy.
                .SelectMany(static root => Directory.EnumerateDirectories(root))
                .SelectMany(static plugin => Directory.EnumerateDirectories(plugin))
                .Select(static path => Path.GetFileName(path)!)
                .ToArray()
            : [];

    /// <summary>Waits for a plugin to stop with nothing left: no live generation, none awaiting
    /// collection, no shadow copy. "Stopped" and "reclaimed" are two different instants.</summary>
    public static async Task<PluginDotnetRuntimeInfo> WaitForReclaimedAsync(
        DotnetPluginRuntimeManager manager,
        string pluginId,
        string runtimeRoot,
        int attempts = 1500)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            OfferCollection();
            var plugin = Plugin(manager, pluginId);
            if (plugin.State == PluginDotnetRuntimeState.Stopped
                && plugin.LeakedGenerations == 0
                && GenerationCopies(runtimeRoot).Count == 0)
            {
                return plugin;
            }

            await Task.Delay(20);
        }

        var observed = Plugin(manager, pluginId);
        AssertState(observed, PluginDotnetRuntimeState.Stopped);
        Assert.Equal(0, observed.LeakedGenerations);
        var copies = GenerationCopies(runtimeRoot);
        Assert.True(
            copies.Count == 0,
            $"'{pluginId}' left {copies.Count} generation copies behind: {string.Join(", ", copies)}");
        return observed;
    }

    /// <summary>Offers the collection an idle test process would not otherwise run. The runtime reclaims on
    /// the ambient GC, which a host under load provides and a waiting test does not.</summary>
    /// <remarks>Not inlined: an inlined frame can keep the load context reachable.</remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void OfferCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
    }

    public static void AssertState(PluginDotnetRuntimeInfo plugin, PluginDotnetRuntimeState expected) =>
        Assert.True(
            plugin.State == expected,
            $"Expected {expected} for '{plugin.PluginId}', observed {plugin.State}: "
            + string.Join(" | ", plugin.Blockers.Select(static blocker => $"{blocker.Code}: {blocker.Message}")));

    public static void AssertBlocker(
        DotnetPluginRuntimeManager manager,
        string pluginId,
        string code,
        string? reason = null)
    {
        var plugin = Plugin(manager, pluginId);
        AssertState(plugin, PluginDotnetRuntimeState.Blocked);
        Assert.Contains(
            plugin.Blockers,
            blocker => blocker.Code == code
                && (reason == null
                    || (blocker.Parameters.TryGetValue("reason", out var observed)
                        && observed.GetString() == reason)));
    }

    /// <summary>The Host planning inputs a Tool test resolves a plugin source under.</summary>
    public static ToolPlanningContext PlanningContext(long revision, string threadId = "thread-1") =>
        new(
            threadId: threadId,
            turnId: "turn-1",
            workspacePath: Path.GetTempPath(),
            dataPath: Path.GetTempPath(),
            mode: "default",
            profile: null,
            providerCapabilities: null,
            revision: revision);

    public static async Task<EffectiveToolSnapshot> BuildSnapshotAsync(IToolSource source, long revision) =>
        await new EffectiveToolSnapshotBuilder().BuildAsync([source], PlanningContext(revision));

    public static ToolInvocationRequest Request(string callId) =>
        new("thread-1", "turn-1", callId, ToolInvocationAudience.Model);

    public static async Task WaitForFileAsync(string path)
    {
        for (var attempt = 0; attempt < 3000; attempt++)
        {
            if (File.Exists(path))
                return;
            await Task.Delay(10);
        }

        Assert.Fail($"Timed out waiting for '{path}'.");
    }

    public static async Task WaitForLineAsync(string path, string expected)
    {
        for (var attempt = 0; attempt < 3000; attempt++)
        {
            try
            {
                if (PluginLogFile.ReadLines(path).Contains(expected, StringComparer.Ordinal))
                    return;
            }
            catch (IOException)
            {
                // The plugin may be appending the line concurrently.
            }

            await Task.Delay(10);
        }

        Assert.Fail(
            $"Timed out waiting for '{expected}' in '{path}'. "
            + $"Observed: {string.Join(" | ", PluginLogFile.ReadLines(path))}");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // A deliberately leaked generation keeps its shadow copy mapped.
        }
    }
}
