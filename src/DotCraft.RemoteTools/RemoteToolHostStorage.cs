using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Tools;

namespace DotCraft.RemoteTools;

internal sealed class RemoteToolHostStorage
{
    private readonly string _root;
    private readonly object _auditGate = new();

    public RemoteToolHostStorage(string? craftHome = null)
    {
        var home = craftHome ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".craft");
        CraftHomePath = Path.GetFullPath(home);
        _root = Path.Combine(CraftHomePath, "remote-tool-host");
    }

    public string CraftHomePath { get; }
    public string GlobalConfigPath => Path.Combine(CraftHomePath, "config.json");
    public string RootPath => _root;
    public string HostStatePath => Path.Combine(_root, "host.json");
    public string CredentialsPath => Path.Combine(_root, "credentials.json");
    public string ServeLockPath => Path.Combine(_root, "serve.lock");
    public string ArtifactsRootPath => Path.Combine(_root, "artifacts");

    internal void AppendAudit(RemoteToolAuditEntry entry)
    {
        var directory = Path.Combine(_root, "audit");
        var path = Path.Combine(directory, $"{entry.Timestamp:yyyy-MM-dd}.jsonl");
        var line = JsonSerializer.Serialize(entry, RemoteToolHostProtocol.JsonOptions) + Environment.NewLine;
        lock (_auditGate)
        {
            Directory.CreateDirectory(directory);
            File.AppendAllText(path, line);
        }
    }

    public RemoteToolHostState? LoadHostState() => Read<RemoteToolHostState>(HostStatePath);

    public void SaveHostState(RemoteToolHostState state) => Write(HostStatePath, state);

    public RemoteToolHubPeer AddPeer(RemoteToolHubPeer peer, string rawCredential)
    {
        var state = LoadHostState()
            ?? throw new RemoteToolHostException(
                Tools.RemoteToolErrorCodes.HostNotRegistered,
                "Remote Tool Host is not set up.");
        SaveCredentials(LoadCredentials().SetItem(peer.PeerId, rawCredential));
        SaveHostState(state with
        {
            Peers =
            [
                .. state.Peers.Where(item => !string.Equals(item.PeerId, peer.PeerId, StringComparison.Ordinal)),
                peer
            ]
        });
        return peer;
    }

    public bool RemovePeer(string peerId)
    {
        var state = LoadHostState();
        var peer = state?.Peers.FirstOrDefault(
            item => string.Equals(item.PeerId, peerId, StringComparison.Ordinal));
        if (state is null || peer is null)
            return false;
        SaveCredentials(LoadCredentials().Remove(peerId));
        SaveHostState(state with
        {
            Peers = [.. state.Peers.Where(item => !ReferenceEquals(item, peer))]
        });
        return true;
    }

    public string GetPeerCredential(RemoteToolHubPeer peer) =>
        LoadCredentials().GetValueOrDefault(peer.PeerId)
        ?? throw new RemoteToolHostException(
            Tools.RemoteToolErrorCodes.AuthenticationFailed,
            $"The credential for pairing '{peer.PeerId}' is missing from {CredentialsPath}.");

    public ImmutableDictionary<string, string> LoadCredentials() =>
        Read<ImmutableDictionary<string, string>>(CredentialsPath) ?? ImmutableDictionary<string, string>.Empty;

    private void SaveCredentials(ImmutableDictionary<string, string> credentials) =>
        Write(CredentialsPath, credentials, OwnerOnlyFile.Create);

    private T? Read<T>(string path)
    {
        if (!File.Exists(path))
            return default;
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), RemoteToolHostProtocol.JsonOptions);
    }

    private void Write<T>(string path, T value, Func<string, FileStream>? create = null)
    {
        Directory.CreateDirectory(_root);
        var tempPath = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = (create ?? File.Create)(tempPath))
                JsonSerializer.Serialize(stream, value, RemoteToolHostProtocol.JsonOptions);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}

internal static class TokenUtilities
{
    public static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static bool VerifyToken(string token, string expectedHash)
    {
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        byte[] expected;
        try
        {
            expected = Convert.FromHexString(expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
