using System.Text;
using DotCraft.Hub;
using DotCraft.RemoteTools;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class SatelliteRegistryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "DotCraftSatelliteRegistry_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void CreateInvite_ThenConsume_Once()
    {
        var registry = NewRegistry();

        var (inviteId, expiresAt) = registry.CreateInvite("Ann", purpose: null, TimeSpan.FromHours(24));

        Assert.True(expiresAt > DateTimeOffset.UtcNow.AddHours(23));
        Assert.NotNull(registry.FindInvite(inviteId));
        Assert.True(registry.TryConsumeInvite(inviteId, out var consumed));
        Assert.Equal("Ann", consumed!.Label);
        Assert.False(registry.TryConsumeInvite(inviteId, out _));
        Assert.Null(registry.FindInvite(inviteId));
    }

    [Fact]
    public void ConsumeInvite_AfterExpiry_Fails()
    {
        var registry = NewRegistry();

        var (inviteId, _) = registry.CreateInvite("Ann", purpose: null, TimeSpan.FromMilliseconds(-1));

        Assert.Null(registry.FindInvite(inviteId));
        Assert.False(registry.TryConsumeInvite(inviteId, out _));
    }

    [Fact]
    public void Pair_PersistsOnlyCredentialHash()
    {
        var registry = NewRegistry();
        var (inviteId, _) = registry.CreateInvite(
            "Ann",
            purpose: "Fix the build",
            TimeSpan.FromHours(24));
        Assert.True(registry.TryConsumeInvite(inviteId, out _));

        var (peer, credential) = registry.Pair("Ann", Hello());

        var bytes = File.ReadAllBytes(SatellitesPath());
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain(credential, text, StringComparison.Ordinal);
        Assert.DoesNotContain(inviteId, text, StringComparison.Ordinal);
        Assert.Contains(peer.PeerId, text, StringComparison.Ordinal);
        Assert.StartsWith("sat_", peer.PeerId, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_RejectsWrongCredentialAndUnknownPeer()
    {
        var registry = NewRegistry();
        var (peer, credential) = registry.Pair("Ann", Hello());

        Assert.NotNull(registry.Verify(peer.PeerId, credential));
        Assert.Null(registry.Verify(peer.PeerId, credential + "x"));
        Assert.Null(registry.Verify("sat_unknown", credential));
    }

    [Fact]
    public void Revoke_RemovesPeerAndIsIdempotent()
    {
        var registry = NewRegistry();
        var (peer, credential) = registry.Pair("Ann", Hello());

        Assert.True(registry.Revoke(peer.PeerId));
        Assert.False(registry.Revoke(peer.PeerId));
        Assert.Null(registry.Verify(peer.PeerId, credential));
        Assert.Empty(registry.ListPeers());
        Assert.False(registry.HasPeers());
    }

    [Fact]
    public void UpdateFromHeartbeat_ReplacesWorkspacesAndLastSeen()
    {
        var registry = NewRegistry();
        var (peer, _) = registry.Pair("Ann", Hello());
        var seenAt = DateTimeOffset.UtcNow.AddMinutes(5);

        registry.UpdateFromHeartbeat(
            peer.PeerId,
            [new SatelliteWorkspaceInfo("repo", "C:\\repo", true, "other", seenAt.AddSeconds(60))],
            seenAt);

        var reloaded = new SatelliteRegistry(SatellitesPath()).ListPeers().Single();
        var workspace = Assert.Single(reloaded.Workspaces);
        Assert.Equal("repo", workspace.WorkspaceId);
        Assert.True(workspace.Busy);
        Assert.Equal(seenAt, reloaded.LastSeenAt);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (Exception) { }
    }

    private SatelliteRegistry NewRegistry() => new(SatellitesPath());

    private string SatellitesPath() => Path.Combine(_directory, "satellites.json");

    private static SatelliteFrame Hello() => new()
    {
        Kind = "hello",
        DisplayName = "ANN-PC",
        MachineName = "ANN-PC",
        OperatingSystem = "Windows",
        UserName = "ann",
        BuildVersion = "0.6.2",
        Workspaces = [new SatelliteWorkspaceInfo("repo", "C:\\repo", false)]
    };
}
