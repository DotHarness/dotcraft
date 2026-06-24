using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceForkTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ThreadStore _store;
    private readonly SessionPersistenceService _persistence;

    public SessionServiceForkTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SSFork_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _store = new ThreadStore(_tempDir);
        _persistence = new SessionPersistenceService(_store);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task ForkThreadAsync_CopiesHistoryAndPersistsLineage()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var source = await service.CreateThreadAsync(MakeIdentity(), displayName: "Source");
        AddCompletedTurn(source, "turn_001", "first", "answer one");
        AddCompletedTurn(source, "turn_002", "second", "answer two");

        var fork = await service.ForkThreadAsync(source.Id, new ThreadForkOptions { DisplayName = "Branch" });

        Assert.NotEqual(source.Id, fork.Id);
        Assert.Equal(source.Id, fork.ForkedFromId);
        Assert.Equal("Branch", fork.DisplayName);
        Assert.Empty(fork.QueuedInputs);
        Assert.Equal(2, fork.Turns.Count);
        Assert.All(fork.Turns, turn => Assert.Equal(fork.Id, turn.ThreadId));
        Assert.Equal("second", Assert.IsType<UserMessagePayload>(fork.Turns[1].Input!.Payload).Text);
        AssertForkBoundaryNotice(fork.Turns[^1].Items[^1], source.Id);

        var loaded = await _store.LoadThreadAsync(fork.Id);
        Assert.NotNull(loaded);
        Assert.Equal(source.Id, loaded!.ForkedFromId);
        Assert.Equal(2, loaded.Turns.Count);
        AssertForkBoundaryNotice(loaded.Turns[^1].Items[^1], source.Id);

        var indexed = Assert.Single(await _store.LoadIndexAsync(), summary => summary.Id == fork.Id);
        Assert.Equal(source.Id, indexed.ForkedFromId);
    }

    [Fact]
    public async Task ForkThreadAsync_DefaultDisplayNameUsesSourceDisplayName()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var source = await service.CreateThreadAsync(MakeIdentity(), displayName: "Research worktree handoff");
        AddCompletedTurn(source, "turn_001", "first", "answer one");

        var fork = await service.ForkThreadAsync(source.Id);

        Assert.Equal("Research worktree handoff", fork.DisplayName);
        var loaded = await _store.LoadThreadAsync(fork.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Research worktree handoff", loaded!.DisplayName);
        var indexed = Assert.Single(await _store.LoadIndexAsync(), summary => summary.Id == fork.Id);
        Assert.Equal("Research worktree handoff", indexed.DisplayName);
    }

    [Fact]
    public async Task ForkThreadAsync_DefaultDisplayNameFallsBackToFirstRetainedUserMessage()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var source = await service.CreateThreadAsync(MakeIdentity());
        const string firstUserMessage = "Review the worktree handoff behavior and draft the implementation plan";
        AddCompletedTurn(source, "turn_001", firstUserMessage, "answer one");

        var fork = await service.ForkThreadAsync(source.Id);

        var expected = firstUserMessage[..50] + "...";
        Assert.Equal(expected, fork.DisplayName);
        var loaded = await _store.LoadThreadAsync(fork.Id);
        Assert.NotNull(loaded);
        Assert.Equal(expected, loaded!.DisplayName);
    }

    [Fact]
    public async Task ForkThreadAsync_ItemForkPoint_TruncatesAndCancelsPartialTurn()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var source = await service.CreateThreadAsync(MakeIdentity());
        AddCompletedTurn(source, "turn_001", "first", "answer one");

        var fork = await service.ForkThreadAsync(
            source.Id,
            new ThreadForkOptions
            {
                ForkPoint = new ThreadForkPoint
                {
                    TurnId = "turn_001",
                    ItemId = "turn_001_user",
                    Position = ThreadForkPositions.After
                }
            });

        var turn = Assert.Single(fork.Turns);
        Assert.Equal(TurnStatus.Cancelled, turn.Status);
        Assert.Equal(2, turn.Items.Count);
        Assert.Equal("first", Assert.IsType<UserMessagePayload>(turn.Input!.Payload).Text);
        AssertForkBoundaryNotice(turn.Items[^1], source.Id);
        Assert.Contains("partial", turn.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ForkThreadAsync_FromCompactedSource_PersistsForkCheckpointSessionAndUsesCompactedHistory()
    {
        var chatClient = new RecordingChatClient("fork answer");
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory, chatClient);
        var source = await service.CreateThreadAsync(MakeIdentity());
        AddCompletedTurn(source, "turn_001", "RAW_BEFORE_COMPACT", "raw answer");
        AddCompletedTurn(source, "turn_002", "covered raw request", "covered raw answer");
        AddCompletedTurn(source, "turn_003", "AFTER_COMPACT", "after answer");
        await _store.SaveThreadAsync(source);
        var replacementHistory = new[]
        {
            new ChatMessage(ChatRole.User, "COMPACTED_SUMMARY"),
            new ChatMessage(ChatRole.Assistant, "summary answer")
        };
        await _store.AppendCompactionCheckpointAsync(
            source.Id,
            "turn_002",
            replacementHistory,
            "manual",
            "partial",
            tokensBefore: 10_000,
            tokensAfter: 100);

        var fork = await service.ForkThreadAsync(source.Id);

        Assert.True(_store.SessionFileExists(fork.Id));
        var checkpoints = await _store.LoadCompactionCheckpointsAsync(fork.Id);
        var checkpoint = Assert.Single(checkpoints);
        Assert.Equal(fork.Id, checkpoint.ThreadId);
        Assert.Equal("turn_002", checkpoint.CoveredThroughTurnId);
        Assert.Equal("fork", checkpoint.Trigger);
        Assert.Equal("partial", checkpoint.Mode);
        var usage = _store.LoadContextUsageSnapshot(fork.Id);
        Assert.NotNull(usage);
        Assert.True(usage!.Tokens > 0);
        Assert.Equal("compacted_estimate", usage.Source);
        Assert.True(usage.IsEstimate);

        var session = await _store.LoadOrCreateSessionAsync(CreateAgent(), fork.Id);
        Assert.Equal(
            [
                "user:COMPACTED_SUMMARY",
                "assistant:summary answer",
                "user:AFTER_COMPACT",
                "assistant:after answer"
            ],
            FormatHistory(session));

        await DrainAsync(service.SubmitInputAsync(fork.Id, [new TextContent("FOLLOW_UP")]));
        var requestText = string.Join("\n", chatClient.LastMessages.Select(MessageText));
        Assert.Contains("COMPACTED_SUMMARY", requestText, StringComparison.Ordinal);
        Assert.Contains("AFTER_COMPACT", requestText, StringComparison.Ordinal);
        Assert.Contains("FOLLOW_UP", requestText, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_BEFORE_COMPACT", requestText, StringComparison.Ordinal);
        Assert.DoesNotContain("covered raw request", requestText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForkThreadAsync_ItemForkPointInsideCheckpointCoveredTurn_DoesNotRebaseCheckpoint()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var source = await service.CreateThreadAsync(MakeIdentity());
        AddCompletedTurn(source, "turn_001", "first", "answer one");
        AddCompletedTurn(source, "turn_002", "covered raw request", "covered raw answer");
        await _store.SaveThreadAsync(source);
        await _store.AppendCompactionCheckpointAsync(
            source.Id,
            "turn_002",
            [new ChatMessage(ChatRole.User, "COMPACTED_SUMMARY")],
            "manual",
            "partial",
            tokensBefore: 10_000,
            tokensAfter: 100);

        var fork = await service.ForkThreadAsync(
            source.Id,
            new ThreadForkOptions
            {
                ForkPoint = new ThreadForkPoint
                {
                    TurnId = "turn_002",
                    ItemId = "turn_002_user",
                    Position = ThreadForkPositions.After
                }
            });

        Assert.Empty(await _store.LoadCompactionCheckpointsAsync(fork.Id));
        Assert.True(_store.SessionFileExists(fork.Id));
        var usage = _store.LoadContextUsageSnapshot(fork.Id);
        Assert.NotNull(usage);
        Assert.Equal("history_estimate", usage!.Source);
        Assert.True(usage.IsEstimate);
        var session = await _store.LoadOrCreateSessionAsync(CreateAgent(), fork.Id);
        Assert.Equal(
            [
                "user:first",
                "assistant:answer one",
                "user:covered raw request"
            ],
            FormatHistory(session));
    }

    [Fact]
    public async Task ForkThreadAsync_EphemeralFork_IsCachedButNotPersistedOrListed()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var identity = MakeIdentity();
        var source = await service.CreateThreadAsync(identity);
        AddCompletedTurn(source, "turn_001", "first", "answer one");

        var fork = await service.ForkThreadAsync(source.Id, new ThreadForkOptions { Ephemeral = true });

        Assert.True(fork.Ephemeral);
        Assert.Same(fork, await service.GetThreadAsync(fork.Id));
        Assert.Null(await _store.LoadThreadAsync(fork.Id));
        var listed = await service.FindThreadsAsync(identity);
        Assert.DoesNotContain(listed, summary => summary.Id == fork.Id);
    }

    private SessionService CreateService(AgentFactory agentFactory, IChatClient? chatClient = null)
    {
        var defaultAgent = chatClient == null
            ? agentFactory.CreateAgentForMode(AgentMode.Agent)
            : chatClient.AsAIAgent(new ChatClientAgentOptions());
        return new SessionService(agentFactory, defaultAgent, _persistence, new SessionGate());
    }

    private AgentFactory CreateAgentFactory()
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        return new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: new MemoryStore(_tempDir),
            skillsLoader: new SkillsLoader(_tempDir),
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            toolProviders: Array.Empty<IAgentToolProvider>());
    }

    private SessionIdentity MakeIdentity() =>
        new()
        {
            ChannelName = "test",
            UserId = "user",
            WorkspacePath = _tempDir
        };

    private static void AddCompletedTurn(SessionThread thread, string turnId, string userText, string agentText)
    {
        var now = DateTimeOffset.UtcNow.AddSeconds(thread.Turns.Count);
        var user = new SessionItem
        {
            Id = $"{turnId}_user",
            TurnId = turnId,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = new UserMessagePayload { Text = userText }
        };
        var agent = new SessionItem
        {
            Id = $"{turnId}_agent",
            TurnId = turnId,
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            CreatedAt = now.AddMilliseconds(1),
            CompletedAt = now.AddMilliseconds(1),
            Payload = new AgentMessagePayload { Text = agentText }
        };
        thread.Turns.Add(new SessionTurn
        {
            Id = turnId,
            ThreadId = thread.Id,
            Status = TurnStatus.Completed,
            Input = user,
            Items = [user, agent],
            StartedAt = now,
            CompletedAt = now.AddMilliseconds(2)
        });
    }

    private static void AssertForkBoundaryNotice(SessionItem item, string sourceThreadId)
    {
        Assert.Equal(ItemType.SystemNotice, item.Type);
        var notice = Assert.IsType<SystemNoticePayload>(item.Payload);
        Assert.Equal("forked", notice.Kind);
        Assert.Equal(sourceThreadId, notice.SourceThreadId);
    }

    private static AIAgent CreateAgent() =>
        new RecordingChatClient("unused").AsAIAgent(new ChatClientAgentOptions());

    private static List<string> FormatHistory(AgentSession session)
    {
        Assert.True(session.TryGetInMemoryChatHistory(
            out var chatHistory,
            jsonSerializerOptions: SessionPersistenceJsonOptions.Default));

        return chatHistory.Select(message => $"{message.Role}:{MessageText(message)}").ToList();
    }

    private static string MessageText(ChatMessage message) =>
        string.Concat(message.Contents.Select(content => content switch
        {
            TextContent text => text.Text,
            FunctionCallContent call => $"function_call:{call.Name}:{call.CallId}",
            FunctionResultContent result => $"function_result:{result.CallId}:{result.Result}",
            _ => content.ToString() ?? string.Empty
        }));

    private static async Task DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    private sealed class RecordingChatClient(string responseText) : IChatClient
    {
        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = chatMessages.ToList();
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, responseText)]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = chatMessages.ToList();
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(responseText)]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
