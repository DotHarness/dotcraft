using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Security.ShellCommands;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceInterruptedTurnRecoveryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        "InterruptedTurnRecovery_" + Guid.NewGuid().ToString("N")[..8]);
    private string CraftPath => Path.Combine(_tempDir, ".craft");

    public SessionServiceInterruptedTurnRecoveryTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
        }
    }

    [Theory]
    [InlineData(TurnStatus.Running)]
    [InlineData(TurnStatus.WaitingApproval)]
    [InlineData(TurnStatus.WaitingInput)]
    public async Task EnsureThreadLoadedAsync_ColdLoadCommitsInterruptedTurn(TurnStatus initialStatus)
    {
        var (threadId, expectedItemIds, expectedUsage, expectedError) = await SeedThreadAsync(initialStatus);
        var chatClient = new StaticChatClient("unused");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(agentFactory, chatClient);

        var loaded = await service.EnsureThreadLoadedAsync(threadId);

        AssertRecoveredTurn(
            Assert.Single(loaded.Turns),
            expectedItemIds,
            expectedUsage,
            expectedError);

        await using var freshStore = new ThreadStore(CraftPath);
        var persisted = Assert.IsType<SessionThread>(await freshStore.LoadThreadAsync(threadId));
        AssertRecoveredTurn(
            Assert.Single(persisted.Turns),
            expectedItemIds,
            expectedUsage,
            expectedError);

        var turns = await freshStore.ListThreadTurnsAsync(
            threadId,
            cursor: null,
            limit: 10,
            ThreadHistorySortDirection.Ascending);
        AssertRecoveredTurn(
            Assert.Single(turns.Data),
            expectedItemIds: [],
            expectedUsage,
            expectedError,
            expectItems: false);

        var items = await freshStore.ListThreadItemsAsync(
            threadId,
            turnId: "turn_001",
            cursor: null,
            limit: 10,
            ThreadHistorySortDirection.Ascending);
        Assert.Equal(expectedItemIds, items.Data.Select(static entry => entry.Item.Id));
        Assert.DoesNotContain(items.Data, static entry =>
            entry.Item.Type is ItemType.ApprovalResponse or ItemType.UserInputResponse or ItemType.Error);

        var rolloutPath = Assert.Single(Directory.EnumerateFiles(
            Path.Combine(CraftPath, "threads", "active"),
            "*.jsonl"));
        Assert.Contains(
            "\"status\":\"Cancelled\"",
            await File.ReadAllTextAsync(rolloutPath),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureThreadLoadedAsync_SettlesCommandsWhoseTerminalsNoLongerRun()
    {
        await using var terminals = new BackgroundTerminalService(
            CraftPath,
            new AppConfig.ShellBackgroundConfig { DefaultReadMaxOutputChars = 4 });
        var finished = await terminals.StartAsync(new BackgroundTerminalStartRequest
        {
            ThreadId = "thread_terminals",
            Command = "echo finished",
            Shell = FastShell(),
            WorkingDirectory = _tempDir,
            TimeoutSeconds = 30
        });
        var running = await terminals.StartAsync(new BackgroundTerminalStartRequest
        {
            ThreadId = "thread_terminals",
            Command = OperatingSystem.IsWindows() ? "ping -n 30 127.0.0.1 >nul" : "sleep 30",
            Shell = FastShell(),
            WorkingDirectory = _tempDir,
            RunInBackground = true,
            YieldTimeMs = 100
        });
        var threadId = await SeedCompletedTurnWithCommandsAsync(finished.SessionId, "term_missing", running.SessionId);
        var chatClient = new StaticChatClient("unused");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = new SessionService(
            agentFactory,
            chatClient.AsAIAgent(),
            new SessionPersistenceService(new ThreadStore(CraftPath)),
            new SessionGate(),
            backgroundTerminalService: terminals);

        await service.EnsureThreadLoadedAsync(threadId);

        await using var freshStore = new ThreadStore(CraftPath);
        var items = Assert.Single(Assert.IsType<SessionThread>(await freshStore.LoadThreadAsync(threadId)).Turns).Items;
        var payloads = items.Select(static item => Assert.IsType<CommandExecutionPayload>(item.Payload)).ToArray();
        Assert.Equal(["completed", "lost", "inProgress"], payloads.Select(static payload => payload.Status));
        Assert.True(payloads[0].Truncated);
        Assert.True(payloads[0].OriginalOutputChars >= "finished".Length);
        Assert.Contains($"Full output: {finished.OutputPath}", payloads[0].AggregatedOutput);
        Assert.Equal([ItemStatus.Completed, ItemStatus.Completed, ItemStatus.Started], items.Select(static item => item.Status));
        await terminals.StopAsync(running.SessionId);
    }

    private static ShellIdentity FastShell()
    {
        Assert.True(ShellIdentityResolver.Host.TryResolve(OperatingSystem.IsWindows() ? "cmd" : "sh", out var shell, out var reason), reason);
        return shell;
    }

    private async Task<string> SeedCompletedTurnWithCommandsAsync(params string[] sessionIds)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var thread = new SessionThread
        {
            Id = SessionIdGenerator.NewThreadId(),
            WorkspacePath = _tempDir,
            UserId = "u",
            OriginChannel = "test",
            DisplayName = "Orphaned command test",
            Status = ThreadStatus.Active,
            CreatedAt = now,
            LastActiveAt = now,
            HistoryMode = HistoryMode.Server,
            ProviderHistorySchemaVersion = ProviderHistorySchema.CurrentSchemaVersion
        };
        var turn = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = thread.Id,
            Status = TurnStatus.Completed,
            StartedAt = now,
            CompletedAt = now,
            Items = sessionIds.Select((sessionId, index) => new SessionItem
            {
                Id = $"item_00{index + 1}",
                TurnId = "turn_001",
                Type = ItemType.CommandExecution,
                Status = ItemStatus.Started,
                CreatedAt = now,
                Payload = new CommandExecutionPayload
                {
                    CallId = $"call_{index + 1}",
                    Command = "command",
                    WorkingDirectory = _tempDir,
                    Status = "inProgress",
                    SessionId = sessionId
                }
            }).ToList()
        };

        await using var store = new ThreadStore(CraftPath);
        await store.SaveThreadAsync(thread);
        thread.Turns.Add(turn);
        await store.SaveTurnAsync(thread, turn);
        return thread.Id;
    }

    [Fact]
    public async Task UnloadedThread_ReadsInterruptedTurnAsCancelledWithoutPersisting()
    {
        var (threadId, _, _, _) = await SeedThreadAsync(TurnStatus.WaitingApproval);
        var chatClient = new StaticChatClient("unused");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(agentFactory, chatClient);

        var snapshot = await service.ReadThreadSnapshotAsync(threadId);
        var turns = await service.ListThreadTurnsAsync(threadId, cursor: null, limit: 10, ThreadHistorySortDirection.Ascending);

        Assert.False(snapshot.PersistedRuntime.Running);
        Assert.True(snapshot.PersistedRuntime.QueuePaused);
        Assert.Equal(TurnStatus.Cancelled, Assert.Single(turns.Data).Status);
        Assert.Null(service.TryGetLoadedThread(threadId));
        await using var freshStore = new ThreadStore(CraftPath);
        Assert.Equal(
            TurnStatus.WaitingApproval,
            Assert.Single((await freshStore.LoadThreadAsync(threadId))!.Turns).Status);
    }

    [Fact]
    public async Task SubscribeThreadAsync_ObservesLaterEventsWithoutLoadingThread()
    {
        var (threadId, _, _, _) = await SeedThreadAsync(TurnStatus.Running);
        var chatClient = new StaticChatClient("unused");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(agentFactory, chatClient);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var events = service.SubscribeThreadAsync(threadId, ct: timeout.Token)
            .GetAsyncEnumerator(timeout.Token);

        var first = events.MoveNextAsync();
        Assert.Null(service.TryGetLoadedThread(threadId));
        await service.ResumeThreadAsync(threadId);

        Assert.True(await first);
        Assert.Equal(SessionEventType.TurnCancelled, events.Current.EventType);
        Assert.True(await events.MoveNextAsync());
        Assert.Equal(SessionEventType.ThreadResumed, events.Current.EventType);
    }

    [Fact]
    public async Task SubscribeThreadAsync_EndsForMissingThread()
    {
        var chatClient = new StaticChatClient("unused");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(agentFactory, chatClient);

        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
        {
            await foreach (var _ in service.SubscribeThreadAsync("thread_missing"))
            {
            }
        });
    }

    [Fact]
    public async Task ResumeThreadAsync_PublishesInterruptedCancellationBeforeResumed()
    {
        var (threadId, _, _, _) = await SeedThreadAsync(TurnStatus.Running);
        var chatClient = new StaticChatClient("unused");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(agentFactory, chatClient);

        var resumed = await service.ResumeThreadAsync(threadId);
        var events = await ReadReplayEventsAsync(service, threadId, expectedCount: 2);

        Assert.Equal(TurnStatus.Cancelled, Assert.Single(resumed.Turns).Status);
        Assert.Equal(
            [SessionEventType.TurnCancelled, SessionEventType.ThreadResumed],
            events.Select(static evt => evt.EventType));
        var cancelled = Assert.IsType<TurnCancelledPayload>(events[0].Payload);
        Assert.Equal("interrupted", cancelled.Reason);
        Assert.Equal(TurnStatus.Cancelled, cancelled.Turn.Status);
    }

    [Fact]
    public async Task CancelTurnAsync_ColdLoadsAndPublishesInterruptedCancellationOnce()
    {
        var (threadId, _, _, _) = await SeedThreadAsync(TurnStatus.WaitingInput);
        var chatClient = new StaticChatClient("unused");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(agentFactory, chatClient);
        var signals = new List<SessionThreadRuntimeSignal>();
        service.ThreadRuntimeSignalForBroadcast = (id, signal, _) =>
        {
            if (id == threadId)
                signals.Add(signal);
        };

        await service.CancelTurnAsync(threadId, "turn_001");
        await service.EnsureThreadLoadedAsync(threadId);
        await service.ResumeThreadAsync(threadId);
        await service.CancelTurnAsync(threadId, "turn_001");

        Assert.Equal([SessionThreadRuntimeSignal.TurnCancelled], signals);
        var cancelled = Assert.Single(
            await ReadReplayEventsUntilQuietAsync(service, threadId),
            static evt => evt.EventType == SessionEventType.TurnCancelled);
        Assert.Equal("interrupted", Assert.IsType<TurnCancelledPayload>(cancelled.Payload).Reason);

        await using var freshStore = new ThreadStore(CraftPath);
        Assert.Equal(
            TurnStatus.Cancelled,
            Assert.Single((await freshStore.LoadThreadAsync(threadId))!.Turns).Status);
    }

    [Fact]
    public async Task ConcurrentColdLoads_CommitAndPublishInterruptedCancellationOnce()
    {
        var (threadId, _, _, _) = await SeedThreadAsync(TurnStatus.Running);
        var chatClient = new StaticChatClient("unused");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(agentFactory, chatClient);
        var cancelledSignals = 0;
        service.ThreadRuntimeSignalForBroadcast = (id, signal, _) =>
        {
            if (id == threadId && signal == SessionThreadRuntimeSignal.TurnCancelled)
                Interlocked.Increment(ref cancelledSignals);
        };

        using var persistenceGate = await ThreadRolloutWriteGate.AcquireAsync(CraftPath, threadId);
        using var ready = new CountdownEvent(3);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ensureTask = StartConcurrentLoadAsync(() => service.EnsureThreadLoadedAsync(threadId));
        var readTask = StartConcurrentLoadAsync(() => service.GetThreadAsync(threadId));
        var cancelTask = StartConcurrentLoadAsync(async () =>
        {
            await service.CancelTurnAsync(threadId, "turn_001");
            return await service.GetThreadAsync(threadId);
        });

        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
        start.SetResult();
        await Task.Delay(100);
        persistenceGate.Dispose();

        var loaded = await Task.WhenAll(ensureTask, readTask, cancelTask);
        Assert.All(loaded, thread => Assert.Same(loaded[0], thread));
        Assert.Equal(TurnStatus.Cancelled, Assert.Single(loaded[0].Turns).Status);
        Assert.Equal(1, Volatile.Read(ref cancelledSignals));
        Assert.Equal(0, service.ThreadLoadGateCount);

        var replayed = await ReadSingleReplayEventAndAssertQuietAsync(service, threadId);
        var cancelled = Assert.IsType<TurnCancelledPayload>(replayed.Payload);
        Assert.Equal(SessionEventType.TurnCancelled, replayed.EventType);
        Assert.Equal("interrupted", cancelled.Reason);

        await using var freshStore = new ThreadStore(CraftPath);
        var history = await freshStore.LoadModelHistoryAsync(threadId);
        Assert.Equal(["original request", "partial response"], history.Select(MessageText));

        Task<SessionThread> StartConcurrentLoadAsync(Func<Task<SessionThread>> load) => Task.Run(async () =>
        {
            ready.Signal();
            await start.Task;
            return await load();
        });
    }

    [Fact]
    public async Task ColdLoadFailure_ReleasesGate()
    {
        var chatClient = new StaticChatClient("unused");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(agentFactory, chatClient);

        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => service.GetThreadAsync(SessionIdGenerator.NewThreadId()));
            Assert.Equal(0, service.ThreadLoadGateCount);
        }
    }

    [Fact]
    public async Task CancelledColdLoadWaiter_DoesNotReleaseActiveGate()
    {
        var (threadId, _, _, _) = await SeedThreadAsync(TurnStatus.Running);
        var chatClient = new StaticChatClient("unused");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(agentFactory, chatClient);
        var cancelledSignals = 0;
        service.ThreadRuntimeSignalForBroadcast = (id, signal, _) =>
        {
            if (id == threadId && signal == SessionThreadRuntimeSignal.TurnCancelled)
                Interlocked.Increment(ref cancelledSignals);
        };

        using var persistenceGate = await ThreadRolloutWriteGate.AcquireAsync(CraftPath, threadId);
        var leaderTask = service.GetThreadAsync(threadId);
        Assert.Equal(1, service.ThreadLoadGateCount);

        using var waiterCancellation = new CancellationTokenSource();
        var waiterTask = service.GetThreadAsync(threadId, waiterCancellation.Token);
        waiterCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiterTask);
        Assert.Equal(1, service.ThreadLoadGateCount);

        persistenceGate.Dispose();
        var loaded = await leaderTask;

        Assert.Equal(TurnStatus.Cancelled, Assert.Single(loaded.Turns).Status);
        Assert.Equal(1, Volatile.Read(ref cancelledSignals));
        Assert.Equal(0, service.ThreadLoadGateCount);
    }

    [Fact]
    public async Task InterruptedTurn_AllowsNextTurnAndRecoveryExport()
    {
        var (threadId, _, _, _) = await SeedThreadAsync(TurnStatus.WaitingApproval);
        var chatClient = new StaticChatClient("continued");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(agentFactory, chatClient);

        await service.EnsureThreadLoadedAsync(threadId);
        await DrainAsync(service.SubmitInputAsync(threadId, [new TextContent("continue")]));
        var recovery = await service.ExportThreadRecoveryAsync(threadId);

        var turns = await service.ReadThreadTurnsAsync(threadId, ThreadHistorySortDirection.Ascending).ToListAsync();
        Assert.Equal([TurnStatus.Cancelled, TurnStatus.Completed], turns.Select(static turn => turn.Status));
        Assert.True(File.Exists(recovery.PackagePath));
        Assert.Equal("turn_002", recovery.TerminalTurnId);
    }

    [Fact]
    public async Task LoadedRunningTurn_IsNotRecoveredByEnsureOrResume()
    {
        var chatClient = new BlockingChatClient();
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(agentFactory, chatClient);
        var thread = await service.CreateThreadAsync(CreateIdentity());

        var turnTask = DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("block")]));
        await chatClient.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(TurnStatus.Running, Assert.Single((await service.EnsureThreadLoadedAsync(thread.Id)).Turns).Status);
        Assert.Equal(TurnStatus.Running, Assert.Single((await service.ResumeThreadAsync(thread.Id)).Turns).Status);

        await service.CancelTurnAsync(thread.Id, "turn_001");
        await turnTask;
        Assert.Equal(TurnStatus.Cancelled, Assert.Single((await service.GetThreadAsync(thread.Id)).Turns).Status);
    }

    [Fact]
    public async Task IdleThread_UnloadsAndReloadsOnNextUse()
    {
        var chatClient = new StaticChatClient("done");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(agentFactory, chatClient);
        var thread = await service.CreateThreadAsync(CreateIdentity());
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("hello")]));
        var delay = SessionService.ThreadIdleUnloadDelay;

        Assert.Equal(0, await service.UnloadIdleThreadsAsync(DateTimeOffset.UtcNow, delay));
        Assert.Equal(1, await service.UnloadIdleThreadsAsync(DateTimeOffset.UtcNow + delay, delay));
        Assert.Null(service.TryGetLoadedThread(thread.Id));

        await service.EnsureThreadLoadedAsync(thread.Id);
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("again")]));
        Assert.Equal(
            [TurnStatus.Completed, TurnStatus.Completed],
            (await service.ReadThreadTurnsAsync(thread.Id, ThreadHistorySortDirection.Ascending).ToListAsync())
            .Select(static turn => turn.Status));
    }

    [Fact]
    public async Task IdleThread_StaysLoadedWhileSubscribedAndEventsSurviveUnload()
    {
        var chatClient = new StaticChatClient("done");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(agentFactory, chatClient);
        var thread = await service.CreateThreadAsync(CreateIdentity());
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("hello")]));
        var delay = SessionService.ThreadIdleUnloadDelay;
        var later = DateTimeOffset.UtcNow + delay;

        using (var subscription = new CancellationTokenSource())
        {
            await using var events = service.SubscribeThreadAsync(thread.Id, ct: subscription.Token)
                .GetAsyncEnumerator(subscription.Token);
            var pending = events.MoveNextAsync();
            Assert.Equal(0, await service.UnloadIdleThreadsAsync(later, delay));
            await subscription.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        }

        Assert.Equal(1, await service.UnloadIdleThreadsAsync(DateTimeOffset.UtcNow + delay, delay));
        using var observe = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var observer = service.SubscribeThreadAsync(thread.Id, ct: observe.Token).GetAsyncEnumerator(observe.Token);
        var resumed = observer.MoveNextAsync();
        await service.ResumeThreadAsync(thread.Id);
        Assert.True(await resumed);
        Assert.Equal(SessionEventType.ThreadResumed, observer.Current.EventType);
    }

    [Fact]
    public async Task EphemeralThread_IsNeverUnloaded()
    {
        var chatClient = new StaticChatClient("done");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(agentFactory, chatClient);
        var thread = await service.CreateThreadAsync(CreateIdentity(), ephemeral: true);
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("hello")]));
        var delay = SessionService.ThreadIdleUnloadDelay;

        Assert.Equal(0, await service.UnloadIdleThreadsAsync(DateTimeOffset.UtcNow + delay, delay));
        Assert.NotNull(service.TryGetLoadedThread(thread.Id));
    }

    private async Task<(string ThreadId, string[] ItemIds, TokenUsageInfo Usage, string Error)> SeedThreadAsync(
        TurnStatus status)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var thread = new SessionThread
        {
            Id = SessionIdGenerator.NewThreadId(),
            WorkspacePath = _tempDir,
            UserId = "u",
            OriginChannel = "test",
            DisplayName = "Interrupted turn test",
            Status = ThreadStatus.Active,
            CreatedAt = now,
            LastActiveAt = now,
            HistoryMode = HistoryMode.Server,
            ProviderHistorySchemaVersion = ProviderHistorySchema.CurrentSchemaVersion
        };
        var input = new SessionItem
        {
            Id = "item_001",
            TurnId = "turn_001",
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = new UserMessagePayload { Text = "original request" }
        };
        var partialResponse = new SessionItem
        {
            Id = "item_002",
            TurnId = "turn_001",
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = new AgentMessagePayload { Text = "partial response" }
        };
        var pending = CreatePendingItem(status, now);
        var usage = new TokenUsageInfo { InputTokens = 13, OutputTokens = 5, TotalTokens = 18 };
        const string existingError = "preserved diagnostic";
        var turn = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = thread.Id,
            Status = status,
            Input = input,
            Items = [input, partialResponse, pending],
            StartedAt = now,
            TokenUsage = usage,
            Error = existingError
        };

        await using var store = new ThreadStore(CraftPath);
        await store.SaveThreadAsync(thread);
        thread.Turns.Add(turn);
        await store.SaveTurnAsync(thread, turn);
        return (thread.Id, turn.Items.Select(static item => item.Id).ToArray(), usage, existingError);
    }

    private static SessionItem CreatePendingItem(TurnStatus status, DateTimeOffset now) => status switch
    {
        TurnStatus.WaitingApproval => new SessionItem
        {
            Id = "item_003",
            TurnId = "turn_001",
            Type = ItemType.ApprovalRequest,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = new ApprovalRequestPayload
            {
                ApprovalType = "shell",
                Operation = "dotnet test",
                Target = ".",
                RequestId = "approval_001"
            }
        },
        TurnStatus.WaitingInput => new SessionItem
        {
            Id = "item_003",
            TurnId = "turn_001",
            Type = ItemType.UserInputRequest,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = new UserInputRequestPayload
            {
                RequestId = "input_001",
                IsBlocking = true,
                Questions = [new RequestUserInputQuestion { Id = "choice", Header = "Choice", Question = "Continue?" }]
            }
        },
        _ => new SessionItem
        {
            Id = "item_003",
            TurnId = "turn_001",
            Type = ItemType.ReasoningContent,
            Status = ItemStatus.Streaming,
            CreatedAt = now,
            Payload = new ReasoningContentPayload { Text = "unfinished reasoning" }
        }
    };

    private static void AssertRecoveredTurn(
        SessionTurn turn,
        IReadOnlyList<string> expectedItemIds,
        TokenUsageInfo expectedUsage,
        string expectedError,
        bool expectItems = true)
    {
        Assert.Equal(TurnStatus.Cancelled, turn.Status);
        Assert.NotNull(turn.CompletedAt);
        Assert.Equal(expectedUsage, turn.TokenUsage);
        Assert.Equal(expectedError, turn.Error);
        if (expectItems)
        {
            Assert.Equal(expectedItemIds, turn.Items.Select(static item => item.Id));
            Assert.DoesNotContain(turn.Items, static item =>
                item.Type is ItemType.ApprovalResponse or ItemType.UserInputResponse or ItemType.Error);
        }
    }

    private SessionService CreateService(AgentFactory agentFactory, IChatClient chatClient) => new(
        agentFactory,
        chatClient.AsAIAgent(),
        new SessionPersistenceService(new ThreadStore(CraftPath)),
        new SessionGate());

    private AgentFactory CreateAgentFactory(IChatClient chatClient)
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        return new AgentFactory(
            dotcraftPath: CraftPath,
            workspacePath: _tempDir,
            config: config,
            memoryStore: new MemoryStore(CraftPath),
            skillsLoader: new SkillsLoader(CraftPath),
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            chatClient: chatClient,
            toolSources: Array.Empty<IToolSource>());
    }

    private SessionIdentity CreateIdentity() => new()
    {
        ChannelName = "test",
        UserId = "u",
        WorkspacePath = _tempDir
    };

    private static async Task<List<SessionEvent>> ReadReplayEventsAsync(
        SessionService service,
        string threadId,
        int expectedCount)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var events = service.SubscribeThreadAsync(threadId, replayRecent: true, timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        var result = new List<SessionEvent>();
        for (var i = 0; i < expectedCount; i++)
        {
            Assert.True(await events.MoveNextAsync());
            result.Add(events.Current);
        }

        return result;
    }

    private static async Task<List<SessionEvent>> ReadReplayEventsUntilQuietAsync(
        SessionService service,
        string threadId)
    {
        using var timeout = new CancellationTokenSource();
        await using var events = service.SubscribeThreadAsync(threadId, replayRecent: true, timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        var result = new List<SessionEvent>();
        Assert.True(await events.MoveNextAsync());
        result.Add(events.Current);
        Assert.True(await events.MoveNextAsync());
        result.Add(events.Current);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await events.MoveNextAsync().AsTask());
        return result;
    }

    private static async Task<SessionEvent> ReadSingleReplayEventAndAssertQuietAsync(
        SessionService service,
        string threadId)
    {
        using var timeout = new CancellationTokenSource();
        await using var events = service.SubscribeThreadAsync(threadId, replayRecent: true, timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        Assert.True(await events.MoveNextAsync());
        var result = events.Current;
        timeout.CancelAfter(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await events.MoveNextAsync().AsTask());
        return result;
    }

    private static string MessageText(ChatMessage message) =>
        string.Concat(message.Contents.OfType<TextContent>().Select(static content => content.Text));

    private static async Task DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    private sealed class StaticChatClient(string response) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(response)]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class BlockingChatClient : IChatClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "unused")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
