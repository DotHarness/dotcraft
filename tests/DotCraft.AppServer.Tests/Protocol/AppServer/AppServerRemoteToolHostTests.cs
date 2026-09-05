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
    public async Task Initialize_WhenClientProvided_AdvertisesCapability()
    {
        using var harness = new AppServerTestHarness(remoteToolHostClient: new FakeRemoteToolHostClient());

        var init = await harness.InitializeAsync();

        var capabilities = init.RootElement.GetProperty("result").GetProperty("capabilities");
        Assert.True(capabilities.GetProperty("remoteToolHost").GetBoolean());
    }

    [Fact]
    public async Task Initialize_WithoutClient_OmitsCapability()
    {
        using var harness = new AppServerTestHarness();

        var init = await harness.InitializeAsync();

        var capabilities = init.RootElement.GetProperty("result").GetProperty("capabilities");
        Assert.False(capabilities.TryGetProperty("remoteToolHost", out _));
    }

    [Fact]
    public async Task List_MapsCatalogAndCurrentRoute()
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

        using var response = await harness.Transport.ReadNextSentAsync();
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

    /// <summary>The welcome composer lists before a thread exists, so the catalog cannot depend on one.</summary>
    [Fact]
    public async Task List_WithoutThreadId_ReturnsSameMachinesAndOmitsRoute()
    {
        var client = new FakeRemoteToolHostClient
        {
            Catalog = new RemoteToolHostCatalog(
            [
                new RemoteToolHostDescriptor(
                    HostId,
                    "Studio PC",
                    Online: true,
                    [new RemoteToolWorkspaceDescriptor(WorkspaceId, "game-client", Available: true)])
            ])
        };
        using var harness = new AppServerTestHarness(remoteToolHostClient: client);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(MethodNames.RemoteToolHostList, new { }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        var host = Assert.Single(result.GetProperty("hosts").EnumerateArray().ToArray());
        Assert.Equal(HostId, host.GetProperty("hostId").GetString());
        var workspace = Assert.Single(host.GetProperty("workspaces").EnumerateArray().ToArray());
        Assert.Equal(WorkspaceId, workspace.GetProperty("workspaceId").GetString());
        Assert.True(workspace.GetProperty("available").GetBoolean());
        Assert.False(result.TryGetProperty("route", out _));
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

        var notification = Assert.Single(broadcasts);
        Assert.Equal(thread.Id, notification.ThreadId);
        Assert.Equal("connected", notification.Reason);
        Assert.Equal(WorkspaceId, notification.Route.Value!.WorkspaceId);
    }

    [Fact]
    public async Task Connect_WhenWorkspaceBusy_ReturnsBusyErrorWithOwner()
    {
        var client = new FakeRemoteToolHostClient
        {
            ConnectFailure = new RemoteToolHostException(
                RemoteToolErrorCodes.WorkspaceBusy,
                "Workspace 'workspace_001' is leased by another Agent Host.")
        };
        using var harness = new AppServerTestHarness(remoteToolHostClient: client);
        await harness.InitializeAsync();
        var thread = await SeedThreadAsync(harness);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            MethodNames.RemoteToolHostConnect,
            new { threadId = thread.Id, hostId = HostId, workspaceId = WorkspaceId }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.RemoteToolWorkspaceBusyCode);
        var data = response.RootElement.GetProperty("error").GetProperty("data");
        Assert.Equal(RemoteToolErrorCodes.WorkspaceBusy, data.GetProperty("code").GetString());
        Assert.Equal("error.remoteToolHost.remoteWorkspaceBusy", data.GetProperty("messageKey").GetString());
        Assert.Equal("other", data.GetProperty("params").GetProperty("owner").GetString());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("fallbackText").GetString()));
    }

    [Fact]
    public async Task Connect_WhenHostOffline_ReturnsUnavailableErrorWithCode()
    {
        var client = new FakeRemoteToolHostClient
        {
            ConnectFailure = new RemoteToolHostException(
                RemoteToolErrorCodes.HostOffline,
                "The paired machine is not connected to the Hub.")
        };
        using var harness = new AppServerTestHarness(remoteToolHostClient: client);
        await harness.InitializeAsync();
        var thread = await SeedThreadAsync(harness);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            MethodNames.RemoteToolHostConnect,
            new { threadId = thread.Id, hostId = HostId, workspaceId = WorkspaceId }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.RemoteToolHostUnavailableCode);
        var data = response.RootElement.GetProperty("error").GetProperty("data");
        Assert.Equal(RemoteToolErrorCodes.HostOffline, data.GetProperty("code").GetString());
        Assert.Equal("error.remoteToolHost.remoteHostOffline", data.GetProperty("messageKey").GetString());
        Assert.False(data.TryGetProperty("params", out _));
    }

    [Fact]
    public async Task Connect_WhenTurnRunning_ReturnsTurnInProgress()
    {
        var client = new FakeRemoteToolHostClient();
        using var harness = new AppServerTestHarness(remoteToolHostClient: client);
        await harness.InitializeAsync();
        var thread = await SeedThreadAsync(harness, running: true);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            MethodNames.RemoteToolHostConnect,
            new { threadId = thread.Id, hostId = HostId, workspaceId = WorkspaceId }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.TurnInProgressCode);
        Assert.Empty(client.ConnectedRoutes);
    }

    [Fact]
    public async Task Connect_WhenThreadUnknown_ReturnsThreadNotFound()
    {
        using var harness = new AppServerTestHarness(remoteToolHostClient: new FakeRemoteToolHostClient());
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            MethodNames.RemoteToolHostConnect,
            new { threadId = "thread_missing", hostId = HostId, workspaceId = WorkspaceId }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.ThreadNotFoundCode);
    }

    [Fact]
    public async Task Connect_WithMalformedParams_ReturnsInvalidParams()
    {
        using var harness = new AppServerTestHarness(remoteToolHostClient: new FakeRemoteToolHostClient());
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            MethodNames.RemoteToolHostConnect,
            new { threadId = "thread_001", hostId = HostId }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
    }

    [Fact]
    public async Task Disconnect_ReturnsPreviousRouteAndBroadcasts()
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

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("disconnected").GetBoolean());
        var previous = result.GetProperty("previousRoute");
        Assert.Equal(HostId, previous.GetProperty("hostId").GetString());
        Assert.Equal("connected", previous.GetProperty("status").GetString());

        var notification = Assert.Single(broadcasts);
        Assert.Equal("disconnected", notification.Reason);
        Assert.Null(notification.Route.Value);
    }

    [Fact]
    public async Task Disconnect_WhenNoRoute_ReportsNoChangeAndDoesNotBroadcast()
    {
        var client = new FakeRemoteToolHostClient { Disconnects = false };
        var broadcasts = new List<Contract.RemoteToolHostRouteChangedNotification>();
        using var harness = new AppServerTestHarness(
            remoteToolHostClient: client,
            broadcastRemoteToolHostRouteChanged: broadcasts.Add);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            MethodNames.RemoteToolHostDisconnect,
            new { threadId = "thread_001" }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.False(response.RootElement.GetProperty("result").GetProperty("disconnected").GetBoolean());
        Assert.Empty(broadcasts);
    }

    [Fact]
    public async Task Methods_WithoutClient_ReturnMethodNotFound()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(MethodNames.RemoteToolHostList, new { }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.MethodNotFoundCode);
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
        public RemoteToolHostCatalog Catalog { get; set; } = new([]);

        public Dictionary<string, RemoteToolConnectionSnapshot> Snapshots { get; } = new(StringComparer.Ordinal);

        public RemoteToolHostException? ConnectFailure { get; set; }

        public bool Disconnects { get; set; } = true;

        public List<string> ListedThreadIds { get; } = [];

        public List<(string ThreadId, string HostId, string WorkspaceId)> ConnectedRoutes { get; } = [];

        public void UpdateRemoteToolDefinitions(IReadOnlyList<ToolDefinition> definitions)
        {
        }

        public ValueTask<RemoteToolHostCatalog> ListAsync(string threadId, CancellationToken cancellationToken = default)
        {
            ListedThreadIds.Add(threadId);
            return ValueTask.FromResult(Catalog);
        }

        public ValueTask<RemoteToolConnectResult> ConnectAsync(
            string threadId,
            string hostId,
            string workspaceId,
            CancellationToken cancellationToken = default)
        {
            if (ConnectFailure is not null)
                throw ConnectFailure;

            ConnectedRoutes.Add((threadId, hostId, workspaceId));
            return ValueTask.FromResult(new RemoteToolConnectResult(
                new RemoteToolRoute(hostId, workspaceId, "lease_fixture", "instance_fixture"),
                NewEnvironment(),
                ["Exec"],
                ["LSP"],
                [new RemoteToolUnavailableReason("LSP", RemoteToolErrorCodes.RemoteToolUnavailable, "Not exported.")]));
        }

        public ValueTask<RemoteToolDisconnectResult> DisconnectAsync(
            string threadId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RemoteToolDisconnectResult(
                Disconnects,
                Disconnects ? new RemoteToolRoute(HostId, WorkspaceId, "lease_fixture", "instance_fixture") : null));

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
