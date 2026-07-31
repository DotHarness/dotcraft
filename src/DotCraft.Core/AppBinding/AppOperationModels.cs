using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.Plugins;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.AppBinding;

public sealed class AppInfoWire
{
    public string AppId { get; set; } = string.Empty;

    [JsonIgnore]
    public string ToolNamespace { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string DeveloperName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }

    public string PluginId { get; set; } = string.Empty;

    public bool Installed { get; set; }

    public bool Enabled { get; set; }

    public bool CatalogVisible { get; set; } = true;

    public bool Managed { get; set; }

    public bool RequiresExternalConnection { get; set; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReleasePage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DownloadUrl { get; set; }

    public AppNativeApplicationWire NativeApp { get; set; } = new();

    public string ConnectionState { get; set; } = AppConnectionStates.NotConnected;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccountLabel { get; set; }

    public List<AppHandoffModeDescriptor> HandoffModes { get; set; } = [];

    [JsonIgnore]
    public List<AppScopeDescriptor> Scopes { get; set; } = [];

    [JsonIgnore]
    public List<AppToolCatalogEntry> ToolCatalog { get; set; } = [];

    [JsonIgnore]
    public AppDynamicToolCatalogDescriptor DynamicToolCatalog { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadAppBindingSummaryWire? BindingSummary { get; set; }

    public List<PluginDiagnosticWire> Diagnostics { get; set; } = [];
}
public sealed class AppNativeApplicationWire
{
    public string DisplayName { get; set; } = string.Empty;

    public string Protocol { get; set; } = string.Empty;

    public string Status { get; set; } = AppNativeApplicationStates.Unknown;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstallUrl { get; set; }
}

public sealed class AppListParams
{
    public bool? IncludeCatalog { get; set; }

    public bool? IncludeDisabled { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThreadId { get; set; }

    public bool? ForceRefresh { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Surface { get; set; }
}

public sealed class AppListResult
{
    public List<AppInfoWire> Apps { get; set; } = [];
}

public sealed class AppViewParams
{
    public string AppId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThreadId { get; set; }
}

public sealed class AppViewResult
{
    public AppInfoWire App { get; set; } = new();
}

public sealed class AppConnectionStatusParams
{
    public string AppId { get; set; } = string.Empty;
}

/// <summary>
/// Params for <c>app/connection/refreshMetadata</c>: an already-connected app
/// re-publishes only its <see cref="PublicMetadata"/> (for example a new dynamic
/// loopback port), authorized by replaying its app-owned <see cref="ConnectionProof"/>.
/// </summary>
public sealed class AppConnectionMetadataRefreshParams
{
    public string AppId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? ConnectionProof { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? PublicMetadata { get; set; }
}

public sealed class AppConnectionRequestGetResult
{
    [JsonPropertyName("connectionRequestId")]
    public string ConnectionRequestId { get; set; } = string.Empty;

    [JsonPropertyName("appId")]
    public string AppId { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("developerName")]
    public string DeveloperName { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>Result for <c>app/connection/authenticate</c>.</summary>
public sealed class AppConnectionAuthenticateResult
{
    [JsonPropertyName("principal")]
    public AppPrincipalWire Principal { get; set; } = new();
}

/// <summary>Current wire result for <c>app/connection/status</c>.</summary>
public sealed class AppConnectionStatusResult
{
    [JsonPropertyName("appId")]
    public string AppId { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = AppConnectionStates.NotConnected;

    [JsonPropertyName("principal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AppPrincipalWire? Principal { get; set; }
}

/// <summary>Result for <c>app/connection/revoke</c>.</summary>
public sealed class AppConnectionRevokeResult
{
    [JsonPropertyName("state")]
    public string State { get; set; } = AppBindingStates.Revoked;
}

/// <summary>Result for <c>app/bindings/list</c>.</summary>
public sealed class AppBindingsListResult
{
    [JsonPropertyName("bindings")]
    public IReadOnlyList<AppBindingWire> Bindings { get; set; } = [];
}

public sealed class AppConnectionStatusWire
{
    public string AppId { get; set; } = string.Empty;

    public string State { get; set; } = AppConnectionStates.NotConnected;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ConnectedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccountLabel { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Diagnostic { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? PublicMetadata { get; set; }
}

public sealed class AppConnectionRevokeParams
{
    public string AppId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }
}

public sealed class AppBindingRequestCreateParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string AppId { get; set; } = string.Empty;

    public List<string> RequestedScopes { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? RequestedTools { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }

    public string Source { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BindingKind { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SocialBindingIntentWire? SocialIntent { get; set; }
}

public sealed class AppBindingRequestCreateResult
{
    public string BindingRequestId { get; set; } = string.Empty;

    public string ThreadId { get; set; } = string.Empty;

    public string AppId { get; set; } = string.Empty;

    public List<string> RequestedScopes { get; set; } = [];

    public string State { get; set; } = LegacyAppBindingStates.Pending;

    public DateTimeOffset TokenExpiresAt { get; set; }

    public AppHandoffWire Handoff { get; set; } = new();

    public AppBindingConfirmationWire Confirmation { get; set; } = new();
}

public sealed class AppBindingRequestCancelParams
{
    public string BindingRequestId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }
}

public sealed class AppBindingRequestCancelResult
{
    public string BindingRequestId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThreadId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AppId { get; set; }

    public string State { get; set; } = AppBindingStates.Cancelled;
}

public sealed class AppBindingRequestGetResult
{
    public string AppId { get; set; } = string.Empty;

    public string BindingRequestId { get; set; } = string.Empty;

    public string ThreadId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThreadTitle { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string DeveloperName { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }

    public List<string> RequestedScopes { get; set; } = [];

    public List<AppScopeDescriptor> ScopeCatalog { get; set; } = [];

    public List<string> RequestedTools { get; set; } = [];

    public List<AppToolCatalogEntry> ToolCatalog { get; set; } = [];

    public AppDynamicToolCatalogDescriptor DynamicToolCatalog { get; set; } = new();

    public DateTimeOffset ExpiresAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BindingKind { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SocialBindingIntentWire? SocialIntent { get; set; }
}

public sealed class AppBindingAcceptParams
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string BindingRequestId { get; set; } = string.Empty;

    public string RequestToken { get; set; } = string.Empty;

    public string GrantId { get; set; } = string.Empty;

    public List<string> GrantedScopes { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ExpiresAt { get; set; }

    public string ApprovalMode { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ApprovedBy { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AuditRef { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? GrantProof { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SocialChannelTargetWire? SocialTarget { get; set; }
}

public sealed class AppBindingAcceptResult
{
    public ThreadAppBindingWire Binding { get; set; } = new();
}

public sealed class AppBindingAttachToolsParams
{
    public string BindingId { get; set; } = string.Empty;

    public string ThreadId { get; set; } = string.Empty;

    public string AppId { get; set; } = string.Empty;

    public string GrantId { get; set; } = string.Empty;

    public List<JsonObject> Tools { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AppToolCatalogEntry>? ToolCatalog { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? DirectToolNames { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? DeferredToolNames { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? GrantProof { get; set; }
}

public sealed class AppBindingAttachToolsResult
{
    public ThreadAppBindingWire Binding { get; set; } = new();

    public int AcceptedToolCount { get; set; }

    public List<string> Warnings { get; set; } = [];
}
