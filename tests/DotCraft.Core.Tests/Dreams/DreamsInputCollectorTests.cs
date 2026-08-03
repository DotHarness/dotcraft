using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Dreams;
using DotCraft.Protocol;
using DotCraft.Sessions;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using AgentMessagePayload = DotCraft.Sessions.AgentMessagePayload;
using SubAgentThreadSource = DotCraft.Sessions.SubAgentThreadSource;
using ThreadSource = DotCraft.Sessions.ThreadSource;
using UserMessagePayload = DotCraft.Sessions.UserMessagePayload;
using Xunit;

namespace DotCraft.Tests.Dreams;

public sealed class DreamsInputCollectorTests : IDisposable
{
    private readonly string _root;
    private readonly string _workspace;
    private readonly ThreadStore _threadStore;
    private readonly MemoryStore _memoryStore;
    private readonly DreamStore _dreamStore;

    public DreamsInputCollectorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "DreamsInput_" + Guid.NewGuid().ToString("N")[..8]);
        _workspace = Path.Combine(_root, "workspace");
        var craft = Path.Combine(_root, ".craft");
        Directory.CreateDirectory(_workspace);
        Directory.CreateDirectory(craft);
        _threadStore = new ThreadStore(craft);
        _memoryStore = new MemoryStore(craft);
        _dreamStore = new DreamStore(craft);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch { }
    }

    [Fact]
    public async Task CollectAsync_FiltersToTopLevelServerManagedVisibleThreads()
    {
        await SaveThreadAsync(CreateThread("thread_user", "user thread", HistoryMode.Server, ThreadSource.User()));
        await SaveThreadAsync(CreateThread("thread_client", "client thread", HistoryMode.Client, ThreadSource.User()));
        var internalThread = CreateThread("thread_internal", "internal thread", HistoryMode.Server, ThreadSource.User());
        internalThread.Metadata[ThreadVisibility.InternalMetadataKey] = "true";
        await SaveThreadAsync(internalThread);
        var subAgentThread = CreateThread(
            "thread_subagent",
            "subagent thread",
            HistoryMode.Server,
            ThreadSource.ForSubAgent(new SubAgentThreadSource { ParentThreadId = "thread_user" }));
        subAgentThread.OriginChannel = SubAgentThreadOrigin.ChannelName;
        await SaveThreadAsync(subAgentThread);

        var collector = new DreamsInputCollector(
            new AppConfig(),
            _workspace,
            _memoryStore,
            _dreamStore,
            _threadStore);

        var input = await collector.CollectAsync();

        var thread = Assert.Single(input.Threads);
        Assert.Equal("thread_user", thread.ThreadId);
        Assert.Equal("user thread", thread.DisplayName);
        Assert.Equal(1, thread.CompletedTurnCount);
        Assert.Equal(1, input.CompletedTurnCount);
    }

    [Fact]
    public async Task CollectAsync_AppliesLookbackAndIncludesTopicManifestWithoutTranscripts()
    {
        _memoryStore.WriteLongTerm("explicit memory");
        _memoryStore.AppendHistory(new string('x', 40));
        _dreamStore.SaveDreamRun(
            "# Dream Memory\nold dream",
            [new DreamTopicFileWrite { Path = "api-conventions.md", Content = "# API Conventions\nUse typed clients." }],
            null,
            null);

        await SaveThreadAsync(CreateThread("thread_old", "old", HistoryMode.Server, ThreadSource.User(), DateTimeOffset.UtcNow.AddMinutes(-2)));
        await SaveThreadAsync(CreateThread("thread_new", "new", HistoryMode.Server, ThreadSource.User(), DateTimeOffset.UtcNow));

        var config = new AppConfig
        {
            Dreams = new DreamsConfig
            {
                ThreadLookbackCount = 1,
                HistoryTailChars = 10
            }
        };
        var collector = new DreamsInputCollector(config, _workspace, _memoryStore, _dreamStore, _threadStore);

        var input = await collector.CollectAsync();

        Assert.Equal("explicit memory", input.ExplicitMemory);
        Assert.Contains("old dream", input.ExistingDream, StringComparison.Ordinal);
        var topic = Assert.Single(input.TopicFiles);
        Assert.Equal("api-conventions.md", topic.Path);
        Assert.Contains("typed clients", topic.Preview, StringComparison.Ordinal);
        Assert.Equal("thread_new", Assert.Single(input.Threads).ThreadId);
    }

    private Task SaveThreadAsync(SessionThread thread) => _threadStore.SaveThreadAsync(thread);

    private SessionThread CreateThread(
        string id,
        string label,
        HistoryMode historyMode,
        ThreadSource source,
        DateTimeOffset? lastActiveAt = null)
    {
        var now = lastActiveAt ?? DateTimeOffset.UtcNow;
        var user = new SessionItem
        {
            Id = "item_user",
            TurnId = "turn_001",
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            Payload = new UserMessagePayload { Text = $"{label} question" },
            CreatedAt = now
        };
        var assistant = new SessionItem
        {
            Id = "item_agent",
            TurnId = "turn_001",
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            Payload = new AgentMessagePayload { Text = $"{label} answer" },
            CreatedAt = now
        };
        return new SessionThread
        {
            Id = id,
            WorkspacePath = _workspace,
            OriginChannel = "cli",
            DisplayName = label,
            Source = source,
            Status = ThreadStatus.Active,
            CreatedAt = now.AddMinutes(-1),
            LastActiveAt = now,
            HistoryMode = historyMode,
            Turns =
            [
                new SessionTurn
                {
                    Id = "turn_001",
                    ThreadId = id,
                    Status = TurnStatus.Completed,
                    Input = user,
                    Items = [user, assistant],
                    StartedAt = now.AddSeconds(-10),
                    CompletedAt = now
                }
            ]
        };
    }
}
