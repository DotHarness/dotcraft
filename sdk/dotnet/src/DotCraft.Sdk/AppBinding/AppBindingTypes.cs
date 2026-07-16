using System.Text.Json;
using DotCraft.Sdk.AppServer;

namespace DotCraft.Sdk.AppBinding;

/// <summary>A connection/binding handoff mode from an app descriptor.</summary>
public sealed record AppHandoffMode(
    string Mode,
    string? Uri = null,
    string? Command = null,
    IReadOnlyList<string>? Args = null,
    string? TrustedRoot = null);

/// <summary>An installed/visible app, as returned by app/list and app/view.</summary>
public sealed record AppInfo(
    string AppId,
    string DisplayName,
    string DeveloperName,
    string Description,
    string PluginId,
    bool Installed,
    bool Enabled,
    bool CatalogVisible,
    string ConnectionState,
    IReadOnlyList<AppHandoffMode>? HandoffModes = null,
    string? Category = null,
    string? Icon = null,
    string? AccountLabel = null,
    string? ReleasePage = null,
    string? DownloadUrl = null);

/// <summary>Current connection state for one app.</summary>
public sealed record AppConnectionStatus(
    string AppId,
    string State,
    string? ConnectedAt = null,
    string? ExpiresAt = null,
    string? AccountLabel = null,
    string? Diagnostic = null);

/// <summary>Result of app/connection/start.</summary>
public sealed record AppConnectionStartResult(
    string ConnectionRequestId,
    string RequestToken,
    string ExpiresAt,
    AppHandoffMode? Handoff = null);

public sealed record AppPrincipal(
    string PrincipalId,
    string AppId,
    string UserId,
    string ExpiresAt);

public sealed record AppConnectionConnectResult(AppPrincipal Principal, string Credential);

/// <summary>An active thread binding.</summary>
public sealed record ThreadAppBinding(
    string BindingId,
    string ThreadId,
    string AppId,
    string State,
    long AuthorityRevision = 0,
    long ApprovedCapabilityRevision = 0,
    long? CandidateCapabilityRevision = null,
    string? DisplayName = null,
    IReadOnlyList<JsonElement>? ApprovedTools = null,
    IReadOnlyList<JsonElement>? PendingChanges = null,
    string? FailureReason = null);

/// <summary>A thread binding summary as returned by thread/appBindings/list.</summary>
public sealed record ThreadAppBindingSummary(
    string ThreadId,
    string BindingId,
    string AppId,
    string State,
    long AuthorityRevision = 0,
    long ApprovedCapabilityRevision = 0,
    long? CandidateCapabilityRevision = null,
    string? DisplayName = null,
    string? FailureReason = null);

/// <summary>A pending binding request, as returned by app/binding/request/get.</summary>
public sealed record AppBindingRequestInfo(
    string BindingRequestId,
    string BindingId,
    string ThreadId,
    string AppId,
    string State,
    string? ExpiresAt = null);

/// <summary>Result of thread/appBindings/enable.</summary>
public sealed record AppBindingRequestCreateResult(
    string BindingRequestId,
    string BindingId,
    string State,
    string ExpiresAt,
    AppHandoffMode? Handoff = null);

/// <summary>An app-owned surface published through AppServer.</summary>
public sealed record AppSurface(
    string AppId,
    string SurfaceId,
    string Endpoint,
    string Bearer,
    string ExpiresAt);

/// <summary>Parameters for completing an app connection (app/connection/connect).</summary>
public sealed record CompleteConnectionRequest(
    string ConnectionRequestId,
    string RequestToken,
    string? AccountLabel = null);
