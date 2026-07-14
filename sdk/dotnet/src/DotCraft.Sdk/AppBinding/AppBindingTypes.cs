using System.Text.Json;
using DotCraft.Sdk.AppServer;

namespace DotCraft.Sdk.AppBinding;

/// <summary>A scope declared by an app descriptor.</summary>
public sealed record AppScopeDescriptor(
    string Id,
    string DisplayName,
    string Description,
    string Risk,
    bool? DefaultSelected = null);

/// <summary>A tool catalog entry declared by an app descriptor.</summary>
public sealed record AppToolCatalogEntry(
    string Name,
    string Scope,
    string Risk,
    string DefaultExposure,
    string? Description = null);

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
    string ToolNamespace,
    string DisplayName,
    string DeveloperName,
    string Description,
    string PluginId,
    bool Installed,
    bool Enabled,
    bool CatalogVisible,
    string ConnectionState,
    IReadOnlyList<AppScopeDescriptor> Scopes,
    IReadOnlyList<AppToolCatalogEntry> ToolCatalog,
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
    string AppId,
    string State,
    string ExpiresAt,
    AppHandoffMode Handoff);

/// <summary>An active thread binding.</summary>
public sealed record ThreadAppBinding(
    string BindingId,
    string ThreadId,
    string AppId,
    string State,
    IReadOnlyList<string> GrantedScopes,
    int AttachedToolCount,
    string? BindingRequestId = null,
    string? DisplayName = null,
    string? ToolNamespace = null,
    string? ConnectionState = null,
    string? ExpiresAt = null,
    string? LastChangedAt = null,
    string? ApprovalMode = null,
    string? AuditRef = null,
    string? Diagnostic = null);

/// <summary>A thread binding summary as returned by thread/appBindings/list.</summary>
public sealed record ThreadAppBindingSummary(
    string ThreadId,
    string BindingId,
    string AppId,
    string State,
    string ConnectionState,
    IReadOnlyList<string> GrantedScopes,
    string? BindingRequestId = null,
    string? DisplayName = null,
    string? ToolNamespace = null,
    string? ExpiresAt = null);

/// <summary>A pending binding request, as returned by app/binding/request/get.</summary>
public sealed record AppBindingRequestInfo(
    string BindingRequestId,
    string ThreadId,
    string AppId,
    IReadOnlyList<AppScopeDescriptor> RequestedScopes,
    IReadOnlyList<AppToolCatalogEntry> RequestedTools,
    string Source,
    string? ThreadTitle = null,
    string? Reason = null,
    string? ExpiresAt = null);

/// <summary>Result of app/binding/request/create.</summary>
public sealed record AppBindingRequestCreateResult(
    string BindingRequestId,
    string ThreadId,
    string AppId,
    IReadOnlyList<string> RequestedScopes,
    string State,
    string TokenExpiresAt,
    AppHandoffMode Handoff,
    JsonElement? Confirmation = null);

/// <summary>Result of app/binding/accept.</summary>
public sealed record AppBindingAcceptResult(ThreadAppBinding Binding);

/// <summary>Result of app/binding/attachTools.</summary>
public sealed record AppBindingAttachToolsResult(
    ThreadAppBinding Binding,
    int AcceptedToolCount,
    IReadOnlyList<JsonElement>? RejectedTools = null,
    IReadOnlyList<string>? Warnings = null);

/// <summary>Parameters for completing an app connection (app/connection/connect).</summary>
public sealed record CompleteConnectionRequest(
    string ConnectionRequestId,
    string RequestToken,
    string AppId,
    string? AccountLabel = null,
    string? ExpiresAt = null,
    object? ConnectionProof = null);

/// <summary>Parameters for accepting a binding request (app/binding/accept).</summary>
public sealed record AcceptBindingRequest(
    string BindingRequestId,
    string RequestToken,
    string GrantId,
    IReadOnlyList<string> GrantedScopes,
    string ApprovalMode,
    string? ApprovedBy = null,
    string? ExpiresAt = null,
    object? GrantProof = null,
    string? AuditRef = null);

/// <summary>Parameters for attaching tools to an accepted binding (app/binding/attachTools).</summary>
public sealed record AttachToolsRequest(
    string BindingId,
    string ThreadId,
    string AppId,
    string GrantId,
    IReadOnlyList<AppBoundToolSpec> Tools,
    IReadOnlyList<AppToolCatalogEntry>? ToolCatalog = null,
    IReadOnlyList<string>? DirectToolNames = null,
    IReadOnlyList<string>? DeferredToolNames = null,
    object? GrantProof = null);
