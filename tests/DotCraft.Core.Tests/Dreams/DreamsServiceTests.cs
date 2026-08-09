using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Dreams;
using DotCraft.Sessions;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using AgentMessagePayload = DotCraft.Sessions.AgentMessagePayload;
using ThreadSource = DotCraft.Sessions.ThreadSource;
using UserMessagePayload = DotCraft.Sessions.UserMessagePayload;
using Xunit;

namespace DotCraft.Tests.Dreams;

public sealed class DreamsServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _workspace;
    private readonly string _craft;
    private readonly ThreadStore _threadStore;
    private readonly MemoryStore _memoryStore;
    private readonly DreamStore _dreamStore;
    private readonly DreamsStateStore _stateStore;

    public DreamsServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "DreamsService_" + Guid.NewGuid().ToString("N")[..8]);
        _workspace = Path.Combine(_root, "workspace");
        _craft = Path.Combine(_root, ".craft");
        Directory.CreateDirectory(_workspace);
        Directory.CreateDirectory(_craft);
        _threadStore = new ThreadStore(_craft);
        _memoryStore = new MemoryStore(_craft);
        _dreamStore = new DreamStore(_craft);
        _stateStore = new DreamsStateStore(_dreamStore);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch { }
    }

    [Fact]
    public async Task RunOnceAsync_SkipsWithoutCallingRunnerWhenCompletedTurnsBelowThreshold()
    {
        await SaveThreadAsync("thread_one");
        var runner = new FakeRunner();
        var service = CreateService(runner, minCompletedTurns: 5);

        var state = await service.RunOnceAsync(force: false);

        Assert.Equal(DreamsRunStatuses.Skipped, state.Status);
        Assert.Equal("insufficient_new_completed_turns", state.Message);
        Assert.Equal(0, runner.Calls);
        Assert.Null(state.ThreadId);
        Assert.Equal(DreamsConstants.TriggerScheduled, state.Trigger);
        Assert.Equal(string.Empty, _dreamStore.ReadDream());
        Assert.Equal(DreamsRunStatuses.Skipped, _stateStore.Load()?.Status);
    }

    [Fact]
    public async Task RunOnceAsync_ForceCreatesPendingStoreAndState()
    {
        await SaveThreadAsync("thread_one");
        var runner = new FakeRunner
        {
            Result = DreamsGenerationResult.Success(ValidDreamMarkdown(), "Dreams processed one thread.")
        };
        var service = CreateService(runner, minCompletedTurns: 5);

        var state = await service.RunOnceAsync(force: true);

        Assert.Equal(DreamsRunStatuses.Succeeded, state.Status);
        Assert.True(state.DreamWritten);
        Assert.False(state.HistoryWritten);
        Assert.Equal(1, state.CompletedTurnWatermark);
        Assert.Equal(1, state.CandidateThreadCount);
        Assert.Equal("thread_dream_fake", state.ThreadId);
        Assert.Equal("turn_dream_fake", state.TurnId);
        Assert.Equal(DreamsConstants.TriggerManual, state.Trigger);
        Assert.NotNull(state.OutputStoreId);
        Assert.Equal(string.Empty, _dreamStore.ReadDream());
        Assert.Equal(DreamsReviewStatuses.Pending, state.ReviewStatus);
        var persisted = _stateStore.Load();
        Assert.NotNull(persisted);
        Assert.Equal(DreamsRunStatuses.Succeeded, persisted.Status);
        Assert.Equal(["thread_one"], persisted.ProcessedThreadIds);
        Assert.Equal("thread_dream_fake", persisted.ThreadId);
        Assert.Equal("turn_dream_fake", persisted.TurnId);
    }

    [Fact]
    public async Task RunOnceAsync_WithAutoApplyMakesSuccessfulStoreActive()
    {
        await SaveThreadAsync("thread_one");
        var runner = new FakeRunner
        {
            Result = DreamsGenerationResult.Success(ValidDreamMarkdown("- auto"), "Dreams processed one thread.")
        };
        var service = CreateService(runner, minCompletedTurns: 1, autoApply: true);

        var state = await service.RunOnceAsync(force: true);

        Assert.Equal(DreamsRunStatuses.Succeeded, state.Status);
        Assert.Equal(DreamsReviewStatuses.Applied, state.ReviewStatus);
        Assert.True(state.AutoApplied);
        Assert.NotNull(state.OutputStoreId);
        Assert.Equal(state.OutputStoreId, _dreamStore.GetActiveStoreId());
        Assert.Contains("- auto", _dreamStore.ReadDream(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AutoApplyDoesNotRetroactivelyApplyExistingPendingRun()
    {
        await SaveThreadAsync("thread_one");
        var pendingRunner = new FakeRunner
        {
            Result = DreamsGenerationResult.Success(ValidDreamMarkdown("- pending"), "Dreams processed one thread.")
        };
        var pendingService = CreateService(pendingRunner, minCompletedTurns: 1, autoApply: false);

        var pending = await pendingService.RunOnceAsync(force: true);

        Assert.Equal(DreamsReviewStatuses.Pending, pending.ReviewStatus);
        Assert.Null(_dreamStore.GetActiveStoreId());

        var autoRunner = new FakeRunner
        {
            Result = DreamsGenerationResult.Success(ValidDreamMarkdown("- auto"), "Dreams processed one thread.")
        };
        var autoService = CreateService(autoRunner, minCompletedTurns: 1, autoApply: true);

        var autoApplied = await autoService.RunOnceAsync(force: true);

        var reloadedPending = _stateStore.Load(pending.Id);
        Assert.Equal(DreamsReviewStatuses.Pending, reloadedPending?.ReviewStatus);
        Assert.False(reloadedPending?.AutoApplied);
        Assert.Equal(autoApplied.OutputStoreId, _dreamStore.GetActiveStoreId());
    }

    [Fact]
    public async Task RunOnceAsync_FailedGenerationLeavesExistingDreamUnchanged()
    {
        await SaveThreadAsync("thread_one");
        _dreamStore.SaveDreamRun(ValidDreamMarkdown("- old"), null);
        var runner = new FakeRunner
        {
            Result = DreamsGenerationResult.Failed("provider unavailable")
        };
        var service = CreateService(runner, minCompletedTurns: 1);

        var state = await service.RunOnceAsync(force: false);

        Assert.Equal(DreamsRunStatuses.Failed, state.Status);
        Assert.Equal("provider unavailable", state.Message);
        Assert.Equal("thread_dream_fake", state.ThreadId);
        Assert.Equal("turn_dream_fake", state.TurnId);
        Assert.Equal(DreamsConstants.TriggerScheduled, state.Trigger);
        Assert.Contains("- old", _dreamStore.ReadDream(), StringComparison.Ordinal);
        Assert.Contains("- old", _dreamStore.ReadDream(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunOnceAsync_DoesNotStartDuplicateRun()
    {
        await SaveThreadAsync("thread_one");
        var runner = new FakeRunner
        {
            BlockUntilReleased = true,
            Result = DreamsGenerationResult.Success(ValidDreamMarkdown(), "Dreams processed one thread.")
        };
        var service = CreateService(runner, minCompletedTurns: 1);

        var first = service.RunOnceAsync(force: true);
        await runner.WaitUntilCalledAsync();
        var second = await service.RunOnceAsync(force: true);
        runner.Release();
        var firstState = await first;

        Assert.Equal(DreamsRunStatuses.Running, second.Status);
        Assert.Null(second.EndedAt);
        Assert.Equal(DreamsConstants.TriggerManual, second.Trigger);
        Assert.Equal(DreamsRunStatuses.Succeeded, firstState.Status);
        Assert.Equal(DreamsRunStatuses.Succeeded, _stateStore.Load()?.Status);
        Assert.Equal(1, runner.Calls);
    }

    [Fact]
    public async Task StartAsync_WithFutureNextRunAtDoesNotRunBeforeDueTime()
    {
        await SaveThreadAsync("thread_one");
        var future = DateTimeOffset.UtcNow.AddHours(12);
        SaveSchedulerState("dream_previous", future);
        var runner = new FakeRunner();
        var service = CreateService(runner, minCompletedTurns: 1, interval: TimeSpan.FromHours(12));

        await service.StartAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(150));
        await service.StopAsync();

        Assert.Equal(0, runner.Calls);
        Assert.Equal("dream_previous", _stateStore.Load()?.Id);
    }

    [Fact]
    public async Task StartAsync_AfterScheduledRunPersistsCadenceAcrossRestart()
    {
        await SaveThreadAsync("thread_one");
        var firstRunner = new FakeRunner
        {
            Result = DreamsGenerationResult.Success(ValidDreamMarkdown(), "Dreams processed one thread.")
        };
        var interval = TimeSpan.FromHours(12);
        var firstService = CreateService(firstRunner, minCompletedTurns: 1, interval: interval);

        await firstService.StartAsync();
        var firstState = await WaitForLatestStateAsync(state => state.Status == DreamsRunStatuses.Succeeded);
        await firstService.StopAsync();

        Assert.NotNull(firstState.NextRunAt);
        Assert.True(firstState.NextRunAt > DateTimeOffset.UtcNow);

        var secondRunner = new FakeRunner();
        var secondService = CreateService(secondRunner, minCompletedTurns: 1, interval: interval);
        await secondService.StartAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(150));
        await secondService.StopAsync();

        Assert.Equal(0, secondRunner.Calls);
        Assert.Equal(firstState.Id, _stateStore.Load()?.Id);
    }

    [Fact]
    public async Task StartAsync_SkippedScheduledRunPersistsFutureNextRunAt()
    {
        await SaveThreadAsync("thread_one");
        var runner = new FakeRunner();
        var service = CreateService(
            runner,
            minCompletedTurns: 5,
            interval: TimeSpan.FromHours(12));

        await service.StartAsync();
        var state = await WaitForLatestStateAsync(state => state.Status == DreamsRunStatuses.Skipped);
        await service.StopAsync();

        Assert.Equal("insufficient_new_completed_turns", state.Message);
        Assert.Equal(0, runner.Calls);
        Assert.NotNull(state.NextRunAt);
        Assert.True(state.NextRunAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task StartAsync_WithPastNextRunAtRunsOnStartup()
    {
        await SaveThreadAsync("thread_one");
        SaveSchedulerState("dream_previous", DateTimeOffset.UtcNow.AddMinutes(-1));
        var runner = new FakeRunner
        {
            Result = DreamsGenerationResult.Success(ValidDreamMarkdown(), "Dreams processed one thread.")
        };
        var service = CreateService(runner, minCompletedTurns: 1, interval: TimeSpan.FromHours(12));

        await service.StartAsync();
        await runner.WaitUntilCalledAsync();
        await service.StopAsync();

        Assert.Equal(1, runner.Calls);
        Assert.NotEqual("dream_previous", _stateStore.Load()?.Id);
    }

    private DreamsService CreateService(
        FakeRunner runner,
        int minCompletedTurns,
        bool autoApply = false,
        TimeSpan? interval = null,
        TimeSpan? startupDelay = null)
    {
        runner.Store ??= _dreamStore;
        var config = new AppConfig
        {
            Dreams = new DreamsConfig
            {
                Enabled = true,
                MinCompletedTurnsSinceLastRun = minCompletedTurns,
                ThreadLookbackCount = 20,
                AutoApply = autoApply,
                HistoryTailChars = 20_000,
                Interval = interval ?? TimeSpan.FromHours(24),
                StartupDelay = startupDelay ?? TimeSpan.Zero
            }
        };
        var collector = new DreamsInputCollector(config, _workspace, _memoryStore, _dreamStore, _threadStore);
        return new DreamsService(config, collector, runner, _dreamStore, _stateStore);
    }

    private void SaveSchedulerState(string id, DateTimeOffset nextRunAt)
    {
        var now = DateTimeOffset.UtcNow;
        _stateStore.Save(new DreamsRunState
        {
            Id = id,
            Status = DreamsRunStatuses.Skipped,
            StartedAt = now.AddMinutes(-10),
            EndedAt = now.AddMinutes(-10),
            Message = "test_state",
            NextRunAt = nextRunAt,
            Trigger = DreamsConstants.TriggerScheduled
        });
    }

    private async Task<DreamsRunState> WaitForLatestStateAsync(Func<DreamsRunState, bool> predicate)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!cts.IsCancellationRequested)
        {
            var state = _stateStore.Load();
            if (state != null && predicate(state))
                return state;
            await Task.Delay(TimeSpan.FromMilliseconds(25), cts.Token);
        }

        throw new TimeoutException("Timed out waiting for Dreams state.");
    }

    private async Task SaveThreadAsync(string id)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new SessionItem
        {
            Id = "item_user",
            TurnId = "turn_001",
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            Payload = new UserMessagePayload { Text = "Please design Dreams." },
            CreatedAt = now
        };
        var assistant = new SessionItem
        {
            Id = "item_agent",
            TurnId = "turn_001",
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            Payload = new AgentMessagePayload { Text = "Dreams will write passive memory." },
            CreatedAt = now
        };
        await _threadStore.SaveThreadAsync(new SessionThread
        {
            Id = id,
            WorkspacePath = _workspace,
            OriginChannel = "cli",
            DisplayName = "Dreams design",
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

    private static string ValidDreamMarkdown(string focus = "- Dreams organizes passive workspace memory.") =>
        $$"""
        # Dream Memory

        Generated by scheduled Dreams from recent workspace sessions. Treat as inferred background context, not explicit user instruction.

        ## Workspace Focus
        {{focus}}

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

        public DreamsGenerationResult Result { get; init; } =
            DreamsGenerationResult.Success(ValidDreamMarkdown(), "Dreams processed test input.");

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
            LastRunId = runId;
            LastTrigger = trigger;
            string? createdOutputStoreId = null;
            if (Store != null && Result.Succeeded && !string.IsNullOrWhiteSpace(Result.DreamMarkdown))
            {
                var store = Store.CreateOutputStore(runId, DateTimeOffset.UtcNow);
                await File.WriteAllTextAsync(store.IndexPath, Result.DreamMarkdown, cancellationToken);
                createdOutputStoreId = store.StoreId;
            }
            return Result with
            {
                ThreadId = Result.ThreadId ?? "thread_dream_fake",
                TurnId = Result.TurnId ?? "turn_dream_fake",
                OutputStoreId = Result.OutputStoreId ?? outputStoreId ?? createdOutputStoreId,
                Diagnostics = Result.Diagnostics
                    ?? new DreamsRunDiagnostics(input.Threads.Count, [], 0, 0)
            };
        }

        public string? LastRunId { get; private set; }

        public string? LastTrigger { get; private set; }

        public Task WaitUntilCalledAsync() => _called.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void Release() => _release.TrySetResult();
    }
}
