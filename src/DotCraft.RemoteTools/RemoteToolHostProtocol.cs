using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.RemoteTools;

internal static class RemoteToolHostProtocol
{
    public const string ProfileVersion = "1";
    public const string McpProtocolVersion = "2025-06-18";
    public const int MaxTransportResultChars = 100_000;
    public const string WorkspacesList = "dotcraft/remoteToolHost/workspaces/list";
    public const string WorkspacesAcquire = "dotcraft/remoteToolHost/workspaces/acquire";
    public const string WorkspacesRelease = "dotcraft/remoteToolHost/workspaces/release";
    public const string WorkspacesHeartbeat = "dotcraft/remoteToolHost/workspaces/heartbeat";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record RemoteToolPairingBundle
{
    public string ProfileVersion { get; init; } = RemoteToolHostProtocol.ProfileVersion;
    public required string HostId { get; init; }
    public required string DisplayName { get; init; }
    public required string Endpoint { get; init; }
    public required string CertificateFingerprint { get; init; }
    public required string Token { get; init; }
}

internal sealed record RemoteToolHostRegistration
{
    public string ProfileVersion { get; init; } = RemoteToolHostProtocol.ProfileVersion;
    public required string HostId { get; init; }
    public required string DisplayName { get; init; }
    public required string Endpoint { get; init; }
    public required string CertificateFingerprint { get; init; }
    public required string CredentialReference { get; init; }
}

internal sealed record RemoteToolHostState
{
    public string ProfileVersion { get; init; } = RemoteToolHostProtocol.ProfileVersion;
    public required string HostId { get; init; }
    public required string DisplayName { get; init; }
    public required string ListenEndpoint { get; init; }
    public required string CertificatePath { get; init; }
    public required string CertificateFingerprint { get; init; }
    public required string TokenHash { get; init; }
    public long CatalogRevision { get; init; } = 1;
    public Dictionary<string, string> Workspaces { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> ToolPolicies { get; init; } = new(StringComparer.Ordinal);
}

internal sealed record WorkspaceListRequest(string ProfileVersion, string ClientInstanceId);
internal sealed record WorkspaceListResponse(
    string ProfileVersion,
    string HostId,
    string DisplayName,
    string HostInstanceId,
    string Hostname,
    string Os,
    string Username,
    long CatalogRevision,
    IReadOnlyList<WorkspaceCatalogEntry> Workspaces);
internal sealed record WorkspaceCatalogEntry(string WorkspaceId, string Path, bool Busy);
internal sealed record WorkspaceAcquireRequest(
    string ProfileVersion,
    string ClientInstanceId,
    string WorkspaceId);
internal sealed record WorkspaceAcquireResponse(
    string LeaseId,
    string WorkspaceId,
    string WorkspacePath,
    DateTimeOffset ExpiresAt,
    string HostInstanceId,
    long CatalogRevision);
internal sealed record WorkspaceLeaseRequest(
    string ProfileVersion,
    string ClientInstanceId,
    string LeaseId,
    string WorkspaceId);
internal sealed record WorkspaceHeartbeatResponse(DateTimeOffset ExpiresAt);
internal sealed record ExtensionError(string Code, string Message);
internal sealed record ExtensionResponse<T>(bool Success, T? Result, ExtensionError? Error);

internal sealed record RemoteInvocationMeta(
    string LeaseId,
    string WorkspaceId,
    string InvocationId,
    string DefinitionId,
    string ContractHash,
    string? ThreadId,
    string? TurnId,
    int MaxResultChars,
    int SpillPreviewLines);

internal sealed record RemoteToolArtifactMeta(string Path, long CharacterCount);

internal sealed record RemoteToolAuditEntry(
    DateTimeOffset Timestamp,
    string? ThreadId,
    string? TurnId,
    string WorkspaceId,
    string ToolName,
    string InvocationId,
    string ResultCode,
    long DurationMilliseconds,
    bool Cancelled);
