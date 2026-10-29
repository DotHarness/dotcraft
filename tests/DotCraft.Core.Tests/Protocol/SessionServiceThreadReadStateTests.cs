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

public sealed class SessionServiceThreadReadStateTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "SSReadState_" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<AgentFactory> _factories = [];

    public SessionServiceThreadReadStateTests() => Directory.CreateDirectory(_tempDir);

    private string CraftPath => Path.Combine(_tempDir, ".craft");

    public void Dispose()
    {
        foreach (var factory in _factories)
            factory.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    [Fact]
    public async Task CompletedReply_IsCommittedAndPublishedBeforeTurnCompleted()
    {
        var (service, published) = CreateService(ScriptedChatClient.Reply("done"));
        var thread = await service.CreateThreadAsync(Identity());

        var publishedAtCompletion = -1;
        await foreach (var evt in service.SubmitInputAsync(thread.Id, [new TextContent("hi")]))
        {
            if (evt.EventType == SessionEventType.TurnCompleted)
                publishedAtCompletion = published.Count;
        }

        Assert.Equal(1, publishedAtCompletion);
        var (threadId, first) = Assert.Single(published);
        Assert.Equal(thread.Id, threadId);
        Assert.Equal("turn_001", first.FirstUnread?.TurnId);
        Assert.Equal(first, await service.GetThreadReadStateAsync(thread));

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("again")]));

        var second = await service.GetThreadReadStateAsync(thread);
        Assert.Equal("turn_001", second?.FirstUnread?.TurnId);
        Assert.NotEqual(first.Revision, second?.Revision);
        Assert.Equal(2, published.Count);
    }

    [Fact]
    public async Task FailedTurn_IsANewResultPublishedBeforeTurnFailed()
    {
        var (service, published) = CreateService(ScriptedChatClient.Throw("provider exploded"));
        var thread = await service.CreateThreadAsync(Identity());

        var publishedAtFailure = -1;
        await foreach (var evt in service.SubmitInputAsync(thread.Id, [new TextContent("hi")]))
        {
            if (evt.EventType == SessionEventType.TurnFailed)
                publishedAtFailure = published.Count;
        }

        Assert.Equal(TurnStatus.Failed, thread.Turns[^1].Status);
        Assert.Equal(1, publishedAtFailure);
        Assert.Equal("turn_001", (await service.GetThreadReadStateAsync(thread))?.FirstUnread?.TurnId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancelledTurn_IsANewResultOnlyWithAReply(bool withReply)
    {
        var client = ScriptedChatClient.ReplyThenHang(withReply ? "partial" : null);
        var (service, published) = CreateService(client);
        var thread = await service.CreateThreadAsync(Identity());

        var drain = DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("hi")]));
        await client.Hanging.WaitAsync(TimeSpan.FromSeconds(10));
        await service.CancelTurnAsync(thread.Id, "turn_001");
        await drain;

        Assert.Equal(TurnStatus.Cancelled, thread.Turns[^1].Status);
        var state = await service.GetThreadReadStateAsync(thread);
        Assert.Equal(withReply ? "turn_001" : null, state?.FirstUnread?.TurnId);
        Assert.Equal(withReply ? 1 : 0, published.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ActiveGoalTurn_IsANewResultOnlyWithoutAutomaticContinuation(bool autoContinue)
    {
        var client = ScriptedChatClient.ReplyThenHangOnNextCall("progress");
        var (service, published) = CreateService(
            client,
            config => config.Goals = new AppConfig.GoalsConfig { AutoContinueEnabled = autoContinue });
        var thread = await service.CreateThreadAsync(Identity());
        await service.SetThreadGoalAsync(thread.Id, new ThreadGoalUpdate { Objective = "Keep going" });

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("work")]));

        if (autoContinue)
        {
            await client.Hanging.WaitAsync(TimeSpan.FromSeconds(10));
            await service.CancelTurnAsync(thread.Id, thread.Turns[^1].Id);
            Assert.Null((await service.GetThreadReadStateAsync(thread))?.FirstUnread);
        }
        else
        {
            Assert.Equal("turn_001", (await service.GetThreadReadStateAsync(thread))?.FirstUnread?.TurnId);
            Assert.Single(published);
        }
    }

    [Fact]
    public async Task ColdLoadCancellationAndImport_LeaveReceiptsUnchanged()
    {
        var interruptedId = await SeedInterruptedThreadAsync();
        var (service, published) = CreateService(ScriptedChatClient.Reply("unused"));

        var loaded = await service.EnsureThreadLoadedAsync(interruptedId);
        var imported = await service.ImportThreadAsync(new ThreadImportRequest
        {
            Identity = Identity() with { ChannelName = ThreadImportConstants.ChannelName },
            ThreadId = "thread_import_sample_0123456789abcdef",
            DisplayName = "Imported",
            Metadata = new Dictionary<string, string>(),
            Turns =
            [
                new ImportedTurnInput
                {
                    UserText = "question",
                    AgentTexts = ["answer"],
                    StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                    CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
                }
            ]
        });

        Assert.Equal(TurnStatus.Cancelled, Assert.Single(loaded.Turns).Status);
        Assert.Empty(published);
        Assert.Null((await service.GetThreadReadStateAsync(loaded))?.FirstUnread);
        Assert.Null((await service.GetThreadReadStateAsync(imported.Thread))?.FirstUnread);
    }

    [Fact]
    public async Task Marks_RequireTheCurrentRevision_WithoutLoadingOrReorderingThreads()
    {
        var (writer, _) = CreateService(ScriptedChatClient.Reply("done"));
        var thread = await writer.CreateThreadAsync(Identity());
        await DrainAsync(writer.SubmitInputAsync(thread.Id, [new TextContent("hi")]));
        var unread = (await writer.GetThreadReadStateAsync(thread))!;

        var (service, published) = CreateService(ScriptedChatClient.Reply("unused"));
        var before = Assert.Single(await service.FindThreadsAsync(Identity()));
        Assert.Equal(unread, (await service.GetThreadReadStatesAsync([before]))?[thread.Id]);

        var conflict = await Assert.ThrowsAsync<ThreadReadStateConflictException>(() =>
            service.UpdateThreadReadStateAsync(thread.Id, ThreadReadStateStore.InitialRevision, ThreadReadStateOperation.Read));
        Assert.Equal(unread, conflict.Current);

        var read = await service.UpdateThreadReadStateAsync(thread.Id, unread.Revision, ThreadReadStateOperation.Read);
        var markedUnread = await service.UpdateThreadReadStateAsync(thread.Id, read.Revision, ThreadReadStateOperation.Unread);

        Assert.Null(read.FirstUnread);
        Assert.NotEqual(unread.Revision, read.Revision);
        Assert.Equal(ThreadFirstUnread.ThreadStart, markedUnread.FirstUnread);
        Assert.Equal(new[] { read, markedUnread }, published.Select(static entry => entry.State));
        Assert.Null(service.TryGetLoadedThread(thread.Id));
        var after = Assert.Single(await service.FindThreadsAsync(Identity()));
        Assert.Equal(before.LastActiveAt, after.LastActiveAt);

        await service.ResumeThreadAsync(thread.Id);
        await service.ReadThreadSnapshotAsync(thread.Id);
        var fork = await service.ForkThreadAsync(thread.Id);

        Assert.Equal(markedUnread, await service.GetThreadReadStateAsync(thread));
        Assert.Equal(
            new ThreadReadState(null, ThreadReadStateStore.InitialRevision),
            await service.GetThreadReadStateAsync(fork));
    }

    [Fact]
    public async Task IneligibleAndUnknownThreads_HaveNoReceipt()
    {
        var (service, published) = CreateService(ScriptedChatClient.Reply("done"));
        var ephemeral = await service.CreateThreadAsync(Identity(), ephemeral: true);
        var parent = await service.CreateThreadAsync(Identity());
        var internalThread = await service.CreateThreadAsync(Identity() with { ChannelName = CommitMessageSuggestConstants.ChannelName });
        var child = await service.CreateThreadAsync(
            Identity(),
            source: ThreadSource.ForSubAgent(new SubAgentThreadSource
            {
                ParentThreadId = parent.Id,
                RootThreadId = parent.Id,
                Depth = 1
            }));

        await DrainAsync(service.SubmitInputAsync(ephemeral.Id, [new TextContent("hi")]));

        foreach (var thread in new[] { ephemeral, internalThread, child })
        {
            Assert.Null(await service.GetThreadReadStateAsync(thread));
            await Assert.ThrowsAsync<ThreadReadStateUnavailableException>(() =>
                service.UpdateThreadReadStateAsync(thread.Id, ThreadReadStateStore.InitialRevision, ThreadReadStateOperation.Unread));
        }

        var summaries = await service.FindThreadsAsync(
            Identity(),
            includeSubAgents: true,
            scope: ThreadDiscoveryScope.Workspace);
        Assert.Equal(new[] { parent.Id }, (await service.GetThreadReadStatesAsync(summaries))!.Keys);
        Assert.Empty(published);
        await Assert.ThrowsAsync<ThreadNotFoundException>(() =>
            service.UpdateThreadReadStateAsync("thread_missing", ThreadReadStateStore.InitialRevision, ThreadReadStateOperation.Read));
    }

    [Fact]
    public async Task Rollback_ClearsARemovedFirstUnreadTurn_AndKeepsAThreadStartMark()
    {
        var (service, published) = CreateService(ScriptedChatClient.Reply("done"));
        var thread = await service.CreateThreadAsync(Identity());
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("one")]));
        var read = await service.UpdateThreadReadStateAsync(
            thread.Id,
            (await service.GetThreadReadStateAsync(thread))!.Revision,
            ThreadReadStateOperation.Read);
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("two")]));
        Assert.Equal("turn_002", (await service.GetThreadReadStateAsync(thread))?.FirstUnread?.TurnId);

        await service.RollbackThreadAsync(thread.Id, 1);

        var cleared = (await service.GetThreadReadStateAsync(thread))!;
        Assert.Null(cleared.FirstUnread);
        Assert.NotEqual(read.Revision, cleared.Revision);
        Assert.Equal(cleared, published[^1].State);

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("two again")]));
        var replayed = (await service.GetThreadReadStateAsync(thread))!;
        Assert.Equal(thread.Turns[^1].Id, replayed.FirstUnread?.TurnId);

        var readAgain = await service.UpdateThreadReadStateAsync(thread.Id, replayed.Revision, ThreadReadStateOperation.Read);
        var mark = await service.UpdateThreadReadStateAsync(thread.Id, readAgain.Revision, ThreadReadStateOperation.Unread);
        var publishedBeforeRollback = published.Count;
        await service.RollbackThreadAsync(thread.Id, 1);

        Assert.Equal(mark, await service.GetThreadReadStateAsync(thread));
        Assert.Equal(publishedBeforeRollback, published.Count);
    }

    [Fact]
    public async Task Receipts_AreRemovedWithTheirThread()
    {
        var (service, _) = CreateService(ScriptedChatClient.Reply("done"));
        var thread = await service.CreateThreadAsync(Identity());
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("hi")]));
        Assert.NotNull((await service.GetThreadReadStateAsync(thread))?.FirstUnread);

        await service.DeleteThreadPermanentlyAsync(thread.Id);

        using var database = new DotCraft.Persistence.WorkspaceStateDatabase(CraftPath);
        Assert.Equal(
            new ThreadReadState(null, ThreadReadStateStore.InitialRevision),
            new ThreadReadStateStore(database).Load(thread.Id));
    }

    private (SessionService Service, List<(string ThreadId, ThreadReadState State)> Published) CreateService(
        IChatClient chatClient,
        Action<AppConfig>? configure = null)
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        configure?.Invoke(config);
        var agentFactory = new AgentFactory(
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
        _factories.Add(agentFactory);
        var service = new SessionService(
            agentFactory,
            chatClient.AsAIAgent(),
            new SessionPersistenceService(new ThreadStore(CraftPath)),
            new SessionGate());
        var published = new List<(string ThreadId, ThreadReadState State)>();
        service.ThreadReadStateChangedForBroadcast = (threadId, state) =>
        {
            lock (published)
                published.Add((threadId, state));
        };
        return (service, published);
    }

    private SessionIdentity Identity() => new()
    {
        ChannelName = "test",
        UserId = "u",
        WorkspacePath = _tempDir
    };

    private async Task<string> SeedInterruptedThreadAsync()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var thread = new SessionThread
        {
            Id = SessionIdGenerator.NewThreadId(),
            WorkspacePath = _tempDir,
            UserId = "u",
            OriginChannel = "test",
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
            Payload = new UserMessagePayload { Text = "request" }
        };
        var reply = new SessionItem
        {
            Id = "item_002",
            TurnId = "turn_001",
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = new AgentMessagePayload { Text = "partial reply" }
        };
        var turn = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = thread.Id,
            Status = TurnStatus.Running,
            Input = input,
            Items = [input, reply],
            StartedAt = now,
            Error = "interrupted diagnostic"
        };

        await using var store = new ThreadStore(CraftPath);
        await store.SaveThreadAsync(thread);
        thread.Turns.Add(turn);
        await store.SaveTurnAsync(thread, turn);
        return thread.Id;
    }

    private static async Task DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    private sealed class ScriptedChatClient(
        Func<ScriptedChatClient, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> respond) : IChatClient
    {
        private readonly TaskCompletionSource _hanging = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Hanging => _hanging.Task;

        public static ScriptedChatClient Reply(string text) => new((_, _) => Updates(text));

        public static ScriptedChatClient Throw(string message) => new((_, _) => Fail(message));

        public static ScriptedChatClient ReplyThenHang(string? text) =>
            new((client, ct) => Hang(text, client._hanging, ct));

        public static ScriptedChatClient ReplyThenHangOnNextCall(string text)
        {
            var calls = 0;
            return new((client, ct) => Interlocked.Increment(ref calls) == 1 ? Updates(text) : Hang(null, client._hanging, ct));
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "title")));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            respond(this, cancellationToken);

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        private static async IAsyncEnumerable<ChatResponseUpdate> Updates(string text)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(text)]);
            await Task.CompletedTask;
        }

        private static async IAsyncEnumerable<ChatResponseUpdate> Fail(string message)
        {
            await Task.Yield();
            if (message.Length > 0)
                throw new InvalidOperationException(message);
            yield break;
        }

        private static async IAsyncEnumerable<ChatResponseUpdate> Hang(
            string? text,
            TaskCompletionSource hanging,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            if (text is not null)
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(text)]);
            hanging.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        }
    }
}
