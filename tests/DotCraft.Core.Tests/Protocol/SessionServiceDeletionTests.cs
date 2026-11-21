using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tests.Tools;
using DotCraft.Tools.BackgroundTerminals;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceDeletionTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "SessionDeletion_" + Guid.NewGuid().ToString("N"));
    private string CraftPath => Path.Combine(_workspace, ".craft");

    [Fact]
    public async Task ColdSubtreeDeletion_BlocksLoadingAndCanRetryAfterCleanupFailure()
    {
        await using var store = new ThreadStore(CraftPath);
        await store.SaveThreadAsync(CreateThread("root"));
        await store.SaveThreadAsync(CreateThread("child"));
        await store.UpsertThreadSpawnEdgeAsync(new ThreadSpawnEdge
        {
            ParentThreadId = "root",
            ChildThreadId = "child",
            Status = ThreadSpawnEdgeStatus.Closed,
            Depth = 1
        });

        await using var factory = new AgentFactory(CraftPath, _workspace, AppConfigTestFactory.CreateOpenAI(),
            new MemoryStore(CraftPath), new SkillsLoader(CraftPath), new AutoApproveApprovalService(),
            blacklist: null, chatClientRegistry: TestModelProviderRegistry.Create());
        var terminals = new BlockingDeleteTerminals();
        await using var service = new SessionService(factory, factory.CreateAgentForMode(AgentMode.Agent),
            new SessionPersistenceService(store), new SessionGate(), backgroundTerminalService: terminals);
        var deleted = new List<string>();
        service.ThreadDeletedForBroadcast = deleted.Add;
        var projectedBefore = store.ProjectedRolloutBytes;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteThreadPermanentlyAsync("child"));

        var deletion = service.DeleteThreadPermanentlyAsync("root");
        var timeout = TimeSpan.FromSeconds(10);
        try
        {
            await terminals.Entered.Task.WaitAsync(timeout);
            Assert.Null(service.TryGetLoadedThread("root"));
            Assert.Null(service.TryGetLoadedThread("child"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResumeThreadAsync("root").WaitAsync(timeout));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResumeThreadAsync("child").WaitAsync(timeout));
        }
        finally
        {
            terminals.Continue.TrySetResult();
        }

        await Assert.ThrowsAsync<IOException>(() => deletion.WaitAsync(timeout));
        Assert.Empty(deleted);
        Assert.Equal("child", Assert.Single(await service.ListSubAgentChildrenAsync("root", includeClosed: true)).ChildThreadId);

        await service.DeleteThreadPermanentlyAsync("root").WaitAsync(timeout);

        Assert.Equal(["child", "root"], deleted);
        Assert.False(store.ThreadExists("root"));
        Assert.False(store.ThreadExists("child"));
        Assert.Empty(await service.ListSubAgentChildrenAsync("root", includeClosed: true));
        Assert.Equal(0, store.RolloutReplayCount);
        Assert.Equal(projectedBefore, store.ProjectedRolloutBytes);
    }

    [Fact]
    public async Task ColdDeletion_WhenRolloutDeleteFails_KeepsSubscriptionOpen()
    {
        const string threadId = "thread";
        await using var store = new ThreadStore(CraftPath, stateRuntime: null,
            beforeRolloutDeleteAsync: (_, _) => throw new IOException("Injected rollout delete failure."));
        await store.SaveThreadAsync(CreateThread(threadId));
        await using var factory = new AgentFactory(CraftPath, _workspace, AppConfigTestFactory.CreateOpenAI(),
            new MemoryStore(CraftPath), new SkillsLoader(CraftPath), new AutoApproveApprovalService(),
            blacklist: null, chatClientRegistry: TestModelProviderRegistry.Create());
        await using var service = new SessionService(factory, factory.CreateAgentForMode(AgentMode.Agent),
            new SessionPersistenceService(store), new SessionGate());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var nextEvent = ReadFirstEventAsync();
        Assert.Null(service.TryGetLoadedThread(threadId));
        var failure = await Assert.ThrowsAsync<IOException>(() => service.DeleteThreadPermanentlyAsync(threadId, timeout.Token));
        Assert.Contains("Injected rollout delete failure.", failure.Message);

        await service.ResumeThreadAsync(threadId, timeout.Token);

        Assert.Equal(SessionEventType.ThreadResumed, (await nextEvent)?.EventType);

        async Task<SessionEvent?> ReadFirstEventAsync()
        {
            await foreach (var evt in service.SubscribeThreadAsync(threadId, timeout.Token))
                return evt;
            return null;
        }
    }

    private SessionThread CreateThread(string threadId)
    {
        var at = DateTimeOffset.UtcNow;
        return new SessionThread
        {
            Id = threadId,
            WorkspacePath = _workspace,
            UserId = "user",
            OriginChannel = "test",
            Source = ThreadSource.User(),
            CreatedAt = at,
            LastActiveAt = at,
            Status = ThreadStatus.Active
        };
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_workspace)) Directory.Delete(_workspace, recursive: true);
    }

    private sealed class BlockingDeleteTerminals : StubBackgroundTerminalService, IBackgroundTerminalService
    {
        private int _attempts;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<IReadOnlyList<string>> IBackgroundTerminalService.DeleteThreadArtifactsAsync(string threadId, CancellationToken ct)
        {
            if (threadId == "child" && Interlocked.Increment(ref _attempts) == 1)
            {
                Entered.TrySetResult();
                await Continue.Task.WaitAsync(ct);
                throw new IOException("Injected child cleanup failure.");
            }

            return [];
        }
    }
}
