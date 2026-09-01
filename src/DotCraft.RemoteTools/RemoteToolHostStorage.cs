using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

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
        _root = Path.Combine(Path.GetFullPath(home), "remote-tool-host");
        _credentials = credentials ?? new WindowsRemoteToolCredentialStore();
    }

    public string RootPath => _root;
    public string HostStatePath => Path.Combine(_root, "host.json");
    public string CertificatePath => Path.Combine(_root, "host.pfx");
    public string RegistrationsPath => Path.Combine(_root, "registrations.json");

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

    public IReadOnlyList<RemoteToolHostRegistration> LoadRegistrations() =>
        Read<List<RemoteToolHostRegistration>>(RegistrationsPath) ?? [];

    public void Register(RemoteToolPairingBundle bundle)
    {
        if (!string.Equals(bundle.ProfileVersion, RemoteToolHostProtocol.ProfileVersion, StringComparison.Ordinal))
            throw new RemoteToolHostException(
                Tools.RemoteToolErrorCodes.ProtocolMismatch,
                $"Unsupported Remote Tool Host profile '{bundle.ProfileVersion}'.");
        if (!Uri.TryCreate(bundle.Endpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Pairing endpoint must be an absolute HTTPS URL.", nameof(bundle));
        }

        var registrations = LoadRegistrations().ToList();
        var reference = CredentialPrefix + bundle.HostId;
        _credentials.Write(reference, bundle.Token);
        registrations.RemoveAll(item => string.Equals(item.HostId, bundle.HostId, StringComparison.Ordinal));
        registrations.Add(new RemoteToolHostRegistration
        {
            HostId = bundle.HostId,
            DisplayName = bundle.DisplayName,
            Endpoint = endpoint.ToString().TrimEnd('/'),
            CertificateFingerprint = NormalizeFingerprint(bundle.CertificateFingerprint),
            CredentialReference = reference
        });
        Write(RegistrationsPath, registrations.OrderBy(item => item.HostId, StringComparer.Ordinal).ToArray());
    }

    public bool Unregister(string hostId)
    {
        var registrations = LoadRegistrations().ToList();
        var registration = registrations.FirstOrDefault(
            item => string.Equals(item.HostId, hostId, StringComparison.Ordinal));
        if (registration is null)
            return false;
        registrations.Remove(registration);
        _credentials.Delete(registration.CredentialReference);
        Write(RegistrationsPath, registrations);
        return true;
    }

    public string GetToken(RemoteToolHostRegistration registration) =>
        _credentials.Read(registration.CredentialReference)
        ?? throw new RemoteToolHostException(
            Tools.RemoteToolErrorCodes.AuthenticationFailed,
            $"Credential '{registration.CredentialReference}' is missing.");

    public RemoteToolPairingBundle RotateToken(RemoteToolHostState state)
    {
        var token = TokenUtilities.GenerateToken();
        SaveHostState(state with { TokenHash = TokenUtilities.HashToken(token) });
        return new RemoteToolPairingBundle
        {
            HostId = state.HostId,
            DisplayName = state.DisplayName,
            Endpoint = state.ListenEndpoint,
            CertificateFingerprint = state.CertificateFingerprint,
            Token = token
        };
    }

    public static string NormalizeFingerprint(string value) =>
        value.Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

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

internal static class RemoteToolCertificate
{
    public static X509Certificate2 Create(string endpoint, string path)
    {
        var uri = new Uri(endpoint, UriKind.Absolute);
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest(
            $"CN={uri.Host}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            false));
        var san = new SubjectAlternativeNameBuilder();
        if (System.Net.IPAddress.TryParse(uri.Host, out var address))
            san.AddIpAddress(address);
        else
            san.AddDnsName(uri.Host);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(5));
        var bytes = certificate.Export(X509ContentType.Pfx);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        CryptographicOperations.ZeroMemory(bytes);
        return X509CertificateLoader.LoadPkcs12FromFile(
            path,
            password: null,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
    }

    public static string Fingerprint(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData));
}
