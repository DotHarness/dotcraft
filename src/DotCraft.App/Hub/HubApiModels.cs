namespace DotCraft.Hub;

public sealed record HubStatusResponse(
    string HubVersion,
    int Pid,
    DateTimeOffset StartedAt,
    string StatePath,
    string ApiBaseUrl,
    string? BinaryPath,
    HubCapabilities Capabilities);

public sealed record HubCapabilities(
    bool AppServerManagement,
    bool ManagedServiceManagement,
    bool PortManagement,
    bool Events,
    bool Notifications,
    bool Tray,
    bool Satellites);

public sealed record HubErrorResponse(HubError Error);

public sealed record HubError(string Code, string Message, object? Details);

/// <summary>One paired Remote Tool Host as seen by local clients.</summary>
public sealed record HubSatelliteResponse(
    string PeerId,
    string DisplayName,
    bool Online,
    string MachineName,
    string OperatingSystem,
    string UserName,
    string BuildVersion,
    IReadOnlyList<HubSatelliteWorkspaceResponse> Workspaces,
    DateTimeOffset PairedAt,
    DateTimeOffset? LastSeenAt);

/// <summary>One workspace last reported by a paired Remote Tool Host.</summary>
public sealed record HubSatelliteWorkspaceResponse(
    string WorkspaceId,
    string Path,
    bool Busy,
    string? BusyOwner,
    DateTimeOffset? LeaseExpiresAt);

public sealed class CreateSatelliteInviteRequest
{
    /// <summary>Label shown to the invited machine owner.</summary>
    public string? Name { get; set; }

    /// <summary>Address the invited machine should dial; defaults to a local LAN address.</summary>
    public string? Host { get; set; }

    public string? Purpose { get; set; }

    /// <summary>Folder proposed on the invited machine; the owner may choose another one.</summary>
    public string? Folder { get; set; }

    /// <summary>Validity in hours; defaults to the configured invitation lifetime.</summary>
    public int? TtlHours { get; set; }
}

public sealed record CreateSatelliteInviteResponse(string InviteId, string Url, DateTimeOffset ExpiresAt);
