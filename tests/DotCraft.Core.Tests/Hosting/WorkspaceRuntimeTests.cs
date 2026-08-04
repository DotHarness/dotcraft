using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.Agents;
using DotCraft.Hosting;
using DotCraft.Lsp;
using DotCraft.Mcp;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Modules;
using Microsoft.Extensions.DependencyInjection;
using DotCraft.AppServer;
using DotCraft.Sessions;
using ConfigSchemaSection = DotCraft.Configuration.ConfigSchemaSection;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using SessionThread = DotCraft.Sessions.SessionThread;
using ConfigSchemaField = DotCraft.Configuration.ConfigSchemaField;
using Xunit;

namespace DotCraft.Tests.Hosting;

public sealed class WorkspaceRuntimeTests
{
    [Fact]
    public async Task AddDotCraft_RegistersWorkspaceRuntimeWithExpectedSingletons()
    {
        using var fixture = new WorkspaceFixture();
        var config = CreateConfig();

        await using var provider = CreateRuntimeServices(fixture, config)
            .BuildServiceProvider();

        var runtime = provider.GetRequiredService<WorkspaceRuntime>();

        Assert.NotNull(runtime.Services);
        Assert.Same(config, runtime.Config);
        Assert.Equal(fixture.WorkspacePath, runtime.Paths.WorkspacePath);
        Assert.Equal(fixture.BotPath, runtime.Paths.CraftPath);
        Assert.Same(provider.GetRequiredService<MemoryStore>(), runtime.MemoryStore);
        Assert.Same(provider.GetRequiredService<SkillsLoader>(), runtime.SkillsLoader);
        Assert.Same(provider.GetRequiredService<PathBlacklist>(), runtime.PathBlacklist);
        Assert.Same(provider.GetRequiredService<McpClientManager>(), runtime.McpClientManager);
        Assert.Same(provider.GetRequiredService<LspServerManager>(), runtime.LspServerManager);
    }

    [Fact]
    public async Task WorkspaceRuntime_ServicesResolveAppServerWorkspaceDependencies()
    {
        using var fixture = new WorkspaceFixture();

        await using var provider = CreateRuntimeServices(fixture)
            .BuildServiceProvider();

        var runtime = provider.GetRequiredService<WorkspaceRuntime>();

        Assert.NotNull(runtime.Services.GetRequiredService<ThreadStore>());
        Assert.NotNull(runtime.Services.GetRequiredService<SessionPersistenceService>());
        Assert.NotNull(runtime.Services.GetRequiredService<CronService>());
        Assert.NotNull(runtime.Services.GetRequiredService<IAppConfigMonitor>());
    }

    [Fact]
    public async Task WorkspaceRuntime_StartStop_ExposesStartedStateAndFeatureOutputs()
    {
        using var fixture = new WorkspaceFixture();
        var featureFactory = new FakeWorkspaceRuntimeAppServerFeatureFactory();

        await using var provider = CreateRuntimeServices(fixture)
            .AddSingleton<IWorkspaceRuntimeAppServerFeatureFactory>(featureFactory)
            .BuildServiceProvider();

        var runtime = provider.GetRequiredService<WorkspaceRuntime>();

        Assert.False(runtime.IsStarted);
        Assert.Throws<InvalidOperationException>(() => _ = runtime.SessionService);

        await runtime.StartAsync(new ModuleRegistry());

        Assert.True(runtime.IsStarted);
        Assert.NotNull(runtime.SessionService);
        Assert.NotNull(runtime.CommitMessageSuggestService);
        Assert.NotNull(runtime.WelcomeSuggestionService);
        Assert.NotNull(runtime.CronService);
        Assert.NotNull(runtime.HeartbeatService);
        Assert.NotNull(runtime.WireAcpExtensionProxy);
        Assert.NotNull(runtime.ChannelListContributor);
        Assert.NotEmpty(runtime.ConfigSchema);
        Assert.Same(featureFactory.LastCreatedFeature!.ChannelStatusProvider, runtime.ChannelStatusProvider);
        Assert.Equal(FakeWorkspaceRuntimeAppServerFeature.DashboardAddress, runtime.DashboardUrl);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartAsync(new ModuleRegistry()));

        await runtime.StopAsync();
        Assert.False(runtime.IsStarted);
        Assert.Throws<InvalidOperationException>(() => _ = runtime.SessionService);

        await runtime.StopAsync();
    }

    [Fact]
    public async Task WorkspaceRuntime_StartAsync_UsesRegisteredConfigSchemaProvider()
    {
        using var fixture = new WorkspaceFixture();
        var schemaProvider = new RecordingConfigSchemaProvider([
            new ConfigSchemaSection
            {
                Section = "Provider Only",
                Fields =
                [
                    new ConfigSchemaField
                    {
                        Key = "Enabled",
                        Type = "bool"
                    }
                ]
            }
        ]);

        await using var provider = CreateRuntimeServices(fixture, schemaProvider: schemaProvider)
            .AddSingleton<IWorkspaceRuntimeAppServerFeatureFactory>(new FakeWorkspaceRuntimeAppServerFeatureFactory())
            .BuildServiceProvider();

        var runtime = provider.GetRequiredService<WorkspaceRuntime>();
        await runtime.StartAsync(new ModuleRegistry());

        var section = Assert.Single(runtime.ConfigSchema);
        Assert.Equal("Provider Only", section.Section);
        Assert.Single(section.Fields);
        Assert.Equal("Enabled", section.Fields[0].Key);
        Assert.Equal(1, schemaProvider.GetConfigSchemaCallCount);

        await runtime.StopAsync();
    }

    [Fact]
    public async Task WorkspaceRuntime_StartAsync_RequiresRegisteredConfigSchemaProvider()
    {
        using var fixture = new WorkspaceFixture();

        await using var provider = new ServiceCollection()
            .AddDotCraft(CreateConfig(), fixture.WorkspacePath, fixture.BotPath)
            .AddSingleton<IWorkspaceRuntimeAppServerFeatureFactory>(new FakeWorkspaceRuntimeAppServerFeatureFactory())
            .BuildServiceProvider();

        var runtime = provider.GetRequiredService<WorkspaceRuntime>();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartAsync(new ModuleRegistry()));

        Assert.Contains(nameof(IConfigSchemaProvider), error.Message);
        Assert.Contains("ConfigSchemaRegistrations.CreateSchemaProvider", error.Message);
    }

    [Fact]
    public async Task WorkspaceRuntime_ForwardsFeatureAndSessionEvents()
    {
        using var fixture = new WorkspaceFixture();
        var featureFactory = new FakeWorkspaceRuntimeAppServerFeatureFactory();

        await using var provider = CreateRuntimeServices(fixture)
            .AddSingleton<IWorkspaceRuntimeAppServerFeatureFactory>(featureFactory)
            .BuildServiceProvider();

        var runtime = provider.GetRequiredService<WorkspaceRuntime>();
        await runtime.StartAsync(new ModuleRegistry());

        AppConfigChangedEventArgs? configChanged = null;
        McpServerStatusChangedEventArgs? mcpChanged = null;
        SessionThread? threadStarted = null;
        SessionThread? threadRenamed = null;
        string? threadDeleted = null;
        (CronJob? Job, string Id, bool Removed)? cronChanged = null;
        BackgroundJobResult? backgroundJobResult = null;
        IAutomationTaskEventPayload? automationTask = null;
        (string ThreadId, SessionThreadRuntimeSignal Signal)? runtimeSignal = null;

        runtime.WorkspaceConfigChanged += evt => configChanged = evt;
        runtime.McpStatusChanged += evt => mcpChanged = evt;
        runtime.ThreadStarted += thread => threadStarted = thread;
        runtime.ThreadRenamed += thread => threadRenamed = thread;
        runtime.ThreadDeleted += threadId => threadDeleted = threadId;
        runtime.CronStateChanged += (job, id, removed) => cronChanged = (job, id, removed);
        runtime.BackgroundJobResultProduced += result => backgroundJobResult = result;
        runtime.AutomationTaskUpdated += task => automationTask = task;
        runtime.ThreadRuntimeSignal += (threadId, signal) => runtimeSignal = (threadId, signal);

        provider.GetRequiredService<IAppConfigMonitor>().NotifyChanged("workspace/test", [ConfigChangeRegions.Skills]);
        Assert.NotNull(configChanged);
        Assert.Equal("workspace/test", configChanged!.Source);

        await runtime.McpClientManager.UpsertAsync(new McpServerConfig
        {
            Name = "demo-mcp",
            Transport = "stdio",
            Command = "echo",
            Enabled = false
        });
        Assert.NotNull(mcpChanged);
        Assert.Equal("demo-mcp", mcpChanged!.Status.Name);

        var thread = await runtime.SessionService.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "cli",
            UserId = "test-user",
            WorkspacePath = fixture.WorkspacePath
        }, displayName: "Initial Name");
        Assert.Equal(thread.Id, threadStarted?.Id);

        await runtime.SessionService.RenameThreadAsync(thread.Id, "Renamed Thread");
        Assert.Equal(thread.Id, threadRenamed?.Id);
        Assert.Equal("Renamed Thread", threadRenamed?.DisplayName);

        runtime.SessionService.ThreadRuntimeSignalForBroadcast?.Invoke(thread.Id, SessionThreadRuntimeSignal.TurnStarted);
        Assert.Equal((thread.Id, SessionThreadRuntimeSignal.TurnStarted), runtimeSignal);

        featureFactory.LastCreatedFeature!.EmitCronStateChanged(null, "job-1", removed: true);
        Assert.NotNull(cronChanged);
        Assert.Equal("job-1", cronChanged?.Id);
        Assert.True(cronChanged?.Removed);

        featureFactory.LastCreatedFeature.EmitBackgroundJobResult(new BackgroundJobResult(
            "cron",
            "job-1",
            "Nightly",
            "done",
            null,
            thread.Id,
            12,
            34));
        Assert.NotNull(backgroundJobResult);
        Assert.Equal("cron", backgroundJobResult!.Source);
        Assert.Equal("done", backgroundJobResult.Result);

        var fakeTask = new FakeAutomationTaskPayload();
        featureFactory.LastCreatedFeature.EmitAutomationTaskUpdated(fakeTask);
        Assert.Same(fakeTask, automationTask);

        await runtime.SessionService.DeleteThreadPermanentlyAsync(thread.Id);
        Assert.Equal(thread.Id, threadDeleted);
    }

    [Fact]
    public async Task WorkspaceRuntime_StopAsync_ClearsBroadcastHooks_AndUnsubscribesFromSources()
    {
        using var fixture = new WorkspaceFixture();
        var feature = new FakeWorkspaceRuntimeAppServerFeature
        {
            SetCallbacksOnStart = true
        };
        var featureFactory = new FakeWorkspaceRuntimeAppServerFeatureFactory(feature);

        await using var provider = CreateRuntimeServices(fixture)
            .AddSingleton<IWorkspaceRuntimeAppServerFeatureFactory>(featureFactory)
            .BuildServiceProvider();

        var runtime = provider.GetRequiredService<WorkspaceRuntime>();
        await runtime.StartAsync(new ModuleRegistry());

        var sessionService = runtime.SessionService;
        Assert.NotNull(sessionService.ThreadCreatedForBroadcast);
        Assert.NotNull(sessionService.ThreadDeletedForBroadcast);
        Assert.NotNull(sessionService.ThreadRenamedForBroadcast);
        Assert.NotNull(sessionService.ThreadRuntimeSignalForBroadcast);

        var configChangedCount = 0;
        var mcpChangedCount = 0;
        runtime.WorkspaceConfigChanged += _ => configChangedCount++;
        runtime.McpStatusChanged += _ => mcpChangedCount++;

        provider.GetRequiredService<IAppConfigMonitor>().NotifyChanged("workspace/test", [ConfigChangeRegions.Skills]);
        await runtime.McpClientManager.UpsertAsync(new McpServerConfig
        {
            Name = "before-stop",
            Transport = "stdio",
            Command = "echo",
            Enabled = false
        });

        Assert.Equal(1, configChangedCount);
        Assert.Equal(1, mcpChangedCount);

        await runtime.StopAsync();

        Assert.Null(sessionService.ThreadCreatedForBroadcast);
        Assert.Null(sessionService.ThreadDeletedForBroadcast);
        Assert.Null(sessionService.ThreadRenamedForBroadcast);
        Assert.Null(sessionService.ThreadRuntimeSignalForBroadcast);
        Assert.True(feature.SawCronCallbackDuringStop);
        Assert.True(feature.SawCronPersistedCallbackDuringStop);
        Assert.NotNull(feature.LastStoppedContext);
        Assert.Null(feature.LastStoppedContext!.CronService.OnJob);
        Assert.Null(feature.LastStoppedContext.CronService.CronJobPersistedAfterExecution);
        Assert.Null(feature.LastStoppedContext.HeartbeatService.OnResult);

        provider.GetRequiredService<IAppConfigMonitor>().NotifyChanged("workspace/after-stop", [ConfigChangeRegions.Skills]);
        await runtime.McpClientManager.UpsertAsync(new McpServerConfig
        {
            Name = "after-stop",
            Transport = "stdio",
            Command = "echo",
            Enabled = false
        });

        Assert.Equal(1, configChangedCount);
        Assert.Equal(1, mcpChangedCount);
    }

    [Fact]
    public async Task WorkspaceRuntime_DisposeAsync_StopsFeature_AndDisposesRuntime()
    {
        using var fixture = new WorkspaceFixture();
        var feature = new FakeWorkspaceRuntimeAppServerFeature
        {
            SetCallbacksOnStart = true
        };
        var featureFactory = new FakeWorkspaceRuntimeAppServerFeatureFactory(feature);

        await using var provider = CreateRuntimeServices(fixture)
            .AddSingleton<IWorkspaceRuntimeAppServerFeatureFactory>(featureFactory)
            .BuildServiceProvider();

        var runtime = provider.GetRequiredService<WorkspaceRuntime>();
        await runtime.StartAsync(new ModuleRegistry());

        await runtime.DisposeAsync();

        Assert.Equal(2, feature.StopCalls);
        Assert.Equal(1, feature.DisposeCalls);
        Assert.True(feature.SawCronCallbackDuringStop);
        Assert.Throws<ObjectDisposedException>(() => _ = runtime.SessionService);
    }

    [Fact]
    public void CreateHeartbeatBackgroundJobResult_OnSuccess_ReturnsHeartbeatResult()
    {
        var run = new AgentRunResult("done", "thread-1", 12, 34, null);

        var result = WorkspaceRuntime.CreateHeartbeatBackgroundJobResult(run);

        Assert.NotNull(result);
        Assert.Equal("heartbeat", result!.Source);
        Assert.Null(result.JobId);
        Assert.Null(result.JobName);
        Assert.Equal("done", result.Result);
        Assert.Null(result.Error);
        Assert.Equal("thread-1", result.ThreadId);
        Assert.Equal(12, result.InputTokens);
        Assert.Equal(34, result.OutputTokens);
    }

    [Fact]
    public void CreateHeartbeatBackgroundJobResult_OnFailure_ReturnsHeartbeatError()
    {
        var run = new AgentRunResult(null, "thread-2", 21, 43, "boom");

        var result = WorkspaceRuntime.CreateHeartbeatBackgroundJobResult(run);

        Assert.NotNull(result);
        Assert.Equal("heartbeat", result!.Source);
        Assert.Null(result.JobId);
        Assert.Null(result.JobName);
        Assert.Null(result.Result);
        Assert.Equal("boom", result.Error);
        Assert.Equal("thread-2", result.ThreadId);
        Assert.Equal(21, result.InputTokens);
        Assert.Equal(43, result.OutputTokens);
    }

    [Fact]
    public void CreateHeartbeatBackgroundJobResult_WhenRunIsNull_ReturnsNull()
    {
        var result = WorkspaceRuntime.CreateHeartbeatBackgroundJobResult(null);

        Assert.Null(result);
    }

    [Fact]
    public void CreateHeartbeatBackgroundJobResult_WhenRunHasNoResultOrError_ReturnsNull()
    {
        var run = new AgentRunResult(null, "thread-3", 5, 8, null);

        var result = WorkspaceRuntime.CreateHeartbeatBackgroundJobResult(run);

        Assert.Null(result);
    }

    [Fact]
    public async Task WorkspaceRuntime_FailedStart_LeavesRuntimeUnstarted_AndFreshRuntimeCanStart()
    {
        using var fixture = new WorkspaceFixture();
        var failingFeature = new FakeWorkspaceRuntimeAppServerFeature
        {
            ThrowOnStart = true
        };

        await using var failingProvider = CreateRuntimeServices(fixture)
            .AddSingleton<IWorkspaceRuntimeAppServerFeatureFactory>(
                new FakeWorkspaceRuntimeAppServerFeatureFactory(failingFeature))
            .BuildServiceProvider();

        var failingRuntime = failingProvider.GetRequiredService<WorkspaceRuntime>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => failingRuntime.StartAsync(new ModuleRegistry()));
        Assert.False(failingRuntime.IsStarted);
        Assert.Equal(1, failingFeature.StartCalls);
        Assert.Equal(1, failingFeature.StopCalls);
        Assert.Equal(1, failingFeature.DisposeCalls);

        await using var successProvider = CreateRuntimeServices(fixture)
            .AddSingleton<IWorkspaceRuntimeAppServerFeatureFactory>(
                new FakeWorkspaceRuntimeAppServerFeatureFactory(new FakeWorkspaceRuntimeAppServerFeature()))
            .BuildServiceProvider();

        var successRuntime = successProvider.GetRequiredService<WorkspaceRuntime>();
        await successRuntime.StartAsync(new ModuleRegistry());
        Assert.True(successRuntime.IsStarted);
        await successRuntime.StopAsync();
    }

    private static IServiceCollection CreateRuntimeServices(
        WorkspaceFixture fixture,
        AppConfig? config = null,
        IConfigSchemaProvider? schemaProvider = null)
        => new ServiceCollection()
            .AddDotCraft(config ?? CreateConfig(), fixture.WorkspacePath, fixture.BotPath)
            .AddSingleton<IConfigSchemaProvider>(
                schemaProvider ?? new RecordingConfigSchemaProvider([
                    new ConfigSchemaSection
                    {
                        Section = "Core",
                        Fields = []
                    }
                ]));

    private sealed class RecordingConfigSchemaProvider(IReadOnlyList<ConfigSchemaSection> schema) : IConfigSchemaProvider
    {
        public int GetConfigSchemaCallCount { get; private set; }

        public IReadOnlyList<ConfigSchemaSection> GetConfigSchema()
        {
            GetConfigSchemaCallCount++;
            return schema;
        }
    }

    private sealed class WorkspaceFixture : IDisposable
    {
        public string WorkspacePath { get; } = Path.Combine(
            Path.GetTempPath(),
            "WorkspaceRuntimeWs_" + Guid.NewGuid().ToString("N")[..8]);

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
                // ignored
            }
        }
    }

    private static AppConfig CreateConfig() =>
        AppConfigTestFactory.CreateOpenAI(
            model: "gpt-4.1-mini",
            apiKey: "test-key",
            endPoint: "https://api.openai.com/v1");

    private sealed class FakeWorkspaceRuntimeAppServerFeatureFactory : IWorkspaceRuntimeAppServerFeatureFactory
    {
        private readonly Func<FakeWorkspaceRuntimeAppServerFeature> _featureFactory;

        public FakeWorkspaceRuntimeAppServerFeature? LastCreatedFeature { get; private set; }

        public FakeWorkspaceRuntimeAppServerFeatureFactory()
            : this(static () => new FakeWorkspaceRuntimeAppServerFeature())
        {
        }

        public FakeWorkspaceRuntimeAppServerFeatureFactory(FakeWorkspaceRuntimeAppServerFeature feature)
            : this(() => feature)
        {
        }

        private FakeWorkspaceRuntimeAppServerFeatureFactory(Func<FakeWorkspaceRuntimeAppServerFeature> featureFactory)
        {
            _featureFactory = featureFactory;
        }

        public IWorkspaceRuntimeAppServerFeature Create(IServiceProvider services)
        {
            _ = services;
            LastCreatedFeature = _featureFactory();
            return LastCreatedFeature;
        }
    }

    private sealed class FakeWorkspaceRuntimeAppServerFeature : IWorkspaceRuntimeAppServerFeature
    {
        public const string DashboardAddress = "http://127.0.0.1:4310";

        private WorkspaceRuntimeAppServerFeatureContext? _context;
        private bool _started;

        public bool SetCallbacksOnStart { get; init; }
        public bool ThrowOnStart { get; init; }
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public bool SawCronCallbackDuringStop { get; private set; }
        public bool SawCronPersistedCallbackDuringStop { get; private set; }
        public WorkspaceRuntimeAppServerFeatureContext? LastContext => _context;
        public WorkspaceRuntimeAppServerFeatureContext? LastStoppedContext { get; private set; }

        public IChannelStatusProvider? ChannelStatusProvider { get; } = new FakeChannelStatusProvider();

        public IExternalChannelLogProvider? ExternalChannelLogProvider => null;

        public string? DashboardUrl => DashboardAddress;

        public event Action<IAutomationTaskEventPayload>? AutomationTaskUpdated;

        public Task StartAsync(WorkspaceRuntimeAppServerFeatureContext context, CancellationToken ct = default)
        {
            _ = ct;
            if (_started)
                throw new InvalidOperationException("feature already started");

            StartCalls++;
            _context = context;
            if (SetCallbacksOnStart)
            {
                context.CronService.OnJob = _ => Task.FromResult(new CronOnJobResult(null, null, null, true, null, null));
                context.CronService.CronJobPersistedAfterExecution = (_, _, _) => { };
                context.HeartbeatService.OnResult = _ => Task.CompletedTask;
            }

            if (ThrowOnStart)
                throw new InvalidOperationException("feature start failed");

            _started = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            _ = ct;
            StopCalls++;
            if (_context != null)
            {
                LastStoppedContext = _context;
                SawCronCallbackDuringStop = _context.CronService.OnJob != null;
                SawCronPersistedCallbackDuringStop = _context.CronService.CronJobPersistedAfterExecution != null;
            }
            _context = null;
            _started = false;
            return Task.CompletedTask;
        }

        public Task ApplyExternalChannelUpsertAsync(ExternalChannelEntry entry, CancellationToken ct = default)
        {
            _ = entry;
            _ = ct;
            return Task.CompletedTask;
        }

        public Task ApplyExternalChannelRemoveAsync(string channelName, CancellationToken ct = default)
        {
            _ = channelName;
            _ = ct;
            return Task.CompletedTask;
        }

        public void EmitCronStateChanged(CronJob? job, string id, bool removed)
        {
            _context!.EmitCronStateChanged(job, id, removed);
        }

        public void EmitBackgroundJobResult(BackgroundJobResult result)
        {
            _context!.EmitBackgroundJobResult(result);
        }

        public void EmitAutomationTaskUpdated(IAutomationTaskEventPayload task)
        {
            AutomationTaskUpdated?.Invoke(task);
        }

        public async ValueTask DisposeAsync()
        {
            DisposeCalls++;
            await StopAsync();
        }
    }

    private sealed class FakeChannelStatusProvider : IChannelStatusProvider
    {
        public IReadOnlyList<ChannelStatusSnapshot> GetChannelStatuses() =>
        [
            new ChannelStatusSnapshot
            {
                Name = "fake-channel",
                Category = "external",
                Enabled = true,
                Running = true
            }
        ];
    }

    private sealed class FakeAutomationTaskPayload : IAutomationTaskEventPayload;
}
