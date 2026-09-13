using System.Net;
using System.Net.Sockets;
using System.Text;
using DotCraft.Hub;
using DotCraft.RemoteTools;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class SatellitePairingLifecycleTests : IDisposable
{
    private readonly string _userProfile = Path.Combine(
        Path.GetTempPath(),
        "DotCraftSatelliteLifecycle_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Runtime_AcceptInvite_WritesOnePeerAndOneCredential()
    {
        await using var hub = await SatelliteHubFixture.StartAsync(_userProfile);
        var workspacePath = Path.Combine(_userProfile, "workspace");
        Directory.CreateDirectory(workspacePath);
        var credentials = new MemoryCredentialStore();
        var storage = new RemoteToolHostStorage(Path.Combine(_userProfile, "host-craft"), credentials);
        await using var runtime = new RemoteToolHostRuntime(storage, "host-machine");
        var invite = await hub.CreateInviteAsync("Ann");

        var peer = await runtime.AcceptInviteAsync(
            new RemoteToolJoinDecision(RemoteToolHostRuntime.ParseInvite(invite.Url), workspacePath));

        var stored = Assert.Single(storage.LoadHostState()!.Peers);
        Assert.Equal(peer.PeerId, stored.PeerId);
        Assert.Equal("Ann", peer.DisplayName);
        Assert.Equal(workspacePath, peer.WorkspacePath);
        var credential = Assert.Single(credentials.Values);
        Assert.Equal(stored.CredentialReference, credential.Key);
        var hostJson = await File.ReadAllTextAsync(storage.HostStatePath);
        Assert.DoesNotContain(credential.Value, hostJson, StringComparison.Ordinal);
        Assert.DoesNotContain(invite.InviteId, hostJson, StringComparison.Ordinal);

        var listed = Assert.Single(await hub.GetAsync<HubSatelliteResponse[]>("/v1/satellites"));
        Assert.Equal(peer.PeerId, listed.PeerId);
    }

    [Fact]
    public async Task Runtime_PairingsForTheSameDirectory_KeepOneWorkspaceAndSeparateAuthorization()
    {
        await using var hub = await SatelliteHubFixture.StartAsync(_userProfile);
        var workspace = Directory.CreateDirectory(Path.Combine(_userProfile, "workspace")).FullName;
        var credentials = new MemoryCredentialStore();
        var storage = new RemoteToolHostStorage(Path.Combine(_userProfile, "host-craft"), credentials);
        await using var runtime = new RemoteToolHostRuntime(storage, "host-machine");
        var first = await hub.CreateInviteAsync("First");
        var second = await hub.CreateInviteAsync("Second");
        var a = await runtime.AcceptInviteAsync(new(RemoteToolHostRuntime.ParseInvite(first.Url), workspace,
            RemoteToolAuthorization.WorkspacePreferred));
        var b = await runtime.AcceptInviteAsync(new(RemoteToolHostRuntime.ParseInvite(second.Url), workspace,
            RemoteToolAuthorization.FullAccess));
        Assert.Equal(a.WorkspaceId, b.WorkspaceId);
        Assert.NotEqual(a.PeerId, b.PeerId);
        Assert.NotEqual(a.AuthorizationMode, b.AuthorizationMode);
        Assert.Single(storage.LoadHostState()!.Workspaces);
        Assert.Equal(2, credentials.Values.Count);
    }

    [Fact]
    public async Task PeerConnector_ReconnectsAfterHubRestart()
    {
        var satellitePort = SatelliteHubFixture.GetAvailablePort();
        await using var scenario = await SatelliteScenario.StartAsync(_userProfile, satellitePort);

        await scenario.Hub.DisposeAsync();
        await SatelliteBridgeEndToEndTests.WaitUntilAsync(
            () => Task.FromResult(scenario.Runtime.Status == RemoteToolHostStatus.Offline));

        await using var restarted = await SatelliteHubFixture.StartAsync(_userProfile, satellitePort);
        await SatelliteBridgeEndToEndTests.WaitUntilAsync(async () =>
            (await restarted.GetAsync<HubSatelliteResponse[]>("/v1/satellites")).Any(peer => peer.Online));

        var peer = Assert.Single(await restarted.GetAsync<HubSatelliteResponse[]>("/v1/satellites"));
        Assert.Equal(scenario.PeerId, peer.PeerId);
        Assert.True(peer.Online);
    }

    [Fact]
    public async Task PeerConnector_HeartbeatPublishesLeaseState()
    {
        await using var scenario = await SatelliteScenario.StartAsync(
            _userProfile,
            heartbeatInterval: TimeSpan.FromMilliseconds(200));
        await using var client = new RemoteToolHostClient(scenario.Directory);

        await client.ConnectAsync("thread", scenario.PeerId, scenario.WorkspaceId);

        await SatelliteBridgeEndToEndTests.WaitUntilAsync(async () =>
        {
            var peer = (await scenario.Hub.GetAsync<HubSatelliteResponse[]>("/v1/satellites")).Single();
            return peer.Workspaces.Any(workspace => workspace.Busy && workspace.LeaseExpiresAt is not null);
        });

        var busy = (await scenario.Hub.GetAsync<HubSatelliteResponse[]>("/v1/satellites"))
            .Single().Workspaces.Single();
        Assert.Equal(scenario.WorkspaceId, busy.WorkspaceId);
        Assert.Equal("other", busy.BusyOwner);
        Assert.True(busy.LeaseExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task PeerConnector_OnRevoked_DeletesLocalPairingAndCredential()
    {
        var credentials = new MemoryCredentialStore();
        await using var scenario = await SatelliteScenario.StartAsync(_userProfile, credentials: credentials);
        Assert.Single(credentials.Values);

        var response = await scenario.Hub.DeleteAsync($"/v1/satellites/{scenario.PeerId}");
        response.EnsureSuccessStatusCode();
        await scenario.Running.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Empty(scenario.Storage.LoadHostState()!.Peers);
        Assert.Empty(credentials.Values);
        Assert.Equal(RemoteToolHostStatus.Offline, scenario.Runtime.Status);
    }

    [Fact]
    public async Task PeerConnector_WhenTheHubRefusesTheReconnect_DeletesLocalPairingAndCredential()
    {
        var credentials = new MemoryCredentialStore();
        await using var scenario = await SatelliteScenario.StartAsync(_userProfile, credentials: credentials);
        await scenario.Runtime.StopAsync();

        var response = await scenario.Hub.DeleteAsync($"/v1/satellites/{scenario.PeerId}");
        response.EnsureSuccessStatusCode();
        await scenario.Runtime.RunAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Empty(scenario.Storage.LoadHostState()!.Peers);
        Assert.Empty(credentials.Values);
        Assert.Equal(RemoteToolHostStatus.Offline, scenario.Runtime.Status);
    }

    [Fact]
    public async Task PeerConnector_WhenTheHubFailsTheReconnect_KeepsThePairingAndRetries()
    {
        using var hub = new RefusingControlEndpoint();
        var workspacePath = Directory.CreateDirectory(Path.Combine(_userProfile, "workspace")).FullName;
        var credentials = new MemoryCredentialStore();
        var storage = new RemoteToolHostStorage(Path.Combine(_userProfile, "host-craft"), credentials);
        storage.SaveHostState(new RemoteToolHostState
        {
            HostId = "rth_lifecycle",
            DisplayName = "host-machine",
            Workspaces = new(StringComparer.Ordinal) { ["workspace"] = workspacePath }
        });
        storage.AddPeer(
            new RemoteToolHubPeer
            {
                PeerId = "peer-retry",
                HubHost = "127.0.0.1",
                HubPort = hub.Port,
                HubScheme = Uri.UriSchemeHttp,
                CredentialReference = RemoteToolHostStorage.PeerCredentialReference("peer-retry"),
                WorkspaceId = "workspace",
                AuthorizationMode = RemoteToolAuthorization.FullAccess,
                AuthorizationRevision = 1,
                PairedAt = DateTimeOffset.UtcNow
            },
            "credential");
        await using var runtime = new RemoteToolHostRuntime(storage, "host-machine");

        var running = runtime.RunAsync();
        await SatelliteBridgeEndToEndTests.WaitUntilAsync(() => Task.FromResult(hub.Attempts >= 2));

        Assert.False(running.IsCompleted);
        Assert.Single(storage.LoadHostState()!.Peers);
        Assert.Single(credentials.Values);
    }

    public void Dispose()
    {
        try { Directory.Delete(_userProfile, recursive: true); }
        catch (Exception) { }
    }
}

/// <summary>A Hub control endpoint that answers every handshake with HTTP 500.</summary>
internal sealed class RefusingControlEndpoint : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _serving;
    private int _attempts;

    public RefusingControlEndpoint()
    {
        _listener.Start();
        _serving = ServeAsync(_stopping.Token);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public int Attempts => Volatile.Read(ref _attempts);

    public void Dispose()
    {
        _stopping.Cancel();
        _listener.Dispose();
        _serving.Wait(TimeSpan.FromSeconds(5));
        _stopping.Dispose();
    }

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        var refusal = Encoding.ASCII.GetBytes(
            "HTTP/1.1 500 Internal Server Error\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        var request = new byte[2048];
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref _attempts);
                var stream = client.GetStream();
                // The request is read first so the refusal is not lost to a reset connection.
                await stream.ReadAsync(request, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(refusal, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;
            }
        }
    }
}
