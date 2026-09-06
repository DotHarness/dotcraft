using System.Net;
using System.Text;
using System.Text.Json;
using DotCraft.RemoteTools;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class RemoteToolHostPairingTests
{
    [Fact]
    public void Runtime_ParseInvite_WritesNothing()
    {
        using var home = new TemporaryDirectory();
        using var scope = new HubEnvironmentScope(null, null);
        var runtime = RemoteToolHostRuntime.Create(new RemoteToolHostRuntimeOptions { CraftHome = home.Path });

        var direct = RemoteToolHostRuntime.ParseInvite("http://192.168.1.5:47600/i/inv_abcdefgh");
        var deepLink = RemoteToolHostRuntime.ParseInvite(
            "dotcraft://satellite/join?invite="
            + Uri.EscapeDataString("http://192.168.1.5:47600/i/inv_abcdefgh"));

        Assert.Equal("inv_abcdefgh", direct.InviteId);
        Assert.Equal("192.168.1.5", direct.InviterDisplayName);
        Assert.Equal(string.Empty, direct.Purpose);
        Assert.Equal(new Uri("http://192.168.1.5:47600"), direct.HubEndpoint);
        Assert.Equal(direct, deepLink);
        Assert.Empty(Directory.GetFileSystemEntries(home.Path));
        Assert.Equal(RemoteToolHostStatus.Offline, runtime.Status);
        Assert.Empty(runtime.Peers);

        Assert.Throws<FormatException>(() => RemoteToolHostRuntime.ParseInvite("http://host:47600/join/abc"));
        Assert.Throws<FormatException>(() =>
            RemoteToolHostRuntime.ParseInvite("dotcraft://workspace/open?path=C:%5Crepo"));
    }

    [Fact]
    public async Task Runtime_ResolveInvite_WritesNothing()
    {
        using var home = new TemporaryDirectory();
        using var hub = new HttpListener();
        var port = RemoteToolHostTestHost.GetAvailablePort();
        hub.Prefixes.Add($"http://127.0.0.1:{port}/");
        hub.Start();
        var served = Task.Run(async () =>
        {
            var context = await hub.GetContextAsync();
            var body = Encoding.UTF8.GetBytes(
                """
                {"inviteId":"inv_abcdefgh","inviterDisplayName":"Ann","purpose":"Fix the build",
                "expiresAt":"2030-01-01T00:00:00+00:00","hubEndpoint":"http://127.0.0.1"}
                """);
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(body);
            context.Response.Close();
        });

        var parsed = RemoteToolHostRuntime.ParseInvite($"http://127.0.0.1:{port}/i/inv_abcdefgh");
        var resolved = await RemoteToolHostRuntime.ResolveInviteAsync(parsed);
        await served;

        Assert.Equal("Ann", resolved.InviterDisplayName);
        Assert.Equal("Fix the build", resolved.Purpose);
        Assert.Equal(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero), resolved.ExpiresAt);
        Assert.Equal(parsed.InviteId, resolved.InviteId);
        Assert.Equal(parsed.HubEndpoint, resolved.HubEndpoint);
        Assert.Empty(Directory.GetFileSystemEntries(home.Path));
    }

    [Fact]
    public async Task Runtime_ResolveInvite_KeepsTheParsedInvite_WhenTheHubDoesNotAnswer()
    {
        var port = RemoteToolHostTestHost.GetAvailablePort();
        var parsed = RemoteToolHostRuntime.ParseInvite($"http://127.0.0.1:{port}/i/inv_abcdefgh");

        Assert.Equal(parsed, await RemoteToolHostRuntime.ResolveInviteAsync(parsed));
    }

    [Fact]
    public void Runtime_PeerConnections_FollowTheInvitationScheme()
    {
        var secure = RemoteToolHostRuntime.ParseInvite("https://hub.example.com:47600/i/inv_abcdefgh");
        var plain = RemoteToolHostRuntime.ParseInvite("http://192.168.1.5:47600/i/inv_abcdefgh");

        var securePeer = PairedWith(secure.HubEndpoint);
        Assert.Equal(
            new Uri("wss://hub.example.com:47600/satellite/control?peer=sat_1"),
            securePeer.ControlUri);
        Assert.Equal(
            new Uri("wss://hub.example.com:47600/satellite/data?peer=sat_1&session=s1"),
            securePeer.DataUri("s1"));

        var plainPeer = PairedWith(plain.HubEndpoint);
        Assert.Equal(new Uri("ws://192.168.1.5:47600/satellite/control?peer=sat_1"), plainPeer.ControlUri);
        Assert.Equal(
            new Uri("ws://192.168.1.5:47600/satellite/data?peer=sat_1&session=s1"),
            plainPeer.DataUri("s1"));
    }

    [Fact]
    public void Storage_PeerRecordWithoutScheme_ReadsBackAsPlainHttp()
    {
        var state = JsonSerializer.Deserialize<RemoteToolHostState>(
            """
            {"hostId":"rth_test","displayName":"test-host","peers":[
              {"peerId":"sat_1","hubHost":"192.168.1.5","hubPort":47600,
               "credentialReference":"remote-tool-host/peer/sat_1"}]}
            """,
            RemoteToolHostProtocol.JsonOptions);

        var peer = Assert.Single(state!.Peers);
        Assert.Equal(new Uri("ws://192.168.1.5:47600/satellite/control?peer=sat_1"), peer.ControlUri);
    }

    [Fact]
    public void Serve_WithoutPairing_FailsWithJoinHint()
    {
        using var home = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(home.Path, new MemoryCredentialStore());
        RemoteToolHostTestHost.Setup(storage, new Dictionary<string, string>(StringComparer.Ordinal));

        var error = Assert.Throws<RemoteToolHostException>(
            () => new RemoteToolHostOutboundHost(storage).Prepare());

        Assert.Contains("tool-host join", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(storage.ServeLockPath));
    }

    [Fact]
    public void Client_MapsSatelliteOfflineCloseStatus_ToHostOffline()
    {
        var transport = new IOException("The remote party closed the WebSocket.");

        var offline = RemoteToolHostClient.MapConnectionError(transport, null, "satelliteOffline");
        var failed = RemoteToolHostClient.MapConnectionError(transport, null, "satelliteSessionFailed");
        var unknown = RemoteToolHostClient.MapConnectionError(transport, null, null);

        Assert.Equal(RemoteToolErrorCodes.HostOffline, offline.Code);
        Assert.Equal(RemoteToolErrorCodes.SatelliteSessionFailed, failed.Code);
        Assert.Equal(RemoteToolErrorCodes.HostOffline, unknown.Code);
    }

    [Fact]
    public async Task Client_ReportsHubUnavailable_WhenNoHubLock()
    {
        using var home = new TemporaryDirectory();
        using var scope = new HubEnvironmentScope(null, null);
        using var directory = new HubRemoteToolHostDirectory(new HubEndpointProvider(home.Path));
        await using var client = new RemoteToolHostClient(directory, new ApproveService());

        var error = await Assert.ThrowsAsync<RemoteToolHostException>(async () =>
            await client.ListAsync("thread"));

        Assert.Equal(RemoteToolErrorCodes.HubUnavailable, error.Code);
    }

    [Fact]
    public async Task Client_ListsPeersFromHub_WithoutOpeningDataConnection()
    {
        var port = RemoteToolHostTestHost.GetAvailablePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var requests = new List<string>();
        var serving = ServeAsync(listener, requests);

        using var scope = new HubEnvironmentScope($"http://127.0.0.1:{port}", "hub-token");
        using var directory = new HubRemoteToolHostDirectory(new HubEndpointProvider());
        await using var client = new RemoteToolHostClient(directory, new ApproveService());

        var catalog = await client.ListAsync("thread");

        var host = Assert.Single(catalog.Hosts);
        Assert.Equal("sat_remote", host.HostId);
        Assert.True(host.Online);
        var workspace = Assert.Single(host.Workspaces);
        Assert.Equal("repo", workspace.WorkspaceId);
        Assert.False(workspace.Available);
        Assert.Equal("other", workspace.BusyOwner);
        Assert.Equal(["/v1/satellites"], requests);

        listener.Stop();
        await serving;
    }

    private static RemoteToolHubPeer PairedWith(Uri hub) => new()
    {
        PeerId = "sat_1",
        HubHost = hub.Host,
        HubPort = hub.Port,
        HubScheme = hub.Scheme,
        CredentialReference = RemoteToolHostStorage.PeerCredentialReference("sat_1"),
        PairedAt = DateTimeOffset.UtcNow
    };

    private static async Task ServeAsync(HttpListener listener, List<string> requests)
    {
        const string body = """
        [
          {
            "peerId": "sat_remote",
            "displayName": "Ann",
            "online": true,
            "machineName": "ANN-PC",
            "operatingSystem": "Windows",
            "userName": "ann",
            "buildVersion": "0.6.2",
            "workspaces": [
              { "workspaceId": "repo", "path": "C:\\repo", "busy": true, "busyOwner": "other" }
            ],
            "pairedAt": "2026-09-05T10:00:00+00:00",
            "lastSeenAt": "2026-09-05T10:01:00+00:00"
          }
        ]
        """;
        try
        {
            while (listener.IsListening)
            {
                var context = await listener.GetContextAsync();
                requests.Add(context.Request.Url!.AbsolutePath);
                context.Response.ContentType = "application/json";
                var bytes = Encoding.UTF8.GetBytes(body);
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }
        catch (Exception)
        {
            // Stopping the listener ends the loop.
        }
    }

    private sealed class HubEnvironmentScope : IDisposable
    {
        private const string BaseUrlVariable = "DOTCRAFT_HUB_API_BASE_URL";
        private const string TokenVariable = "DOTCRAFT_HUB_TOKEN";
        private readonly string? _baseUrl = Environment.GetEnvironmentVariable(BaseUrlVariable);
        private readonly string? _token = Environment.GetEnvironmentVariable(TokenVariable);

        public HubEnvironmentScope(string? baseUrl, string? token)
        {
            Environment.SetEnvironmentVariable(BaseUrlVariable, baseUrl);
            Environment.SetEnvironmentVariable(TokenVariable, token);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(BaseUrlVariable, _baseUrl);
            Environment.SetEnvironmentVariable(TokenVariable, _token);
        }
    }
    [Fact]
    public async Task Join_WithoutWorkspace_AndNoTrayClient_AsksForWorkspace()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => RemoteToolHostCliRunner.JoinAsync(
            "http://ann-pc:47600/i/inv_abcdefgh",
            workspacePath: null,
            TextWriter.Null,
            CancellationToken.None));

        Assert.Contains("--workspace", error.Message, StringComparison.Ordinal);
    }

}
