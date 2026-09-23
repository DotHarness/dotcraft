using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;
using AgentMessagePayload = DotCraft.Sessions.AgentMessagePayload;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using UserMessagePayload = DotCraft.Sessions.UserMessagePayload;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceImportTests : IDisposable
{
    private const string ThreadId = "thread_import_claude-code_0123456789abcdef";
    private const string Marker = ThreadImportConstants.Marker;
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly string _tempDir;
    private readonly ThreadStore _store;
    private readonly SessionPersistenceService _persistence;

    public SessionServiceImportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SSImport_" + Guid.NewGuid().ToString("N")[..8]);
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
    public async Task ImportThreadAsync_PersistsCompletedHistoryIdentityAndEstimatedUsage()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var announced = new List<SessionThread>();
        service.ThreadCreatedForBroadcast = announced.Add;
        var subfolder = Directory.CreateDirectory(Path.Combine(_tempDir, "src")).FullName;

        var result = await service.ImportThreadAsync(MakeRequest(
            Turn("first question", minute: 0, "first answer", "more detail"),
            Turn("second question", minute: 5, "second answer")) with
        {
            Cwd = subfolder,
            EstimatedTokens = 4321
        });

        Assert.False(result.AlreadyExisted);
        Assert.Same(result.Thread, Assert.Single(announced));
        var loaded = await _store.LoadThreadAsync(ThreadId);
        Assert.NotNull(loaded);
        Assert.Equal(subfolder, loaded!.Configuration?.Cwd);
        Assert.Equal($"workspace:{_tempDir}", loaded.ChannelContext);
        Assert.Equal(2, loaded.Turns.Count);
        Assert.All(loaded.Turns, turn =>
        {
            Assert.Equal(TurnStatus.Completed, turn.Status);
            Assert.Equal(ThreadImportConstants.ChannelName, turn.OriginChannel);
            Assert.Same(turn.Items[0], turn.Input);
            Assert.All(turn.Items, item => Assert.Equal(ItemStatus.Completed, item.Status));
        });
        Assert.Equal(
            ["user:first question", "agent:first answer", "agent:more detail"],
            DescribeItems(loaded.Turns[0]));
        Assert.Equal(
            ["user:second question", "agent:second answer", $"agent:{Marker}"],
            DescribeItems(loaded.Turns[1]));

        var summary = Assert.Single(await _store.LoadIndexAsync());
        Assert.Equal("Imported title", summary.DisplayName);
        Assert.Equal(ThreadImportConstants.ChannelName, summary.OriginChannel);
        Assert.Equal("claude-code", summary.Metadata["dotcraft.import.source"]);
        Assert.Equal("source-session", summary.Metadata["dotcraft.import.sessionId"]);
        Assert.Equal(2, summary.TurnCount);
        Assert.Equal(BaseTime, summary.CreatedAt);
        Assert.Equal(BaseTime.AddMinutes(5).AddSeconds(30), summary.LastActiveAt);
        var usage = _store.LoadContextUsageSnapshot(ThreadId);
        Assert.NotNull(usage);
        Assert.Equal(4321, usage!.Tokens);
        Assert.True(usage.IsEstimate);
    }

    [Fact]
    public async Task ImportThreadAsync_ExistingThreadIsReturnedWithoutWriting()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        await service.ImportThreadAsync(MakeRequest(Turn("first question", minute: 0, "first answer")));
        var announced = new List<SessionThread>();
        service.ThreadCreatedForBroadcast = announced.Add;
        var retry = MakeRequest(
            Turn("other question", minute: 1, "other answer"),
            Turn("another question", minute: 2, "another answer"));

        var again = await service.ImportThreadAsync(retry);
        var afterRestart = await CreateService(agentFactory).ImportThreadAsync(retry);

        Assert.True(again.AlreadyExisted);
        Assert.True(afterRestart.AlreadyExisted);
        Assert.Empty(announced);
        var loaded = await _store.LoadThreadAsync(ThreadId);
        var turn = Assert.Single(loaded!.Turns);
        Assert.Equal(["user:first question", "agent:first answer", $"agent:{Marker}"], DescribeItems(turn));
    }

    [Fact]
    public async Task ImportThreadAsync_RaisesTurnStartsThatAreNotMonotonic()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var late = BaseTime.AddMinutes(10);

        await service.ImportThreadAsync(MakeRequest(
            new ImportedTurnInput { UserText = "first", AgentTexts = ["a"], StartedAt = late, CompletedAt = late.AddMinutes(1) },
            new ImportedTurnInput { UserText = "second", AgentTexts = ["b"], StartedAt = BaseTime, CompletedAt = BaseTime.AddMinutes(1) },
            new ImportedTurnInput { UserText = "third", AgentTexts = ["c"], StartedAt = late, CompletedAt = late }));

        var loaded = await _store.LoadThreadAsync(ThreadId);
        var turns = loaded!.Turns;
        Assert.Equal(["first", "second", "third"], turns.Select(turn => turn.Input!.AsUserMessage!.Text));
        Assert.Equal(
            [late, late.AddMilliseconds(1), late.AddMilliseconds(2)],
            turns.Select(turn => turn.StartedAt));
        Assert.Equal(
            [late.AddMinutes(1), late.AddMilliseconds(1), late.AddMilliseconds(2)],
            turns.Select(turn => turn.CompletedAt!.Value));
        Assert.All(turns, turn =>
        {
            Assert.Equal(turn.StartedAt, turn.Items[0].CreatedAt);
            Assert.All(turn.Items.Skip(1), item => Assert.Equal(turn.CompletedAt, item.CompletedAt));
        });
        Assert.Equal(late.AddMilliseconds(2), loaded.LastActiveAt);
    }

    [Fact]
    public async Task ImportThreadAsync_RejectsIdentityOutsideImportChannel()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var request = MakeRequest(Turn("question", minute: 0, "answer"));

        await Assert.ThrowsAsync<ArgumentException>(() => service.ImportThreadAsync(request with
        {
            Identity = request.Identity with { ChannelName = "cli" }
        }));

        Assert.Empty(await _store.LoadIndexAsync());
    }

    [Fact]
    public async Task ImportedThread_FollowUpTurnSendsImportedHistoryToModel()
    {
        var chatClient = new RecordingChatClient("local answer");
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory, chatClient);
        await service.ImportThreadAsync(MakeRequest(Turn("imported question", minute: 0, "imported answer")));

        await DrainAsync(service.SubmitInputAsync(ThreadId, [new TextContent("local follow-up")]));

        var requestText = string.Join("\n", chatClient.LastMessages.Select(MessageText));
        var positions = new[] { "imported question", "imported answer", Marker, "local follow-up" }
            .Select(text => requestText.IndexOf(text, StringComparison.Ordinal))
            .ToList();
        Assert.All(positions, position => Assert.True(position >= 0));
        Assert.Equal(positions.Order(), positions);
    }

    private static List<string> DescribeItems(SessionTurn turn) =>
        turn.Items.Select(item => item.Payload switch
        {
            UserMessagePayload user => $"user:{user.Text}",
            AgentMessagePayload agent => $"agent:{agent.Text}",
            _ => item.Type.ToString()
        }).ToList();

    private ThreadImportRequest MakeRequest(params ImportedTurnInput[] turns) => new()
    {
        Identity = new SessionIdentity
        {
            ChannelName = ThreadImportConstants.ChannelName,
            UserId = "local",
            ChannelContext = $"workspace:{_tempDir}",
            WorkspacePath = _tempDir
        },
        ThreadId = ThreadId,
        DisplayName = "Imported title",
        Metadata = new Dictionary<string, string>
        {
            ["dotcraft.import.source"] = "claude-code",
            ["dotcraft.import.sessionId"] = "source-session"
        },
        Turns = turns
    };

    private static ImportedTurnInput Turn(string userText, int minute, params string[] agentTexts) => new()
    {
        UserText = userText,
        AgentTexts = agentTexts,
        StartedAt = BaseTime.AddMinutes(minute),
        CompletedAt = BaseTime.AddMinutes(minute).AddSeconds(30)
    };

    private SessionService CreateService(AgentFactory agentFactory, IChatClient? chatClient = null)
    {
        var defaultAgent = chatClient == null
            ? agentFactory.CreateAgentForMode(AgentMode.Agent)
            : chatClient.AsAIAgent();
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
            chatClientRegistry: TestModelProviderRegistry.Create(),
            toolSources: Array.Empty<IToolSource>());
    }

    private static string MessageText(ChatMessage message) =>
        string.Concat(message.Contents.OfType<TextContent>().Select(static content => content.Text));

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
