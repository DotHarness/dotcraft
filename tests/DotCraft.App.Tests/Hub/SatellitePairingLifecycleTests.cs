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
        await using var client = new RemoteToolHostClient(scenario.Directory, new CountingApprovalService());

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

    public void Dispose()
    {
        try { Directory.Delete(_userProfile, recursive: true); }
        catch (Exception) { }
    }
}
