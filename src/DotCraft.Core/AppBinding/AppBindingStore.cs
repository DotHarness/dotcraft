using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.AppBinding;

/// <summary>
/// File-backed App Binding state store scoped to one workspace .craft directory.
/// </summary>
internal sealed class AppBindingStore(string workspaceCraftPath)
{
    private readonly Lock _lock = new();
    private readonly string _statePath = Path.Combine(workspaceCraftPath, "app-bindings", "state.json");

    public AppBindingStateDocument Snapshot()
    {
        lock (_lock)
        {
            return Clone(LoadNoLock());
        }
    }

    public T Update<T>(Func<AppBindingStateDocument, T> update)
    {
        lock (_lock)
        {
            var state = LoadNoLock();
            var result = update(state);
            SaveNoLock(state);
            return result;
        }
    }

    private AppBindingStateDocument LoadNoLock()
    {
        if (!File.Exists(_statePath))
            return new AppBindingStateDocument();

        try
        {
            return JsonSerializer.Deserialize<AppBindingStateDocument>(
                       File.ReadAllText(_statePath),
                       SessionWireJsonOptions.Default)
                   ?? new AppBindingStateDocument();
        }
        catch
        {
            return new AppBindingStateDocument();
        }
    }

    private void SaveNoLock(AppBindingStateDocument state)
    {
        var directory = Path.GetDirectoryName(_statePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(
            state,
            new JsonSerializerOptions(SessionWireJsonOptions.Default)
            {
                WriteIndented = true
            });
        File.WriteAllText(_statePath, $"{json}{Environment.NewLine}", new UTF8Encoding(false));
    }

    private static AppBindingStateDocument Clone(AppBindingStateDocument state) =>
        JsonSerializer.Deserialize<AppBindingStateDocument>(
            JsonSerializer.Serialize(state, SessionWireJsonOptions.Default),
            SessionWireJsonOptions.Default) ?? new AppBindingStateDocument();
}

internal sealed class AppBindingStateDocument
{
    public List<AppConnectionRecord> Connections { get; set; } = [];

    public List<AppConnectionRequestRecord> ConnectionRequests { get; set; } = [];

    public List<AppBindingRequestRecord> BindingRequests { get; set; } = [];

    public List<AppBindingRecord> Bindings { get; set; } = [];

    public List<AppBindingAuditRecord> Audit { get; set; } = [];
}

internal sealed class AppConnectionRecord
{
    public string AppId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string State { get; set; } = AppConnectionStates.NotConnected;

    public DateTimeOffset? ConnectedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public string? AccountLabel { get; set; }

    public JsonObject? ConnectionProof { get; set; }

    public JsonObject? PublicMetadata { get; set; }

    public string? Diagnostic { get; set; }
}

internal sealed class AppConnectionRequestRecord
{
    public string ConnectionRequestId { get; set; } = string.Empty;

    public string AppId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string RequestTokenHash { get; set; } = string.Empty;

    public string State { get; set; } = AppConnectionStates.Connecting;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public bool Consumed { get; set; }
}

internal sealed class AppBindingRequestRecord
{
    public string BindingRequestId { get; set; } = string.Empty;

    public string ThreadId { get; set; } = string.Empty;

    public string AppId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public List<string> RequestedScopes { get; set; } = [];

    public List<string>? RequestedTools { get; set; }

    public string? Reason { get; set; }

    public string Source { get; set; } = string.Empty;

    public string RequestTokenHash { get; set; } = string.Empty;

    public string BindingKind { get; set; } = AppBindingKinds.App;

    public SocialBindingIntentWire? SocialIntent { get; set; }

    public string State { get; set; } = AppBindingStates.Pending;

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

    public string State { get; set; } = AppBindingStates.Pending;

    public string BindingKind { get; set; } = AppBindingKinds.App;

    public string GrantId { get; set; } = string.Empty;

    public List<string> RequestedScopes { get; set; } = [];

    public List<string> GrantedScopes { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastChangedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public string ApprovalMode { get; set; } = string.Empty;

    public string? ApprovedBy { get; set; }

    public string? AuditRef { get; set; }

    public string? Diagnostic { get; set; }

    public SocialChannelTargetWire? SocialTarget { get; set; }

    public long ExposureRevision { get; set; }

    public List<AppBoundToolSpec> AttachedTools { get; set; } = [];

    public List<string> DirectToolNames { get; set; } = [];

    public List<string> DeferredToolNames { get; set; } = [];

    public JsonObject? GrantProof { get; set; }

    public List<AppContextBlockRecord> ContextBlocks { get; set; } = [];
}

internal sealed class AppContextBlockRecord
{
    public string BlockId { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public int Order { get; set; }

    public string Version { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public string Visibility { get; set; } = AppContextBlockVisibilities.Model;
}

internal sealed class AppBindingAuditRecord
{
    public DateTimeOffset Timestamp { get; set; }

    public string Event { get; set; } = string.Empty;

    public string? ThreadId { get; set; }

    public string? BindingId { get; set; }

    public string? AppId { get; set; }

    public string? UserId { get; set; }

    public string? Detail { get; set; }
}

internal static class AppBindingToken
{
    public static string NewToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static bool Matches(string token, string hash) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Hash(token)),
            Encoding.UTF8.GetBytes(hash));
}
