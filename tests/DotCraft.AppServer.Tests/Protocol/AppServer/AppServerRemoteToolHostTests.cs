using System.Text.Json.Nodes;
using DotCraft.AppServer;
using DotCraft.Sessions;
using DotCraft.Tools;
using Contract = DotCraft.Protocol.AppServer;
using MethodNames = DotCraft.Protocol.AppServer.AppServerMethodNames;
using SessionThread = DotCraft.Sessions.SessionThread;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerRemoteToolHostTests
{
    private const string HostId = "peer_001";
    private const string WorkspaceId = "workspace_001";

    [Fact]
    public async Task CapabilityAndMethods_ExistOnlyWithAClient()
    {
        using (var withClient = new AppServerTestHarness(remoteToolHostClient: new FakeRemoteToolHostClient()))
        {
            using var init = await withClient.InitializeAsync();
            Assert.True(init.RootElement.GetProperty("result").GetProperty("capabilities").GetProperty("remoteToolHost").GetBoolean());
        }

        using var harness = new AppServerTestHarness();
        using (var init = await harness.InitializeAsync())
            Assert.False(init.RootElement.GetProperty("result").GetProperty("capabilities").TryGetProperty("remoteToolHost", out _));

        await harness.ExecuteRequestAsync(harness.BuildRequest(MethodNames.RemoteToolHostList, new { }));
        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.MethodNotFoundCode);
    }

    [Fact]
    public async Task List_MapsCatalogWithTheThreadRouteOrWithoutAThread()
    {
        var client = new FakeRemoteToolHostClient
        {
            Catalog = new RemoteToolHostCatalog(
            [
                new RemoteToolHostDescriptor(
                    HostId,
                    "Studio PC",
                    Online: true,
                    [
                        new RemoteToolWorkspaceDescriptor(
                            WorkspaceId,
                            "game-client",
                            Available: false,
                            BusyOwner: "other",
                            LeaseExpiresAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
                    ],
                    ErrorCode: RemoteToolErrorCodes.HostOffline)
            ])
        };
        client.Snapshots["thread_001"] = new RemoteToolConnectionSnapshot(
            RemoteToolConnectionStatus.LeaseLost,
            HostId,
            WorkspaceId,
            NewEnvironment());
        using var harness = new AppServerTestHarness(remoteToolHostClient: client);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(
            harness.BuildRequest(MethodNames.RemoteToolHostList, new { threadId = "thread_001" }));
        using (var response = await harness.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(response);
            var result = response.RootElement.GetProperty("result");
            var host = Assert.Single(result.GetProperty("hosts").EnumerateArray().ToArray());
            Assert.Equal(HostId, host.GetProperty("hostId").GetString());
            Assert.Equal("Studio PC", host.GetProperty("displayName").GetString());
            Assert.True(host.GetProperty("online").GetBoolean());
            Assert.Equal(RemoteToolErrorCodes.HostOffline, host.GetProperty("errorCode").GetString());
            var workspace = Assert.Single(host.GetProperty("workspaces").EnumerateArray().ToArray());
            Assert.Equal(WorkspaceId, workspace.GetProperty("workspaceId").GetString());
            Assert.False(workspace.GetProperty("available").GetBoolean());
            Assert.Equal("other", workspace.GetProperty("busyOwner").GetString());
            Assert.Equal(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                workspace.GetProperty("leaseExpiresAt").GetDateTimeOffset());
            var route = result.GetProperty("route");
            Assert.Equal("thread_001", route.GetProperty("threadId").GetString());
            Assert.Equal("leaseLost", route.GetProperty("status").GetString());
            Assert.Equal("studio-pc", route.GetProperty("environment").GetProperty("hostName").GetString());
            Assert.Equal("thread_001", client.ListedThreadIds.Single());
        }

        await harness.ExecuteRequestAsync(harness.BuildRequest(MethodNames.RemoteToolHostList, new { }));
        using var withoutThread = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(withoutThread);
        var withoutThreadResult = withoutThread.RootElement.GetProperty("result");
        Assert.Equal(HostId, Assert.Single(withoutThreadResult.GetProperty("hosts").EnumerateArray().ToArray()).GetProperty("hostId").GetString());
        Assert.False(withoutThreadResult.TryGetProperty("route", out _));
    }

    [Fact]
    public async Task Connect_PublishesRouteBroadcastsAndHidesLeaseIdentifiers()
    {
        var client = new FakeRemoteToolHostClient();
        var broadcasts = new List<Contract.RemoteToolHostRouteChangedNotification>();
        using var harness = new AppServerTestHarness(
            remoteToolHostClient: client,
            broadcastRemoteToolHostRouteChanged: broadcasts.Add);
        await harness.InitializeAsync();
        var thread = await SeedThreadAsync(harness);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            MethodNames.RemoteToolHostConnect,
            new { threadId = thread.Id, hostId = HostId, workspaceId = WorkspaceId }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var raw = response.RootElement.GetRawText();
        Assert.DoesNotContain("leaseId", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hostInstanceId", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lease_", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("instance_", raw, StringComparison.Ordinal);

        var result = response.RootElement.GetProperty("result");
        var route = result.GetProperty("route");
        Assert.Equal(thread.Id, route.GetProperty("threadId").GetString());
        Assert.Equal(HostId, route.GetProperty("hostId").GetString());
        Assert.Equal(WorkspaceId, route.GetProperty("workspaceId").GetString());
        Assert.Equal("connected", route.GetProperty("status").GetString());
        Assert.Equal("D:/example/game-client", route.GetProperty("environment").GetProperty("workspacePath").GetString());
        Assert.Equal(["Exec"], result.GetProperty("matchedTools").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(["LSP"], result.GetProperty("unavailableTools").EnumerateArray().Select(item => item.GetString()));
        Assert.False(result.GetProperty("alreadyConnected").GetBoolean());
        Assert.Equal(thread.Id, Assert.Single(client.PreparedThreads));

        var notification = Assert.Single(broadcasts);
        Assert.Equal(thread.Id, notification.ThreadId);
        Assert.Equal("connected", notification.Reason);
        Assert.Equal("client", notification.Initiator.Value);
        Assert.Equal(WorkspaceId, notification.Route.Value!.WorkspaceId);
    }

    [Fact]
    public async Task RouteChangesFromTheAgentOrLeaseLoss_AreBroadcast()
    {
        var client = new FakeRemoteToolHostClient();
        var broadcasts = new List<Contract.RemoteToolHostRouteChangedNotification>();
        using var harness = new AppServerTestHarness(
            remoteToolHostClient: client,
            broadcastRemoteToolHostRouteChanged: broadcasts.Add);
        await harness.InitializeAsync();

        await client.DisconnectAsync("thread_001", initiator: RemoteToolRouteInitiator.Agent);
        client.RaiseLeaseLost("thread_001");

        Assert.Equal(2, broadcasts.Count);
        var disconnected = broadcasts[0];
        Assert.Equal("thread_001", disconnected.ThreadId);
        Assert.Equal("disconnected", disconnected.Reason);
        Assert.Equal("agent", disconnected.Initiator.Value);
        Assert.Null(disconnected.Route.Value);

        var leaseLost = broadcasts[1];
        Assert.Equal("leaseLost", leaseLost.Reason);
        Assert.Equal("system", leaseLost.Initiator.Value);
        var route = Assert.IsType<Contract.RemoteToolRouteInfo>(leaseLost.Route.Value);
        Assert.Equal(HostId, route.HostId);
        Assert.Equal(WorkspaceId, route.WorkspaceId);
        Assert.Equal("leaseLost", route.Status);
    }

    [Theory]
    [InlineData(RemoteToolErrorCodes.WorkspaceBusy, "error.remoteToolHost.remoteWorkspaceBusy", true)]
    [InlineData(RemoteToolErrorCodes.HostOffline, "error.remoteToolHost.remoteHostOffline", false)]
    public async Task Connect_MapsHostFailuresToStructuredErrors(string failureCode, string messageKey, bool busy)
    {
        var client = new FakeRemoteToolHostClient
        {
            ConnectFailure = new RemoteToolHostException(
                failureCode,
                busy ? "Workspace 'workspace_001' is leased by another Agent Host." : "The paired machine is not connected to the Hub.")
        };
        using var harness = new AppServerTestHarness(remoteToolHostClient: client);
        await harness.InitializeAsync();
        var thread = await SeedThreadAsync(harness);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            MethodNames.RemoteToolHostConnect,
            new { threadId = thread.Id, hostId = HostId, workspaceId = WorkspaceId }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(
            response,
            busy ? AppServerErrors.RemoteToolWorkspaceBusyCode : AppServerErrors.RemoteToolHostUnavailableCode);
        var data = response.RootElement.GetProperty("error").GetProperty("data");
        Assert.Equal(failureCode, data.GetProperty("code").GetString());
        Assert.Equal(messageKey, data.GetProperty("messageKey").GetString());
        if (busy)
        {
            Assert.Equal("other", data.GetProperty("params").GetProperty("owner").GetString());
            Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("fallbackText").GetString()));
        }
        else
        {
            Assert.False(data.TryGetProperty("params", out _));
        }
    }

    [Fact]
    public async Task Connect_RejectsUnknownThreadMalformedParamsAndRunningTurnWithoutConnecting()
    {
        var client = new FakeRemoteToolHostClient();
        using var harness = new AppServerTestHarness(remoteToolHostClient: client);
        await harness.InitializeAsync();

        foreach (var (parameters, code) in new (object Params, int Code)[]
                 {
                     (new { threadId = "thread_missing", hostId = HostId, workspaceId = WorkspaceId }, AppServerErrors.ThreadNotFoundCode),
                     (new { threadId = "thread_001", hostId = HostId }, AppServerErrors.InvalidParamsCode)
                 })
        {
            await harness.ExecuteRequestAsync(harness.BuildRequest(MethodNames.RemoteToolHostConnect, parameters));
            using var rejected = await harness.Transport.ReadNextSentAsync();
            AppServerTestHarness.AssertIsErrorResponse(rejected, code);
        }

        var thread = await SeedThreadAsync(harness, running: true);
        await harness.ExecuteRequestAsync(harness.BuildRequest(
            MethodNames.RemoteToolHostConnect,
            new { threadId = thread.Id, hostId = HostId, workspaceId = WorkspaceId }));
        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.TurnInProgressCode);
        Assert.Empty(client.ConnectedRoutes);
    }

    [Fact]
    public async Task Disconnect_ReturnsPreviousRouteAndBroadcastsOnlyWhenARouteExisted()
    {
        var client = new FakeRemoteToolHostClient();
        client.Snapshots["thread_001"] = new RemoteToolConnectionSnapshot(
            RemoteToolConnectionStatus.Connected,
            HostId,
            WorkspaceId,
            NewEnvironment());
        var broadcasts = new List<Contract.RemoteToolHostRouteChangedNotification>();
        using var harness = new AppServerTestHarness(
            remoteToolHostClient: client,
            broadcastRemoteToolHostRouteChanged: broadcasts.Add);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            MethodNames.RemoteToolHostDisconnect,
            new { threadId = "thread_001" }));

        using (var response = await harness.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(response);
            var result = response.RootElement.GetProperty("result");
            Assert.True(result.GetProperty("disconnected").GetBoolean());
            var previous = result.GetProperty("previousRoute");
            Assert.Equal(HostId, previous.GetProperty("hostId").GetString());
            Assert.Equal("connected", previous.GetProperty("status").GetString());
        }

        var notification = Assert.Single(broadcasts);
        Assert.Equal("disconnected", notification.Reason);
        Assert.Equal("client", notification.Initiator.Value);
        Assert.Null(notification.Route.Value);

        client.Disconnects = false;
        await harness.ExecuteRequestAsync(harness.BuildRequest(
            MethodNames.RemoteToolHostDisconnect,
            new { threadId = "thread_001" }));
        using var noRoute = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(noRoute);
        Assert.False(noRoute.RootElement.GetProperty("result").GetProperty("disconnected").GetBoolean());
        Assert.Single(broadcasts);
    }

    private static RemoteToolEnvironment NewEnvironment() =>
        new("studio-pc", "Windows", "designer", "D:/example/game-client");

    private static async Task<SessionThread> SeedThreadAsync(AppServerTestHarness harness, bool running = false)
    {
        var thread = await harness.Service.CreateThreadAsync(harness.Identity, threadId: "thread_001");
        if (running)
        {
            thread.Turns.Add(AppServerTestHarness.MakeTurn(thread.Id));
            await harness.Service.SeedThreadAsync(thread);
        }
        return thread;
    }

    private sealed class FakeRemoteToolHostClient : IRemoteToolHostClient
    {
        public ValueTask<string> WriteImageAsync(RemoteToolRoute route, string threadId, string callId,
            byte[] bytes, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<byte[]> ReadImageAsync(RemoteToolRoute route, string threadId, string callId,
            string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public RemoteToolHostCatalog Catalog { get; set; } = new([]);

        public Dictionary<string, RemoteToolConnectionSnapshot> Snapshots { get; } = new(StringComparer.Ordinal);

        public RemoteToolHostException? ConnectFailure { get; set; }

        public bool Disconnects { get; set; } = true;

        public List<string> ListedThreadIds { get; } = [];

        public List<(string ThreadId, string HostId, string WorkspaceId)> ConnectedRoutes { get; } = [];

        public event Action<RemoteToolRouteChange>? RouteChanged;

        public void UpdateRemoteToolSnapshot(string threadId, EffectiveToolSnapshot snapshot, string mode)
        {
            PreparedThreads.Add(threadId);
        }

        public List<string> PreparedThreads { get; } = [];

        public ValueTask PrepareTurnAsync(string threadId, EffectiveToolSnapshot snapshot, string mode,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void ReportTurn(string threadId, string turnId, TurnStatus status) { }

        public ValueTask<RemoteToolHostCatalog> ListAsync(string threadId, CancellationToken cancellationToken = default)
        {
            ListedThreadIds.Add(threadId);
            return ValueTask.FromResult(Catalog);
        }

        public ValueTask<RemoteToolConnectResult> ConnectAsync(
            string threadId,
            string hostId,
            string workspaceId,
            CancellationToken cancellationToken = default,
            RemoteToolRouteInitiator initiator = RemoteToolRouteInitiator.Client)
        {
            if (ConnectFailure is not null)
                throw ConnectFailure;

            ConnectedRoutes.Add((threadId, hostId, workspaceId));
            var route = new RemoteToolRoute(hostId, workspaceId, "lease_fixture", "instance_fixture", "session_fixture");
            Raise(threadId, RemoteToolRouteChangeReason.Connected, initiator, route);
            return ValueTask.FromResult(new RemoteToolConnectResult(
                route,
                NewEnvironment(),
                ["Exec"],
                ["LSP"],
                [new RemoteToolUnavailableReason("LSP", RemoteToolErrorCodes.RemoteToolUnavailable, "Not exported.")]));
        }

        public ValueTask<RemoteToolDisconnectResult> DisconnectAsync(
            string threadId,
            CancellationToken cancellationToken = default,
            RemoteToolRouteInitiator initiator = RemoteToolRouteInitiator.Client)
        {
            if (!Disconnects)
                return ValueTask.FromResult(new RemoteToolDisconnectResult(false));
            var previous = new RemoteToolRoute(HostId, WorkspaceId, "lease_fixture", "instance_fixture", "session_fixture");
            Snapshots.Remove(threadId);
            Raise(threadId, RemoteToolRouteChangeReason.Disconnected, initiator, previous);
            return ValueTask.FromResult(new RemoteToolDisconnectResult(true, previous));
        }

        public void RaiseLeaseLost(string threadId)
        {
            Snapshots[threadId] = new RemoteToolConnectionSnapshot(
                RemoteToolConnectionStatus.LeaseLost,
                HostId,
                WorkspaceId,
                NewEnvironment());
            Raise(
                threadId,
                RemoteToolRouteChangeReason.LeaseLost,
                RemoteToolRouteInitiator.System,
                new RemoteToolRoute(HostId, WorkspaceId, "lease_fixture", "instance_fixture", "session_fixture"));
        }

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

        public bool TryGetRoute(string threadId, out RemoteToolRoute route)
        {
            route = null!;
            return false;
        }

        public bool TryGetConnectionSnapshot(string threadId, out RemoteToolConnectionSnapshot snapshot) =>
            Snapshots.TryGetValue(threadId, out snapshot!);

        public bool TryForkRoute(string parentThreadId, string childThreadId) => false;

        public ValueTask<ToolExecutionResult> InvokeAsync(
            RemoteToolRoute route,
            ToolDefinition definition,
            string contractHash,
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
