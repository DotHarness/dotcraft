using DotCraft.RemoteTools;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class RemoteToolHostStateConcurrencyTests
{
    [Fact]
    public async Task ConcurrentPairingsAndStateUpdates_PreserveEveryPeerAndCredential()
    {
        using var home = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(home.Path);
        var initial = RemoteToolHostTestHost.Setup(storage, new Dictionary<string, string>());
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const int workers = 4;
        const int pairings = 12;
        var tasks = Enumerable.Range(0, workers).Select(worker => Task.Run(async () =>
        {
            var writer = new RemoteToolHostStorage(Path.Combine(home.Path, "."));
            await start.Task;
            for (var index = 0; index < pairings; index++)
            {
                var id = $"peer_{worker}_{index}";
                writer.AddPeer(NewPeer(id), $"credential_{id}");
                writer.UpdateHostState(current => current! with
                {
                    CatalogRevision = current.CatalogRevision + 1
                });
            }
        })).ToArray();

        start.SetResult();
        await Task.WhenAll(tasks);

        var persisted = storage.LoadHostState()!;
        Assert.Equal(initial.HostId, persisted.HostId);
        Assert.Equal(initial.CatalogRevision + workers * pairings, persisted.CatalogRevision);
        var expectedIds = Enumerable.Range(0, workers)
            .SelectMany(worker => Enumerable.Range(0, pairings).Select(index => $"peer_{worker}_{index}"))
            .Order(StringComparer.Ordinal);
        Assert.Equal(expectedIds, persisted.Peers.Select(peer => peer.PeerId).Order(StringComparer.Ordinal));
        Assert.All(persisted.Peers, peer =>
            Assert.Equal($"credential_{peer.PeerId}", storage.GetPeerCredential(peer)));
    }

    [Fact]
    public async Task ConcurrentAuthorizationAndRevocation_PreserveSurvivingPeersAndRevisions()
    {
        using var home = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(home.Path);
        var initial = RemoteToolHostTestHost.Setup(storage, new Dictionary<string, string>());
        const int workers = 4;
        const int updates = 8;
        storage.AddPeer(NewPeer("unchanged"), "credential_unchanged");
        for (var worker = 0; worker < workers; worker++)
        {
            storage.AddPeer(NewPeer($"authorized_{worker}"), $"credential_authorized_{worker}");
            for (var index = 0; index < updates; index++)
                storage.AddPeer(NewPeer($"revoked_{worker}_{index}"), "revoked-credential");
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, workers * 2).Select(worker => Task.Run(async () =>
        {
            var path = OperatingSystem.IsWindows() && worker % 2 == 0 ? home.Path.ToUpperInvariant() : home.Path;
            await using var runtime = RemoteToolHostRuntime.Create(new RemoteToolHostRuntimeOptions { CraftHome = path });
            await start.Task;
            for (var index = 0; index < updates; index++)
            {
                if (worker < workers)
                    await runtime.SetAuthorizationAsync($"authorized_{worker}", RemoteToolAuthorization.WorkspacePreferred);
                else
                    await runtime.RevokeAsync($"revoked_{worker - workers}_{index}");
            }
        })).ToArray();

        start.SetResult();
        await Task.WhenAll(tasks);

        var persisted = storage.LoadHostState()!;
        Assert.Equal(initial.CatalogRevision + workers * updates, persisted.CatalogRevision);
        Assert.Equal(workers + 1, persisted.Peers.Count);
        var unchanged = Assert.Single(persisted.Peers, peer => peer.PeerId == "unchanged");
        Assert.Equal(RemoteToolAuthorization.FullAccess, unchanged.AuthorizationMode);
        Assert.Equal(0, unchanged.AuthorizationRevision);
        for (var worker = 0; worker < workers; worker++)
        {
            var peer = Assert.Single(persisted.Peers, peer => peer.PeerId == $"authorized_{worker}");
            Assert.Equal(RemoteToolAuthorization.WorkspacePreferred, peer.AuthorizationMode);
            Assert.Equal(updates, peer.AuthorizationRevision);
            for (var index = 0; index < updates; index++)
                Assert.Throws<RemoteToolHostException>(() =>
                    storage.GetPeerCredential(NewPeer($"revoked_{worker}_{index}")));
        }
        Assert.Equal(workers + 1, Directory.GetFiles(storage.CredentialsPath).Length);
        Assert.All(persisted.Peers, peer =>
            Assert.Equal($"credential_{peer.PeerId}", storage.GetPeerCredential(peer)));

        await using var reloaded = RemoteToolHostRuntime.Create(new RemoteToolHostRuntimeOptions { CraftHome = home.Path });
        Assert.Equal(
            persisted.Peers.Select(peer => peer.PeerId).Order(StringComparer.Ordinal),
            reloaded.Peers.Select(peer => peer.PeerId).Order(StringComparer.Ordinal));
        var removed = false;
        reloaded.PeersChanged += (_, _) => removed = reloaded.Peers.All(peer => peer.PeerId != "unchanged");
        await reloaded.RevokeAsync("unchanged");
        Assert.True(removed);
    }

    private static RemoteToolHubPeer NewPeer(string id) => new()
    {
        PeerId = id,
        HubHost = "127.0.0.1",
        HubPort = 47600,
        HubScheme = Uri.UriSchemeHttp,
        WorkspaceId = "repo",
        AuthorizationMode = RemoteToolAuthorization.FullAccess,
        PairedAt = DateTimeOffset.UtcNow
    };
}
