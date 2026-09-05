using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotCraft.Tools;

namespace DotCraft.RemoteTools;

internal interface IRemoteToolCredentialStore
{
    void Write(string reference, string secret);
    string? Read(string reference);
    void Delete(string reference);
}

internal sealed class WindowsRemoteToolCredentialStore : IRemoteToolCredentialStore
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;

    public void Write(string reference, string secret)
    {
        EnsureWindows();
        var bytes = Encoding.Unicode.GetBytes(secret);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = CredTypeGeneric,
                TargetName = reference,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = Environment.UserName
            };
            if (!CredWrite(ref credential, 0))
                throw new InvalidOperationException($"Credential Manager write failed ({Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    public string? Read(string reference)
    {
        EnsureWindows();
        if (!CredRead(reference, CredTypeGeneric, 0, out var pointer))
        {
            const int ErrorNotFound = 1168;
            if (Marshal.GetLastWin32Error() == ErrorNotFound)
                return null;
            throw new InvalidOperationException($"Credential Manager read failed ({Marshal.GetLastWin32Error()}).");
        }
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            return credential.CredentialBlob == IntPtr.Zero
                ? string.Empty
                : Marshal.PtrToStringUni(
                    credential.CredentialBlob,
                    checked((int)credential.CredentialBlobSize / 2));
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Delete(string reference)
    {
        EnsureWindows();
        if (!CredDelete(reference, CredTypeGeneric, 0) && Marshal.GetLastWin32Error() != 1168)
            throw new InvalidOperationException($"Credential Manager delete failed ({Marshal.GetLastWin32Error()}).");
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "Remote Tool Host credential persistence is currently implemented for Windows v1.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("Advapi32.dll")]
    private static extern void CredFree(IntPtr credential);
}

internal sealed class RemoteToolHostStorage
{
    private const string CredentialPrefix = "DotCraft/RemoteToolHost/";
    private readonly string _root;
    private readonly IRemoteToolCredentialStore _credentials;
    private readonly object _auditGate = new();

    public RemoteToolHostStorage(string? craftHome = null, IRemoteToolCredentialStore? credentials = null)
    {
        var home = craftHome ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".craft");
        CraftHomePath = Path.GetFullPath(home);
        _root = Path.Combine(CraftHomePath, "remote-tool-host");
        _credentials = credentials ?? new WindowsRemoteToolCredentialStore();
    }

    public string CraftHomePath { get; }
    public string GlobalConfigPath => Path.Combine(CraftHomePath, "config.json");
    public string RootPath => _root;
    public string HostStatePath => Path.Combine(_root, "host.json");
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

    public static string PeerCredentialReference(string peerId) => CredentialPrefix + "peer/" + peerId;

    public RemoteToolHubPeer AddPeer(RemoteToolHubPeer peer, string rawCredential)
    {
        var state = LoadHostState()
            ?? throw new RemoteToolHostException(
                Tools.RemoteToolErrorCodes.HostNotRegistered,
                "Remote Tool Host is not set up.");
        _credentials.Write(peer.CredentialReference, rawCredential);
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
        _credentials.Delete(peer.CredentialReference);
        SaveHostState(state with
        {
            Peers = [.. state.Peers.Where(item => !ReferenceEquals(item, peer))]
        });
        return true;
    }

    public string GetPeerCredential(RemoteToolHubPeer peer) =>
        _credentials.Read(peer.CredentialReference)
        ?? throw new RemoteToolHostException(
            Tools.RemoteToolErrorCodes.AuthenticationFailed,
            $"Credential '{peer.CredentialReference}' is missing.");

    private T? Read<T>(string path)
    {
        if (!File.Exists(path))
            return default;
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), RemoteToolHostProtocol.JsonOptions);
    }

    private void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(_root);
        var tempPath = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(value, RemoteToolHostProtocol.JsonOptions));
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
