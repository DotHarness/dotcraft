using System.Text.Json.Serialization;

namespace DotCraft.AppBinding;

/// <summary>App Binding protocol and persistence constants.</summary>
public static class AppBindingContract
{
    public const int Version = 2;
    public static readonly TimeSpan HandoffLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan PrincipalCredentialLifetime = TimeSpan.FromDays(30);
}

/// <summary>App Binding runtime states.</summary>
public static class AppBindingStates
{
    public const string Connecting = "connecting";
    public const string Syncing = "syncing";
    public const string Active = "active";
    public const string Offline = "offline";
    public const string NeedsConfirmation = "needsConfirmation";
    public const string Revoked = "revoked";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public sealed class AppPrincipalWire
{
    public string PrincipalId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class AppConnectionStartParams
{
    public string AppId { get; set; } = string.Empty;
}

public sealed class AppConnectionStartResult
{
    public string ConnectionRequestId { get; set; } = string.Empty;
    public string RequestToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AppHandoffWire? Handoff { get; set; }
}

public class AppConnectionRequestGetParams
{
    public string ConnectionRequestId { get; set; } = string.Empty;
    public string RequestToken { get; set; } = string.Empty;
}

public sealed class AppConnectionConnectParams : AppConnectionRequestGetParams
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccountLabel { get; set; }
}

public sealed class AppConnectionConnectResult
{
    public AppPrincipalWire Principal { get; set; } = new();
    public string Credential { get; set; } = string.Empty;
}

public sealed class AppConnectionAuthenticateParams
{
    public string AppId { get; set; } = string.Empty;
    public string Credential { get; set; } = string.Empty;
}

public sealed class AppConnectionRefreshResult
{
    public AppPrincipalWire Principal { get; set; } = new();
    public string Credential { get; set; } = string.Empty;
}

/// <summary>Publishes or renews one short-lived app-owned Desktop surface.</summary>
public sealed class AppSurfacePublishParams
{
    public string SurfaceId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Bearer { get; set; } = string.Empty;
}

/// <summary>Resolves one live app-owned Desktop surface.</summary>
public sealed class AppSurfaceResolveParams
{
    public string AppId { get; set; } = string.Empty;
    public string SurfaceId { get; set; } = string.Empty;
}

/// <summary>A short-lived app-owned Desktop surface lease.</summary>
public sealed class AppSurfaceWire
{
    public string AppId { get; set; } = string.Empty;
    public string SurfaceId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Bearer { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}
