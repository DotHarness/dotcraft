using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceContextUsageSnapshotTests : IDisposable
{
    private readonly string _tempDir;

    public SessionServiceContextUsageSnapshotTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ContextUsage_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task TryGetContextUsageSnapshot_ReturnsNull_ForBlankThreadId()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateSessionService(agentFactory);

        Assert.Null(service.TryGetContextUsageSnapshot(""));
        Assert.Null(service.TryGetContextUsageSnapshot("   "));
    }

    [Fact]
    public async Task TryGetContextUsageSnapshot_ReturnsNull_WhenPersistedUsageDoesNotExist()
    {
        const string threadId = "thread-missing";

        await using var agentFactory = CreateAgentFactory();
        var service = CreateSessionService(agentFactory);

        var snapshot = service.TryGetContextUsageSnapshot(threadId);

        Assert.Null(snapshot);
        Assert.Null(agentFactory.TryGetTokenTracker(threadId));
    }

    [Fact]
    public async Task TryGetContextUsageSnapshot_DoesNotCreateTracker_WhenReadingMissingSnapshot()
    {
        const string threadId = "thread-readonly";

        await using var agentFactory = CreateAgentFactory();
        var service = CreateSessionService(agentFactory);

        var snapshot = service.TryGetContextUsageSnapshot(threadId);

        Assert.Null(snapshot);
        Assert.Null(agentFactory.TryGetTokenTracker(threadId));
    }

    [Fact]
    public async Task TryGetContextUsageSnapshot_ReturnsZeroSnapshot_ForFreshThread()
    {
        const string threadId = "thread-fresh";

        await using var agentFactory = CreateAgentFactory();
        var service = CreateSessionService(agentFactory);
        var identity = new SessionIdentity
        {
            WorkspacePath = _tempDir,
            ChannelName = "desktop",
            UserId = "user"
        };
        await service.CreateThreadAsync(identity, threadId: threadId);

        var snapshot = service.TryGetContextUsageSnapshot(threadId);

        Assert.NotNull(snapshot);
        var threshold = agentFactory.CompactionPipeline.EvaluateThreshold(0);
        Assert.Equal(threshold.Tokens, snapshot!.Tokens);
        Assert.Equal(agentFactory.CompactionPipeline.EffectiveContextWindow, snapshot.ContextWindow);
        Assert.Equal(threshold.AutoThreshold, snapshot.AutoCompactThreshold);
        Assert.Equal(threshold.WarningThreshold, snapshot.WarningThreshold);
        Assert.Equal(threshold.ErrorThreshold, snapshot.ErrorThreshold);
        Assert.Equal(threshold.PercentLeft, snapshot.PercentLeft);
        Assert.Null(agentFactory.TryGetTokenTracker(threadId));
    }

    [Fact]
    public async Task TryGetContextUsageSnapshot_UsesThreadModelOverrideWindow_WhenContextWindowIsInferred()
    {
        const string threadId = "thread-model-override";
        var config = AppConfigTestFactory.CreateOpenAI(model: "mimo-v2.5-pro");
        ModelCatalog.ApplyToConfig(
            config,
            System.Text.Json.Nodes.JsonNode.Parse("""{ "Model": "mimo-v2.5-pro" }""")!,
            globalConfigPath: null,
            workspaceConfigPath: null);

        await using var agentFactory = CreateAgentFactory(config);
        var service = CreateSessionService(agentFactory);
        var identity = new SessionIdentity
        {
            WorkspacePath = _tempDir,
            ChannelName = "desktop",
            UserId = "user"
        };
        await service.CreateThreadAsync(
            identity,
            new ThreadConfiguration { Model = "provider/glm-5.1" },
            threadId: threadId);

        var snapshot = service.TryGetContextUsageSnapshot(threadId);

        Assert.NotNull(snapshot);
        Assert.Equal(180_000, snapshot!.ContextWindow);
        Assert.Equal(167_000, snapshot.AutoCompactThreshold);
    }

    [Fact]
    public async Task TryGetContextUsageSnapshot_MaxContextWindowModeUsesCatalogWindow()
    {
        var config = AppConfigTestFactory.CreateOpenAI(model: "gpt-5.5");
        ModelCatalog.ApplyToConfig(
            config,
            System.Text.Json.Nodes.JsonNode.Parse("""{ "Model": "gpt-5.5" }""")!,
            globalConfigPath: null,
            workspaceConfigPath: null);

        await using var agentFactory = CreateAgentFactory(config);
        var service = CreateSessionService(agentFactory);
        var identity = new SessionIdentity
        {
            WorkspacePath = _tempDir,
            ChannelName = "desktop",
            UserId = "user"
        };

        await service.CreateThreadAsync(
            identity,
            new ThreadConfiguration { Model = "gpt-5.5" },
            threadId: "thread-default-window");
        await service.CreateThreadAsync(
            identity,
            new ThreadConfiguration
            {
                Model = "gpt-5.5",
                ContextWindow = new ThreadContextWindowConfig { Mode = ContextWindowMode.Max }
            },
            threadId: "thread-max-window");

        var defaultSnapshot = service.TryGetContextUsageSnapshot("thread-default-window");
        var maxSnapshot = service.TryGetContextUsageSnapshot("thread-max-window");

        Assert.NotNull(defaultSnapshot);
        Assert.NotNull(maxSnapshot);
        Assert.Equal(236_000, defaultSnapshot!.ContextWindow);
        Assert.Equal(1_030_000, maxSnapshot!.ContextWindow);
        Assert.Equal(1_017_000, maxSnapshot.AutoCompactThreshold);
    }

    [Fact]
    public async Task TryGetContextUsageSnapshot_UsesPersistedUsage_WhenTrackerExists()
    {
        const string threadId = "thread-active";

        await using var agentFactory = CreateAgentFactory();
        var service = CreateSessionService(agentFactory);
        var identity = new SessionIdentity
        {
            WorkspacePath = _tempDir,
            ChannelName = "desktop",
            UserId = "user"
        };
        await service.CreateThreadAsync(identity, threadId: threadId);
        var store = new ThreadStore(_tempDir);
        await store.SaveContextUsageTokensAsync(threadId, 34_000);

        var tracker = agentFactory.GetOrCreateTokenTracker(threadId);
        tracker.Update(12_345, 67);

        var snapshot = service.TryGetContextUsageSnapshot(threadId);

        Assert.NotNull(snapshot);

        var threshold = agentFactory.CompactionPipeline.EvaluateThreshold(34_000);
        Assert.Equal(threshold.Tokens, snapshot!.Tokens);
        Assert.Equal(agentFactory.CompactionPipeline.EffectiveContextWindow, snapshot.ContextWindow);
        Assert.Equal(threshold.AutoThreshold, snapshot.AutoCompactThreshold);
        Assert.Equal(threshold.WarningThreshold, snapshot.WarningThreshold);
        Assert.Equal(threshold.ErrorThreshold, snapshot.ErrorThreshold);
        Assert.Equal(threshold.PercentLeft, snapshot.PercentLeft);
    }

    [Fact]
    public async Task TryGetContextUsageSnapshot_IgnoresTurnTokenUsageAndHistory()
    {
        const string threadId = "thread-history";

        await using var agentFactory = CreateAgentFactory();
        var service = CreateSessionService(agentFactory);

        var identity = new SessionIdentity
        {
            WorkspacePath = _tempDir,
            ChannelName = "desktop",
            UserId = "user"
        };
        var thread = await service.CreateThreadAsync(identity, threadId: threadId);
        thread.Turns.Add(new SessionTurn
        {
            Id = "turn_001",
            ThreadId = threadId,
            Status = TurnStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            Items =
            [
                new SessionItem
                {
                    Id = "item_001",
                    TurnId = "turn_001",
                    Type = ItemType.UserMessage,
                    Status = ItemStatus.Completed,
                    CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                    CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                    Payload = new UserMessagePayload { Text = "Short user question" }
                },
                new SessionItem
                {
                    Id = "item_002",
                    TurnId = "turn_001",
                    Type = ItemType.AgentMessage,
                    Status = ItemStatus.Completed,
                    CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                    CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                    Payload = new AgentMessagePayload { Text = "Short assistant answer" }
                }
            ],
            TokenUsage = new TokenUsageInfo
            {
                InputTokens = 12_000,
                OutputTokens = 400,
                TotalTokens = 12_400
            }
        });
        thread.Turns.Add(new SessionTurn
        {
            Id = "turn_002",
            ThreadId = threadId,
            Status = TurnStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Items =
            [
                new SessionItem
                {
                    Id = "item_003",
                    TurnId = "turn_002",
                    Type = ItemType.UserMessage,
                    Status = ItemStatus.Completed,
                    CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                    CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                    Payload = new UserMessagePayload { Text = "Another short user question" }
                },
                new SessionItem
                {
                    Id = "item_004",
                    TurnId = "turn_002",
                    Type = ItemType.AgentMessage,
                    Status = ItemStatus.Completed,
                    CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                    CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                    Payload = new AgentMessagePayload { Text = "Another short assistant answer" }
                }
            ],
            TokenUsage = new TokenUsageInfo
            {
                InputTokens = 80_000,
                OutputTokens = 800,
                TotalTokens = 80_800
            }
        });
        var store = new ThreadStore(_tempDir);
        await store.SaveContextUsageTokensAsync(threadId, 321);

        Assert.Null(agentFactory.TryGetTokenTracker(threadId));

        var snapshot = service.TryGetContextUsageSnapshot(threadId);

        Assert.NotNull(snapshot);
        var threshold = agentFactory.CompactionPipeline.EvaluateThreshold(321);
        Assert.Equal(threshold.Tokens, snapshot!.Tokens);
        Assert.Equal(agentFactory.CompactionPipeline.EffectiveContextWindow, snapshot.ContextWindow);
        Assert.Equal(threshold.AutoThreshold, snapshot.AutoCompactThreshold);
        Assert.Equal(threshold.WarningThreshold, snapshot.WarningThreshold);
        Assert.Equal(threshold.ErrorThreshold, snapshot.ErrorThreshold);
        Assert.Equal(threshold.PercentLeft, snapshot.PercentLeft);
        Assert.Null(agentFactory.TryGetTokenTracker(threadId));
    }

    [Fact]
    public async Task TryGetContextUsageSnapshot_LoadsPersistedUsage_AfterServiceRecreate()
    {
        const string threadId = "thread-recreated";

        await using (var agentFactory = CreateAgentFactory())
        {
            var service = CreateSessionService(agentFactory);
            var identity = new SessionIdentity
            {
                WorkspacePath = _tempDir,
                ChannelName = "desktop",
                UserId = "user"
            };
            await service.CreateThreadAsync(identity, threadId: threadId);
            var store = new ThreadStore(_tempDir);
            await store.SaveContextUsageTokensAsync(
                threadId,
                77_000,
                source: "history_estimate",
                isEstimate: true);
        }

        await using var recreatedAgentFactory = CreateAgentFactory();
        var recreatedService = CreateSessionService(recreatedAgentFactory);

        var snapshot = recreatedService.TryGetContextUsageSnapshot(threadId);

        Assert.NotNull(snapshot);
        var threshold = recreatedAgentFactory.CompactionPipeline.EvaluateThreshold(77_000);
        Assert.Equal(threshold.Tokens, snapshot!.Tokens);
        Assert.Equal(threshold.PercentLeft, snapshot.PercentLeft);
        Assert.Equal("history_estimate", snapshot.Source);
        Assert.True(snapshot.IsEstimate);
        Assert.Null(recreatedAgentFactory.TryGetTokenTracker(threadId));
    }

    private SessionService CreateSessionService(AgentFactory agentFactory)
    {
        var store = new ThreadStore(_tempDir);
        var persistence = new SessionPersistenceService(store);
        var defaultAgent = agentFactory.CreateAgentForMode(AgentMode.Agent);
        return new SessionService(agentFactory, defaultAgent, persistence, new SessionGate());
    }

    private AgentFactory CreateAgentFactory(AppConfig? config = null)
    {
        config ??= AppConfigTestFactory.CreateOpenAI();
        var memory = new MemoryStore(_tempDir);
        var skills = new SkillsLoader(_tempDir);
        return new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: memory,
            skillsLoader: skills,
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            toolSources: Array.Empty<IToolSource>());
    }
}
