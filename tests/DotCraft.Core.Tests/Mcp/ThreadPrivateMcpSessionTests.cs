using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Mcp;
using DotCraft.McpTestServer;
using DotCraft.Memory;
using DotCraft.Persistence;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Mcp;

// Stateful stdio reuse requires a stable inherited environment while other tests change process variables.
[CollectionDefinition("Thread MCP environment", DisableParallelization = true)]
public sealed class ThreadMcpEnvironmentCollection;

[Collection("Thread MCP environment")]
public sealed class ThreadPrivateMcpSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InheritedServers_KeepStateAcrossTurnsWithoutSharingWithOtherThreads(bool plugin)
    {
        await using var harness = await Harness.CreateAsync(plugin);
        var parent = await harness.StartAsync();
        var other = await harness.StartAsync();
        var first = await harness.CallAsync(parent.Id, 42);
        var workspace = await harness.Workspace.CallToolAsync(harness.ServerName, "state");
        Assert.Equal(0, JsonSerializer.SerializeToElement(workspace.StructuredContent).GetProperty("value").GetInt32());
        var sibling = await harness.CallAsync(other.Id);
        Assert.Equal(0, sibling.GetProperty("value").GetInt32());
        Assert.NotEqual(first.GetProperty("session").GetString(), sibling.GetProperty("session").GetString());
        foreach (var text in new[] { "first", "second" })
        {
            var completed = false;
            await foreach (var item in harness.Sessions.SubmitInputAsync(parent.Id, text))
                completed |= item.EventType == SessionEventType.TurnCompleted;
            Assert.True(completed);
        }
        await harness.Sessions.SetThreadModeAsync(parent.Id, "plan");
        await harness.Sessions.SetThreadModeAsync(parent.Id, "agent");
        await harness.Sessions.RefreshThreadAgentAsync(parent.Id);
        var next = await harness.CallAsync(parent.Id);
        Assert.Equal(first.GetProperty("session").GetString(), next.GetProperty("session").GetString());
        Assert.Equal(42, next.GetProperty("value").GetInt32());
        var fork = await harness.Sessions.ForkThreadAsync(parent.Id);
        var forkState = await harness.CallAsync(fork.Id);
        Assert.Equal(0, forkState.GetProperty("value").GetInt32());
        Assert.NotEqual(first.GetProperty("session").GetString(), forkState.GetProperty("session").GetString());
        var manager = (await harness.Sessions.GetEffectiveMcpRuntimeAsync(parent.Id))!;
        Assert.Equal(plugin ? "plugin" : "workspace", Assert.Single(await manager.ListConfigsAsync()).Origin.Kind);
    }

    [Fact]
    public async Task ArchiveAndUnload_ReleaseOnlyOwnedProcesses()
    {
        await using var harness = await Harness.CreateAsync();
        var archived = await harness.StartAsync();
        var idle = await harness.StartAsync();
        var survivor = await harness.StartAsync(ephemeral: true);
        var a = await harness.CallAsync(archived.Id);
        var b = await harness.CallAsync(idle.Id, 5);
        var c = await harness.CallAsync(survivor.Id, 8);
        using var processA = Process.GetProcessById(a.GetProperty("pid").GetInt32());
        using var processB = Process.GetProcessById(b.GetProperty("pid").GetInt32());
        using var processC = Process.GetProcessById(c.GetProperty("pid").GetInt32());
        await harness.Sessions.ArchiveThreadAsync(archived.Id);
        await processA.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Sessions.GetEffectiveToolSnapshotAsync(archived.Id));
        await harness.Sessions.SetBindingMcpServersAsync(archived.Id, "removed-binding", []);
        Assert.Null(await harness.Sessions.GetEffectiveMcpRuntimeAsync(archived.Id));
        Assert.False(processC.HasExited);
        await harness.Sessions.UnloadIdleThreadsAsync(DateTimeOffset.UtcNow.AddHours(1), TimeSpan.FromMinutes(30));
        await processB.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        var restored = await harness.CallAsync(idle.Id);
        Assert.NotEqual(b.GetProperty("session").GetString(), restored.GetProperty("session").GetString());
        Assert.Equal(0, restored.GetProperty("value").GetInt32());
        var stillLive = await harness.CallAsync(survivor.Id);
        Assert.Equal(c.GetProperty("session").GetString(), stillLive.GetProperty("session").GetString());
        Assert.Equal(8, stillLive.GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task ProfileOnlyTask_DoesNotStartInheritedServers()
    {
        await using var harness = await Harness.CreateAsync();
        var updates = new System.Collections.Concurrent.ConcurrentQueue<string>();
        harness.Sessions.ThreadMcpStatusChangedForBroadcast = (id, _) => updates.Enqueue(id);
        var thread = await harness.StartAsync(profileOnly: true);
        Assert.Null(await harness.Sessions.GetEffectiveMcpRuntimeAsync(thread.Id));
        Assert.Empty(updates);
        var snapshot = await harness.Sessions.GetEffectiveToolSnapshotAsync(thread.Id);
        Assert.DoesNotContain(snapshot.Registrations.Values, registration => registration.Definition.Id.Kind == ToolSourceKind.Mcp);
    }

    [Fact]
    public async Task CancelledFork_ReleasesItsUnpublishedMcpProcess()
    {
        var gate = new BuildGate();
        await using var harness = await Harness.CreateAsync(finalizer: gate);
        var parent = await harness.StartAsync();
        gate.Block = true;
        using var cancellation = new CancellationTokenSource();
        var fork = harness.Sessions.ForkThreadAsync(parent.Id, ct: cancellation.Token);
        var started = await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        using var process = Process.GetProcessById(started.Pid);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fork);
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        await Assert.ThrowsAsync<ThreadNotFoundException>(() => harness.Sessions.GetThreadAsync(started.ThreadId));
        Assert.Equal(0, (await harness.CallAsync(parent.Id)).GetProperty("value").GetInt32());
    }

    private sealed class BuildGate : IToolSnapshotFinalizer
    {
        public bool Block { get; set; }
        public TaskCompletionSource<(string ThreadId, int Pid)> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<ToolSnapshotFinalization> FinalizeAsync(EffectiveToolSnapshot snapshot,
            ToolPlanningContext context, CancellationToken cancellationToken = default)
        {
            if (!Block) return ToolSnapshotFinalization.None;
            var tool = Assert.Single(snapshot.Registrations.Values, item => item.Definition.Id.Kind == ToolSourceKind.Mcp);
            var state = await tool.Binding.Runtime.InvokeAsync(new ToolInvocationContext(
                context.ThreadId, null, "fixture", ToolInvocationAudience.Host,
                tool.Definition.Name, tool.Definition.Id, tool.Binding.Id, snapshot.Revision, DateTimeOffset.UtcNow),
                new JsonObject(), cancellationToken);
            Assert.True(state.Success, state.Error?.Message);
            Started.TrySetResult((context.ThreadId, state.StructuredContent!.Value.GetProperty("pid").GetInt32()));
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return ToolSnapshotFinalization.None;
        }
    }

    private sealed class LocalSource : AIFunctionToolSource
    {
        public override string SourceId => "local";
        protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context) =>
            [AIFunctionFactory.Create(() => "ok", "local")];
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string _root;
        private readonly WorkspaceStateDatabase _database;
        private readonly ThreadStore _store;
        private readonly AgentFactory _factory;
        public McpClientManager Workspace { get; }
        public SessionService Sessions { get; }
        public string ServerName { get; }

        private Harness(string root, McpClientManager workspace, WorkspaceStateDatabase database, ThreadStore store,
            AgentFactory factory, SessionService sessions, string serverName)
        {
            _root = root; Workspace = workspace; _database = database; _store = store;
            _factory = factory; Sessions = sessions; ServerName = serverName;
        }

        public static async Task<Harness> CreateAsync(bool plugin = false, IToolSnapshotFinalizer? finalizer = null)
        {
            var root = Path.Combine(Path.GetTempPath(), $"thread-mcp-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var server = new McpServerConfig
            {
                Name = plugin ? "plugin:fixture:state" : "state", Command = "dotnet",
                Arguments = [McpFixtureProcess.AssemblyPath, "stateful"], ToolApprovalMode = McpToolApprovalMode.Approve,
                Origin = plugin ? McpServerOrigin.Plugin("fixture", "Fixture", "state") : McpServerOrigin.Workspace()
            };
            var workspace = new McpClientManager();
            await workspace.ConnectAsync([server]);
            await workspace.WaitForStartupCompletionAsync();
            var status = Assert.Single(await workspace.ListStatusesAsync());
            Assert.True(status.StartupState == "ready", status.LastError);
            var config = AppConfigTestFactory.CreateOpenAI();
            config.Tools.DeferredLoading.Strategy = AppConfig.DeferredLoadingStrategy.Off;
            var memory = new MemoryStore(root);
            var skills = new SkillsLoader(root);
            var approval = new AutoApproveApprovalService();
            var registry = TestModelProviderRegistry.Create();
            var client = new ReplyClient();
            var modelRuntime = registry.ResolveMainRuntime(config);
            var factory = new AgentFactory(root, root, config, memory, skills, approval, null,
                runtimeContext: new AgentRuntimeContext
                {
                    Config = config, WorkspacePath = root, BotPath = root, MemoryStore = memory,
                    SkillsLoader = skills, ContextPageManager = new ContextPageManager(), ApprovalService = approval,
                    ChatClientRegistry = registry, McpClientManager = workspace, ChatClient = client,
                    EffectiveProviderId = modelRuntime.ProviderId, EffectiveProviderProtocol = modelRuntime.Protocol,
                    EffectiveMainModel = modelRuntime.Model
                }, chatClientRegistry: registry, chatClient: client);
            var database = new WorkspaceStateDatabase(root);
            var store = new ThreadStore(root, database);
            var profiles = new ToolProfileRegistry();
            profiles.Register("local", [new LocalSource()]);
            var sessions = new SessionService(factory, null, new SessionPersistenceService(store), new SessionGate(),
                toolProfileRegistry: profiles, toolSnapshotFinalizers: finalizer is null ? [] : [finalizer]);
            return new Harness(root, workspace, database, store, factory, sessions, server.Name);
        }

        public Task<SessionThread> StartAsync(bool ephemeral = false, bool profileOnly = false) => Sessions.CreateThreadAsync(
            new SessionIdentity { ChannelName = "test", UserId = "user", WorkspacePath = _root },
            new ThreadConfiguration
            {
                Mode = "agent", ApprovalPolicy = ApprovalPolicy.AutoApprove,
                UseToolProfileOnly = profileOnly, ToolProfile = profileOnly ? "local" : null
            }, ephemeral: ephemeral);

        public async Task<JsonElement> CallAsync(string threadId, int? value = null)
        {
            var snapshot = await Sessions.GetEffectiveToolSnapshotAsync(threadId);
            var tool = Assert.Single(snapshot.Registrations.Values, registration =>
                registration.Definition.Id.Kind == ToolSourceKind.Mcp);
            var arguments = value is null ? new JsonObject() : new JsonObject { ["value"] = value.Value };
            var result = await Sessions.DispatchThreadToolAsync(threadId, tool.Definition.Name, arguments,
                Guid.NewGuid().ToString("N"), ToolInvocationAudience.Host);
            Assert.True(result.Success, result.Error?.Message);
            return result.StructuredContent!.Value;
        }

        public async ValueTask DisposeAsync()
        {
            await Sessions.DisposeAsync();
            await _factory.DisposeAsync();
            await _store.DisposeAsync();
            _database.Dispose();
            await Workspace.DisposeAsync();
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class ReplyClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
            await Task.CompletedTask;
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
