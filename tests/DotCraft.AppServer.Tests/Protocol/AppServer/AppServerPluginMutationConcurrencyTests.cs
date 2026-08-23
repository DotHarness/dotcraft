using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Lsp;
using DotCraft.Plugins;
using DotCraft.Workspaces;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerPluginMutationConcurrencyTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        $"plugin_mutation_concurrency_{Guid.NewGuid():N}");

    private string WorkspaceCraftPath => Path.Combine(_tempRoot, ".craft");

    [Fact]
    public async Task MutationMarker_IsVisibleOnlyToTheOwningAsyncFlow()
    {
        var state = new AppServerPluginManagementState();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var mutation = state.RunMutationAsync(async cancellationToken =>
        {
            Assert.True(state.IsMutationInProgress);
            entered.SetResult();
            await release.Task.WaitAsync(cancellationToken);
            Assert.True(state.IsMutationInProgress);
            return true;
        }, CancellationToken.None);

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(state.IsMutationInProgress);
        release.SetResult();
        Assert.True(await mutation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(state.IsMutationInProgress);
    }

    [Fact]
    public async Task CommitCompletion_ReleasesGateBeforeConnectionDeliveryFinishes()
    {
        var state = new AppServerPluginManagementState();
        var deliveryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelivery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = state.RunMutationAsync(async _ =>
        {
            state.CompleteCurrentMutation();
            Assert.False(state.IsMutationInProgress);
            deliveryStarted.SetResult();
            await releaseDelivery.Task;
            return 1;
        }, CancellationToken.None);

        await deliveryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = await state.RunMutationAsync(_ => Task.FromResult(2), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, second);

        releaseDelivery.SetResult();
        Assert.Equal(1, await first.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Mutations_AcrossConnections_AreSerializedWhileReadsRemainAvailable()
    {
        WriteBrowserFixture(Path.Combine(WorkspaceCraftPath, "plugins", "browser"));
        var runtime = new BlockingPluginRuntimeCoordinator(new PluginRuntimeSnapshot(
            3,
            [new PluginDotnetRuntimeInfo(
                "browser",
                "1.0.0",
                PluginDotnetRuntimeState.Blocked,
                null,
                [new PluginRuntimeBlocker(
                    PluginDotnetDiagnosticCodes.Untrusted,
                    "The plugin has no trust grant.",
                    new Dictionary<string, JsonElement>())],
                TrustStatus: PluginDotnetTrustStatus.Untrusted)],
            []));
        var managementState = new AppServerPluginManagementState();
        var broadcaster = new OrderedSnapshotBroadcaster();
        using var first = CreateHarness(runtime, managementState, broadcaster.Broadcast);
        using var second = CreateHarness(runtime, managementState, broadcaster.Broadcast);
        broadcaster.Add(first.Transport);
        broadcaster.Add(second.Transport);
        await first.InitializeAsync();
        await second.InitializeAsync();

        var trustTask = first.ExecuteRequestAsync(first.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginSetTrusted,
            new { id = "browser", trusted = true }));
        await runtime.TrustEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await second.ExecuteRequestAsync(second.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList,
            new { includeDisabled = true })).WaitAsync(TimeSpan.FromSeconds(5));
        using (var listResponse = await second.Transport.ReadNextSentAsync())
            AppServerTestHarness.AssertIsSuccessResponse(listResponse);

        var revokeTask = second.ExecuteRequestAsync(second.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginSetTrusted,
            new { id = "browser", trusted = false }));
        var revokeEnteredWhileTrustWasBlocked = runtime.RevokeTrustEntered.Task.IsCompleted;

        runtime.ReleaseTrust();
        await Task.WhenAll(trustTask, revokeTask).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(revokeEnteredWhileTrustWasBlocked);
        Assert.True(runtime.RevokeTrustEntered.Task.IsCompleted);
        Assert.Equal(1, runtime.MaxConcurrentMutations);

        using var firstTrustResponse = await first.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(firstTrustResponse);
        using var firstTrustNotification = await first.Transport.ReadNextSentAsync();
        using var firstRevokeNotification = await first.Transport.ReadNextSentAsync();

        using var secondTrustNotification = await second.Transport.ReadNextSentAsync();
        using var secondRevokeResponse = await second.Transport.ReadNextSentAsync();
        using var secondRevokeNotification = await second.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(secondRevokeResponse);

        var firstTrustRevision = AssertSnapshotNotification(firstTrustNotification, "browser");
        var secondTrustRevision = AssertSnapshotNotification(secondTrustNotification, "browser");
        var firstRevokeRevision = AssertSnapshotNotification(firstRevokeNotification, "browser");
        var secondRevokeRevision = AssertSnapshotNotification(secondRevokeNotification, "browser");
        Assert.Equal(firstTrustRevision, secondTrustRevision);
        Assert.Equal(firstRevokeRevision, secondRevokeRevision);
        Assert.True(firstRevokeRevision > firstTrustRevision);
    }

    [Fact]
    public async Task Mutation_AfterCommitBegins_ConvergesAfterClientCancellation()
    {
        WriteBrowserFixture(Path.Combine(WorkspaceCraftPath, "plugins", "browser"));
        var runtime = new BlockingPluginRuntimeCoordinator(new PluginRuntimeSnapshot(
            3,
            [new PluginDotnetRuntimeInfo(
                "browser",
                "1.0.0",
                PluginDotnetRuntimeState.Blocked,
                null,
                [new PluginRuntimeBlocker(
                    PluginDotnetDiagnosticCodes.Untrusted,
                    "The plugin has no trust grant.",
                    new Dictionary<string, JsonElement>())],
                TrustStatus: PluginDotnetTrustStatus.Untrusted)],
            []));
        var broadcasts = new List<DotCraft.Protocol.AppServer.PluginSnapshotUpdatedNotification>();
        using var harness = CreateHarness(
            runtime,
            new AppServerPluginManagementState(),
            (_, notification, _) => broadcasts.Add(notification));
        await harness.InitializeAsync();

        using var requestCancellation = new CancellationTokenSource();
        var request = harness.ExecuteRequestAsync(
            harness.BuildRequest(
                DotCraft.Protocol.AppServer.AppServerMethodNames.PluginSetTrusted,
                new { id = "browser", trusted = true }),
            requestCancellation.Token);

        await runtime.TrustEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        requestCancellation.Cancel();
        runtime.ReleaseTrust();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => request.WaitAsync(TimeSpan.FromSeconds(5)));

        var notification = Assert.Single(broadcasts);
        Assert.Contains(notification.PluginIds.Value!, id => PluginIds.EqualsCanonical(id, "browser"));
    }

    [Fact]
    public async Task Remove_WhenRootContributionQuiesceFails_BroadcastsRecoveredGeneration()
    {
        WriteBrowserFixture(Path.Combine(WorkspaceCraftPath, "plugins", "browser"));
        var runtime = new RecoveringPluginRuntimeCoordinator();
        var managementState = new AppServerPluginManagementState();
        var broadcaster = new OrderedSnapshotBroadcaster();
        await using var lsp = new ThrowingLspServerManager(_tempRoot);
        using var first = CreateHarness(runtime, managementState, broadcaster.Broadcast, lsp);
        using var second = CreateHarness(runtime, managementState, broadcaster.Broadcast);
        broadcaster.Add(first.Transport);
        broadcaster.Add(second.Transport);
        await first.InitializeAsync();
        await second.InitializeAsync();

        await AssertFailedRemoveBroadcastsRecoveryAsync(
            first,
            second,
            runtime,
            managementState,
            "PluginContributionQuiesceFailed");
    }

    [Fact]
    public async Task Remove_WhenDirectoryDeleteFails_BroadcastsRecoveredGeneration()
    {
        WriteBrowserFixture(Path.Combine(WorkspaceCraftPath, "plugins", "browser"));
        File.WriteAllText(Path.Combine(WorkspaceCraftPath, ".plugin-trash"), "blocks trash directory creation");
        var runtime = new RecoveringPluginRuntimeCoordinator();
        var managementState = new AppServerPluginManagementState();
        var broadcaster = new OrderedSnapshotBroadcaster();
        using var first = CreateHarness(runtime, managementState, broadcaster.Broadcast);
        using var second = CreateHarness(runtime, managementState, broadcaster.Broadcast);
        broadcaster.Add(first.Transport);
        broadcaster.Add(second.Transport);
        await first.InitializeAsync();
        await second.InitializeAsync();

        await AssertFailedRemoveBroadcastsRecoveryAsync(
            first,
            second,
            runtime,
            managementState,
            "PluginFilesystemCommitFailed");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private AppServerTestHarness CreateHarness(
        IPluginDotnetRuntimeCoordinator runtime,
        AppServerPluginManagementState managementState,
        Action<IAppServerTransport, DotCraft.Protocol.AppServer.PluginSnapshotUpdatedNotification, Task> broadcast,
        LspServerManager? lspServerManager = null) =>
        new(
            workspaceCraftPath: WorkspaceCraftPath,
            appConfigMonitor: new AppConfigMonitor(new AppConfig()),
            pluginDotnetRuntimeCoordinator: runtime,
            pluginManagementState: managementState,
            broadcastPluginSnapshotUpdated: broadcast,
            lspServerManager: lspServerManager);

    private static async Task AssertFailedRemoveBroadcastsRecoveryAsync(
        AppServerTestHarness first,
        AppServerTestHarness second,
        RecoveringPluginRuntimeCoordinator runtime,
        AppServerPluginManagementState managementState,
        string expectedDiagnosticCode)
    {
        var initialRevision = runtime.Snapshot.Revision;
        var suppressedRuntimeNotifications = 0;
        runtime.SnapshotChanged += OnRuntimeSnapshotChanged;
        try
        {
            await first.ExecuteRequestAsync(first.BuildRequest(
                DotCraft.Protocol.AppServer.AppServerMethodNames.PluginRemove,
                new { id = "browser" }));

            using var response = await first.Transport.ReadNextSentAsync();
            AppServerTestHarness.AssertIsSuccessResponse(response);
            var result = response.RootElement.GetProperty("result");
            Assert.Equal("notApplied", result.GetProperty("outcome").GetString());
            Assert.Contains(
                result.GetProperty("diagnostics").EnumerateArray(),
                diagnostic => diagnostic.GetProperty("code").GetString() == expectedDiagnosticCode);
            Assert.Equal(
                runtime.RecoveredGenerationId,
                result.GetProperty("plugin").GetProperty("dotnetRuntime").GetProperty("generationId").GetString());
            var responseRevision = result.GetProperty("snapshotRevision").GetInt64();

            // The initiating connection observes its response before its invalidation.
            using var firstNotification = await first.Transport.ReadNextSentAsync();
            var firstRevision = AssertSnapshotNotification(firstNotification, "browser");
            using var secondNotification = await second.Transport.ReadNextSentAsync();
            var secondRevision = AssertSnapshotNotification(secondNotification, "browser");
            Assert.Equal(responseRevision, firstRevision);
            Assert.Equal(firstRevision, secondRevision);
            Assert.True(secondRevision > initialRevision);

            await second.ExecuteRequestAsync(second.BuildRequest(
                DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList,
                new { includeDisabled = true }));
            using var listResponse = await second.Transport.ReadNextSentAsync();
            AppServerTestHarness.AssertIsSuccessResponse(listResponse);
            var listResult = listResponse.RootElement.GetProperty("result");
            Assert.True(listResult.GetProperty("snapshotRevision").GetInt64() >= secondRevision);
            var browser = Assert.Single(
                listResult.GetProperty("plugins").EnumerateArray(),
                plugin => plugin.GetProperty("id").GetString() == "browser");
            Assert.Equal(
                runtime.RecoveredGenerationId,
                browser.GetProperty("dotnetRuntime").GetProperty("generationId").GetString());

            Assert.Equal(1, runtime.ReconcileCount);
            Assert.Equal(2, suppressedRuntimeNotifications);
        }
        finally
        {
            runtime.SnapshotChanged -= OnRuntimeSnapshotChanged;
        }

        void OnRuntimeSnapshotChanged(object? sender, PluginRuntimeSnapshotChangedEventArgs args)
        {
            _ = sender;
            _ = args;
            if (managementState.IsMutationInProgress)
                Interlocked.Increment(ref suppressedRuntimeNotifications);
        }
    }

    private sealed class OrderedSnapshotBroadcaster
    {
        private readonly object _sync = new();
        private readonly List<InMemoryTransport> _transports = [];
        private readonly Dictionary<InMemoryTransport, Task> _tails = [];

        public void Add(InMemoryTransport transport)
        {
            lock (_sync)
            {
                _transports.Add(transport);
                _tails[transport] = Task.CompletedTask;
            }
        }

        public void Broadcast(
            IAppServerTransport source,
            DotCraft.Protocol.AppServer.PluginSnapshotUpdatedNotification notification,
            Task sourceResponseCompleted)
        {
            lock (_sync)
            {
                foreach (var target in _transports)
                {
                    var prerequisite = ReferenceEquals(target, source)
                        ? sourceResponseCompleted
                        : Task.CompletedTask;
                    _tails[target] = DeliverAsync(_tails[target], prerequisite, target, notification);
                }
            }
        }

        private static async Task DeliverAsync(
            Task previous,
            Task prerequisite,
            InMemoryTransport target,
            DotCraft.Protocol.AppServer.PluginSnapshotUpdatedNotification notification)
        {
            await previous.ConfigureAwait(false);
            await prerequisite.ConfigureAwait(false);
            await target.NotifyContractAsync(
                DotCraft.Protocol.AppServer.AppServerRpc.PluginSnapshotUpdated,
                notification,
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static long AssertSnapshotNotification(JsonDocument message, string pluginId)
    {
        AppServerTestHarness.AssertIsNotification(
            message,
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginSnapshotUpdated);
        var parameters = message.RootElement.GetProperty("params");
        Assert.Contains(
            parameters.GetProperty("pluginIds").EnumerateArray(),
            value => string.Equals(value.GetString(), pluginId, StringComparison.Ordinal));
        return parameters.GetProperty("snapshotRevision").GetInt64();
    }

    private static void WriteBrowserFixture(string pluginRoot)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "skills", "browser"));
        File.WriteAllText(
            Path.Combine(pluginRoot, "skills", "browser", "SKILL.md"),
            "---\nname: browser\ndescription: Test browser skill.\n---\n");
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            """
            {
              "schemaVersion": 1,
              "id": "browser",
              "version": "1.0.0",
              "displayName": "Browser",
              "description": "Test browser plugin.",
              "capabilities": ["skill"],
              "skills": "./skills/"
            }
            """);
    }

    private sealed class BlockingPluginRuntimeCoordinator(PluginRuntimeSnapshot snapshot)
        : IPluginDotnetRuntimeCoordinator
    {
        private readonly object _sync = new();
        private readonly TaskCompletionSource<bool> _releaseTrust =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeMutations;

        public TaskCompletionSource<bool> TrustEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> RevokeTrustEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaxConcurrentMutations { get; private set; }

        public PluginRuntimeSnapshot Snapshot { get; } = snapshot;

        public event EventHandler<PluginRuntimeSnapshotChangedEventArgs>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task SetEnabledAsync(
            string pluginId,
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            _ = pluginId;
            _ = enabled;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<PluginRuntimeMutationResult> QuiesceForMutationAsync(
            string pluginId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result(pluginId));

        public Task<PluginRuntimeMutationResult> ReconcileAfterMutationAsync(
            string pluginId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result(pluginId));

        public async Task<PluginRuntimeMutationResult> TrustAsync(
            string pluginId,
            CancellationToken cancellationToken = default)
        {
            EnterMutation();
            TrustEntered.TrySetResult(true);
            try
            {
                await _releaseTrust.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                ExitMutation();
            }

            return Result(pluginId);
        }

        public Task<PluginRuntimeMutationResult> RevokeTrustAsync(
            string pluginId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnterMutation();
            RevokeTrustEntered.TrySetResult(true);
            ExitMutation();
            return Task.FromResult(Result(pluginId));
        }

        public void ReleaseTrust() => _releaseTrust.TrySetResult(true);

        private void EnterMutation()
        {
            lock (_sync)
            {
                _activeMutations++;
                MaxConcurrentMutations = Math.Max(MaxConcurrentMutations, _activeMutations);
            }
        }

        private void ExitMutation()
        {
            lock (_sync)
                _activeMutations--;
        }

        private PluginRuntimeMutationResult Result(string pluginId) =>
            new(PluginRuntimeMutationOutcome.Applied, [pluginId], []);
    }

    private sealed class RecoveringPluginRuntimeCoordinator : IPluginDotnetRuntimeCoordinator
    {
        public PluginRuntimeSnapshot Snapshot { get; private set; } = new(
            10,
            [new PluginDotnetRuntimeInfo(
                "browser",
                "1.0.0",
                PluginDotnetRuntimeState.Active,
                "browser-g1",
                [],
                TrustStatus: PluginDotnetTrustStatus.Trusted)],
            []);

        public event EventHandler<PluginRuntimeSnapshotChangedEventArgs>? SnapshotChanged;

        public int ReconcileCount { get; private set; }

        public string? RecoveredGenerationId { get; private set; }

        public Task SetEnabledAsync(
            string pluginId,
            bool enabled,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PluginRuntimeMutationResult> QuiesceForMutationAsync(
            string pluginId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Publish(pluginId, PluginDotnetRuntimeState.Stopped, generationId: null);
            return Task.FromResult(Result(pluginId));
        }

        public Task<PluginRuntimeMutationResult> ReconcileAfterMutationAsync(
            string pluginId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReconcileCount++;
            RecoveredGenerationId = $"browser-g{Snapshot.Revision + 1}";
            Publish(pluginId, PluginDotnetRuntimeState.Active, RecoveredGenerationId);
            return Task.FromResult(Result(pluginId));
        }

        public Task<PluginRuntimeMutationResult> TrustAsync(
            string pluginId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PluginRuntimeMutationResult> RevokeTrustAsync(
            string pluginId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private void Publish(string pluginId, PluginDotnetRuntimeState state, string? generationId)
        {
            Snapshot = new PluginRuntimeSnapshot(
                Snapshot.Revision + 1,
                Snapshot.Plugins.Select(plugin => PluginIds.EqualsCanonical(plugin.PluginId, pluginId)
                    ? plugin with { State = state, GenerationId = generationId }
                    : plugin).ToArray(),
                Snapshot.Diagnostics);
            SnapshotChanged?.Invoke(this, new PluginRuntimeSnapshotChangedEventArgs(Snapshot, [pluginId]));
        }

        private static PluginRuntimeMutationResult Result(string pluginId) =>
            new(PluginRuntimeMutationOutcome.Applied, [pluginId], []);
    }

    private sealed class ThrowingLspServerManager(string workspacePath) : LspServerManager(
        new AppConfig(),
        new DotCraftPaths(workspacePath, Path.Combine(workspacePath, ".craft"), userDataPath: null))
    {
        public override Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException("Synthetic LSP refresh failure."));
    }
}
