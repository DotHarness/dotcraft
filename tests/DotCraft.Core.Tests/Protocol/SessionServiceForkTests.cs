using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;

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

    private SessionService CreateService(AgentFactory agentFactory)
    {
        var defaultAgent = agentFactory.CreateAgentForMode(AgentMode.Agent);
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
}
