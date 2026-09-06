using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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

    /// <summary>The DotCraft build both sides compare when a catalog or profile does not match.</summary>
    public static string BuildVersion { get; } =
        ReadInformationalVersion(Assembly.GetEntryAssembly())
        ?? ReadInformationalVersion(typeof(RemoteToolHostProtocol).Assembly)
        ?? "unknown";

    public static string ComputeCatalogDigest(IReadOnlyList<RemoteToolContractSummary> contracts)
    {
        var canonical = string.Join(
            '\n',
            contracts
                .Select(contract => $"{contract.DefinitionId}:{contract.ContractHash}")
                .OrderBy(entry => entry, StringComparer.Ordinal));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string? ReadInformationalVersion(Assembly? assembly)
    {
        var value = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var separator = value.IndexOf('+', StringComparison.Ordinal);
        return separator < 0 ? value : value[..separator];
    }
}

internal sealed record RemoteToolHubPeer
{
    public required string PeerId { get; init; }
    public required string HubHost { get; init; }
    public required int HubPort { get; init; }
    public required string CredentialReference { get; init; }
    public string HubScheme { get; init; } = Uri.UriSchemeHttp;
    public string HubLabel { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public DateTimeOffset PairedAt { get; init; }

    public Uri ControlUri => BuildUri(SatelliteWire.ControlPath, $"?peer={Uri.EscapeDataString(PeerId)}");

    public Uri DataUri(string sessionId) => BuildUri(
        SatelliteWire.DataPath,
        $"?peer={Uri.EscapeDataString(PeerId)}&session={Uri.EscapeDataString(sessionId)}");

    private Uri BuildUri(string path, string query) =>
        new(
            $"{SatelliteWire.WebSocketScheme(HubScheme)}://{HubHost}:{HubPort}{path}{query}",
            UriKind.Absolute);
}

internal sealed record RemoteToolHostState
{
    public string ProfileVersion { get; init; } = RemoteToolHostProtocol.ProfileVersion;
    public required string HostId { get; init; }
    public required string DisplayName { get; init; }
    public long CatalogRevision { get; init; } = 1;
    public IReadOnlyList<RemoteToolHubPeer> Peers { get; init; } = [];
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
    string BuildVersion,
    string CatalogDigest,
    IReadOnlyList<RemoteToolContractSummary> Contracts,
    IReadOnlyList<WorkspaceCatalogEntry> Workspaces);
internal sealed record RemoteToolContractSummary(
    string DefinitionId,
    string ToolName,
    string ContractHash);
internal sealed record WorkspaceCatalogEntry(
    string WorkspaceId,
    string Path,
    bool Busy,
    string? BusyOwner = null,
    DateTimeOffset? LeaseExpiresAt = null);
internal sealed record RemoteCatalogScope(string LeaseId, string WorkspaceId);
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
