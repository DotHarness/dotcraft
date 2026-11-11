using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Tools;

namespace DotCraft.RemoteTools;

internal sealed class RemoteToolHostStorage
{
    private readonly string _root;
    private readonly string _stateMutexName;
    private readonly object _auditGate = new();

    public RemoteToolHostStorage(string? craftHome = null)
    {
        var home = craftHome ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".craft");
        CraftHomePath = Path.GetFullPath(home);
        _root = Path.Combine(CraftHomePath, "remote-tool-host");
        var identity = OperatingSystem.IsWindows() ? HostStatePath.ToUpperInvariant() : HostStatePath;
        _stateMutexName = @"Global\DotCraft.RemoteToolHost.State."
            + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    public string CraftHomePath { get; }
    public string GlobalConfigPath => Path.Combine(CraftHomePath, "config.json");
    public string RootPath => _root;
    public string HostStatePath => Path.Combine(_root, "host.json");
    public string CredentialsPath => Path.Combine(_root, "credentials");
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

    public RemoteToolHostState? LoadHostState() => WithStateLock(() => Read<RemoteToolHostState>(HostStatePath));

    public void SaveHostState(RemoteToolHostState state) => UpdateHostState(_ => state);

    public RemoteToolHostState UpdateHostState(Func<RemoteToolHostState?, RemoteToolHostState> update) =>
        WithStateLock(() =>
        {
            var state = Read<RemoteToolHostState>(HostStatePath);
            var updated = update(state);
            if (!ReferenceEquals(state, updated))
                Write(HostStatePath, updated);
            return updated;
        });

    public RemoteToolHubPeer AddPeer(RemoteToolHubPeer peer, string rawCredential)
    {
        UpdateHostState(current =>
        {
            var state = current ?? throw new RemoteToolHostException(
                Tools.RemoteToolErrorCodes.HostNotRegistered,
                "Remote Tool Host is not set up.");
            Write(CredentialPath(peer.PeerId), rawCredential, OwnerOnlyFile.Create);
            return state with
            {
                Peers =
                [
                    .. state.Peers.Where(item => !string.Equals(item.PeerId, peer.PeerId, StringComparison.Ordinal)),
                    peer
                ]
            };
        });
        return peer;
    }

    public bool RemovePeer(string peerId) => WithStateLock(() =>
    {
        var state = Read<RemoteToolHostState>(HostStatePath);
        var peer = state?.Peers.FirstOrDefault(
            item => string.Equals(item.PeerId, peerId, StringComparison.Ordinal));
        if (state is null || peer is null)
            return false;
        File.Delete(CredentialPath(peerId));
        Write(HostStatePath, state with
        {
            Peers = [.. state.Peers.Where(item => !ReferenceEquals(item, peer))]
        });
        return true;
    });

    public string GetPeerCredential(RemoteToolHubPeer peer) => WithStateLock(() =>
        Read<string>(CredentialPath(peer.PeerId))
        ?? throw new RemoteToolHostException(
            Tools.RemoteToolErrorCodes.AuthenticationFailed,
            $"The credential for pairing '{peer.PeerId}' is missing from {CredentialsPath}."));

    private T WithStateLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(initiallyOwned: false, _stateMutexName);
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired)
                throw new IOException("Remote Tool Host state is busy.");
            return action();
        }
        finally
        {
            if (acquired)
                mutex.ReleaseMutex();
        }
    }

    private string CredentialPath(string peerId) => Path.Combine(
        CredentialsPath,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(peerId))).ToLowerInvariant());

    private T? Read<T>(string path)
    {
        if (!File.Exists(path))
            return default;
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), RemoteToolHostProtocol.JsonOptions);
    }

    private void Write<T>(string path, T value, Func<string, FileStream>? create = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
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
