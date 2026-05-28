using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.AppServer;
using DotCraft.Automations.Abstractions;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.Dreams;
using DotCraft.Gateway;
using DotCraft.Heartbeat;
using DotCraft.Hosting;
using DotCraft.Memory;
using DotCraft.Modules;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Tests.AppServer;

public sealed class AppServerWorkspaceRuntimeFeatureTests
{
    [Fact]
    public async Task StartStop_WiresLifecycleCallbacks_AndForwardsAutomationUpdates()
    {
        using var fixture = new WorkspaceFixture();
        var runnerFactory = new FakeChannelRunnerFactory(new FakeChannelRunner());
        var automationFactory = new FakeAutomationRuntimeFactory(new FakeAutomationRuntime());
        await using var provider = CreateProvider(runnerFactory, automationFactory);
        var feature = CreateFeature(provider);
        var context = CreateContext(fixture);

        IAutomationTaskEventPayload? forwardedTask = null;
        feature.AutomationTaskUpdated += task => forwardedTask = task;

        await feature.StartAsync(context);

        Assert.NotNull(context.CronService.CronJobPersistedAfterExecution);
        Assert.NotNull(context.CronService.OnJob);
        Assert.NotNull(context.HeartbeatService.OnResult);
        Assert.Equal(1, automationFactory.Instance.StartCalls);
        Assert.Same(context, automationFactory.Instance.LastContext);
        Assert.Equal(1, runnerFactory.Instance!.InitializeCalls);
        Assert.Equal(1, runnerFactory.Instance.StartWebPoolCalls);
        Assert.Equal(1, runnerFactory.Instance.BeginLoopCalls);
        Assert.Same(runnerFactory.Instance, feature.ChannelStatusProvider);
        Assert.Equal(FakeChannelRunner.DashboardAddress, feature.DashboardUrl);

        var task = new FakeAutomationTask
        {
            Id = "task-1",
            Title = "Test Task",
            Status = AutomationTaskStatus.Pending
        };
        automationFactory.Instance.EmitTaskUpdated(task);
        Assert.Same(task, forwardedTask);

        await feature.StopAsync();

        Assert.Null(context.CronService.CronJobPersistedAfterExecution);
        Assert.Null(context.CronService.OnJob);
        Assert.Null(context.HeartbeatService.OnResult);
        Assert.Equal(1, automationFactory.Instance.StopCalls);
        Assert.Equal(1, automationFactory.Instance.DisposeCalls);
        Assert.Equal(1, runnerFactory.Instance.DisposeCalls);

        await feature.StopAsync();
        Assert.Equal(1, automationFactory.Instance.StopCalls);
        Assert.Equal(1, runnerFactory.Instance.DisposeCalls);
    }

    [Fact]
    public async Task StartAsync_WhenAutomationRuntimeFails_CleansPartialState_AndNewFeatureCanStart()
    {
        using var fixture = new WorkspaceFixture();
        var failingAutomationFactory = new FakeAutomationRuntimeFactory(new FakeAutomationRuntime
        {
            ThrowOnStart = true
        });
        await using var failingProvider = CreateProvider(
            new FakeChannelRunnerFactory(new FakeChannelRunner()),
            failingAutomationFactory);
        var failingFeature = CreateFeature(failingProvider);
        var failingContext = CreateContext(fixture);

        await Assert.ThrowsAsync<InvalidOperationException>(() => failingFeature.StartAsync(failingContext));

        Assert.Null(failingContext.CronService.CronJobPersistedAfterExecution);
        Assert.Null(failingContext.CronService.OnJob);
        Assert.Null(failingContext.HeartbeatService.OnResult);
        Assert.Equal(1, failingAutomationFactory.Instance.StartCalls);
        Assert.Equal(1, failingAutomationFactory.Instance.StopCalls);
        Assert.Equal(1, failingAutomationFactory.Instance.DisposeCalls);

        await using var successfulProvider = CreateProvider(
            new FakeChannelRunnerFactory(new FakeChannelRunner()),
            new FakeAutomationRuntimeFactory(new FakeAutomationRuntime()));
        var successfulFeature = CreateFeature(successfulProvider);
        var successfulContext = CreateContext(fixture);

        await successfulFeature.StartAsync(successfulContext);
        Assert.NotNull(successfulContext.CronService.OnJob);
        await successfulFeature.StopAsync();
    }

    [Fact]
    public async Task ApplyExternalChannelUpdates_WithoutRunner_IsSafeNoOp()
    {
        using var fixture = new WorkspaceFixture();
        await using var provider = CreateProvider(new FakeChannelRunnerFactory(null), null);
        var feature = CreateFeature(provider);
        var context = CreateContext(fixture);

        await feature.StartAsync(context);

        await feature.ApplyExternalChannelUpsertAsync(new ExternalChannelEntry
        {
            Name = "demo",
            Enabled = true
        });
        await feature.ApplyExternalChannelRemoveAsync("demo");

        await feature.StopAsync();
    }

    [Fact]
    public async Task ApplyExternalChannelUpdates_WithRunner_AreForwarded()
    {
        using var fixture = new WorkspaceFixture();
        var runnerFactory = new FakeChannelRunnerFactory(new FakeChannelRunner());
        await using var provider = CreateProvider(runnerFactory, null);
        var feature = CreateFeature(provider);
        var context = CreateContext(fixture);

        await feature.StartAsync(context);

        var entry = new ExternalChannelEntry
        {
            Name = "demo",
            Enabled = true
        };

        await feature.ApplyExternalChannelUpsertAsync(entry);
        await feature.ApplyExternalChannelRemoveAsync("demo");

        Assert.Same(entry, runnerFactory.Instance!.LastUpsertedEntry);
        Assert.Equal("demo", runnerFactory.Instance.LastRemovedChannelName);

        await feature.StopAsync();
    }

    private static IWorkspaceRuntimeAppServerFeature CreateFeature(ServiceProvider provider) =>
        new AppServerWorkspaceRuntimeFeatureFactory().Create(provider);

    private static ServiceProvider CreateProvider(
        FakeChannelRunnerFactory? runnerFactory,
        FakeAutomationRuntimeFactory? automationFactory)
    {
        var services = new ServiceCollection()
            .AddSingleton<IChannelRuntimeRegistry, ChannelRuntimeRegistry>()
            .AddSingleton(sp => new MessageRouter(sp.GetRequiredService<IChannelRuntimeRegistry>()));

        if (runnerFactory != null)
            services.AddSingleton<IAppServerChannelRunnerFactory>(runnerFactory);
        if (automationFactory != null)
            services.AddSingleton<IAppServerAutomationRuntimeFactory>(automationFactory);

        return services.BuildServiceProvider();
    }

    private static WorkspaceRuntimeAppServerFeatureContext CreateContext(WorkspaceFixture fixture)
    {
        var config = new AppConfig
        {
            Cron = new AppConfig.CronConfig
            {
                Enabled = false
            },
            Heartbeat = new AppConfig.HeartbeatConfig
            {
                Enabled = false,
                NotifyAdmin = true,
                IntervalSeconds = 300
            }
        };

        var paths = new DotCraftPaths
        {
            WorkspacePath = fixture.WorkspacePath,
            CraftPath = fixture.BotPath
        };
        var sessionService = new FakeSessionService();
        var cronService = new CronService(Path.Combine(fixture.BotPath, "cron-jobs.json"));
        var heartbeatService = new HeartbeatService(
            fixture.WorkspacePath,
            (_, _, _, _) => Task.FromResult<AgentRunResult?>(null),
            enabled: false);
        var dreamStore = new DreamStore(fixture.BotPath);
        var dreamsService = new DreamsService(
            config,
            new DreamsInputCollector(
                config,
                fixture.WorkspacePath,
                new MemoryStore(fixture.BotPath),
                dreamStore,
                new ThreadStore(fixture.BotPath)),
            new FakeDreamsRunner(),
            dreamStore,
            new DreamsStateStore(dreamStore));
        var services = new ServiceCollection().BuildServiceProvider();

        return new WorkspaceRuntimeAppServerFeatureContext(
            services,
            config,
            paths,
            new ModuleRegistry(),
            sessionService,
            new AgentRunner(fixture.WorkspacePath, sessionService, quiet: true),
            cronService,
            heartbeatService,
            dreamsService,
            emitCronStateChanged: (_, _, _) => { },
            emitBackgroundJobResult: _ => { });
    }

    private sealed class WorkspaceFixture : IDisposable
    {
        public string WorkspacePath { get; } = Path.Combine(
            Path.GetTempPath(),
            "AppServerRuntimeFeatureWs_" + Guid.NewGuid().ToString("N")[..8]);

        public string BotPath { get; }

        public WorkspaceFixture()
        {
            Directory.CreateDirectory(WorkspacePath);
            BotPath = Path.Combine(WorkspacePath, ".craft");
            Directory.CreateDirectory(BotPath);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(WorkspacePath, recursive: true);
            }
            catch
            {
            }
        }
    }

    private sealed class FakeChannelRunnerFactory(FakeChannelRunner? instance) : IAppServerChannelRunnerFactory
    {
        public FakeChannelRunner? Instance { get; } = instance;

        public IAppServerChannelRunner? Create(
            IServiceProvider services,
            AppConfig config,
            DotCraftPaths paths,
            ModuleRegistry moduleRegistry)
        {
            _ = services;
            _ = config;
            _ = paths;
            _ = moduleRegistry;
            return Instance;
        }
    }

    private sealed class FakeChannelRunner : IAppServerChannelRunner
    {
        public const string DashboardAddress = "http://127.0.0.1:9910/dashboard";

        public int InitializeCalls { get; private set; }
        public int StartWebPoolCalls { get; private set; }
        public int BeginLoopCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public ExternalChannelEntry? LastUpsertedEntry { get; private set; }
        public string? LastRemovedChannelName { get; private set; }

        public string? DashboardUrl => DashboardAddress;

        public void Initialize(
            ISessionService sessionService,
            HeartbeatService heartbeatService,
            CronService cronService,
            DreamsService dreamsService)
        {
            _ = sessionService;
            _ = heartbeatService;
            _ = cronService;
            _ = dreamsService;
            InitializeCalls++;
        }

        public Task StartWebPoolAsync()
        {
            StartWebPoolCalls++;
            return Task.CompletedTask;
        }

        public void BeginChannelLoops(CancellationToken ct)
        {
            _ = ct;
            BeginLoopCalls++;
        }

        public Task ApplyExternalChannelUpsertAsync(ExternalChannelEntry entry, CancellationToken ct = default)
        {
            _ = ct;
            LastUpsertedEntry = entry;
            return Task.CompletedTask;
        }

        public Task ApplyExternalChannelRemoveAsync(string channelName, CancellationToken ct = default)
        {
            _ = ct;
            LastRemovedChannelName = channelName;
            return Task.CompletedTask;
        }

        public IReadOnlyList<ChannelStatusInfo> GetChannelStatuses() =>
        [
            new ChannelStatusInfo
            {
                Name = "demo",
                Category = "external",
                Enabled = true,
                Running = true
            }
        ];

        public IReadOnlyList<string> GetRecentExternalChannelLogs(string channelName, int? tail = null)
        {
            _ = channelName;
            _ = tail;
            return [];
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAutomationRuntimeFactory(FakeAutomationRuntime instance) : IAppServerAutomationRuntimeFactory
    {
        public FakeAutomationRuntime Instance { get; } = instance;

        public IAppServerAutomationRuntime? Create(IServiceProvider services)
        {
            _ = services;
            return Instance;
        }
    }

    private sealed class FakeAutomationRuntime : IAppServerAutomationRuntime
    {
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public bool ThrowOnStart { get; init; }
        public WorkspaceRuntimeAppServerFeatureContext? LastContext { get; private set; }

        public event Action<IAutomationTaskEventPayload>? AutomationTaskUpdated;

        public Task StartAsync(WorkspaceRuntimeAppServerFeatureContext context, CancellationToken ct = default)
        {
            _ = ct;
            StartCalls++;
            LastContext = context;
            if (ThrowOnStart)
                throw new InvalidOperationException("automation runtime failed");

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            _ = ct;
            StopCalls++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }

        public void EmitTaskUpdated(IAutomationTaskEventPayload task)
        {
            AutomationTaskUpdated?.Invoke(task);
        }
    }

    private sealed class FakeAutomationTask : AutomationTask;

    private sealed class FakeSessionService : ISessionService
    {
        public Action<SessionThread>? ThreadCreatedForBroadcast { get; set; }
        public Action<string>? ThreadDeletedForBroadcast { get; set; }
        public Action<SessionThread>? ThreadRenamedForBroadcast { get; set; }
        public Action<string, ThreadStatus, ThreadStatus>? ThreadStatusChangedForBroadcast { get; set; }
        public Action<string, SessionThreadRuntimeSignal>? ThreadRuntimeSignalForBroadcast { get; set; }

        public Task<SessionThread> CreateThreadAsync(SessionIdentity identity, ThreadConfiguration? config = null, HistoryMode historyMode = HistoryMode.Server, string? threadId = null, string? displayName = null, CancellationToken ct = default, ThreadSource? source = null) => throw new NotImplementedException();
        public Task<ThreadResetResult> ResetConversationAsync(SessionIdentity identity, ThreadConfiguration? config = null, HistoryMode historyMode = HistoryMode.Server, string? displayName = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SessionThread> ResumeThreadAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task PauseThreadAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ArchiveThreadAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UnarchiveThreadAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(SessionIdentity identity, bool includeArchived = false, IReadOnlyList<string>? crossChannelOrigins = null, CancellationToken ct = default, bool includeSubAgents = false) => throw new NotImplementedException();
        public Task UpsertThreadSpawnEdgeAsync(ThreadSpawnEdge edge, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SetThreadSpawnEdgeStatusAsync(string parentThreadId, string childThreadId, string status, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ThreadSpawnEdge>> ListSubAgentChildrenAsync(string parentThreadId, bool includeClosed = false, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<SessionEvent> SubscribeThreadAsync(string threadId, bool replayRecent = false, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<SessionEvent> SubmitInputAsync(string threadId, IList<AIContent> content, SenderContext? sender = null, ChatMessage[]? messages = null, CancellationToken ct = default, SessionInputSnapshot? inputSnapshot = null) => throw new NotImplementedException();
        public Task ResolveApprovalAsync(string threadId, string turnId, string requestId, SessionApprovalDecision decision, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ResolveUserInputRequestAsync(string threadId, string turnId, string requestId, RequestUserInputResponse response, CancellationToken ct = default) => throw new NotImplementedException();
        public Task CancelTurnAsync(string threadId, string turnId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task CleanBackgroundTerminalsAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SessionThread> RollbackThreadAsync(string threadId, int numTurns, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<QueuedTurnInput> EnqueueTurnInputAsync(string threadId, IList<AIContent> content, SenderContext? sender = null, CancellationToken ct = default, SessionInputSnapshot? inputSnapshot = null) => throw new NotImplementedException();
        public Task<IReadOnlyList<QueuedTurnInput>> RemoveQueuedTurnInputAsync(string threadId, string queuedInputId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<QueuedTurnInput>> ReorderQueuedTurnInputsAsync(string threadId, IReadOnlyList<string> orderedQueuedInputIds, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<TurnSteerResult> SteerTurnAsync(string threadId, string expectedTurnId, string queuedInputId, CancellationToken ct = default, SenderContext? sender = null) => throw new NotImplementedException();
        public Task SetThreadModeAsync(string threadId, string mode, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateThreadConfigurationAsync(string threadId, ThreadConfiguration config, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SessionThread> GetThreadAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SessionThread> EnsureThreadLoadedAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteThreadPermanentlyAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RenameThreadAsync(string threadId, string displayName, CancellationToken ct = default) => throw new NotImplementedException();
        public ContextUsageSnapshot? TryGetContextUsageSnapshot(string threadId) => null;
    }

    private sealed class FakeDreamsRunner : IDreamsRunner
    {
        public string ModelId => "fake-dream-model";

        public Task<DreamsGenerationResult> GenerateAsync(
            DreamsRunInput input,
            string runId,
            string trigger,
            string? outputStoreId = null,
            string? modelId = null,
            Action<DreamsRunSessionBinding>? onSessionBinding = null,
            CancellationToken cancellationToken = default)
        {
            _ = input;
            _ = runId;
            _ = trigger;
            _ = outputStoreId;
            _ = modelId;
            _ = onSessionBinding;
            _ = cancellationToken;
            return Task.FromResult(DreamsGenerationResult.Failed("not used"));
        }
    }
}
