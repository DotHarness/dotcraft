using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using Xunit;

namespace DotCraft.Tests.Tools.Architecture;

/// <summary>Background terminals on a Remote Tool Host route are shell tasks of their Agent Thread.</summary>
public sealed class RemoteTerminalTaskTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "RTHTask_" + Guid.NewGuid().ToString("N")[..8]);

    public RemoteTerminalTaskTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task RemoteCompletionNotice_QueuesATaskNotificationForItsThread()
    {
        var client = new TerminalClient();
        var (service, threadId) = await CreateThreadAsync(client);

        client.Raise(Terminal(threadId, BackgroundTerminalStatus.Failed, exitCode: 1));
        await service.DrainTerminalNoticesAsync();

        var thread = await service.GetThreadAsync(threadId);
        var queued = Assert.Single(thread.QueuedInputs);
        Assert.Equal(SessionService.TerminalTriggerKind, queued.TriggerKind);
        Assert.Equal("term_0123456789ab", queued.TriggerRefId);
        Assert.Equal(BackgroundTerminalStatus.Failed, queued.TriggerStatus);
    }

    [Fact]
    public async Task RemoteTerminals_AreListedAndStoppedThroughTheTaskRegistry_WithoutANotice()
    {
        var client = new TerminalClient();
        var (service, threadId) = await CreateThreadAsync(client);
        client.Running = Terminal(threadId, BackgroundTerminalStatus.Running);

        var listed = Assert.Single(await service.ListBackgroundTasksAsync(threadId));
        var stopped = await service.StopBackgroundTaskAsync(threadId, listed.TaskId);
        client.Raise(Terminal(threadId, BackgroundTerminalStatus.Killed));
        await service.DrainTerminalNoticesAsync();

        Assert.Equal(BackgroundTaskStatus.Running, listed.Status);
        Assert.Equal(BackgroundTaskStatus.Stopped, stopped.Status);
        Assert.Equal(1, client.Stops);
        Assert.Empty((await service.GetThreadAsync(threadId)).QueuedInputs);
    }

    private static BackgroundTerminalSnapshot Terminal(string threadId, string status, int? exitCode = null) => new()
    {
        SessionId = "term_0123456789ab",
        ThreadId = threadId,
        TurnId = "turn_001",
        Command = "sample --record",
        Status = status,
        ExitCode = exitCode,
        OutputPath = "C:/remote/term_0123456789ab.log",
        StartedAt = DateTimeOffset.UtcNow
    };

    private async Task<(SessionService Service, string ThreadId)> CreateThreadAsync(TerminalClient client)
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        config.Tools.BackgroundTasks.CompletionDelivery = "off";
        var agentFactory = new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: new MemoryStore(_tempDir),
            skillsLoader: new SkillsLoader(_tempDir),
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            toolSources: [],
            remoteToolHostClient: client);
        var service = new SessionService(
            agentFactory,
            defaultAgent: null,
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate());
        service.AttachTerminalNotices();
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "test_user",
            WorkspacePath = _tempDir
        });
        return (service, thread.Id);
    }

    private sealed class TerminalClient : IRemoteToolHostClient
    {
        public BackgroundTerminalSnapshot? Running { get; set; }
        public int Stops { get; private set; }

        public event Action<RemoteToolRouteChange>? RouteChanged { add { } remove { } }
        public event Action<BackgroundTerminalEvent>? TerminalEvent;

        public void Raise(BackgroundTerminalSnapshot terminal) =>
            TerminalEvent?.Invoke(new BackgroundTerminalEvent
            {
                EventType = BackgroundTerminalEventTypes.CompletionNotice,
                Terminal = terminal
            });

        public IReadOnlyList<BackgroundTerminalSnapshot> ListTerminals(string threadId) =>
            Running is { } running && running.ThreadId == threadId ? [running] : [];

        public ValueTask<BackgroundTerminalSnapshot> StopTerminalAsync(string threadId, string sessionId,
            CancellationToken cancellationToken = default)
        {
            Stops++;
            Running = Running! with { Status = BackgroundTerminalStatus.Killed };
            return ValueTask.FromResult(Running);
        }

        public void UpdateRemoteToolSnapshot(string threadId, EffectiveToolSnapshot snapshot, string mode) { }
        public ValueTask PrepareTurnAsync(string threadId, EffectiveToolSnapshot snapshot, string mode,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public void ReportTurn(string threadId, string turnId, TurnStatus status) { }
        public ValueTask<RemoteToolHostCatalog> ListAsync(string threadId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<RemoteToolConnectResult> ConnectAsync(string threadId, string hostId, string workspaceId,
            CancellationToken cancellationToken = default, RemoteToolRouteInitiator initiator = RemoteToolRouteInitiator.Client) =>
            throw new NotSupportedException();
        public ValueTask<RemoteToolDisconnectResult> DisconnectAsync(string threadId, CancellationToken cancellationToken = default,
            RemoteToolRouteInitiator initiator = RemoteToolRouteInitiator.Client) => ValueTask.FromResult(new RemoteToolDisconnectResult(false));
        public bool TryGetRoute(string threadId, out RemoteToolRoute route) { route = null!; return false; }
        public bool TryGetConnectionSnapshot(string threadId, out RemoteToolConnectionSnapshot snapshot) { snapshot = null!; return false; }
        public bool TryForkRoute(string parentThreadId, string childThreadId) => false;
        public ValueTask<string> WriteImageAsync(RemoteToolRoute route, string threadId, string callId, byte[] bytes,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<byte[]> ReadImageAsync(RemoteToolRoute route, string threadId, string callId, string path,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ToolExecutionResult> InvokeAsync(RemoteToolRoute route, ToolDefinition definition, string contractHash,
            ToolInvocationContext context, JsonObject arguments, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
