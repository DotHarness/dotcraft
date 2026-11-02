using System.Text.Json;
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

public sealed class SessionServiceArchiveTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "SessionArchive_" + Guid.NewGuid().ToString("N"));

    private string CraftPath => Path.Combine(_workspace, ".craft");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_workspace)) Directory.Delete(_workspace, recursive: true);
    }

    [Fact]
    public async Task ColdLongThread_ArchivesWithoutHydratingAndPreservesHistory()
    {
        const int turns = 128;
        var attachment = Path.Combine(CraftPath, "attachments", "images", "kept.png");
        Directory.CreateDirectory(Path.GetDirectoryName(attachment)!);
        await File.WriteAllBytesAsync(attachment, [1, 2, 3]);
        await SeedAsync("cold", turns, attachment: attachment);
        var original = await File.ReadAllBytesAsync(ActivePath("cold"));
        await using var store = new ThreadStore(CraftPath);
        var chat = new RecordingChatClient();
        var resources = new ThreadResources();
        await using var factory = CreateFactory(chat, resources);
        var service = CreateService(factory, chat, store);
        var before = await service.ReadThreadSnapshotAsync("cold");
        var projectedBefore = store.ProjectedRolloutBytes;

        await service.ArchiveThreadAsync("cold");

        AssertNoHydration(service, store, "cold");
        Assert.Equal(["cold"], resources.Released);
        Assert.Equal(0, chat.RequestCount);
        Assert.False(File.Exists(ActivePath("cold")));
        var archived = await File.ReadAllBytesAsync(ArchivedPath("cold"));
        Assert.Equal(original, archived[..original.Length]);
        var record = JsonSerializer.Deserialize<ThreadRolloutRecord>(archived.AsSpan(original.Length), SessionJsonOptions.Default)!;
        Assert.Equal(RolloutKinds.ThreadStatusChanged, record.Kind);
        Assert.Equal(archived.Length - original.Length, store.ProjectedRolloutBytes - projectedBefore);
        var snapshot = await service.ReadThreadSnapshotAsync("cold");
        Assert.Equal(ThreadStatus.Archived, snapshot.Thread.Status);
        Assert.Equal(before.Thread.LastActiveAt, snapshot.Thread.LastActiveAt);
        Assert.Equal(turns, Assert.Single(await store.LoadIndexAsync(), summary => summary.Id == "cold").TurnCount);
        Assert.Empty(snapshot.Thread.Turns);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(attachment));
        var firstItems = await service.ListThreadItemsAsync("cold", "turn_001", null, 10, ThreadHistorySortDirection.Ascending);
        var user = Assert.IsType<UserMessagePayload>(firstItems.Data[0].Item.Payload);
        Assert.Equal(attachment, Assert.Single(user.NativeInputParts!).Path);
        AssertNoHydration(service, store, "cold");

        await service.ArchiveThreadAsync("cold");

        Assert.Equal(archived, await File.ReadAllBytesAsync(ArchivedPath("cold")));
        Assert.Equal(["cold"], resources.Released);
        AssertNoHydration(service, store, "cold");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("root")]
    [InlineData("child")]
    public async Task ParentArchive_CascadesAcrossColdAndLoadedDescendantsIncludingClosedEdges(string? loadedId)
    {
        await SeedAsync("root", 3);
        await SeedAsync("child", 3, parentId: "root");
        await SeedAsync("closed", 3, parentId: "child", closed: true);
        var ids = new[] { "root", "child", "closed" };
        var originals = new Dictionary<string, byte[]>();
        foreach (var id in ids)
            originals[id] = await File.ReadAllBytesAsync(ActivePath(id));
        await using var store = new ThreadStore(CraftPath);
        var chat = new RecordingChatClient();
        var resources = new ThreadResources();
        await using var factory = CreateFactory(chat, resources);
        var service = CreateService(factory, chat, store);
        SessionThread? loaded = loadedId is null ? null : await service.GetThreadAsync(loadedId);
        var replays = store.RolloutReplayCount;
        var forwardReplays = store.ForwardReplayCount;

        await service.ArchiveThreadAsync("root");

        Assert.Equal(replays, store.RolloutReplayCount);
        Assert.Equal(forwardReplays, store.ForwardReplayCount);
        Assert.Equal(ids.Order(), resources.Released.Order());
        Assert.Equal(0, chat.RequestCount);
        foreach (var id in ids)
        {
            Assert.Equal(ThreadStatus.Archived, (await service.ReadThreadSnapshotAsync(id)).Thread.Status);
            Assert.False(File.Exists(ActivePath(id)));
            Assert.Equal(originals[id], (await File.ReadAllBytesAsync(ArchivedPath(id)))[..originals[id].Length]);
            if (id != loadedId)
                Assert.Null(service.TryGetLoadedThread(id));
        }
        if (loaded is not null)
        {
            Assert.Same(loaded, service.TryGetLoadedThread(loadedId!));
            Assert.Equal(ThreadStatus.Archived, loaded.Status);
        }
    }

    [Fact]
    public async Task DirectChildArchive_IsRejectedWithoutLoadingEitherThread()
    {
        await SeedAsync("root", 2);
        await SeedAsync("child", 2, parentId: "root");
        var original = await File.ReadAllBytesAsync(ActivePath("child"));
        await using var store = new ThreadStore(CraftPath);
        var chat = new RecordingChatClient();
        var resources = new ThreadResources();
        await using var factory = CreateFactory(chat, resources);
        var service = CreateService(factory, chat, store);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ArchiveThreadAsync("child"));

        AssertNoHydration(service, store, "root", "child");
        Assert.Empty(resources.Released);
        Assert.Equal(original, await File.ReadAllBytesAsync(ActivePath("child")));
        Assert.Equal(ThreadStatus.Active, (await service.ReadThreadSnapshotAsync("root")).Thread.Status);
    }

    [Fact]
    public async Task ColdArchive_SerializesWithConcurrentResume()
    {
        await SeedAsync("root", 3);
        await using var store = new ThreadStore(CraftPath);
        var chat = new RecordingChatClient();
        var resources = new ThreadResources { BlockThreadId = "root" };
        await using var factory = CreateFactory(chat, resources);
        var service = CreateService(factory, chat, store);
        var archive = service.ArchiveThreadAsync("root");
        Task competing;
        try
        {
            await resources.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            AssertNoHydration(service, store, "root");
            competing = service.ResumeThreadAsync("root");
            Assert.False(competing.IsCompleted);
        }
        finally
        {
            resources.Release.TrySetResult();
        }

        await archive.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<InvalidOperationException>(() => competing.WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Equal(ThreadStatus.Archived, (await service.ReadThreadSnapshotAsync("root")).Thread.Status);
        Assert.Equal(0, chat.RequestCount);
        Assert.Equal(["root"], resources.Released);
        Assert.Equal(3, (await store.ListThreadTurnsAsync("root", null, 10, ThreadHistorySortDirection.Ascending)).Data.Count);
        Assert.True(service.TryGetLoadedThread("root") is null or { Status: ThreadStatus.Archived });
    }

    private async Task SeedAsync(string id, int turnCount, string? parentId = null, bool closed = false, string? attachment = null)
    {
        var created = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var thread = new SessionThread
        {
            Id = id,
            WorkspacePath = _workspace,
            UserId = "user",
            OriginChannel = parentId is null ? "test" : SubAgentThreadOrigin.ChannelName,
            Source = parentId is null ? ThreadSource.User() : ThreadSource.ForSubAgent(new SubAgentThreadSource
            {
                ParentThreadId = parentId,
                RootThreadId = "root",
                Depth = parentId == "root" ? 1 : 2
            }),
            Status = ThreadStatus.Active,
            CreatedAt = created,
            LastActiveAt = created.AddMinutes(turnCount),
            HistoryMode = HistoryMode.Server
        };
        for (var index = 1; index <= turnCount; index++)
        {
            var turnId = $"turn_{index:000}";
            var user = new SessionItem
            {
                Id = $"item_{index:000}_001",
                TurnId = turnId,
                Type = ItemType.UserMessage,
                Status = ItemStatus.Completed,
                CreatedAt = created.AddMinutes(index),
                CompletedAt = created.AddMinutes(index),
                Payload = new UserMessagePayload
                {
                    Text = $"request {index}: " + new string('u', 8192),
                    NativeInputParts = index == 1 && attachment is not null
                        ? [new SessionInputPart { Type = "localImage", Path = attachment, MimeType = "image/png", FileName = "kept.png" }]
                        : []
                }
            };
            thread.Turns.Add(new SessionTurn
            {
                Id = turnId,
                ThreadId = id,
                Status = TurnStatus.Completed,
                StartedAt = created.AddMinutes(index),
                CompletedAt = created.AddMinutes(index).AddSeconds(1),
                Input = user,
                Items = [user],
                TokenUsage = new TokenUsageInfo { InputTokens = 10, OutputTokens = 1, TotalTokens = 11 }
            });
        }
        await using var writer = new ThreadStore(CraftPath);
        await writer.SaveThreadAsync(thread);
        foreach (var turn in thread.Turns)
            await writer.AppendModelHistoryAsync(id,
                [new ChatMessage(ChatRole.User, turn.Input!.AsUserMessage!.Text), new ChatMessage(ChatRole.Assistant, "answer")], turn.Id);
        if (parentId is not null)
            await writer.UpsertThreadSpawnEdgeAsync(new ThreadSpawnEdge
            {
                ParentThreadId = parentId,
                ChildThreadId = id,
                Status = closed ? ThreadSpawnEdgeStatus.Closed : ThreadSpawnEdgeStatus.Open,
                Depth = parentId == "root" ? 1 : 2
            });
    }

    private static void AssertNoHydration(SessionService service, ThreadStore store, params string[] ids)
    {
        Assert.Equal(0, store.RolloutReplayCount);
        Assert.Equal(0, store.ForwardReplayCount);
        foreach (var id in ids)
        {
            Assert.Null(service.TryGetLoadedThread(id));
        }
    }

    private string ActivePath(string id) => Path.Combine(CraftPath, "threads", "active", id + ".jsonl");
    private string ArchivedPath(string id) => Path.Combine(CraftPath, "threads", "archived", id + ".jsonl");

    private AgentFactory CreateFactory(IChatClient chat, IToolSource resources) => new(
        dotcraftPath: CraftPath,
        workspacePath: _workspace,
        config: AppConfigTestFactory.CreateOpenAI(),
        memoryStore: new MemoryStore(CraftPath),
        skillsLoader: new SkillsLoader(CraftPath),
        approvalService: new AutoApproveApprovalService(),
        blacklist: null,
        chatClientRegistry: TestModelProviderRegistry.Create(),
        chatClient: chat,
        toolSources: [resources]);

    private static SessionService CreateService(AgentFactory factory, IChatClient chat, ThreadStore store) =>
        new(factory, chat.AsAIAgent(), new SessionPersistenceService(store), new SessionGate());

    private sealed class ThreadResources : IToolSource, IThreadScopedToolSource
    {
        public string SourceId => "archive-test-resources";
        public string? BlockThreadId { get; init; }
        public List<string> Released { get; } = [];
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
            ToolPlanningContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ToolRegistration>>([]);

        public async ValueTask ReleaseThreadAsync(string threadId, CancellationToken cancellationToken = default)
        {
            Released.Add(threadId);
            if (threadId == BlockThreadId)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
        }
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public int RequestCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            RequestCount++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            RequestCount++;
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("answer")]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
