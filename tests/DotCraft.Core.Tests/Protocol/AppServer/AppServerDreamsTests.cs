using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Dreams;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerDreamsTests : IDisposable
{
    private readonly string _root;
    private readonly string _workspace;
    private readonly string _craft;
    private readonly ThreadStore _threadStore;
    private readonly MemoryStore _memoryStore;
    private readonly DreamStore _dreamStore;
    private readonly DreamsStateStore _stateStore;

    public AppServerDreamsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "AppServerDreams_" + Guid.NewGuid().ToString("N")[..8]);
        _workspace = Path.Combine(_root, "workspace");
        _craft = Path.Combine(_workspace, ".craft");
        Directory.CreateDirectory(_craft);
        _threadStore = new ThreadStore(_craft);
        _memoryStore = new MemoryStore(_craft);
        _dreamStore = new DreamStore(_craft);
        _stateStore = new DreamsStateStore(_dreamStore);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }

    [Fact]
    public async Task Initialize_WithDreamsService_AdvertisesDreamsCapability()
    {
        var config = CreateConfig();
        await using var dreamsService = CreateDreamsService(config, new FakeRunner());
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _craft,
            appConfigMonitor: new AppConfigMonitor(config),
            dreamsService: dreamsService);

        var init = await harness.InitializeAsync();

        var caps = init.RootElement.GetProperty("result").GetProperty("capabilities");
        Assert.True(caps.GetProperty("dreams").GetBoolean());
    }

    [Fact]
    public async Task DreamsStatus_ReturnsConfigAndNullLastRunBeforeFirstRun()
    {
        var config = CreateConfig(interval: TimeSpan.FromHours(12), threadLookback: 50);
        config.Dreams.AutoApply = true;
        await using var dreamsService = CreateDreamsService(config, new FakeRunner());
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _craft,
            appConfigMonitor: new AppConfigMonitor(config),
            dreamsService: dreamsService);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.DreamsStatus, new { }));

        var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("enabled").GetBoolean());
        Assert.Equal("12:00:00", result.GetProperty("interval").GetString());
        Assert.Equal(50, result.GetProperty("threadLookbackCount").GetInt32());
        Assert.True(result.GetProperty("autoApply").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("lastRun").ValueKind);
    }

    [Fact]
    public async Task DreamsRun_StartsOneBackgroundRunAndDoesNotDuplicateActiveRun()
    {
        await SaveThreadAsync("thread_one");
        var config = CreateConfig(minCompletedTurns: 1);
        var runner = new FakeRunner { BlockUntilReleased = true };
        await using var dreamsService = CreateDreamsService(config, runner);
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _craft,
            appConfigMonitor: new AppConfigMonitor(config),
            dreamsService: dreamsService);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.DreamsRun, new { }));
        var first = await harness.Transport.ReadNextSentAsync();
        await runner.WaitUntilCalledAsync();
        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.DreamsRun, new { }));
        var second = await harness.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsSuccessResponse(first);
        AppServerTestHarness.AssertIsSuccessResponse(second);
        Assert.True(first.RootElement.GetProperty("result").GetProperty("running").GetBoolean());
        Assert.True(second.RootElement.GetProperty("result").GetProperty("running").GetBoolean());
        Assert.Equal(DreamsConstants.TriggerManual, first.RootElement.GetProperty("result").GetProperty("lastRun").GetProperty("trigger").GetString());
        Assert.Equal(1, runner.Calls);

        runner.Release();
        await WaitForStateAsync(DreamsRunStatuses.Succeeded);
        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.DreamsStatus, new { }));
        var completed = await harness.Transport.ReadNextSentAsync();
        var lastRun = completed.RootElement.GetProperty("result").GetProperty("lastRun");
        Assert.Equal("thread_dream_fake", lastRun.GetProperty("threadId").GetString());
        Assert.Equal("turn_dream_fake", lastRun.GetProperty("turnId").GetString());
        Assert.Equal(DreamsConstants.TriggerManual, lastRun.GetProperty("trigger").GetString());
        Assert.Equal(1, lastRun.GetProperty("candidateThreadCount").GetInt32());
        Assert.Equal(2, lastRun.GetProperty("evidenceSearchCount").GetInt32());
        Assert.Equal(1, lastRun.GetProperty("evidenceReadCount").GetInt32());
        Assert.False(lastRun.GetProperty("autoApplied").GetBoolean());
        Assert.Equal("thread_one", lastRun.GetProperty("evidenceThreadIds")[0].GetString());
    }

    [Fact]
    public async Task DreamsRun_WithNoInputEvidence_CompletesSkippedWithoutCallingRunner()
    {
        var config = CreateConfig(minCompletedTurns: 1);
        var runner = new FakeRunner();
        await using var dreamsService = CreateDreamsService(config, runner);
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _craft,
            appConfigMonitor: new AppConfigMonitor(config),
            dreamsService: dreamsService);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.DreamsRun, new { }));
        var first = await harness.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsSuccessResponse(first);
        Assert.True(first.RootElement.GetProperty("result").GetProperty("running").GetBoolean());

        await WaitForStateAsync(DreamsRunStatuses.Skipped);
        var state = _stateStore.Load();
        Assert.NotNull(state);
        Assert.Equal("no_input_evidence", state.Message);
        Assert.Null(state.ThreadId);
        Assert.Null(state.TurnId);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task DreamsLifecycle_CreateListApplyDiscardAndArchive()
    {
        await SaveThreadAsync("thread_one");
        var config = CreateConfig(minCompletedTurns: 1);
        await using var dreamsService = CreateDreamsService(config, new FakeRunner());
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _craft,
            appConfigMonitor: new AppConfigMonitor(config),
            dreamsService: dreamsService,
            dreamStore: _dreamStore);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.DreamsCreate, new
        {
            threadIds = new[] { "thread_one" },
            instructions = "Focus protocol memory."
        }));
        var created = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(created);
        var run = created.RootElement.GetProperty("result").GetProperty("run");
        var runId = run.GetProperty("id").GetString()!;
        Assert.True(created.RootElement.GetProperty("result").GetProperty("run").GetProperty("status").GetString() is DreamsRunStatuses.Running);

        await WaitForStateAsync(DreamsRunStatuses.Succeeded);
        var completed = _stateStore.Load(runId)!;
        Assert.Equal(DreamsReviewStatuses.Pending, completed.ReviewStatus);
        Assert.False(string.IsNullOrWhiteSpace(completed.OutputStoreId));
        Assert.Equal(string.Empty, _dreamStore.ReadDream());

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.DreamsList, new { }));
        var listed = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(listed);
        Assert.Contains(
            listed.RootElement.GetProperty("result").GetProperty("runs").EnumerateArray(),
            item => item.GetProperty("id").GetString() == runId);

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.DreamsGet, new { runId }));
        var details = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(details);
        var preview = details.RootElement.GetProperty("result").GetProperty("preview");
        Assert.Equal(string.Empty, preview.GetProperty("activeIndexMarkdown").GetString());
        Assert.Contains("Test focus", preview.GetProperty("outputIndexMarkdown").GetString(), StringComparison.Ordinal);

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.DreamsApply, new { runId }));
        var applied = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(applied);
        var appliedRun = applied.RootElement.GetProperty("result").GetProperty("run");
        Assert.Equal(DreamsReviewStatuses.Applied, appliedRun.GetProperty("reviewStatus").GetString());
        Assert.Equal(completed.OutputStoreId, applied.RootElement.GetProperty("result").GetProperty("activeDreamStoreId").GetString());
        Assert.Contains("Test focus", _dreamStore.ReadDream(), StringComparison.Ordinal);

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.DreamsArchive, new { runId }));
        var archived = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(archived);
        Assert.Equal(
            DreamsReviewStatuses.Archived,
            archived.RootElement.GetProperty("result").GetProperty("run").GetProperty("reviewStatus").GetString());
    }

    [Fact]
    public async Task DreamsRun_WhenDisabled_DoesNotCreateRunnerWork()
    {
        var config = CreateConfig();
        config.Dreams.Enabled = false;
        var runner = new FakeRunner();
        await using var dreamsService = CreateDreamsService(config, runner);
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _craft,
            appConfigMonitor: new AppConfigMonitor(config),
            dreamsService: dreamsService);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.DreamsRun, new { }));
        var response = await harness.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("running").GetBoolean());
        var lastRun = result.GetProperty("lastRun");
        Assert.Equal(DreamsRunStatuses.Skipped, lastRun.GetProperty("status").GetString());
        Assert.Equal("dreams_disabled", lastRun.GetProperty("message").GetString());
        Assert.Equal(DreamsConstants.TriggerManual, lastRun.GetProperty("trigger").GetString());
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_DreamsFields_WriteConfigUpdateMonitorAndEmitMemoryRegion()
    {
        var config = CreateConfig();
        var changes = new List<AppConfigChangedEventArgs>();
        var monitor = new AppConfigMonitor(config);
        monitor.Changed += (_, args) => changes.Add(args);
        await using var dreamsService = CreateDreamsService(config, new FakeRunner());
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _craft,
            appConfigMonitor: monitor,
            dreamsService: dreamsService);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new
        {
            dreamsEnabled = false,
            dreamsInterval = "12:00:00",
            dreamsThreadLookbackCount = 50,
            dreamsAutoApply = true
        }));

        var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("dreamsEnabled").GetBoolean());
        Assert.Equal("12:00:00", result.GetProperty("dreamsInterval").GetString());
        Assert.Equal(50, result.GetProperty("dreamsThreadLookbackCount").GetInt32());
        Assert.True(result.GetProperty("dreamsAutoApply").GetBoolean());
        Assert.False(monitor.Current.Dreams.Enabled);
        Assert.Equal(TimeSpan.FromHours(12), monitor.Current.Dreams.Interval);
        Assert.Equal(50, monitor.Current.Dreams.ThreadLookbackCount);
        Assert.True(monitor.Current.Dreams.AutoApply);
        Assert.Contains(changes, change => change.Regions.Contains(ConfigChangeRegions.Memory));

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_craft, "config.json")));
        var dreams = doc.RootElement.GetProperty("Dreams");
        Assert.False(dreams.GetProperty("Enabled").GetBoolean());
        Assert.Equal("12:00:00", dreams.GetProperty("Interval").GetString());
        Assert.Equal(50, dreams.GetProperty("ThreadLookbackCount").GetInt32());
        Assert.True(dreams.GetProperty("AutoApply").GetBoolean());
    }

    [Fact]
    public async Task DreamsRun_WithAutoApply_ReturnsAppliedAutoAppliedState()
    {
        await SaveThreadAsync("thread_one");
        var config = CreateConfig(minCompletedTurns: 1);
        config.Dreams.AutoApply = true;
        await using var dreamsService = CreateDreamsService(config, new FakeRunner());
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _craft,
            appConfigMonitor: new AppConfigMonitor(config),
            dreamsService: dreamsService,
            dreamStore: _dreamStore);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.DreamsRun, new { }));
        _ = await harness.Transport.ReadNextSentAsync();

        await WaitForStateAsync(DreamsRunStatuses.Succeeded);
        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.DreamsStatus, new { }));
        var completed = await harness.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsSuccessResponse(completed);
        var lastRun = completed.RootElement.GetProperty("result").GetProperty("lastRun");
        Assert.Equal(DreamsReviewStatuses.Applied, lastRun.GetProperty("reviewStatus").GetString());
        Assert.True(lastRun.GetProperty("autoApplied").GetBoolean());
        Assert.Equal(lastRun.GetProperty("outputStoreId").GetString(), completed.RootElement.GetProperty("result").GetProperty("activeDreamStoreId").GetString());
    }

    [Theory]
    [InlineData("dreamsInterval", "00:00:00")]
    [InlineData("dreamsThreadLookbackCount", 0)]
    public async Task WorkspaceConfigUpdate_InvalidDreamsFields_ReturnsInvalidParams(string field, object value)
    {
        var config = CreateConfig();
        await using var dreamsService = CreateDreamsService(config, new FakeRunner());
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _craft,
            appConfigMonitor: new AppConfigMonitor(config),
            dreamsService: dreamsService);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppServerMethods.WorkspaceConfigUpdate,
            new Dictionary<string, object> { [field] = value }));

        var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
    }

    private DreamsService CreateDreamsService(AppConfig config, FakeRunner runner)
    {
        runner.Store ??= _dreamStore;
        var collector = new DreamsInputCollector(config, _workspace, _memoryStore, _dreamStore, _threadStore);
        return new DreamsService(config, collector, runner, _dreamStore, _stateStore);
    }

    private static AppConfig CreateConfig(
        TimeSpan? interval = null,
        int threadLookback = 20,
        int minCompletedTurns = 5) =>
        new()
        {
            Dreams = new DreamsConfig
            {
                Enabled = true,
                Interval = interval ?? TimeSpan.FromHours(24),
                StartupDelay = TimeSpan.Zero,
                ThreadLookbackCount = threadLookback,
                HistoryTailChars = 20_000,
                MinCompletedTurnsSinceLastRun = minCompletedTurns
            }
        };

    private async Task SaveThreadAsync(string id)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new SessionItem
        {
            Id = "item_user",
            TurnId = "turn_001",
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            Payload = new UserMessagePayload { Text = "Please organize workspace memory." },
            CreatedAt = now
        };
        var assistant = new SessionItem
        {
            Id = "item_agent",
            TurnId = "turn_001",
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            Payload = new AgentMessagePayload { Text = "Dreams can help." },
            CreatedAt = now
        };

        await _threadStore.SaveThreadAsync(new SessionThread
        {
            Id = id,
            WorkspacePath = _workspace,
            OriginChannel = "desktop",
            DisplayName = "Dreams test",
            Source = ThreadSource.User(),
            Status = ThreadStatus.Active,
            CreatedAt = now.AddMinutes(-5),
            LastActiveAt = now,
            HistoryMode = HistoryMode.Server,
            Turns =
            [
                new SessionTurn
                {
                    Id = "turn_001",
                    ThreadId = id,
                    Status = TurnStatus.Completed,
                    Input = user,
                    Items = [user, assistant],
                    StartedAt = now.AddMinutes(-1),
                    CompletedAt = now
                }
            ]
        });
    }

    private async Task WaitForStateAsync(string status)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (_stateStore.Load()?.Status == status)
                return;
            await Task.Delay(50);
        }

        Assert.Equal(status, _stateStore.Load()?.Status);
    }

    private static string ValidDreamMarkdown() =>
        """
        # Dream Memory

        Generated by scheduled Dreams from recent workspace sessions. Treat as inferred background context, not explicit user instruction.

        ## Workspace Focus
        - Test focus.

        ## Active Threads And Open Loops

        ## Inferred Project Conventions

        ## Repeated Problems And Prior Mistakes

        ## Latest Stable Understanding

        ## Low-Signal Or One-Off Context To Ignore
        """;

    private sealed class FakeRunner : IDreamsRunner
    {
        private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ModelId => "fake-dream-model";

        public int Calls { get; private set; }

        public bool BlockUntilReleased { get; init; }

        public DreamStore? Store { get; set; }

        public async Task<DreamsGenerationResult> GenerateAsync(
            DreamsRunInput input,
            string runId,
            string trigger,
            string? outputStoreId = null,
            string? modelId = null,
            Action<DreamsRunSessionBinding>? onSessionBinding = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            _called.TrySetResult();
            onSessionBinding?.Invoke(new DreamsRunSessionBinding("thread_dream_fake", "turn_dream_fake"));
            if (BlockUntilReleased)
                await _release.Task.WaitAsync(cancellationToken);
            string? createdOutputStoreId = null;
            if (Store != null)
            {
                var store = Store.CreateOutputStore(runId, DateTimeOffset.UtcNow);
                await File.WriteAllTextAsync(store.IndexPath, ValidDreamMarkdown(), cancellationToken);
                createdOutputStoreId = store.StoreId;
            }
            return DreamsGenerationResult.Success(
                ValidDreamMarkdown(),
                "Dreams processed test input.",
                "thread_dream_fake",
                "turn_dream_fake",
                outputStoreId: outputStoreId ?? createdOutputStoreId,
                diagnostics: new DreamsRunDiagnostics(
                    input.Threads.Count,
                    input.Threads.Select(static thread => thread.ThreadId).ToArray(),
                    2,
                    1));
        }

        public Task WaitUntilCalledAsync() => _called.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void Release() => _release.TrySetResult();
    }
}
