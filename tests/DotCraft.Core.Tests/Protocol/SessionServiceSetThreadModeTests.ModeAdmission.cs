using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Sessions;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceSetThreadModeTests
{
    [Fact]
    public async Task SetThreadModeAsync_DoesNotRebuildTools()
    {
        var source = new ModeChangeToolSource();
        await using var factory = CreateAgentFactory(AppConfigTestFactory.CreateOpenAI(), [source]);
        await using var store = new ThreadStore(_tempDir);
        var service = new SessionService(factory, factory.CreateDefaultAgent(),
            new SessionPersistenceService(store), new SessionGate());
        var thread = await service.CreateThreadAsync(ModeIdentity(), new ThreadConfiguration { Model = "thread-model" });
        source.Throw = true;

        await service.SetThreadModeAsync(thread.Id, "plan");

        Assert.Equal(AgentMode.Plan, service.DebugGetRuntime(thread.Id)!.ModeManager!.CurrentMode);
        Assert.Equal("plan", (await store.LoadThreadAsync(thread.Id))!.Configuration!.Mode);
    }

    [Fact]
    public async Task SetThreadModeAsync_PublishesOnlyAfterPersistenceCompletes()
    {
        var writer = new ModeChangeWriter { Pause = true };
        await using var factory = CreateAgentFactory();
        await using var store = new ThreadStore(_tempDir, null, null, writer);
        var service = new SessionService(factory, factory.CreateDefaultAgent(),
            new SessionPersistenceService(store), new SessionGate());
        var thread = await service.CreateThreadAsync(ModeIdentity());
        var notifications = 0;
        service.ThreadUpdatedForBroadcast += _ => notifications++;

        var change = service.SetThreadModeAsync(thread.Id, "plan");
        await writer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.Equal("agent", thread.Configuration!.Mode);
            Assert.Equal(AgentMode.Agent, service.DebugGetRuntime(thread.Id)!.ModeManager!.CurrentMode);
            Assert.Equal(0, notifications);
        }
        finally { writer.Release.TrySetResult(); }
        await change.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("plan", thread.Configuration!.Mode);
        Assert.Equal(AgentMode.Plan, service.DebugGetRuntime(thread.Id)!.ModeManager!.CurrentMode);
        Assert.Equal(1, notifications);
        Assert.Equal("plan", (await store.LoadThreadAsync(thread.Id))!.Configuration!.Mode);
    }

    [Fact]
    public async Task SetThreadModeAsync_PersistenceFailureKeepsThePreviousMode()
    {
        var writer = new ModeChangeWriter { Fail = true };
        await using var factory = CreateAgentFactory();
        await using var store = new ThreadStore(_tempDir, null, null, writer);
        var service = new SessionService(factory, factory.CreateDefaultAgent(),
            new SessionPersistenceService(store), new SessionGate());
        var thread = await service.CreateThreadAsync(ModeIdentity(), new ThreadConfiguration { Model = "thread-model" });
        var notifications = 0;
        service.ThreadUpdatedForBroadcast += _ => notifications++;

        await Assert.ThrowsAsync<RolloutPersistenceException>(() => service.SetThreadModeAsync(thread.Id, "plan"));

        Assert.Equal("agent", thread.Configuration!.Mode);
        Assert.Equal(AgentMode.Agent, service.DebugGetRuntime(thread.Id)!.ModeManager!.CurrentMode);
        Assert.Equal("agent", (await store.LoadThreadAsync(thread.Id))!.Configuration!.Mode);
        Assert.Equal(0, notifications);

        writer.Fail = false;
        await service.SetThreadModeAsync(thread.Id, "plan");
        Assert.Equal("plan", thread.Configuration!.Mode);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public async Task SubmitInputAsync_AdmitsTheExplicitModeAndPersistsIt()
    {
        await using var factory = CreateAgentFactory();
        await using var store = new ThreadStore(_tempDir);
        var client = new BlockingChatClient();
        var service = new SessionService(factory, client.AsAIAgent(),
            new SessionPersistenceService(store), new SessionGate());
        var thread = await service.CreateThreadAsync(ModeIdentity(), new ThreadConfiguration { Mode = "plan" });
        SetCachedThreadAgent(service, thread.Id, client.AsAIAgent());

        var events = service.SubmitInputAsync(thread.Id, [new TextContent("Implement the plan.")],
            inputSnapshot: new SessionInputSnapshot { Mode = "agent" });
        var drain = DrainAsync(events);
        await client.StreamingStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var runtime = service.DebugGetRuntime(thread.Id)!;
            Assert.Equal("agent", Assert.Single(runtime.Turns.Values).Context!.Configuration.Mode);
            Assert.Equal("agent", thread.Configuration!.Mode);
            Assert.Equal(AgentMode.Agent, runtime.ModeManager!.CurrentMode);
            Assert.Equal("agent", (await store.LoadThreadAsync(thread.Id))!.Configuration!.Mode);
        }
        finally { client.Release(); }
        await drain.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task SubmitInputAsync_BusyRejectionDoesNotChangeTheMode()
    {
        await using var factory = CreateAgentFactory();
        await using var store = new ThreadStore(_tempDir);
        var client = new BlockingChatClient();
        var service = new SessionService(factory, client.AsAIAgent(),
            new SessionPersistenceService(store), new SessionGate());
        var thread = await service.CreateThreadAsync(ModeIdentity(), new ThreadConfiguration { Mode = "plan" });
        SetCachedThreadAgent(service, thread.Id, client.AsAIAgent());
        var drain = DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("planning")]));
        await client.StreamingStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.Throws<InvalidOperationException>(() => service.SubmitInputAsync(thread.Id,
                [new TextContent("execute")], inputSnapshot: new SessionInputSnapshot { Mode = "agent" }));
            Assert.Equal("plan", thread.Configuration!.Mode);
            Assert.Equal(AgentMode.Plan, service.DebugGetRuntime(thread.Id)!.ModeManager!.CurrentMode);
            Assert.Equal("plan", (await store.LoadThreadAsync(thread.Id))!.Configuration!.Mode);
        }
        finally { client.Release(); }
        await drain.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task SubmitInputAsync_FailedModePersistenceDoesNotAdmitATurn()
    {
        var writer = new ModeChangeWriter { Fail = true };
        await using var factory = CreateAgentFactory();
        await using var store = new ThreadStore(_tempDir, null, null, writer);
        var service = new SessionService(factory, factory.CreateDefaultAgent(),
            new SessionPersistenceService(store), new SessionGate());
        var thread = await service.CreateThreadAsync(ModeIdentity());

        Assert.Throws<RolloutPersistenceException>(() => service.SubmitInputAsync(thread.Id,
            [new TextContent("plan")], inputSnapshot: new SessionInputSnapshot { Mode = "plan" }));

        Assert.Empty(thread.Turns);
        Assert.Equal("agent", thread.Configuration!.Mode);
        Assert.Equal(AgentMode.Agent, service.DebugGetRuntime(thread.Id)!.ModeManager!.CurrentMode);
        Assert.Equal("agent", (await store.LoadThreadAsync(thread.Id))!.Configuration!.Mode);
    }

    private SessionIdentity ModeIdentity() => new()
    {
        ChannelName = "test", UserId = "u", WorkspacePath = _tempDir
    };

    private sealed class ModeChangeToolSource : IToolSource
    {
        public string SourceId => "mode-change-regression";
        public int Priority => 100;
        public bool Throw { get; set; }

        public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
            ToolPlanningContext context, CancellationToken cancellationToken = default)
        {
            if (Throw) throw new IOException("plugin discovery failed");
            return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>([]);
        }
    }

    private sealed class ModeChangeWriter : IRolloutWriter
    {
        private readonly OrderedRolloutWriter _inner = new();
        public bool Pause { get; init; }
        public bool Fail { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task AddBatchAsync(string threadId, string path,
            IReadOnlyList<ThreadRolloutRecord> records, CancellationToken ct = default)
        {
            if (records.Any(record => record.ThreadOpened?.Configuration?.Mode == "plan"))
            {
                Entered.TrySetResult();
                if (Pause) await Release.Task.WaitAsync(ct);
                if (Fail) throw new RolloutPersistenceException("mode commit failed", new IOException("test write failure"));
            }
            await _inner.AddBatchAsync(threadId, path, records, ct);
        }

        public Task<RolloutWriteReceipt> FlushAsync(string threadId, CancellationToken ct = default) => _inner.FlushAsync(threadId, ct);
        public Task CloseAsync(string threadId, CancellationToken ct = default) => _inner.CloseAsync(threadId, ct);
        public Task ShutdownAllAsync(CancellationToken ct = default) => _inner.ShutdownAllAsync(ct);
    }
}
