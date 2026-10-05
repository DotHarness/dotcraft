using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using DotCraft.RemoteTools;

namespace DotCraft.Hub;

internal sealed class MobileRegistry(string filePath, TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan PairingValidity = TimeSpan.FromMinutes(10);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private MobileRegistryFile? _cache;

    public bool Enabled
    {
        get
        {
            lock (_gate)
                return Load().Enabled;
        }
    }

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            var file = Load();
            if (file.Enabled == enabled)
                return;
            file.Enabled = enabled;
            Save(file);
        }
    }

    public MobileRelayRecord? Relay
    {
        get
        {
            lock (_gate)
                return Load().Relay;
        }
    }

    public string EnsureComputerId()
    {
        lock (_gate)
        {
            var file = Load();
            if (file.ComputerId is null)
            {
                file.ComputerId = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
                Save(file);
            }
            return file.ComputerId;
        }
    }

    public void SetRelay(MobileRelayRecord? relay)
    {
        lock (_gate)
        {
            var file = Load();
            file.Relay = relay;
            Save(file);
        }
    }

    public IReadOnlyList<MobileDeviceRecord> ListDevices()
    {
        lock (_gate)
            return [.. Load().Devices];
    }

    public MobileDeviceRecord? Find(string deviceId)
    {
        lock (_gate)
            return Load().Devices.FirstOrDefault(device => string.Equals(device.DeviceId, deviceId, StringComparison.Ordinal));
    }

    public MobileDeviceRecord? FindByCredential(string credential)
    {
        lock (_gate)
            return Load().Devices.FirstOrDefault(device => TokenUtilities.VerifyToken(credential, device.CredentialHash));
    }

    public (string PairingId, string Code, DateTimeOffset ExpiresAt) MintPairing()
    {
        var code = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        var pairingId = "pair_" + Guid.NewGuid().ToString("N");
        var expiresAt = _time.GetUtcNow() + PairingValidity;
        lock (_gate)
        {
            var file = Load();
            file.Pairing = new MobilePairingRecord(pairingId, TokenUtilities.HashToken(code), expiresAt);
            Save(file);
        }
        return (pairingId, code, expiresAt);
    }

    public string? TryConsumePairing(string code)
    {
        lock (_gate)
        {
            var file = Load();
            var pairing = file.Pairing;
            if (pairing is null
                || pairing.ExpiresAt <= _time.GetUtcNow()
                || !TokenUtilities.VerifyToken(code, pairing.CodeHash))
                return null;
            file.Pairing = null;
            Save(file);
            return pairing.PairingId;
        }
    }

    public (MobileDeviceRecord Device, string Credential) AddDevice(
        string displayName,
        string platform,
        string osVersion,
        string appVersion)
    {
        var credential = TokenUtilities.GenerateToken();
        var device = new MobileDeviceRecord
        {
            DeviceId = "dev_" + Guid.NewGuid().ToString("N"),
            DisplayName = displayName,
            Platform = platform,
            OsVersion = osVersion,
            AppVersion = appVersion,
            CredentialHash = TokenUtilities.HashToken(credential),
            PairedAt = _time.GetUtcNow()
        };
        lock (_gate)
        {
            var file = Load();
            file.Devices.Add(device);
            Save(file);
        }
        return (device, credential);
    }

    public bool Revoke(string deviceId)
    {
        lock (_gate)
        {
            var file = Load();
            if (file.Devices.RemoveAll(device => string.Equals(device.DeviceId, deviceId, StringComparison.Ordinal)) == 0)
                return false;
            Save(file);
            return true;
        }
    }

    public DateTimeOffset? Touch(string deviceId)
    {
        var seenAt = _time.GetUtcNow();
        lock (_gate)
        {
            var file = Load();
            var index = file.Devices.FindIndex(device => string.Equals(device.DeviceId, deviceId, StringComparison.Ordinal));
            if (index < 0)
                return null;
            file.Devices[index] = file.Devices[index] with { LastSeenAt = seenAt };
            Save(file);
            return seenAt;
        }
    }

    private MobileRegistryFile Load()
    {
        if (_cache is not null)
            return _cache;
        try
        {
            if (File.Exists(filePath))
                _cache = JsonSerializer.Deserialize<MobileRegistryFile>(File.ReadAllText(filePath), HubJson.Options);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _cache = null;
        }
        return _cache ??= new MobileRegistryFile();
    }

    private void Save(MobileRegistryFile file)
    {
        _cache = file;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        var tempPath = filePath + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(tempPath, options))
                JsonSerializer.Serialize(stream, file, HubJson.Options);
            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}

internal sealed record MobileDeviceRecord
{
    public required string DeviceId { get; init; }
    public required string DisplayName { get; init; }
    public required string Platform { get; init; }
    public string OsVersion { get; init; } = string.Empty;
    public string AppVersion { get; init; } = string.Empty;
    public required string CredentialHash { get; init; }
    public DateTimeOffset PairedAt { get; init; }
    public DateTimeOffset? LastSeenAt { get; init; }
}

internal sealed record MobilePairingRecord(string PairingId, string CodeHash, DateTimeOffset ExpiresAt);

internal sealed record MobileRelayRecord(string Url, string Token);

internal sealed class MobileRegistryFile
{
    public bool Enabled { get; set; }
    public List<MobileDeviceRecord> Devices { get; init; } = [];
    public MobilePairingRecord? Pairing { get; set; }
    public string? ComputerId { get; set; }
    public MobileRelayRecord? Relay { get; set; }
}
