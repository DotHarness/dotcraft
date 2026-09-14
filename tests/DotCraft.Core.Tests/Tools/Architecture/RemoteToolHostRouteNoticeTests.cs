using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Tools.Architecture;

/// <summary>
/// Covers the persisted <c>remoteRoute</c> timeline notice and the presentation descriptor the
/// three Remote Tool Host control tools carry.
/// </summary>
public sealed class RemoteToolHostRouteNoticeTests : IDisposable
{
    private const string HostId = "peer_001";
    private const string WorkspaceId = "workspace_001";

    private readonly string _tempDir;

    public RemoteToolHostRouteNoticeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RTHNotice_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task Connect_AppendsNoticeToRunningTurnWithRoutePayload()
    {
        var client = new RouteEventClient();
        var (service, thread) = await CreateThreadAsync(client);
        thread.Turns.Add(MakeTurn("turn_001", TurnStatus.Completed));
        thread.Turns.Add(MakeTurn("turn_002", TurnStatus.Running));

        await client.ConnectAsync(thread.Id, HostId, WorkspaceId);
        await service.DrainRemoteRouteNoticesAsync();

        var notice = SingleNotice(thread, "turn_002");
        Assert.Equal("remoteRoute", notice.Kind);
        Assert.Equal("connected", notice.Reason);
        Assert.Equal("client", notice.Initiator);
        Assert.Equal(HostId, notice.HostId);
        Assert.Equal(WorkspaceId, notice.WorkspaceId);
        Assert.Equal("Studio PC", notice.HostName);
        Assert.Equal("game-client", notice.WorkspaceName);
        Assert.DoesNotContain(thread.Turns.Single(turn => turn.Id == "turn_001").Items, IsRemoteRouteNotice);

        var wire = JsonSerializer.SerializeToElement(
            Assert.Single(thread.Turns.Single(turn => turn.Id == "turn_002").Items, IsRemoteRouteNotice).ToWire(),
            SessionWireJsonOptions.Default);
        Assert.Equal("systemNotice", wire.GetProperty("payloadKind").GetString());
        var payload = wire.GetProperty("payload");
        Assert.Equal("remoteRoute", payload.GetProperty("kind").GetString());
        Assert.Equal("connected", payload.GetProperty("reason").GetString());
        Assert.Equal("client", payload.GetProperty("initiator").GetString());
        Assert.Equal(HostId, payload.GetProperty("hostId").GetString());
        Assert.Equal("Studio PC", payload.GetProperty("hostName").GetString());
        Assert.Equal(WorkspaceId, payload.GetProperty("workspaceId").GetString());
        Assert.Equal("game-client", payload.GetProperty("workspaceName").GetString());
        Assert.False(payload.TryGetProperty("sourceThreadId", out _));
    }

    [Fact]
    public async Task Disconnect_WhenThreadIdle_AppendsNoticeToLastCompletedTurn()
    {
        var client = new RouteEventClient();
        var (service, thread) = await CreateThreadAsync(client);
        thread.Turns.Add(MakeTurn("turn_001", TurnStatus.Completed));
        thread.Turns.Add(MakeTurn("turn_002", TurnStatus.Completed));
        await client.ConnectAsync(thread.Id, HostId, WorkspaceId);

        await client.DisconnectAsync(thread.Id, initiator: RemoteToolRouteInitiator.Agent);
        await service.DrainRemoteRouteNoticesAsync();

        var notices = thread.Turns
            .Single(turn => turn.Id == "turn_002")
            .Items
            .Select(item => item.Payload)
            .OfType<SystemNoticePayload>()
            .Where(payload => payload.Kind == "remoteRoute")
            .ToArray();
        Assert.Equal(2, notices.Length);
        Assert.Equal("disconnected", notices[1].Reason);
        Assert.Equal("agent", notices[1].Initiator);
        Assert.Equal(HostId, notices[1].HostId);
        Assert.Equal(WorkspaceId, notices[1].WorkspaceId);
    }

    /// <summary>A divider with nothing to divide is noise, so a thread with no turn records nothing.</summary>
    [Fact]
    public async Task Connect_WhenThreadHasNoTurn_RecordsNothing()
    {
        var client = new RouteEventClient();
        var (service, thread) = await CreateThreadAsync(client);

        await client.ConnectAsync(thread.Id, HostId, WorkspaceId);
        await service.DrainRemoteRouteNoticesAsync();

        Assert.Empty(thread.Turns);
    }

    /// <summary>Thread release and teardown are not history.</summary>
    [Fact]
    public async Task SystemInitiator_RecordsNothing()
    {
        var client = new RouteEventClient();
        var (service, thread) = await CreateThreadAsync(client);
        thread.Turns.Add(MakeTurn("turn_001", TurnStatus.Completed));
        await client.ConnectAsync(thread.Id, HostId, WorkspaceId, initiator: RemoteToolRouteInitiator.System);

        await client.DisconnectAsync(thread.Id, initiator: RemoteToolRouteInitiator.System);
        await service.DrainRemoteRouteNoticesAsync();

        Assert.DoesNotContain(thread.Turns.Single().Items, IsRemoteRouteNotice);
    }

    [Fact]
    public async Task LeaseLoss_AppendsNoticeAndSurvivesThreadReload()
    {
        var client = new RouteEventClient();
        var (service, thread) = await CreateThreadAsync(client);
        thread.Turns.Add(MakeTurn("turn_001", TurnStatus.Completed));
        await client.ConnectAsync(thread.Id, HostId, WorkspaceId);

        client.RaiseLeaseLost(thread.Id, RemoteToolRouteInitiator.System);
        await service.DrainRemoteRouteNoticesAsync();

        var reloaded = await service.GetThreadAsync(thread.Id);
        var notice = reloaded.Turns
            .SelectMany(turn => turn.Items)
            .Select(item => item.Payload)
            .OfType<SystemNoticePayload>()
            .Last(payload => payload.Kind == "remoteRoute");
        Assert.Equal("leaseLost", notice.Reason);
        Assert.Equal(HostId, notice.HostId);
    }

    /// <summary>
    /// A running Turn holds the session gate for its whole run, so a notice that waited for the gate
    /// could only ever land after the Turn's last Item.
    /// </summary>
    [Fact]
    public async Task Notice_RaisedDuringATurn_LandsAtTheItemBoundaryTheTurnReached()
    {
        var client = new RouteEventClient();
        var gate = new SessionGate();
        var (service, thread) = await CreateThreadAsync(client, gate);
        var turn = MakeTurn("turn_001", TurnStatus.Running);
        turn.Items.Add(MakeItem(turn.Id, "item_001"));
        turn.Items.Add(MakeItem(turn.Id, "item_002"));
        thread.Turns.Add(turn);

        using (await gate.AcquireAsync(thread.Id))
        {
            await client.ConnectAsync(thread.Id, HostId, WorkspaceId);
            var sequence = SessionIdGenerator.LastItemSequence(turn.Items);
            var drained = service.DrainRemoteRouteNoticesIntoTurn(thread.Id, turn, () => ++sequence);
            Assert.Equal("item_003", Assert.Single(drained).Id);
            turn.Items.Add(MakeItem(turn.Id, "item_004"));
        }

        await service.DrainRemoteRouteNoticesAsync();

        Assert.Equal(2, turn.Items.FindIndex(IsRemoteRouteNotice));
        Assert.Single(turn.Items, IsRemoteRouteNotice);
    }

    /// <summary>A duplicate id would silently overwrite the Item already holding it.</summary>
    [Fact]
    public async Task Notice_AllocatesAboveTheTurnsHighestItemSequence()
    {
        var client = new RouteEventClient();
        var (service, thread) = await CreateThreadAsync(client);
        var turn = MakeTurn("turn_001", TurnStatus.Completed);
        turn.Items.Add(MakeItem(turn.Id, "item_001"));
        turn.Items.Add(MakeItem(turn.Id, "item_005"));
        thread.Turns.Add(turn);

        await client.ConnectAsync(thread.Id, HostId, WorkspaceId);
        await service.DrainRemoteRouteNoticesAsync();

        Assert.Equal("item_006", Assert.Single(turn.Items, IsRemoteRouteNotice).Id);
    }

    [Fact]
    public async Task Control_tools_carry_the_remote_tool_host_presentation()
    {
        var source = new RemoteToolHostControlSource(new RouteEventClient());

        var registrations = await source.GetRegistrationsAsync(new ToolPlanningContext(
            "thread-1",
            "turn-1",
            Directory.GetCurrentDirectory(),
            Directory.GetCurrentDirectory(),
            "agent",
            null,
            [],
            1));

        var operations = registrations.ToDictionary(
            registration => registration.Definition.Name.ToString(),
            registration =>
            {
                var presentation = registration.Definition.Presentation;
                Assert.NotNull(presentation);
                Assert.Equal("core.remote-tool-host", presentation!.Id.ToString());
                return presentation.Options!["operation"].GetString();
            },
            StringComparer.Ordinal);

        Assert.Equal("list", operations["RemoteToolHost.List"]);
        Assert.Equal("connect", operations["RemoteToolHost.Connect"]);
        Assert.Equal("disconnect", operations["RemoteToolHost.Disconnect"]);
    }

    private static bool IsRemoteRouteNotice(SessionItem item) =>
        item.Payload is SystemNoticePayload { Kind: "remoteRoute" };

    private static SystemNoticePayload SingleNotice(SessionThread thread, string turnId) =>
        Assert.IsType<SystemNoticePayload>(
            Assert.Single(thread.Turns.Single(turn => turn.Id == turnId).Items, IsRemoteRouteNotice).Payload);

    private static SessionItem MakeItem(string turnId, string itemId) => new()
    {
        Id = itemId,
        TurnId = turnId,
        Type = ItemType.AgentMessage,
        Status = ItemStatus.Completed,
        CreatedAt = DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow,
        Payload = new AgentMessagePayload { Text = itemId }
    };

    private static SessionTurn MakeTurn(string turnId, TurnStatus status) => new()
    {
        Id = turnId,
        Status = status,
        StartedAt = DateTimeOffset.UtcNow,
        CompletedAt = status == TurnStatus.Completed ? DateTimeOffset.UtcNow : null
    };

    private async Task<(SessionService Service, SessionThread Thread)> CreateThreadAsync(
        RouteEventClient client,
        SessionGate? gate = null)
    {
        var service = CreateService(client, gate ?? new SessionGate());
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "test_user",
            WorkspacePath = _tempDir
        });
        return (service, thread);
    }

    private SessionService CreateService(RouteEventClient client, SessionGate gate)
    {
        var agentFactory = new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: AppConfigTestFactory.CreateOpenAI(),
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
            gate);
        service.AttachRemoteRouteNotices();
        return service;
    }

    /// <summary>Raises the route-change event exactly where the real client raises it.</summary>
    private sealed class RouteEventClient : IRemoteToolHostClient
    {
        private readonly Dictionary<string, RemoteToolRoute> _routes = new(StringComparer.Ordinal);

        public event Action<RemoteToolRouteChange>? RouteChanged;

        public void UpdateRemoteToolSnapshot(string threadId, EffectiveToolSnapshot snapshot, string mode) { }
        public ValueTask PrepareTurnAsync(string threadId, EffectiveToolSnapshot snapshot, string mode,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<RemoteToolHostCatalog> ListAsync(
            string threadId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RemoteToolHostCatalog([]));

        public ValueTask<RemoteToolConnectResult> ConnectAsync(
            string threadId,
            string hostId,
            string workspaceId,
            CancellationToken cancellationToken = default,
            RemoteToolRouteInitiator initiator = RemoteToolRouteInitiator.Client)
        {
            var route = new RemoteToolRoute(hostId, workspaceId, "lease_fixture", "instance_fixture", "session_fixture");
            _routes[threadId] = route;
            Raise(threadId, RemoteToolRouteChangeReason.Connected, initiator, route);
            return ValueTask.FromResult(new RemoteToolConnectResult(
                route,
                new RemoteToolEnvironment("studio-pc", "Windows", "designer", "D:/example/game-client"),
                [],
                [],
                []));
        }

        public ValueTask<RemoteToolDisconnectResult> DisconnectAsync(
            string threadId,
            CancellationToken cancellationToken = default,
            RemoteToolRouteInitiator initiator = RemoteToolRouteInitiator.Client)
        {
            if (!_routes.Remove(threadId, out var previous))
                return ValueTask.FromResult(new RemoteToolDisconnectResult(false));
            Raise(threadId, RemoteToolRouteChangeReason.Disconnected, initiator, previous);
            return ValueTask.FromResult(new RemoteToolDisconnectResult(true, previous));
        }

        public void RaiseLeaseLost(string threadId, RemoteToolRouteInitiator initiator) =>
            Raise(threadId, RemoteToolRouteChangeReason.LeaseLost, initiator, _routes[threadId]);

        public bool TryGetRoute(string threadId, out RemoteToolRoute route) =>
            _routes.TryGetValue(threadId, out route!);

        public bool TryGetConnectionSnapshot(string threadId, out RemoteToolConnectionSnapshot snapshot)
        {
            snapshot = null!;
            return false;
        }

        public bool TryForkRoute(string parentThreadId, string childThreadId) => false;

        public ValueTask<string> WriteImageAsync(
            RemoteToolRoute route,
            string threadId,
            string callId,
            byte[] bytes,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<ToolExecutionResult> InvokeAsync(
            RemoteToolRoute route,
            ToolDefinition definition,
            string contractHash,
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private void Raise(
            string threadId,
            RemoteToolRouteChangeReason reason,
            RemoteToolRouteInitiator initiator,
            RemoteToolRoute? route) =>
            RouteChanged?.Invoke(new RemoteToolRouteChange(
                threadId,
                reason,
                initiator,
                route,
                "Studio PC",
                "game-client"));
    }
}
