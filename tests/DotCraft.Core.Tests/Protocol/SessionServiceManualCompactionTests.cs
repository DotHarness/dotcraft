using System.Text.Json;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tracing;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceManualCompactionTests : IDisposable
{
    private readonly string _tempDir;

    public SessionServiceManualCompactionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ManualCompact_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task CompactThreadAsync_Success_EmitsEventsPersistsNoticeAndUpdatesUsage()
    {
        var mainChat = new StreamingReplyChatClient("ok");
        var summaryChat = new SummaryChatClient("<summary>older context summary</summary>");
        await using var agentFactory = CreateAgentFactory(summaryChat);
        var service = CreateService(agentFactory, mainChat);
        var thread = await service.CreateThreadAsync(MakeIdentity(), threadId: "thread-compact");

        for (var i = 0; i < 4; i++)
        {
            await DrainAsync(service.SubmitInputAsync(
                thread.Id,
                [new TextContent($"turn {i} " + new string('u', 1200))]));
        }

        var result = await service.CompactThreadAsync(thread.Id);
        Assert.True(result.Outcome == "partial", result.Message ?? result.Outcome);
        var events = await CollectThreadEventsAsync(
            service,
            thread.Id,
            replayRecent: true,
            events => events.Any(e => IsSystemEvent(e, "compacted"))
                && events.Any(IsManualCompactionNotice));

        Assert.NotNull(result.ContextUsage);
        var resultUsage = result.ContextUsage!;
        Assert.True(resultUsage.Tokens > 0);
        var compactingEvent = Assert.Single(
            events.Select(e => e.SystemEventPayload),
            payload => payload?.Kind == "compacting");
        Assert.Null(compactingEvent!.ContextUsage);
        Assert.Contains(events, e => IsSystemEvent(e, "compacted"));
        Assert.Contains(events, IsManualCompactionNotice);
        var compactedEvent = Assert.Single(
            events.Select(e => e.SystemEventPayload),
            payload => payload?.Kind == "compacted");
        Assert.NotNull(compactedEvent);
        Assert.NotNull(compactedEvent!.ContextUsage);
        var compactedUsage = compactedEvent.ContextUsage!;
        Assert.Equal(resultUsage.Tokens, compactedUsage.Tokens);
        Assert.Equal(resultUsage.PercentLeft, compactedUsage.PercentLeft);

        var reloaded = await service.GetThreadAsync(thread.Id);
        var notice = reloaded.Turns
            .SelectMany(t => t.Items)
            .Select(i => i.Payload)
            .OfType<SystemNoticePayload>()
            .Single(p => p.Kind == "compacted" && p.Trigger == "manual");
        Assert.Equal("partial", notice.Mode);
        Assert.Equal(resultUsage.Tokens, notice.TokensAfter);

        var wireJson = JsonSerializer.Serialize(reloaded.ToWire(includeTurns: true), SessionWireJsonOptions.Default);
        Assert.DoesNotContain("replacementHistory", wireJson, StringComparison.Ordinal);
        Assert.DoesNotContain("contextCompacted", wireJson, StringComparison.Ordinal);

        var rolloutJson = await File.ReadAllTextAsync(Path.Combine(
            _tempDir,
            "threads",
            "active",
            $"{thread.Id}.jsonl"));
        Assert.Contains("context_compacted", rolloutJson, StringComparison.Ordinal);
        Assert.Contains("replacementHistory", rolloutJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompactThreadAsync_ShortHistoryFallsBackToFullCompaction()
    {
        var mainChat = new StreamingReplyChatClient("ok");
        await using var agentFactory = CreateAgentFactory(
            new SummaryChatClient("<summary>short context summary</summary>"));
        var service = CreateService(agentFactory, mainChat);
        var thread = await service.CreateThreadAsync(MakeIdentity(), threadId: "thread-short");

        await DrainAsync(service.SubmitInputAsync(
            thread.Id,
            [new TextContent("single turn " + new string('u', 1200))]));

        var result = await service.CompactThreadAsync(thread.Id);
        Assert.Equal("partial", result.Outcome);
        Assert.Null(result.Message);
        Assert.NotNull(result.ContextUsage);

        var events = await CollectThreadEventsAsync(
            service,
            thread.Id,
            replayRecent: true,
            events => events.Any(e => IsSystemEvent(e, "compacted"))
                && events.Any(IsManualCompactionNotice));

        Assert.Contains(events, e => IsSystemEvent(e, "compacting"));
        Assert.Contains(events, e => IsSystemEvent(e, "compacted"));
        Assert.Contains(events, IsManualCompactionNotice);

        var reloaded = await service.GetThreadAsync(thread.Id);
        var notice = reloaded.Turns
            .SelectMany(t => t.Items)
            .Select(i => i.Payload)
            .OfType<SystemNoticePayload>()
            .Single(p => p.Kind == "compacted" && p.Trigger == "manual");
        Assert.Equal("partial", notice.Mode);
        Assert.Equal(result.ContextUsage!.Tokens, notice.TokensAfter);
    }

    [Fact]
    public async Task CompactThreadAsync_WithoutSnapshotRebuildsCurrentToolsForLegacyCompaction()
    {
        var summaryChat = new SummaryChatClient("<summary>restart summary</summary>");
        await using var agentFactory = CreateAgentFactory(
            summaryChat,
            toolProviders: [new TestToolProvider()]);
        var service = CreateService(agentFactory, new StreamingReplyChatClient("ok"));
        var thread = await service.CreateThreadAsync(
            MakeIdentity(),
            new ThreadConfiguration { ToolAllowList = ["GetStatus"] },
            threadId: "thread-restart-compact");
        var now = DateTimeOffset.UtcNow;
        var turn = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = thread.Id,
            Status = TurnStatus.Completed,
            StartedAt = now,
            CompletedAt = now
        };
        var userItem = new SessionItem
        {
            Id = "item_001",
            TurnId = turn.Id,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = new UserMessagePayload { Text = "user " + new string('u', 1200) }
        };
        turn.Input = userItem;
        turn.Items.Add(userItem);
        turn.Items.Add(new SessionItem
        {
            Id = "item_002",
            TurnId = turn.Id,
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = new AgentMessagePayload { Text = "assistant " + new string('a', 1200) }
        });
        thread.Turns.Add(turn);

        var result = await service.CompactThreadAsync(thread.Id);

        Assert.Equal("partial", result.Outcome);
        var capturedTool = Assert.Single(summaryChat.Options?.Tools ?? []);
        Assert.Equal("GetStatus", capturedTool.Name);
        Assert.Null(summaryChat.Options?.ToolMode);
    }

    [Fact]
    public async Task CompactThreadAsync_RebasesInvalidSnapshotForManualFork()
    {
        var traceStore = new TraceStore();
        var traceCollector = new TraceCollector(traceStore);
        var summaryChat = new SummaryChatClient("<summary>rebased summary</summary>");
        await using var agentFactory = CreateAgentFactory(
            summaryChat,
            traceCollector: traceCollector);
        var service = CreateService(
            agentFactory,
            new StreamingReplyChatClient("ok"),
            traceCollector);
        var thread = await service.CreateThreadAsync(MakeIdentity(), threadId: "thread-rebased-snapshot");
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 4; i++)
            AddCompletedTurn(thread, i, now.AddSeconds(i));

        var staleSnapshot = PromptRequestSnapshot.Capture(
            [new ChatMessage(ChatRole.User, "stale prefix that no longer matches")],
            new ChatOptions
            {
                Instructions = "stable base",
                ModelId = "gpt-test"
            },
            providerId: "provider-test",
            mode: "agent",
            threadId: thread.Id,
            turnId: "turn_stale",
            estimatedInputTokens: 1234);
        SetLastPromptRequestSnapshot(service, thread.Id, staleSnapshot);

        var result = await service.CompactThreadAsync(thread.Id);

        Assert.Equal("partial", result.Outcome);
        var request = Assert.Single(
            traceStore.GetEvents(thread.Id),
            e => e.Type == TraceEventType.MaintenanceForkRequest);
        using var metadata = JsonDocument.Parse(request.MetadataJson!);
        var root = metadata.RootElement;
        Assert.Equal("agent", root.GetProperty("mode").GetString());
        Assert.Equal("provider-test", root.GetProperty("providerId").GetString());
        Assert.Equal("gpt-test", root.GetProperty("modelId").GetString());
        Assert.Equal("manual_rebased", root.GetProperty("snapshotSource").GetString());
        Assert.Equal("history_prefix_mismatch", root.GetProperty("snapshotInvalidReason").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("turnId").ValueKind);
    }

    [Fact]
    public async Task CompactThreadAsync_TwoTurnsWithHighPersistedUsage_CompactsWithPartialWireOutcome()
    {
        var mainChat = new StreamingReplyChatClient("ok");
        var summaryChat = new SummaryChatClient("<summary>large context summary</summary>");
        await using var agentFactory = CreateAgentFactory(
            summaryChat,
            compaction =>
            {
                compaction.ContextWindow = 256_000;
                compaction.KeepRecentMinTokens = 10_000;
                compaction.KeepRecentMinGroups = 3;
                compaction.KeepRecentMaxTokens = 40_000;
            });
        var service = CreateService(agentFactory, mainChat);
        var thread = await service.CreateThreadAsync(MakeIdentity(), threadId: "thread-two-turn-high-context");

        for (var i = 0; i < 2; i++)
        {
            await DrainAsync(service.SubmitInputAsync(
                thread.Id,
                [new TextContent($"turn {i} " + new string('u', 1200))]));
        }

        var store = new ThreadStore(_tempDir);
        await store.SaveContextUsageTokensAsync(thread.Id, 190_000);

        var result = await service.CompactThreadAsync(thread.Id);

        Assert.Equal("partial", result.Outcome);
        Assert.NotNull(result.ContextUsage);
        Assert.True(result.ContextUsage!.Tokens < 190_000);
        Assert.True(result.ContextUsage.Tokens < 50_000);

        var reloaded = await service.GetThreadAsync(thread.Id);
        var notice = reloaded.Turns
            .SelectMany(t => t.Items)
            .Select(i => i.Payload)
            .OfType<SystemNoticePayload>()
            .Single(p => p.Kind == "compacted" && p.Trigger == "manual");
        Assert.Equal("partial", notice.Mode);
        Assert.Equal(result.ContextUsage.Tokens, notice.TokensAfter);
    }

    [Fact]
    public async Task CompactThreadAsync_Success_ReleasesStableContextPages()
    {
        var manager = new ContextPageManager();
        var mainChat = new StreamingReplyChatClient("ok");
        var summaryChat = new SummaryChatClient("<summary>older context summary</summary>");
        await using var agentFactory = CreateAgentFactory(summaryChat, contextPageManager: manager);
        var service = CreateService(agentFactory, mainChat);
        var thread = await service.CreateThreadAsync(MakeIdentity(), threadId: "thread-context-pages");
        var key = new ContextPageKey("test", "page", "variant");
        var pageValue = "page-v1";

        Assert.Equal(
            "page-v1",
            manager.GetOrAdd(thread.Id, key, ContextPageLifecycle.StableUntilCompaction, () => pageValue).Content);

        pageValue = "page-v2";

        await DrainAsync(service.SubmitInputAsync(
            thread.Id,
            [new TextContent("turn 0 " + new string('u', 1200))]));

        var result = await service.CompactThreadAsync(thread.Id);

        Assert.Equal("partial", result.Outcome);
        Assert.Equal(
            "page-v2",
            manager.GetOrAdd(thread.Id, key, ContextPageLifecycle.StableUntilCompaction, () => pageValue).Content);
    }

    [Fact]
    public async Task CancelThreadMaintenanceAsync_CancelsManualCompactionWithoutNoticeAndDrainsQueue()
    {
        var mainChat = new StreamingReplyChatClient("ok");
        var summaryChat = new BlockingSummaryChatClient();
        await using var agentFactory = CreateAgentFactory(summaryChat);
        var service = CreateService(agentFactory, mainChat);
        var thread = await service.CreateThreadAsync(MakeIdentity(), threadId: "thread-cancel-compact");

        await DrainAsync(service.SubmitInputAsync(
            thread.Id,
            [new TextContent("turn 0 " + new string('u', 1200))]));

        var subscription = CollectThreadEventsAsync(
            service,
            thread.Id,
            replayRecent: true,
            events => events.Any(e => IsSystemEvent(e, "compactCancelled")));
        var compactTask = service.CompactThreadAsync(thread.Id);

        await summaryChat.Started.WaitAsync(TimeSpan.FromSeconds(5));
        var runtimeDuringMaintenance = service.GetThreadRuntimeSnapshot(await service.GetThreadAsync(thread.Id));
        Assert.True(runtimeDuringMaintenance.Busy);
        Assert.Equal("compacting", runtimeDuringMaintenance.MaintenanceKind);

        await service.EnqueueTurnInputAsync(thread.Id, [new TextContent("run after cancelled compaction")]);

        await service.CancelThreadMaintenanceAsync(thread.Id);
        var result = await compactTask;
        var events = await subscription;

        Assert.Equal("cancelled", result.Outcome);
        Assert.Contains(events, e => IsSystemEvent(e, "compacting"));
        Assert.Contains(events, e => IsSystemEvent(e, "compactCancelled"));
        Assert.DoesNotContain(events, IsManualCompactionNotice);

        await WaitUntilAsync(() =>
            thread.Turns.Count >= 2
            && thread.Turns[1].Status == TurnStatus.Completed
            && thread.QueuedInputs.Count == 0);

        var reloaded = await service.GetThreadAsync(thread.Id);
        var runtimeAfterMaintenance = service.GetThreadRuntimeSnapshot(reloaded);
        Assert.Null(runtimeAfterMaintenance.MaintenanceKind);
        Assert.DoesNotContain(
            reloaded.Turns.SelectMany(turn => turn.Items),
            item => item.Payload is SystemNoticePayload { Kind: "compacted", Trigger: "manual" });
    }

    [Fact]
    public async Task CompactThreadAsync_RejectsEmptyClientManagedAndActiveThreads()
    {
        var mainChat = new BlockingChatClient();
        await using var agentFactory = CreateAgentFactory(new SummaryChatClient("<summary>unused</summary>"));
        var service = CreateService(agentFactory, mainChat);

        var empty = await service.CreateThreadAsync(MakeIdentity(), threadId: "thread-empty");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompactThreadAsync(empty.Id));

        var clientManaged = await service.CreateThreadAsync(
            MakeIdentity(),
            historyMode: HistoryMode.Client,
            threadId: "thread-client");
        clientManaged.Turns.Add(new SessionTurn
        {
            Id = "turn_001",
            ThreadId = clientManaged.Id,
            Status = TurnStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompactThreadAsync(clientManaged.Id));

        var running = await service.CreateThreadAsync(MakeIdentity(), threadId: "thread-running");
        _ = Task.Run(async () =>
        {
            await foreach (var _ in service.SubmitInputAsync(running.Id, [new TextContent("keep running")]))
            {
            }
        });
        await WaitUntilAsync(() => running.Turns.Any(t => t.Status == TurnStatus.Running));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompactThreadAsync(running.Id));
        mainChat.Release();
    }

    private SessionService CreateService(
        AgentFactory agentFactory,
        IChatClient mainChatClient,
        TraceCollector? traceCollector = null)
    {
        var defaultAgent = mainChatClient.AsAIAgent(new ChatClientAgentOptions());
        return new SessionService(
            agentFactory,
            defaultAgent,
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate(),
            traceCollector: traceCollector);
    }

    private AgentFactory CreateAgentFactory(
        IChatClient compactionChatClient,
        Action<CompactionConfig>? configureCompaction = null,
        IContextPageManager? contextPageManager = null,
        IReadOnlyList<IAgentToolProvider>? toolProviders = null,
        TraceCollector? traceCollector = null)
    {
        var compaction = new CompactionConfig
        {
            ContextWindow = 200_000,
            SummaryReserveTokens = 20_000,
            AutoCompactBufferTokens = 13_000,
            WarningBufferTokens = 20_000,
            ErrorBufferTokens = 10_000,
            KeepRecentMinTokens = 1,
            KeepRecentMinGroups = 1,
            KeepRecentMaxTokens = 1_000,
            MicrocompactEnabled = false
        };
        configureCompaction?.Invoke(compaction);
        var config = AppConfigTestFactory.CreateOpenAI();
        config.Compaction = compaction;
        return new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: new MemoryStore(_tempDir),
            skillsLoader: new SkillsLoader(_tempDir),
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            toolProviders: toolProviders ?? Array.Empty<IAgentToolProvider>(),
            traceCollector: traceCollector,
            compactionChatClient: compactionChatClient,
            contextPageManager: contextPageManager);
    }

    private SessionIdentity MakeIdentity() => new()
    {
        WorkspacePath = _tempDir,
        ChannelName = "desktop",
        UserId = "user"
    };

    private static async Task DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    private static async Task<List<SessionEvent>> CollectThreadEventsAsync(
        ISessionService service,
        string threadId,
        bool replayRecent,
        Func<List<SessionEvent>, bool> done,
        CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var collected = new List<SessionEvent>();
        await foreach (var evt in service.SubscribeThreadAsync(threadId, replayRecent, timeout.Token))
        {
            collected.Add(evt);
            if (done(collected))
                break;
        }

        return collected;
    }

    private static bool IsSystemEvent(SessionEvent evt, string kind) =>
        evt.EventType == SessionEventType.SystemEvent
        && evt.Payload is SystemEventPayload payload
        && payload.Kind == kind;

    private static bool IsManualCompactionNotice(SessionEvent evt) =>
        evt.EventType == SessionEventType.ItemCompleted
        && evt.Payload is SessionItem { Payload: SystemNoticePayload { Kind: "compacted", Trigger: "manual" } };

    private static void AddCompletedTurn(SessionThread thread, int index, DateTimeOffset timestamp)
    {
        var turn = new SessionTurn
        {
            Id = $"turn_{index + 1:000}",
            ThreadId = thread.Id,
            Status = TurnStatus.Completed,
            StartedAt = timestamp,
            CompletedAt = timestamp
        };
        var userItem = new SessionItem
        {
            Id = $"item_{index + 1:000}_user",
            TurnId = turn.Id,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = timestamp,
            CompletedAt = timestamp,
            Payload = new UserMessagePayload { Text = $"user {index} " + new string('u', 1200) }
        };
        turn.Input = userItem;
        turn.Items.Add(userItem);
        turn.Items.Add(new SessionItem
        {
            Id = $"item_{index + 1:000}_agent",
            TurnId = turn.Id,
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            CreatedAt = timestamp,
            CompletedAt = timestamp,
            Payload = new AgentMessagePayload { Text = $"assistant {index} " + new string('a', 1200) }
        });
        thread.Turns.Add(turn);
    }

    private static void SetLastPromptRequestSnapshot(
        SessionService service,
        string threadId,
        PromptRequestSnapshot snapshot)
    {
        var runtime = service.DebugGetRuntime(threadId);
        Assert.NotNull(runtime);
        runtime!.LastPromptRequest = snapshot;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(20, cts.Token);
        }
    }

    private sealed class StreamingReplyChatClient(string responseText) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(responseText)]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private sealed class SummaryChatClient(string responseText) : IChatClient
    {
        public ChatOptions? Options { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Options = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private sealed class BlockingSummaryChatClient : IChatClient
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public ChatOptions? Options { get; private set; }

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Options = options;
            _started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "<summary>cancelled</summary>"));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private sealed class TestToolProvider : IAgentToolProvider
    {
        public IEnumerable<AITool> CreateTools(ToolProviderContext context)
        {
            yield return AIFunctionFactory.Create(() => "tool ok", name: "GetStatus", description: "Get status.");
        }
    }

    private sealed class BlockingChatClient : IChatClient
    {
        private readonly TaskCompletionSource _gate = new();

        public void Release() => _gate.TrySetResult();

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var registration = cancellationToken.Register(() => _gate.TrySetCanceled(cancellationToken));
            await _gate.Task;
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() => _gate.TrySetCanceled();
    }
}
