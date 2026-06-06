using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.Plugins;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.AppBinding;

/// <summary>
/// Top-level plugin app contribution document.
/// </summary>
public sealed class AppDescriptorDocument
{
    public List<AppDescriptor> Apps { get; set; } = [];
}

/// <summary>
/// Plugin-declared app descriptor used for App Binding discovery and validation.
/// </summary>
public sealed class AppDescriptor
{
    public string AppId { get; set; } = string.Empty;

    public string ToolNamespace { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string DeveloperName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }

    public AppConnectionDescriptor Connection { get; set; } = new();

    public AppNativeApplicationDescriptor NativeApplication { get; set; } = new();

    public List<AppScopeDescriptor> Scopes { get; set; } = [];

    public List<AppToolCatalogEntry> ToolCatalog { get; set; } = [];

    public AppDynamicToolCatalogDescriptor DynamicToolCatalog { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PrivacyUrl { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TermsUrl { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReleasePage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DownloadUrl { get; set; }
}

public sealed class AppConnectionDescriptor
{
    public List<AppHandoffModeDescriptor> HandoffModes { get; set; } = [];
}

public sealed class AppHandoffModeDescriptor
{
    public string Mode { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UriTemplate { get; set; }
}

public sealed class AppNativeApplicationDescriptor
{
    public string DisplayName { get; set; } = string.Empty;

    public string Protocol { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstallUrl { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, AppNativeApplicationPlatformDescriptor>? Platforms { get; set; }
}

public sealed class AppNativeApplicationPlatformDescriptor
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Protocol { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AppUserModelId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BundleId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DesktopId { get; set; }
}

public sealed class AppScopeDescriptor
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Risk { get; set; } = AppBindingRisks.Read;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DefaultSelected { get; set; }
}

public sealed class AppToolCatalogEntry
{
    public string Name { get; set; } = string.Empty;

    public string Scope { get; set; } = string.Empty;

    public string Risk { get; set; } = AppBindingRisks.Read;

    public string DefaultExposure { get; set; } = AppBindingExposures.Direct;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
}

public sealed class AppDynamicToolCatalogDescriptor
{
    public bool Enabled { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
}

public static class AppBindingRisks
{
    public const string Read = "read";
    public const string Mutate = "mutate";
    public const string ExternalWrite = "externalWrite";

    public static bool IsKnown(string value) =>
        string.Equals(value, Read, StringComparison.Ordinal)
        || string.Equals(value, Mutate, StringComparison.Ordinal)
        || string.Equals(value, ExternalWrite, StringComparison.Ordinal);

    public static int Rank(string value) =>
        value switch
        {
            Read => 0,
            Mutate => 1,
            ExternalWrite => 2,
            _ => int.MaxValue
        };
}

public static class AppBindingExposures
{
    public const string Direct = "direct";
    public const string Deferred = "deferred";

    public static bool IsKnown(string value) =>
        string.Equals(value, Direct, StringComparison.Ordinal)
        || string.Equals(value, Deferred, StringComparison.Ordinal);
}

public static class AppBindingStates
{
    public const string Pending = "pending";
    public const string Active = "active";
    public const string Offline = "offline";
    public const string Expired = "expired";
    public const string Revoked = "revoked";
    public const string Error = "error";
    public const string Cancelled = "cancelled";
}

public static class AppContextBlockKinds
{
    public const string Role = "role";
    public const string Mission = "mission";
    public const string TeamState = "teamState";
    public const string MailboxDigest = "mailboxDigest";
    public const string ArtifactIndex = "artifactIndex";
    public const string Policy = "policy";

    public static bool IsKnown(string value) =>
        string.Equals(value, Role, StringComparison.Ordinal)
        || string.Equals(value, Mission, StringComparison.Ordinal)
        || string.Equals(value, TeamState, StringComparison.Ordinal)
        || string.Equals(value, MailboxDigest, StringComparison.Ordinal)
        || string.Equals(value, ArtifactIndex, StringComparison.Ordinal)
        || string.Equals(value, Policy, StringComparison.Ordinal);
}

public static class AppContextBlockVisibilities
{
    public const string Model = "model";
    public const string HiddenFromModel = "hiddenFromModel";

    public static bool IsKnown(string value) =>
        string.Equals(value, Model, StringComparison.Ordinal)
        || string.Equals(value, HiddenFromModel, StringComparison.Ordinal);
}

public static class AppThreadInputStartPolicies
{
    public const string QueueOnly = "queueOnly";
    public const string RunWhenIdle = "runWhenIdle";

    public static bool IsKnown(string value) =>
        string.Equals(value, QueueOnly, StringComparison.Ordinal)
        || string.Equals(value, RunWhenIdle, StringComparison.Ordinal);
}

public static class AppConnectionStates
{
    public const string NotConnected = "notConnected";
    public const string Connecting = "connecting";
    public const string Connected = "connected";
    public const string NeedsAuth = "needsAuth";
    public const string Error = "error";
}

public static class AppNativeApplicationStates
{
    public const string Installed = "installed";
    public const string Missing = "missing";
    public const string Unknown = "unknown";
}

public static class AppBindingErrorCodes
{
    public const string Offline = "AppBindingOffline";
    public const string Expired = "AppBindingExpired";
    public const string Revoked = "AppBindingRevoked";
    public const string ScopeDenied = "AppBindingScopeDenied";
    public const string ToolUnavailable = "AppBindingToolUnavailable";
    public const string ProtocolViolation = "AppBindingProtocolViolation";
}

public sealed record ManagedAppBindingCatalogMetadata(
    string OwningPluginId,
    IReadOnlySet<string> Surfaces,
    bool RequiresExternalConnection);

public sealed record AppCatalogEntry(
    AppDescriptor Descriptor,
    DiscoveredPlugin Plugin,
    IReadOnlyList<PluginDiagnostic> Diagnostics)
{
    public ManagedAppBindingCatalogMetadata? ManagedRuntime { get; init; }
}

public sealed record AppCatalogSnapshot(
    IReadOnlyList<AppCatalogEntry> Entries,
    IReadOnlyList<PluginDiagnostic> Diagnostics)
{
    public IReadOnlyList<DiscoveredPlugin> Plugins { get; init; } = [];
}

public sealed class AppInfoWire
{
    public string AppId { get; set; } = string.Empty;

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

    public List<AppScopeDescriptor> Scopes { get; set; } = [];

    public List<AppToolCatalogEntry> ToolCatalog { get; set; } = [];

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

public sealed class AppConnectionStartParams
{
    public string AppId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HandoffMode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReturnTo { get; set; }
}

public sealed class AppConnectionStartResult
{
    public string ConnectionRequestId { get; set; } = string.Empty;

    public string AppId { get; set; } = string.Empty;

    public string State { get; set; } = AppConnectionStates.Connecting;

    public DateTimeOffset ExpiresAt { get; set; }

    public AppHandoffWire Handoff { get; set; } = new();
}

public sealed class AppConnectionConnectParams
{
    public string ConnectionRequestId { get; set; } = string.Empty;

    public string RequestToken { get; set; } = string.Empty;

    public string AppId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccountLabel { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? ConnectionProof { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? PublicMetadata { get; set; }
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

public sealed class AppConnectionRequestGetParams
{
    public string AppId { get; set; } = string.Empty;

    public string ConnectionRequestId { get; set; } = string.Empty;

    public string RequestToken { get; set; } = string.Empty;
}

public sealed class AppConnectionRequestGetResult
{
    public string AppId { get; set; } = string.Empty;

    public string ConnectionRequestId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string DeveloperName { get; set; } = string.Empty;

    public string WorkspaceLabel { get; set; } = string.Empty;

    public string UserLabel { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }
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
}

public sealed class AppBindingRequestCreateResult
{
    public string BindingRequestId { get; set; } = string.Empty;

    public string ThreadId { get; set; } = string.Empty;

    public string AppId { get; set; } = string.Empty;

    public List<string> RequestedScopes { get; set; } = [];

    public string State { get; set; } = AppBindingStates.Pending;

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

public sealed class AppBindingRequestGetParams
{
    public string AppId { get; set; } = string.Empty;

    public string BindingRequestId { get; set; } = string.Empty;

    public string RequestToken { get; set; } = string.Empty;
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
}

public sealed class AppBindingAcceptParams
{
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

    public List<DynamicToolSpec> Tools { get; set; } = [];

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

public sealed class AppBindingContextUpsertParams
{
    public string BindingId { get; set; } = string.Empty;

    public string AppId { get; set; } = string.Empty;

    public string GrantId { get; set; } = string.Empty;

    public string BlockId { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public int Order { get; set; }

    public string Version { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Visibility { get; set; }
}

public sealed class AppBindingContextUpsertResult
{
    public ThreadAppContextBlockWire Block { get; set; } = new();
}

public sealed class AppBindingContextRemoveParams
{
    public string BindingId { get; set; } = string.Empty;

    public string AppId { get; set; } = string.Empty;

    public string GrantId { get; set; } = string.Empty;

    public string BlockId { get; set; } = string.Empty;
}

public sealed class AppBindingContextRemoveResult
{
    public string ThreadId { get; set; } = string.Empty;

    public string BindingId { get; set; } = string.Empty;

    public string BlockId { get; set; } = string.Empty;

    public bool Removed { get; set; }
}

public sealed class AppThreadInputEnqueueParams
{
    public string BindingId { get; set; } = string.Empty;

    public string AppId { get; set; } = string.Empty;

    public string GrantId { get; set; } = string.Empty;

    public List<SessionWireInputPart> Input { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayText { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TriggerLabel { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TriggerRefId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StartPolicy { get; set; }
}

public sealed class AppThreadInputEnqueueResult
{
    public QueuedTurnInput QueuedInput { get; set; } = new();

    public List<QueuedTurnInput> QueuedInputs { get; set; } = [];
}

public sealed class ThreadAppContextBlocksListParams
{
    public string ThreadId { get; set; } = string.Empty;

    public bool? IncludeInactive { get; set; }
}

public sealed class ThreadAppContextBlocksListResult
{
    public List<ThreadAppContextBlockWire> Blocks { get; set; } = [];
}

public sealed class ThreadAppBindingsListParams
{
    public string ThreadId { get; set; } = string.Empty;

    public bool? IncludeRevoked { get; set; }
}

public sealed class ThreadAppBindingsListResult
{
    public List<ThreadAppBindingWire> Bindings { get; set; } = [];
}

public sealed class ThreadAppBindingRevokeParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string BindingId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }
}

public sealed class ThreadAppBindingRevokeResult
{
    public string BindingId { get; set; } = string.Empty;

    public string State { get; set; } = AppBindingStates.Revoked;
}

public sealed class ThreadAppBindingRefreshParams
{
    public string ThreadId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BindingId { get; set; }
}

public sealed class ThreadAppBindingRefreshResult
{
    public List<ThreadAppBindingRefreshWire> Bindings { get; set; } = [];
}

public sealed class ThreadAppBindingWire
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BindingRequestId { get; set; }

    public string BindingId { get; set; } = string.Empty;

    public string ThreadId { get; set; } = string.Empty;

    public string AppId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolNamespace { get; set; }

    public string State { get; set; } = AppBindingStates.Pending;

    public string ConnectionState { get; set; } = AppConnectionStates.NotConnected;

    public bool Managed { get; set; }

    public bool RequiresExternalConnection { get; set; } = true;

    public List<string> GrantedScopes { get; set; } = [];

    public int AttachedToolCount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset LastChangedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ApprovalMode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AuditRef { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Diagnostic { get; set; }
}

public sealed class ThreadAppContextBlockWire
{
    public string BlockId { get; set; } = string.Empty;

    public string ThreadId { get; set; } = string.Empty;

    public string BindingId { get; set; } = string.Empty;

    public string AppId { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public int Order { get; set; }

    public string Version { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Visibility { get; set; }

    public bool Active { get; set; }
}

public sealed class ThreadAppBindingSummaryWire
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BindingRequestId { get; set; }

    public string ThreadId { get; set; } = string.Empty;

    public string BindingId { get; set; } = string.Empty;

    public string AppId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolNamespace { get; set; }

    public string State { get; set; } = AppBindingStates.Pending;

    public string ConnectionState { get; set; } = AppConnectionStates.NotConnected;

    public bool Managed { get; set; }

    public bool RequiresExternalConnection { get; set; } = true;

    public List<string> GrantedScopes { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class ThreadAppBindingRefreshWire
{
    public string BindingId { get; set; } = string.Empty;

    public string State { get; set; } = AppBindingStates.Pending;

    public int AttachedToolCount { get; set; }
}

public sealed class AppHandoffWire
{
    public string Mode { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Uri { get; set; }
}

public sealed class AppBindingConfirmationWire
{
    public bool Required { get; set; } = true;

    public string Risk { get; set; } = AppBindingRisks.Read;

    public string Message { get; set; } = string.Empty;
}
