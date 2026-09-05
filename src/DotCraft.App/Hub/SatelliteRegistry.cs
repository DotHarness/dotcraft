using System.Text.Json;
using DotCraft.RemoteTools;

namespace DotCraft.Hub;

/// <summary>
/// Durable pairing state for Remote Tool Hosts that dial into this Hub, persisting only credential
/// and invitation hashes while raw secrets stay in memory and on the remote machine.
/// </summary>
internal sealed class SatelliteRegistry(string filePath)
{
    private readonly object _gate = new();
    private SatelliteRegistryFile? _cache;

    public IReadOnlyList<SatellitePeerRecord> ListPeers()
    {
        lock (_gate)
            return [.. Load().Peers];
    }

    public bool HasPeers()
    {
        lock (_gate)
            return Load().Peers.Count > 0;
    }

    public SatellitePeerRecord? FindPeer(string peerId)
    {
        lock (_gate)
            return Load().Peers.FirstOrDefault(peer => string.Equals(peer.PeerId, peerId, StringComparison.Ordinal));
    }

    public (string InviteId, DateTimeOffset ExpiresAt) CreateInvite(
        string label,
        string? purpose,
        string? folder,
        TimeSpan validity)
    {
        var inviteId = "inv_" + TokenUtilities.GenerateToken();
        var expiresAt = DateTimeOffset.UtcNow + validity;
        lock (_gate)
        {
            var file = Load();
            PruneExpiredInvites(file);
            file.Invites.Add(new SatelliteInviteRecord(
                TokenUtilities.HashToken(inviteId),
                label,
                purpose,
                folder,
                expiresAt));
            Save(file);
        }
        return (inviteId, expiresAt);
    }

    public SatelliteInviteRecord? FindInvite(string inviteId)
    {
        lock (_gate)
        {
            var file = Load();
            PruneExpiredInvites(file);
            return file.Invites.FirstOrDefault(
                invite => TokenUtilities.VerifyToken(inviteId, invite.InviteIdHash));
        }
    }

    public bool TryConsumeInvite(string inviteId, out SatelliteInviteRecord? invite)
    {
        lock (_gate)
        {
            var file = Load();
            PruneExpiredInvites(file);
            invite = file.Invites.FirstOrDefault(
                candidate => TokenUtilities.VerifyToken(inviteId, candidate.InviteIdHash));
            if (invite is null)
                return false;
            file.Invites.Remove(invite);
            Save(file);
            return true;
        }
    }

    public (SatellitePeerRecord Peer, string Credential) Pair(string displayName, SatelliteFrame hello)
    {
        var credential = TokenUtilities.GenerateToken();
        var peer = new SatellitePeerRecord
        {
            PeerId = "sat_" + Guid.NewGuid().ToString("N"),
            DisplayName = displayName,
            CredentialHash = TokenUtilities.HashToken(credential),
            MachineName = hello.MachineName ?? string.Empty,
            OperatingSystem = hello.OperatingSystem ?? string.Empty,
            UserName = hello.UserName ?? string.Empty,
            BuildVersion = hello.BuildVersion ?? string.Empty,
            Workspaces = hello.Workspaces ?? [],
            PairedAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow
        };
        lock (_gate)
        {
            var file = Load();
            file.Peers.Add(peer);
            Save(file);
        }
        return (peer, credential);
    }

    public SatellitePeerRecord? Verify(string peerId, string credential)
    {
        lock (_gate)
        {
            var peer = Load().Peers.FirstOrDefault(
                candidate => string.Equals(candidate.PeerId, peerId, StringComparison.Ordinal));
            return peer is not null && TokenUtilities.VerifyToken(credential, peer.CredentialHash)
                ? peer
                : null;
        }
    }

    public bool Revoke(string peerId)
    {
        lock (_gate)
        {
            var file = Load();
            var removed = file.Peers.RemoveAll(
                peer => string.Equals(peer.PeerId, peerId, StringComparison.Ordinal));
            if (removed == 0)
                return false;
            Save(file);
            return true;
        }
    }

    public void UpdateFromHeartbeat(
        string peerId,
        IReadOnlyList<SatelliteWorkspaceInfo> workspaces,
        DateTimeOffset seenAt)
    {
        lock (_gate)
        {
            var file = Load();
            var index = file.Peers.FindIndex(
                peer => string.Equals(peer.PeerId, peerId, StringComparison.Ordinal));
            if (index < 0)
                return;
            file.Peers[index] = file.Peers[index] with { Workspaces = workspaces, LastSeenAt = seenAt };
            Save(file);
        }
    }

    private static void PruneExpiredInvites(SatelliteRegistryFile file)
    {
        var now = DateTimeOffset.UtcNow;
        file.Invites.RemoveAll(invite => invite.ExpiresAt <= now);
    }

    private SatelliteRegistryFile Load()
    {
        if (_cache is not null)
            return _cache;
        try
        {
            if (File.Exists(filePath))
            {
                _cache = JsonSerializer.Deserialize<SatelliteRegistryFile>(
                    File.ReadAllText(filePath),
                    HubJson.Options);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _cache = null;
        }
        return _cache ??= new SatelliteRegistryFile();
    }

    private void Save(SatelliteRegistryFile file)
    {
        _cache = file;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        var tempPath = filePath + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(file, HubJson.Options));
            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}

internal sealed record SatellitePeerRecord
{
    public required string PeerId { get; init; }
    public required string DisplayName { get; init; }
    public required string CredentialHash { get; init; }
    public string MachineName { get; init; } = string.Empty;
    public string OperatingSystem { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string BuildVersion { get; init; } = string.Empty;
    public IReadOnlyList<SatelliteWorkspaceInfo> Workspaces { get; init; } = [];
    public DateTimeOffset PairedAt { get; init; }
    public DateTimeOffset? LastSeenAt { get; init; }
}

internal sealed record SatelliteInviteRecord(
    string InviteIdHash,
    string Label,
    string? Purpose,
    string? Folder,
    DateTimeOffset ExpiresAt);

internal sealed record SatelliteRegistryFile
{
    public List<SatellitePeerRecord> Peers { get; init; } = [];
    public List<SatelliteInviteRecord> Invites { get; init; } = [];
}
