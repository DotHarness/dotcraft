using System.Collections.Concurrent;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceLongHistoryTests : IDisposable
{
    private const int SeededTurns = 5;

    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        "LongHistory_" + Guid.NewGuid().ToString("N")[..8]);

    public SessionServiceLongHistoryTests() => Directory.CreateDirectory(_tempDir);

    private string CraftPath => Path.Combine(_tempDir, ".craft");

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

    [Fact]
    public async Task ColdLoad_KeepsRecentTurnsAndRecoversWithoutReplayingTheRollout()
    {
        var threadId = await SeedThreadAsync();
        await AppendInterruptedTurnAndOrphanedCommandAsync(threadId);
        var store = new ThreadStore(CraftPath);
        var chatClient = new RecordingChatClient();
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(agentFactory, chatClient, store);

        var loaded = await service.EnsureThreadLoadedAsync(threadId);

        Assert.Equal(0, store.ForwardReplayCount);
        var recovered = Assert.Single(loaded.Turns);
        Assert.Equal(("turn_006", TurnStatus.Cancelled), (recovered.Id, recovered.Status));
        Assert.Equal(SeededTurns + 1, loaded.TurnCount);
        var orphaned = await service.ReadThreadItemAsync(threadId, "turn_002", "item_orphan");
        Assert.Equal(ItemStatus.Completed, orphaned!.Status);
        Assert.Equal("lost", Assert.IsType<CommandExecutionPayload>(orphaned.Payload).Status);
        Assert.Equal("request 1", (await service.ReadThreadTurnAsync(threadId, "turn_001"))!.Input!.AsUserMessage!.Text);
        var totals = await service.ReadThreadAggregatesAsync(threadId);
        Assert.Equal(SeededTurns + 1, totals.TurnCount);
        Assert.Equal(SeededTurns * 10, totals.TokenUsage.InputTokens);

        await DrainAsync(service.SubmitInputAsync(threadId, [new TextContent("request 7")]));

        Assert.Equal(0, store.ForwardReplayCount);
        Assert.Equal("turn_007", Assert.Single(loaded.Turns).Id);
        var request = chatClient.Requests[^1];
        foreach (var turn in Enumerable.Range(1, SeededTurns))
            Assert.Contains($"request {turn}", request);
        var expectedIds = Enumerable.Range(1, SeededTurns + 2).Select(static turn => $"turn_{turn:000}").ToArray();
        Assert.Equal(
            expectedIds,
            (await service.ReadThreadTurnsAsync(threadId, ThreadHistorySortDirection.Ascending).ToListAsync())
            .Select(static turn => turn.Id));
        var newestFirst = await service.ReadThreadTurnsAsync(threadId, ThreadHistorySortDirection.Descending).ToListAsync();
        Assert.Equal(expectedIds.Reverse(), newestFirst.Select(static turn => turn.Id));
        Assert.Contains(newestFirst[^2].Items, static item => item.Id == "item_orphan");
    }

    [Fact]
    public async Task Rollback_BeyondTheLoadedTurns_ContinuesFromTheSurvivingHistory()
    {
        var threadId = await SeedThreadAsync();
        var chatClient = new RecordingChatClient();
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(agentFactory, chatClient, new ThreadStore(CraftPath));

        var rolledBack = await service.RollbackThreadAsync(threadId, 3);

        Assert.Equal(2, rolledBack.TurnCount);
        Assert.Equal("turn_002", Assert.Single(rolledBack.Turns).Id);
        await DrainAsync(service.SubmitInputAsync(threadId, [new TextContent("edited")]));
        var request = chatClient.Requests[^1];
        Assert.Contains("request 1", request);
        Assert.Contains("request 2", request);
        Assert.DoesNotContain("request 3", request);
        Assert.DoesNotContain("request 5", request);
        Assert.Equal(
            ["turn_001", "turn_002", "turn_006"],
            (await service.ReadThreadTurnsAsync(threadId, ThreadHistorySortDirection.Ascending).ToListAsync())
            .Select(static turn => turn.Id));
    }

    [Fact]
    public async Task Fork_FromALoadedLongThread_CopiesTheForkedPrefix()
    {
        var threadId = await SeedThreadAsync();
        var chatClient = new RecordingChatClient();
        await using var agentFactory = CreateAgentFactory(chatClient);
        var store = new ThreadStore(CraftPath);
        var service = CreateService(agentFactory, chatClient, store);
        await service.EnsureThreadLoadedAsync(threadId);

        var full = await service.ForkThreadAsync(threadId);
        var prefix = await service.ForkThreadAsync(
            threadId,
            new ThreadForkOptions { ForkPoint = new ThreadForkPoint { TurnId = "turn_002" } });

        Assert.Equal(SeededTurns, full.TurnCount);
        Assert.Equal(
            Enumerable.Range(1, SeededTurns).Select(static turn => $"request {turn}"),
            InputTexts(await store.LoadThreadAsync(full.Id)));
        Assert.Equal(["request 1", "request 2"], InputTexts(await store.LoadThreadAsync(prefix.Id)));

        await DrainAsync(service.SubmitInputAsync(full.Id, [new TextContent("after fork")]));
        foreach (var turn in Enumerable.Range(1, SeededTurns))
            Assert.Contains($"request {turn}", chatClient.Requests[^1]);
    }

    [Fact]
    public async Task SubAgentFork_FromALoadedLongParent_CopiesTheRequestedTurns()
    {
        var threadId = await SeedThreadAsync();
        var chatClient = new RecordingChatClient();
        await using var agentFactory = CreateAgentFactory(chatClient);
        var store = new ThreadStore(CraftPath);
        var service = CreateService(agentFactory, chatClient, store);
        var parent = await service.EnsureThreadLoadedAsync(threadId);
        var context = new SubAgentSessionContext
        {
            SessionService = service,
            ParentThread = parent,
            ParentTurnId = parent.Turns[^1].Id,
            RootThreadId = parent.Id
        };

        var lastTwo = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "some history", TaskName = "last_two", ForkTurns = "2" },
            waitForCompletion: true,
            coordinator: null,
            CancellationToken.None);

        Assert.Equal(
            ["request 4", "request 5"],
            InputTexts(await store.LoadThreadAsync(lastTwo.ChildThreadId)).Take(2));
    }

    private async Task<string> SeedThreadAsync()
    {
        var chatClient = new RecordingChatClient();
        await using var agentFactory = CreateAgentFactory(chatClient);
        var store = new ThreadStore(CraftPath);
        var service = CreateService(agentFactory, chatClient, store);
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "u",
            WorkspacePath = _tempDir
        });
        foreach (var turn in Enumerable.Range(1, SeededTurns))
            await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent($"request {turn}")]));
        Assert.Single(thread.Turns);
        await store.FlushAndCloseAsync();
        return thread.Id;
    }

    private async Task AppendInterruptedTurnAndOrphanedCommandAsync(string threadId)
    {
        await using var store = new ThreadStore(CraftPath);
        var thread = (await store.LoadThreadAsync(threadId))!;
        var now = DateTimeOffset.UtcNow;
        var olderTurn = thread.Turns.Single(static turn => turn.Id == "turn_002");
        olderTurn.Items.Add(new SessionItem
        {
            Id = "item_orphan",
            TurnId = olderTurn.Id,
            Type = ItemType.CommandExecution,
            Status = ItemStatus.Started,
            CreatedAt = now,
            Payload = new CommandExecutionPayload
            {
                CallId = "call_orphan",
                Command = "sleep",
                Status = "inProgress",
                SessionId = "term_missing"
            }
        });
        await store.SaveTurnAsync(thread, olderTurn);

        var input = new SessionItem
        {
            Id = "item_001",
            TurnId = "turn_006",
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = new UserMessagePayload { Text = "request 6" }
        };
        var interrupted = new SessionTurn
        {
            Id = "turn_006",
            ThreadId = threadId,
            Status = TurnStatus.Running,
            Input = input,
            Items = [input],
            StartedAt = now
        };
        thread.Turns.Add(interrupted);
        await store.SaveTurnAsync(thread, interrupted);
    }

    private static IEnumerable<string> InputTexts(SessionThread? thread) =>
        thread!.Turns.Select(static turn => turn.Input?.AsUserMessage?.Text ?? string.Empty).ToArray();

    private static SessionService CreateService(AgentFactory agentFactory, IChatClient chatClient, ThreadStore store) => new(
        agentFactory,
        chatClient.AsAIAgent(),
        new SessionPersistenceService(store),
        new SessionGate());

    private AgentFactory CreateAgentFactory(IChatClient chatClient) => new(
        dotcraftPath: CraftPath,
        workspacePath: _tempDir,
        config: AppConfigTestFactory.CreateOpenAI(),
        memoryStore: new MemoryStore(CraftPath),
        skillsLoader: new SkillsLoader(CraftPath),
        approvalService: new AutoApproveApprovalService(),
        blacklist: null,
        chatClientRegistry: TestModelProviderRegistry.Create(),
        chatClient: chatClient,
        toolSources: Array.Empty<IToolSource>());

    private static async Task DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    private sealed class RecordingChatClient : IChatClient
    {
        private readonly ConcurrentQueue<string> _requests = new();

        public IReadOnlyList<string> Requests => _requests.ToArray();

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Record(messages);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Record(messages);
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("answer")]);
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(new UsageDetails
            {
                InputTokenCount = 10,
                OutputTokenCount = 1,
                TotalTokenCount = 11
            })]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        private void Record(IEnumerable<ChatMessage> messages) =>
            _requests.Enqueue(string.Join("\n", messages.Select(static message => message.Text)));
    }
}
