using DotCraft.Agents;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Modules;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using Xunit;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerThreadArchiveTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "ArchiveWire_" + Guid.NewGuid().ToString("N"));
    private string CraftPath => Path.Combine(_workspace, ".craft");

    [Fact]
    public async Task Archive_ColdThread_DoesNotHydrateHistoryAndNotifiesOnlyOnce()
    {
        var at = DateTimeOffset.UtcNow;
        var thread = new SessionThread
        {
            Id = "cold-archive", WorkspacePath = _workspace, UserId = "user", OriginChannel = "appserver",
            CreatedAt = at, LastActiveAt = at, Status = ThreadStatus.Active
        };
        for (var index = 1; index <= 8; index++)
        {
            var turnId = $"turn_{index:000}";
            var input = new SessionItem
            {
                Id = $"item_{index:000}", TurnId = turnId, Type = ItemType.UserMessage,
                Status = ItemStatus.Completed, CreatedAt = at, CompletedAt = at,
                Payload = new UserMessagePayload { Text = new string('x', 2048) }
            };
            thread.Turns.Add(new SessionTurn
            {
                Id = turnId, ThreadId = thread.Id, Status = TurnStatus.Completed,
                StartedAt = at, CompletedAt = at, Input = input, Items = [input]
            });
        }
        await using (var seed = new ThreadStore(CraftPath))
            await seed.SaveThreadAsync(thread);

        await using var store = new ThreadStore(CraftPath);
        await using var factory = CreateFactory();
        var service = new SessionService(factory, factory.CreateAgentForMode(AgentMode.Agent),
            new SessionPersistenceService(store), new SessionGate());
        var transport = new InMemoryTransport();
        var handler = CreateHandler(service, transport);
        await SendAsync(Contract.AppServerMethodNames.Initialize, new
        {
            clientInfo = new { name = "test-client", version = "0.0.1" },
            capabilities = new { approvalSupport = true, streamingSupport = true }
        }, 1);
        using (var initialized = await transport.ReadNextSentAsync())
            CoreAppServerTestHarness.AssertIsSuccessResponse(initialized);
        handler.HandleInitializedNotification();

        await SendAsync(Contract.AppServerMethodNames.ThreadArchive, new { threadId = thread.Id }, 2);
        using (var response = await transport.ReadNextSentAsync())
            CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        using (var notification = await transport.ReadNextSentAsync())
        {
            CoreAppServerTestHarness.AssertIsNotification(notification, Contract.AppServerMethodNames.ThreadArchived);
            Assert.Equal(thread.Id, notification.RootElement.GetProperty("params").GetProperty("threadId").GetString());
        }
        Assert.Equal(0, store.RolloutReplayCount);
        Assert.Equal(0, store.ForwardReplayCount);
        Assert.Equal(ThreadStatus.Archived, (await service.ReadThreadSnapshotAsync(thread.Id)).Thread.Status);

        await SendAsync(Contract.AppServerMethodNames.ThreadArchive, new { threadId = thread.Id }, 3);
        using (var response = await transport.ReadNextSentAsync())
            CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.Null(transport.TryReadSent());
        Assert.Equal(0, store.RolloutReplayCount);

        async Task SendAsync(string method, object parameters, int id)
        {
            var request = InMemoryTransport.BuildRequest(method, parameters, id);
            var result = await handler.HandleRequestAsync(request, default);
            if (result != null)
                await transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(request.Id, result));
        }
    }

    [Fact]
    public async Task Archive_EphemeralThread_DoesNotRequireARollout()
    {
        await using var store = new ThreadStore(CraftPath);
        await using var factory = CreateFactory();
        var service = new SessionService(factory, factory.CreateAgentForMode(AgentMode.Agent),
            new SessionPersistenceService(store), new SessionGate());
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            WorkspacePath = _workspace, ChannelName = "appserver", UserId = "user"
        }, ephemeral: true);
        Assert.False(store.ThreadExists(thread.Id));
        var transport = new InMemoryTransport();
        var handler = CreateHandler(service, transport);
        var initialize = InMemoryTransport.BuildRequest(Contract.AppServerMethodNames.Initialize, new
        {
            clientInfo = new { name = "test-client", version = "0.0.1" },
            capabilities = new { approvalSupport = true, streamingSupport = true }
        }, id: 1);
        var initialized = await handler.HandleRequestAsync(initialize, default);
        if (initialized != null)
            await transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(initialize.Id, initialized));
        using (var response = await transport.ReadNextSentAsync())
            CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        handler.HandleInitializedNotification();

        var request = InMemoryTransport.BuildRequest(Contract.AppServerMethodNames.ThreadArchive, new { threadId = thread.Id }, id: 2);
        var result = await handler.HandleRequestAsync(request, default);
        if (result != null)
            await transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(request.Id, result));
        using (var response = await transport.ReadNextSentAsync())
            CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.Equal(ThreadStatus.Archived, thread.Status);
        Assert.False(store.ThreadExists(thread.Id));
        Assert.Equal(0, store.RolloutReplayCount);
    }

    private AgentFactory CreateFactory() => new(
        dotcraftPath: CraftPath,
        workspacePath: _workspace,
        config: AppConfigTestFactory.CreateOpenAI(),
        memoryStore: new MemoryStore(CraftPath),
        skillsLoader: new SkillsLoader(CraftPath),
        approvalService: new AutoApproveApprovalService(),
        blacklist: null,
        chatClientRegistry: TestModelProviderRegistry.Create(),
        toolSources: Array.Empty<IToolSource>());

    private AppServerRequestHandler CreateHandler(SessionService service, InMemoryTransport transport) => new(
        service, new AppServerConnection(), transport,
        new ModuleRegistryChannelListContributor(new ModuleRegistry()), new AppServerConnectionServices
        {
            ServerVersion = "0.0.1-test", WorkspaceCraftPath = CraftPath, HostWorkspacePath = _workspace,
            MemoryStore = new MemoryStore(CraftPath), SkillsLoader = new SkillsLoader(CraftPath)
        });

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_workspace)) Directory.Delete(_workspace, recursive: true);
    }
}
