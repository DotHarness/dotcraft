using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotCraft.Sessions.Wire;

namespace DotCraft.AppBinding;

internal sealed class AppBindingStateStore(string workspaceCraftPath)
{
    private readonly Lock _lock = new();
    private readonly string _statePath = Path.Combine(workspaceCraftPath, "app-bindings", "state.json");
    private AppBindingStateDocument? _state;

    public AppBindingStateDocument Snapshot()
    {
        lock (_lock)
            return Clone(GetStateNoLock());
    }

    public T Update<T>(Func<AppBindingStateDocument, T> update)
    {
        lock (_lock)
        {
            var state = GetStateNoLock();
            var result = update(state);
            SaveNoLock(state);
            return result;
        }
    }

    private AppBindingStateDocument GetStateNoLock()
    {
        if (_state != null)
            return _state;
        _state = LoadNoLock();
        var changed = false;
        foreach (var binding in _state.Bindings.Where(binding => binding.State != AppBindingStates.Revoked))
        {
            binding.State = AppBindingStates.Offline;
            binding.FailureReason = "runtimeRestarted";
            binding.UpdatedAt = DateTimeOffset.UtcNow;
            _state.Audit.Add(new AppBindingAuditRecord
            {
                Timestamp = binding.UpdatedAt,
                Event = "binding.offline.runtimeRestarted",
                BindingId = binding.BindingId,
                AppId = binding.AppId,
                ThreadId = binding.ThreadId,
                AuthorityRevision = binding.AuthorityRevision,
                CapabilityRevision = binding.ApprovedCapabilityRevision
            });
            changed = true;
        }
        if (changed) SaveNoLock(_state);
        return _state;
    }

    private AppBindingStateDocument LoadNoLock()
    {
        if (!File.Exists(_statePath))
            return new();

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_statePath));
            if (!document.RootElement.TryGetProperty("schemaVersion", out var version)
                || version.GetInt32() != AppBindingContract.Version)
            {
                ArchiveNoLock("v1");
                return new();
            }

            return JsonSerializer.Deserialize<AppBindingStateDocument>(
                       document.RootElement.GetRawText(),
                       SessionWireJsonOptions.Default) ?? new();
        }
        catch
        {
            ArchiveNoLock("corrupt");
            return new();
        }
    }

    private void ArchiveNoLock(string reason)
    {
        if (!File.Exists(_statePath))
            return;
        var archive = Path.Combine(
            Path.GetDirectoryName(_statePath)!,
            $"state.{reason}.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.json");
        File.Move(_statePath, archive, overwrite: false);
    }

    private void SaveNoLock(AppBindingStateDocument state)
    {
        var directory = Path.GetDirectoryName(_statePath)!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(_statePath)}.{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(
            state,
            new JsonSerializerOptions(SessionWireJsonOptions.Default) { WriteIndented = true });
        File.WriteAllText(temp, $"{json}{Environment.NewLine}", new UTF8Encoding(false));
        File.Move(temp, _statePath, overwrite: true);
    }

    private static AppBindingStateDocument Clone(AppBindingStateDocument value) =>
        JsonSerializer.Deserialize<AppBindingStateDocument>(
            JsonSerializer.Serialize(value, SessionWireJsonOptions.Default),
            SessionWireJsonOptions.Default) ?? new();
}

internal sealed class AppBindingStateDocument
{
    public int SchemaVersion { get; set; } = AppBindingContract.Version;
    public List<AppPrincipalRecord> Principals { get; set; } = [];
    public List<AppConnectionRequestRecord> ConnectionRequests { get; set; } = [];
    public List<AppBindingRequestRecord> BindingRequests { get; set; } = [];
    public List<AppBindingRecord> Bindings { get; set; } = [];
    public List<AppBindingAuditRecord> Audit { get; set; } = [];
}

internal sealed class AppPrincipalRecord
{
    public string PrincipalId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string CredentialSalt { get; set; } = string.Empty;
    public string CredentialVerifier { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? AccountLabel { get; set; }
}

internal sealed class AppConnectionRequestRecord
{
    public string ConnectionRequestId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string RequestTokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Consumed { get; set; }
}

internal sealed class AppBindingRequestRecord
{
    public string BindingRequestId { get; set; } = string.Empty;
    public string BindingId { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string RequestTokenHash { get; set; } = string.Empty;
    public string State { get; set; } = AppBindingStates.Connecting;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Consumed { get; set; }
}

internal sealed class AppBindingRecord
{
    public string BindingId { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string PrincipalId { get; set; } = string.Empty;
    public string Kind { get; set; } = "app";
    public string State { get; set; } = AppBindingStates.Connecting;
    public long AuthorityRevision { get; set; } = 1;
    public long ApprovedCapabilityRevision { get; set; }
    public long? CandidateCapabilityRevision { get; set; }
    public List<AppBindingToolCapability> ApprovedTools { get; set; } = [];
    public List<AppBindingToolCapability> CandidateTools { get; set; } = [];
    public List<AppBindingCapabilityChange> PendingChanges { get; set; } = [];
    public SocialChannelTarget? SocialTarget { get; set; }
    public string? EndpointIdentity { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

internal sealed class AppBindingAuditRecord
{
    public DateTimeOffset Timestamp { get; set; }
    public string Event { get; set; } = string.Empty;
    public string? Actor { get; set; }
    public string? AppId { get; set; }
    public string? ThreadId { get; set; }
    public string? BindingId { get; set; }
    public long? AuthorityRevision { get; set; }
    public long? CapabilityRevision { get; set; }
    public string? Reason { get; set; }
}

internal static class AppBindingSecrets
{
    public static string NewSecret()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static (string Salt, string Verifier) CreateVerifier(string secret)
    {
        Span<byte> salt = stackalloc byte[32];
        RandomNumberGenerator.Fill(salt);
        return (Convert.ToHexString(salt).ToLowerInvariant(), Compute(secret, salt));
    }

    public static bool Verify(string secret, string saltHex, string verifierHex)
    {
        try
        {
            var salt = Convert.FromHexString(saltHex);
            var actual = Convert.FromHexString(Compute(secret, salt));
            var expected = Convert.FromHexString(verifierHex);
            return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string HashRequestToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string Compute(string secret, ReadOnlySpan<byte> salt)
    {
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var input = new byte[salt.Length + secretBytes.Length];
        salt.CopyTo(input);
        secretBytes.CopyTo(input.AsSpan(salt.Length));
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }
}
