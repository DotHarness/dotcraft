#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Protocol.Contracts.AppServer;
/// <summary>Executable wire contract for AcpFsReadTextFileParams.</summary>
[ContractModule("acp")]
public sealed class AcpFsReadTextFileParams : ExtensibleJsonObject
{
    [JsonPropertyName("limit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> Limit { get; init; }

    [JsonPropertyName("offset")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> Offset { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Path { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for AcpFsReadTextFileResult.</summary>
[ContractModule("acp")]
public sealed class AcpFsReadTextFileResult : ExtensibleJsonObject
{
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Content { get; init; }

}

/// <summary>Executable wire contract for AcpFsWriteTextFileParams.</summary>
[ContractModule("acp")]
public sealed class AcpFsWriteTextFileParams : ExtensibleJsonObject
{
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Content { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Path { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for AcpFsWriteTextFileResult.</summary>
[ContractModule("acp")]
public sealed class AcpFsWriteTextFileResult : ExtensibleJsonObject
{
    [JsonPropertyName("success")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Success { get; init; }

}

/// <summary>Executable wire contract for AcpTerminalCreateParams.</summary>
[ContractModule("acp")]
public sealed class AcpTerminalCreateParams : ExtensibleJsonObject
{
    [JsonPropertyName("command")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Command { get; init; }

    [JsonPropertyName("cwd")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Cwd { get; init; }

    [JsonPropertyName("env")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, string>?> Env { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for AcpTerminalCreateResult.</summary>
[ContractModule("acp")]
public sealed class AcpTerminalCreateResult : ExtensibleJsonObject
{
    [JsonPropertyName("terminalId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> TerminalId { get; init; }

}

/// <summary>Executable wire contract for AcpTerminalGetOutputParams.</summary>
[ContractModule("acp")]
public sealed class AcpTerminalGetOutputParams : ExtensibleJsonObject
{
    [JsonPropertyName("terminalId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TerminalId { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for AcpTerminalKillParams.</summary>
[ContractModule("acp")]
public sealed class AcpTerminalKillParams : ExtensibleJsonObject
{
    [JsonPropertyName("terminalId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TerminalId { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for AcpTerminalOutputResult.</summary>
[ContractModule("acp")]
public sealed class AcpTerminalOutputResult : ExtensibleJsonObject
{
    [JsonPropertyName("exitCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> ExitCode { get; init; }

    [JsonPropertyName("output")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Output { get; init; }

}

/// <summary>Executable wire contract for AcpTerminalReleaseParams.</summary>
[ContractModule("acp")]
public sealed class AcpTerminalReleaseParams : ExtensibleJsonObject
{
    [JsonPropertyName("terminalId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TerminalId { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for AcpTerminalWaitForExitParams.</summary>
[ContractModule("acp")]
public sealed class AcpTerminalWaitForExitParams : ExtensibleJsonObject
{
    [JsonPropertyName("terminalId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TerminalId { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("timeout")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> Timeout { get; init; }

}

/// <summary>Executable wire contract for AppBindingActivateParams.</summary>
[ContractModule("app-binding")]
public sealed class AppBindingActivateParams : ExtensibleJsonObject
{
    [JsonPropertyName("bearer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Bearer { get; init; }

    [JsonPropertyName("bearerExpiresAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> BearerExpiresAt { get; init; }

    [JsonPropertyName("bindingRequestId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BindingRequestId { get; init; }

    [JsonPropertyName("endpoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Endpoint { get; init; }

}

/// <summary>Executable wire contract for AppBindingCapabilityChangeWire.</summary>
[ContractModule("app-binding")]
public sealed class AppBindingCapabilityChangeWire : ExtensibleJsonObject
{
    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Detail { get; init; }

    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Kind { get; init; }

    [JsonPropertyName("tool")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Tool { get; init; }

}

/// <summary>Executable wire contract for AppBindingRebindParams.</summary>
[ContractModule("app-binding")]
public sealed class AppBindingRebindParams : ExtensibleJsonObject
{
    [JsonPropertyName("authorityRevision")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> AuthorityRevision { get; init; }

    [JsonPropertyName("bearer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Bearer { get; init; }

    [JsonPropertyName("bearerExpiresAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> BearerExpiresAt { get; init; }

    [JsonPropertyName("bindingId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BindingId { get; init; }

    [JsonPropertyName("endpoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Endpoint { get; init; }

}

/// <summary>Executable wire contract for AppBindingRequestGetParams.</summary>
[ContractModule("app-binding")]
public sealed class AppBindingRequestGetParams : ExtensibleJsonObject
{
    [JsonPropertyName("bindingRequestId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BindingRequestId { get; init; }

    [JsonPropertyName("requestToken")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> RequestToken { get; init; }

}

/// <summary>Executable wire contract for AppBindingRequestGetResult.</summary>
[ContractModule("app-binding")]
public sealed class AppBindingRequestGetResult : ExtensibleJsonObject
{
    [JsonPropertyName("appId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AppId { get; init; }

    [JsonPropertyName("bindingKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> BindingKind { get; init; }

    [JsonPropertyName("bindingRequestId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BindingRequestId { get; init; }

    [JsonPropertyName("developerName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DeveloperName { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DisplayName { get; init; }

    [JsonPropertyName("dynamicToolCatalog")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AppDynamicToolCatalogDescriptor> DynamicToolCatalog { get; init; }

    [JsonPropertyName("expiresAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> ExpiresAt { get; init; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Reason { get; init; }

    [JsonPropertyName("requestedScopes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> RequestedScopes { get; init; }

    [JsonPropertyName("requestedTools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> RequestedTools { get; init; }

    [JsonPropertyName("scopeCatalog")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<AppScopeDescriptor>> ScopeCatalog { get; init; }

    [JsonPropertyName("socialIntent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SocialBindingIntentWire?> SocialIntent { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Source { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("threadTitle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ThreadTitle { get; init; }

    [JsonPropertyName("toolCatalog")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<AppToolCatalogEntry>> ToolCatalog { get; init; }

}

/// <summary>Executable wire contract for AppBindingRequestWire.</summary>
[ContractModule("app-binding")]
public sealed class AppBindingRequestWire : ExtensibleJsonObject
{
    [JsonPropertyName("appId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AppId { get; init; }

    [JsonPropertyName("bindingId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BindingId { get; init; }

    [JsonPropertyName("bindingRequestId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BindingRequestId { get; init; }

    [JsonPropertyName("expiresAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> ExpiresAt { get; init; }

    [JsonPropertyName("state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> State { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for AppBindingRequestedNotification.</summary>
[ContractModule("app-binding")]
public sealed class AppBindingRequestedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("appId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AppId { get; init; }

    [JsonPropertyName("bindingId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BindingId { get; init; }

    [JsonPropertyName("bindingRequestId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BindingRequestId { get; init; }

    [JsonPropertyName("channelName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ChannelName { get; init; }

    [JsonPropertyName("code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Code { get; init; }

    [JsonPropertyName("expiresAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> ExpiresAt { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ThreadId { get; init; }

}

/// <summary>Executable wire contract for AppBindingToolCapabilityWire.</summary>
[ContractModule("app-binding")]
public sealed class AppBindingToolCapabilityWire : ExtensibleJsonObject
{
    [JsonPropertyName("annotations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement> Annotations { get; init; }

    [JsonPropertyName("inputSchema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement> InputSchema { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("namespace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Namespace { get; init; }

    [JsonPropertyName("ui")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AppBindingUiCapabilityWire?> Ui { get; init; }

    [JsonPropertyName("visibility")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Visibility { get; init; }

}

/// <summary>Executable wire contract for AppBindingUiCapabilityWire.</summary>
[ContractModule("app-binding")]
public sealed class AppBindingUiCapabilityWire : ExtensibleJsonObject
{
    [JsonPropertyName("connectDomains")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> ConnectDomains { get; init; }

    [JsonPropertyName("permissions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Permissions { get; init; }

    [JsonPropertyName("resourceDomains")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> ResourceDomains { get; init; }

    [JsonPropertyName("resourceUri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ResourceUri { get; init; }

    [JsonPropertyName("securityHash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> SecurityHash { get; init; }

}

/// <summary>Executable wire contract for AppBindingWire.</summary>
[ContractModule("app-binding")]
public sealed class AppBindingWire : ExtensibleJsonObject
{
    [JsonPropertyName("appId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AppId { get; init; }

    [JsonPropertyName("approvedCapabilityRevision")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> ApprovedCapabilityRevision { get; init; }

    [JsonPropertyName("approvedTools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<AppBindingToolCapabilityWire>> ApprovedTools { get; init; }

    [JsonPropertyName("authorityRevision")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> AuthorityRevision { get; init; }

    [JsonPropertyName("bindingId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BindingId { get; init; }

    [JsonPropertyName("candidateCapabilityRevision")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long?> CandidateCapabilityRevision { get; init; }

    [JsonPropertyName("failureReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> FailureReason { get; init; }

    [JsonPropertyName("pendingChanges")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<AppBindingCapabilityChangeWire>> PendingChanges { get; init; }

    [JsonPropertyName("socialTarget")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SocialChannelTargetWire?> SocialTarget { get; init; }

    [JsonPropertyName("state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> State { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("updatedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> UpdatedAt { get; init; }

}

/// <summary>Executable wire contract for AppBindingsListResult.</summary>
[ContractModule("app-binding")]
public sealed class AppBindingsListResult : ExtensibleJsonObject
{
    [JsonPropertyName("bindings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<AppBindingWire>> Bindings { get; init; }

}

/// <summary>Executable wire contract for AppConnectionAuthenticateParams.</summary>
[ContractModule("app-binding")]
public sealed class AppConnectionAuthenticateParams : ExtensibleJsonObject
{
    [JsonPropertyName("appId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AppId { get; init; }

    [JsonPropertyName("credential")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Credential { get; init; }

}

/// <summary>Executable wire contract for AppConnectionAuthenticateResult.</summary>
[ContractModule("app-binding")]
public sealed class AppConnectionAuthenticateResult : ExtensibleJsonObject
{
    [JsonPropertyName("principal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AppPrincipalWire> Principal { get; init; }

}

/// <summary>Executable wire contract for AppConnectionChangedNotification.</summary>
[ContractModule("app-binding")]
public sealed class AppConnectionChangedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("appId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AppId { get; init; }

    [JsonPropertyName("state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> State { get; init; }

}

/// <summary>Executable wire contract for AppConnectionConnectParams.</summary>
[ContractModule("app-binding")]
public sealed class AppConnectionConnectParams : ExtensibleJsonObject
{
    [JsonPropertyName("accountLabel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AccountLabel { get; init; }

    [JsonPropertyName("connectionRequestId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ConnectionRequestId { get; init; }

    [JsonPropertyName("requestToken")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> RequestToken { get; init; }

}

/// <summary>Executable wire contract for AppConnectionConnectResult.</summary>
[ContractModule("app-binding")]
public sealed class AppConnectionConnectResult : ExtensibleJsonObject
{
    [JsonPropertyName("credential")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Credential { get; init; }

    [JsonPropertyName("principal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AppPrincipalWire> Principal { get; init; }

}

/// <summary>Executable wire contract for AppConnectionRefreshResult.</summary>
[ContractModule("app-binding")]
public sealed class AppConnectionRefreshResult : ExtensibleJsonObject
{
    [JsonPropertyName("credential")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Credential { get; init; }

    [JsonPropertyName("principal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AppPrincipalWire> Principal { get; init; }

}

/// <summary>Executable wire contract for AppConnectionRequestGetParams.</summary>
[ContractModule("app-binding")]
public sealed class AppConnectionRequestGetParams : ExtensibleJsonObject
{
    [JsonPropertyName("connectionRequestId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ConnectionRequestId { get; init; }

    [JsonPropertyName("requestToken")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> RequestToken { get; init; }

}

/// <summary>Executable wire contract for AppConnectionRequestGetResult.</summary>
[ContractModule("app-binding")]
public sealed class AppConnectionRequestGetResult : ExtensibleJsonObject
{
    [JsonPropertyName("appId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AppId { get; init; }

    [JsonPropertyName("connectionRequestId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ConnectionRequestId { get; init; }

    [JsonPropertyName("developerName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DeveloperName { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DisplayName { get; init; }

    [JsonPropertyName("expiresAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> ExpiresAt { get; init; }

    [JsonPropertyName("userId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> UserId { get; init; }

}

/// <summary>Executable wire contract for AppConnectionRevokeParams.</summary>
[ContractModule("app-binding")]
public sealed class AppConnectionRevokeParams : ExtensibleJsonObject
{
    [JsonPropertyName("appId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AppId { get; init; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Reason { get; init; }

}

/// <summary>Executable wire contract for AppConnectionRevokeResult.</summary>
[ContractModule("app-binding")]
public sealed class AppConnectionRevokeResult : ExtensibleJsonObject
{
    [JsonPropertyName("state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> State { get; init; }

}

/// <summary>Executable wire contract for AppConnectionStartParams.</summary>
[ContractModule("app-binding")]
public sealed class AppConnectionStartParams : ExtensibleJsonObject
{
    [JsonPropertyName("appId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AppId { get; init; }

}

/// <summary>Executable wire contract for AppConnectionStartResult.</summary>
[ContractModule("app-binding")]
public sealed class AppConnectionStartResult : ExtensibleJsonObject
{
    [JsonPropertyName("connectionRequestId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ConnectionRequestId { get; init; }

    [JsonPropertyName("expiresAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> ExpiresAt { get; init; }

    [JsonPropertyName("handoff")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AppHandoffWire?> Handoff { get; init; }

    [JsonPropertyName("requestToken")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> RequestToken { get; init; }

}

/// <summary>Executable wire contract for AppConnectionStatusParams.</summary>
[ContractModule("app-binding")]
public sealed class AppConnectionStatusParams : ExtensibleJsonObject
{
    [JsonPropertyName("appId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AppId { get; init; }

}

/// <summary>Executable wire contract for AppConnectionStatusResult.</summary>
[ContractModule("app-binding")]
public sealed class AppConnectionStatusResult : ExtensibleJsonObject
{
    [JsonPropertyName("appId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AppId { get; init; }

    [JsonPropertyName("principal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AppPrincipalWire?> Principal { get; init; }

    [JsonPropertyName("state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> State { get; init; }

}

/// <summary>Executable wire contract for AppDynamicToolCatalogDescriptor.</summary>
[ContractModule("app-binding")]
public sealed class AppDynamicToolCatalogDescriptor : ExtensibleJsonObject
{
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Description { get; init; }

    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

}

/// <summary>Executable wire contract for AppHandoffModeDescriptor.</summary>
[ContractModule("app-binding")]
public sealed class AppHandoffModeDescriptor : ExtensibleJsonObject
{
    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Mode { get; init; }

    [JsonPropertyName("uriTemplate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> UriTemplate { get; init; }

}

/// <summary>Executable wire contract for AppHandoffWire.</summary>
[ContractModule("app-binding")]
public sealed class AppHandoffWire : ExtensibleJsonObject
{
    [JsonPropertyName("bindCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> BindCode { get; init; }

    [JsonPropertyName("instructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Instructions { get; init; }

    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Mode { get; init; }

    [JsonPropertyName("uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Uri { get; init; }

}

/// <summary>Executable wire contract for AppInfoWire.</summary>
[ContractModule("app-binding")]
public sealed class AppInfoWire : ExtensibleJsonObject
{
    [JsonPropertyName("accountLabel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AccountLabel { get; init; }

    [JsonPropertyName("appId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AppId { get; init; }

    [JsonPropertyName("bindingSummary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadAppBindingSummaryWire?> BindingSummary { get; init; }

    [JsonPropertyName("catalogVisible")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> CatalogVisible { get; init; }

    [JsonPropertyName("category")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Category { get; init; }

    [JsonPropertyName("connectionState")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ConnectionState { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Description { get; init; }

    [JsonPropertyName("developerName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DeveloperName { get; init; }

    [JsonPropertyName("diagnostics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<PluginDiagnosticWire>> Diagnostics { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DisplayName { get; init; }

    [JsonPropertyName("downloadUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DownloadUrl { get; init; }

    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("handoffModes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<AppHandoffModeDescriptor>> HandoffModes { get; init; }

    [JsonPropertyName("icon")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Icon { get; init; }

    [JsonPropertyName("installed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Installed { get; init; }

    [JsonPropertyName("managed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Managed { get; init; }

    [JsonPropertyName("nativeApp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AppNativeApplicationWire> NativeApp { get; init; }

    [JsonPropertyName("pluginId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> PluginId { get; init; }

    [JsonPropertyName("releasePage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ReleasePage { get; init; }

    [JsonPropertyName("requiresExternalConnection")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> RequiresExternalConnection { get; init; }

}

/// <summary>Executable wire contract for AppListParams.</summary>
[ContractModule("app-binding")]
public sealed class AppListParams : ExtensibleJsonObject
{
    [JsonPropertyName("forceRefresh")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> ForceRefresh { get; init; }

    [JsonPropertyName("includeCatalog")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> IncludeCatalog { get; init; }

    [JsonPropertyName("includeDisabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> IncludeDisabled { get; init; }

    [JsonPropertyName("surface")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Surface { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ThreadId { get; init; }

}

/// <summary>Executable wire contract for AppListResult.</summary>
[ContractModule("app-binding")]
public sealed class AppListResult : ExtensibleJsonObject
{
    [JsonPropertyName("apps")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<AppInfoWire>> Apps { get; init; }

}

/// <summary>Executable wire contract for AppListUpdatedNotification.</summary>
[ContractModule("app-binding")]
public sealed class AppListUpdatedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("appIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> AppIds { get; init; }

    [JsonPropertyName("pluginId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> PluginId { get; init; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Reason { get; init; }

}

/// <summary>Executable wire contract for AppNativeApplicationWire.</summary>
[ContractModule("app-binding")]
public sealed class AppNativeApplicationWire : ExtensibleJsonObject
{
    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DisplayName { get; init; }

    [JsonPropertyName("installUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> InstallUrl { get; init; }

    [JsonPropertyName("protocol")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Protocol { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Status { get; init; }

}

/// <summary>Executable wire contract for AppPrincipalWire.</summary>
[ContractModule("app-binding")]
public sealed class AppPrincipalWire : ExtensibleJsonObject
{
    [JsonPropertyName("appId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AppId { get; init; }

    [JsonPropertyName("expiresAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> ExpiresAt { get; init; }

    [JsonPropertyName("principalId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> PrincipalId { get; init; }

    [JsonPropertyName("userId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> UserId { get; init; }

}

/// <summary>Executable wire contract for AppScopeDescriptor.</summary>
[ContractModule("app-binding")]
public sealed class AppScopeDescriptor : ExtensibleJsonObject
{
    [JsonPropertyName("defaultSelected")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> DefaultSelected { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Description { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DisplayName { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("risk")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Risk { get; init; }

}

/// <summary>Executable wire contract for AppSocialBindingResolveParams.</summary>
[ContractModule("app-binding")]
public sealed class AppSocialBindingResolveParams : ExtensibleJsonObject
{
    [JsonPropertyName("accountId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AccountId { get; init; }

    [JsonPropertyName("channelName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ChannelName { get; init; }

    [JsonPropertyName("conversationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ConversationId { get; init; }

    [JsonPropertyName("conversationKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ConversationKind { get; init; }

}

/// <summary>Executable wire contract for AppSocialBindingResolveResult.</summary>
[ContractModule("app-binding")]
public sealed class AppSocialBindingResolveResult : ExtensibleJsonObject
{
    [JsonPropertyName("binding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AppBindingWire?> Binding { get; init; }

}

/// <summary>Executable wire contract for AppSurfacePublishParams.</summary>
[ContractModule("app-binding")]
public sealed class AppSurfacePublishParams : ExtensibleJsonObject
{
    [JsonPropertyName("bearer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Bearer { get; init; }

    [JsonPropertyName("endpoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Endpoint { get; init; }

    [JsonPropertyName("surfaceId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> SurfaceId { get; init; }

}

/// <summary>Executable wire contract for AppSurfaceResolveParams.</summary>
[ContractModule("app-binding")]
public sealed class AppSurfaceResolveParams : ExtensibleJsonObject
{
    [JsonPropertyName("appId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AppId { get; init; }

    [JsonPropertyName("surfaceId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> SurfaceId { get; init; }

}

/// <summary>Executable wire contract for AppSurfaceWire.</summary>
[ContractModule("app-binding")]
public sealed class AppSurfaceWire : ExtensibleJsonObject
{
    [JsonPropertyName("appId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AppId { get; init; }

    [JsonPropertyName("bearer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Bearer { get; init; }

    [JsonPropertyName("endpoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Endpoint { get; init; }

    [JsonPropertyName("expiresAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> ExpiresAt { get; init; }

    [JsonPropertyName("surfaceId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> SurfaceId { get; init; }

}

/// <summary>Executable wire contract for AppThreadInputEnqueueParams.</summary>
[ContractModule("app-binding")]
public sealed class AppThreadInputEnqueueParams : ExtensibleJsonObject
{
    [JsonPropertyName("bindingId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BindingId { get; init; }

    [JsonPropertyName("displayText")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DisplayText { get; init; }

    [JsonPropertyName("input")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<InputPart>> Input { get; init; }

    [JsonPropertyName("sender")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SenderContext?> Sender { get; init; }

    [JsonPropertyName("startPolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> StartPolicy { get; init; }

    [JsonPropertyName("triggerLabel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> TriggerLabel { get; init; }

    [JsonPropertyName("triggerRefId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> TriggerRefId { get; init; }

}

/// <summary>Executable wire contract for AppThreadInputEnqueueResult.</summary>
[ContractModule("app-binding")]
public sealed class AppThreadInputEnqueueResult : ExtensibleJsonObject
{
    [JsonPropertyName("queuedInput")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<QueuedTurnInput> QueuedInput { get; init; }

    [JsonPropertyName("queuedInputs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<QueuedTurnInput>> QueuedInputs { get; init; }

}

/// <summary>Executable wire contract for AppToolCatalogEntry.</summary>
[ContractModule("app-binding")]
public sealed class AppToolCatalogEntry : ExtensibleJsonObject
{
    [JsonPropertyName("defaultExposure")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DefaultExposure { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Description { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("risk")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Risk { get; init; }

    [JsonPropertyName("scope")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Scope { get; init; }

}

/// <summary>Executable wire contract for AppViewParams.</summary>
[ContractModule("app-binding")]
public sealed class AppViewParams : ExtensibleJsonObject
{
    [JsonPropertyName("appId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AppId { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ThreadId { get; init; }

}

/// <summary>Executable wire contract for AppViewResult.</summary>
[ContractModule("app-binding")]
public sealed class AppViewResult : ExtensibleJsonObject
{
    [JsonPropertyName("app")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AppInfoWire> App { get; init; }

}

/// <summary>Executable wire contract for ArtifactRefRecord.</summary>
[ContractModule("teams")]
public sealed class ArtifactRefRecord : ExtensibleJsonObject
{
    [JsonPropertyName("alias")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Alias { get; init; }

    [JsonPropertyName("artifactId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ArtifactId { get; init; }

    [JsonPropertyName("createdAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> CreatedAt { get; init; }

    [JsonPropertyName("format")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Format { get; init; }

    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Kind { get; init; }

    [JsonPropertyName("memberId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> MemberId { get; init; }

    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> Metadata { get; init; }

    [JsonPropertyName("sourceMessageId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> SourceMessageId { get; init; }

    [JsonPropertyName("sourceTaskId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> SourceTaskId { get; init; }

    [JsonPropertyName("summary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Summary { get; init; }

    [JsonPropertyName("taskId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TaskId { get; init; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Title { get; init; }

    [JsonPropertyName("uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Uri { get; init; }

}

/// <summary>Executable wire contract for AutomationScheduleWire.</summary>
[ContractModule("automations")]
public sealed class AutomationScheduleWire : ExtensibleJsonObject
{
    [JsonPropertyName("atMs")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long?> AtMs { get; init; }

    [JsonPropertyName("dailyHour")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> DailyHour { get; init; }

    [JsonPropertyName("dailyMinute")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> DailyMinute { get; init; }

    [JsonPropertyName("everyMs")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long?> EveryMs { get; init; }

    [JsonPropertyName("expr")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Expr { get; init; }

    [JsonPropertyName("initialDelayMs")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long?> InitialDelayMs { get; init; }

    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Kind { get; init; }

    [JsonPropertyName("tz")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Tz { get; init; }

}

/// <summary>Executable wire contract for AutomationTaskCreateParams.</summary>
[ContractModule("automations")]
public sealed class AutomationTaskCreateParams : ExtensibleJsonObject
{
    [JsonPropertyName("agentProfileId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AgentProfileId { get; init; }

    [JsonPropertyName("approvalPolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ApprovalPolicy { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Description { get; init; }

    [JsonPropertyName("schedule")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AutomationScheduleWire?> Schedule { get; init; }

    [JsonPropertyName("templateId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> TemplateId { get; init; }

    [JsonPropertyName("threadBinding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AutomationThreadBindingWire?> ThreadBinding { get; init; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Title { get; init; }

    [JsonPropertyName("workflowTemplate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> WorkflowTemplate { get; init; }

    [JsonPropertyName("workspaceMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> WorkspaceMode { get; init; }

    [JsonPropertyName("workspacePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> WorkspacePath { get; init; }

}

/// <summary>Executable wire contract for AutomationTaskCreateResult.</summary>
[ContractModule("automations")]
public sealed class AutomationTaskCreateResult : ExtensibleJsonObject
{
    [JsonPropertyName("taskDirectory")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TaskDirectory { get; init; }

    [JsonPropertyName("taskId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TaskId { get; init; }

}

/// <summary>Executable wire contract for AutomationTaskDeleteParams.</summary>
[ContractModule("automations")]
public sealed class AutomationTaskDeleteParams : ExtensibleJsonObject
{
    [JsonPropertyName("taskId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TaskId { get; init; }

    [JsonPropertyName("workspacePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> WorkspacePath { get; init; }

}

/// <summary>Executable wire contract for AutomationTaskDeleteResult.</summary>
[ContractModule("automations")]
public sealed class AutomationTaskDeleteResult : ExtensibleJsonObject
{
    [JsonPropertyName("ok")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Ok { get; init; }

}

/// <summary>Executable wire contract for AutomationTaskDiscardWorktreeParams.</summary>
[ContractModule("automations")]
public sealed class AutomationTaskDiscardWorktreeParams : ExtensibleJsonObject
{
    [JsonPropertyName("taskId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TaskId { get; init; }

    [JsonPropertyName("workspacePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> WorkspacePath { get; init; }

}

/// <summary>Executable wire contract for AutomationTaskDiscardWorktreeResult.</summary>
[ContractModule("automations")]
public sealed class AutomationTaskDiscardWorktreeResult : ExtensibleJsonObject
{
    [JsonPropertyName("task")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AutomationTaskWire> Task { get; init; }

}

/// <summary>Executable wire contract for AutomationTaskListParams.</summary>
[ContractModule("automations")]
public sealed class AutomationTaskListParams : ExtensibleJsonObject
{
    [JsonPropertyName("workspacePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> WorkspacePath { get; init; }

}

/// <summary>Executable wire contract for AutomationTaskListResult.</summary>
[ContractModule("automations")]
public sealed class AutomationTaskListResult : ExtensibleJsonObject
{
    [JsonPropertyName("tasks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<AutomationTaskWire>> Tasks { get; init; }

}

/// <summary>Executable wire contract for AutomationTaskReadParams.</summary>
[ContractModule("automations")]
public sealed class AutomationTaskReadParams : ExtensibleJsonObject
{
    [JsonPropertyName("taskId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TaskId { get; init; }

    [JsonPropertyName("workspacePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> WorkspacePath { get; init; }

}

/// <summary>Executable wire contract for AutomationTaskRunParams.</summary>
[ContractModule("automations")]
public sealed class AutomationTaskRunParams : ExtensibleJsonObject
{
    [JsonPropertyName("taskId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TaskId { get; init; }

    [JsonPropertyName("workspacePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> WorkspacePath { get; init; }

}

/// <summary>Executable wire contract for AutomationTaskRunResult.</summary>
[ContractModule("automations")]
public sealed class AutomationTaskRunResult : ExtensibleJsonObject
{
    [JsonPropertyName("task")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AutomationTaskWire> Task { get; init; }

}

/// <summary>Executable wire contract for AutomationTaskUpdateBindingParams.</summary>
[ContractModule("automations")]
public sealed class AutomationTaskUpdateBindingParams : ExtensibleJsonObject
{
    [JsonPropertyName("taskId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TaskId { get; init; }

    [JsonPropertyName("threadBinding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AutomationThreadBindingWire?> ThreadBinding { get; init; }

    [JsonPropertyName("workspacePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> WorkspacePath { get; init; }

}

/// <summary>Executable wire contract for AutomationTaskUpdateBindingResult.</summary>
[ContractModule("automations")]
public sealed class AutomationTaskUpdateBindingResult : ExtensibleJsonObject
{
    [JsonPropertyName("task")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AutomationTaskWire> Task { get; init; }

}

/// <summary>Executable wire contract for AutomationTaskUpdatedNotification.</summary>
[ContractModule("automations")]
public sealed class AutomationTaskUpdatedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("task")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AutomationTaskWire> Task { get; init; }

    [JsonPropertyName("workspacePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> WorkspacePath { get; init; }

}

/// <summary>Executable wire contract for AutomationTaskWire.</summary>
[ContractModule("automations")]
public sealed class AutomationTaskWire : ExtensibleJsonObject
{
    [JsonPropertyName("agentProfileId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AgentProfileId { get; init; }

    [JsonPropertyName("agentSummary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AgentSummary { get; init; }

    [JsonPropertyName("approvalPolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ApprovalPolicy { get; init; }

    [JsonPropertyName("createdAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> CreatedAt { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Description { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("nextRunAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> NextRunAt { get; init; }

    [JsonPropertyName("schedule")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AutomationScheduleWire?> Schedule { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Status { get; init; }

    [JsonPropertyName("threadBinding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AutomationThreadBindingWire?> ThreadBinding { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ThreadId { get; init; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Title { get; init; }

    [JsonPropertyName("updatedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> UpdatedAt { get; init; }

    [JsonPropertyName("workspaceMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> WorkspaceMode { get; init; }

    [JsonPropertyName("worktree")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AutomationTaskWorktreeWire?> Worktree { get; init; }

}

/// <summary>Executable wire contract for AutomationTaskWorktreeWire.</summary>
[ContractModule("automations")]
public sealed class AutomationTaskWorktreeWire : ExtensibleJsonObject
{
    [JsonPropertyName("branchName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BranchName { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Path { get; init; }

}

/// <summary>Executable wire contract for AutomationTemplateDeleteParams.</summary>
[ContractModule("automations")]
public sealed class AutomationTemplateDeleteParams : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

}

/// <summary>Executable wire contract for AutomationTemplateDeleteResult.</summary>
[ContractModule("automations")]
public sealed class AutomationTemplateDeleteResult : ExtensibleJsonObject
{
    [JsonPropertyName("ok")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Ok { get; init; }

}

/// <summary>Executable wire contract for AutomationTemplateListParams.</summary>
[ContractModule("automations")]
public sealed class AutomationTemplateListParams : ExtensibleJsonObject
{
    [JsonPropertyName("locale")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Locale { get; init; }

}

/// <summary>Executable wire contract for AutomationTemplateListResult.</summary>
[ContractModule("automations")]
public sealed class AutomationTemplateListResult : ExtensibleJsonObject
{
    [JsonPropertyName("templates")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<AutomationTemplateWire>> Templates { get; init; }

}

/// <summary>Executable wire contract for AutomationTemplateSaveParams.</summary>
[ContractModule("automations")]
public sealed class AutomationTemplateSaveParams : ExtensibleJsonObject
{
    [JsonPropertyName("category")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Category { get; init; }

    [JsonPropertyName("defaultAgentProfileId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DefaultAgentProfileId { get; init; }

    [JsonPropertyName("defaultApprovalPolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DefaultApprovalPolicy { get; init; }

    [JsonPropertyName("defaultDescription")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DefaultDescription { get; init; }

    [JsonPropertyName("defaultSchedule")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AutomationScheduleWire?> DefaultSchedule { get; init; }

    [JsonPropertyName("defaultTitle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DefaultTitle { get; init; }

    [JsonPropertyName("defaultWorkspaceMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DefaultWorkspaceMode { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Description { get; init; }

    [JsonPropertyName("icon")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Icon { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Id { get; init; }

    [JsonPropertyName("needsThreadBinding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> NeedsThreadBinding { get; init; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Title { get; init; }

    [JsonPropertyName("workflowMarkdown")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> WorkflowMarkdown { get; init; }

}

/// <summary>Executable wire contract for AutomationTemplateSaveResult.</summary>
[ContractModule("automations")]
public sealed class AutomationTemplateSaveResult : ExtensibleJsonObject
{
    [JsonPropertyName("template")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AutomationTemplateWire> Template { get; init; }

}

/// <summary>Executable wire contract for AutomationTemplateWire.</summary>
[ContractModule("automations")]
public sealed class AutomationTemplateWire : ExtensibleJsonObject
{
    [JsonPropertyName("category")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Category { get; init; }

    [JsonPropertyName("createdAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> CreatedAt { get; init; }

    [JsonPropertyName("defaultAgentProfileId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DefaultAgentProfileId { get; init; }

    [JsonPropertyName("defaultApprovalPolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DefaultApprovalPolicy { get; init; }

    [JsonPropertyName("defaultDescription")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DefaultDescription { get; init; }

    [JsonPropertyName("defaultSchedule")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AutomationScheduleWire?> DefaultSchedule { get; init; }

    [JsonPropertyName("defaultTitle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DefaultTitle { get; init; }

    [JsonPropertyName("defaultWorkspaceMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DefaultWorkspaceMode { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Description { get; init; }

    [JsonPropertyName("icon")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Icon { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("isUser")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IsUser { get; init; }

    [JsonPropertyName("needsThreadBinding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> NeedsThreadBinding { get; init; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Title { get; init; }

    [JsonPropertyName("updatedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> UpdatedAt { get; init; }

    [JsonPropertyName("workflowMarkdown")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> WorkflowMarkdown { get; init; }

}

/// <summary>Executable wire contract for AutomationThreadBindingWire.</summary>
[ContractModule("automations")]
public sealed class AutomationThreadBindingWire : ExtensibleJsonObject
{
    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Mode { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ChannelMediaSource.</summary>
[ContractModule("external-channel")]
public sealed class ChannelMediaSource : ExtensibleJsonObject
{
    [JsonPropertyName("artifactId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ArtifactId { get; init; }

    [JsonPropertyName("dataBase64")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DataBase64 { get; init; }

    [JsonPropertyName("hostPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> HostPath { get; init; }

    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Kind { get; init; }

    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Url { get; init; }

}

/// <summary>Executable wire contract for ChannelOutboundMessage.</summary>
[ContractModule("external-channel")]
public sealed class ChannelOutboundMessage : ExtensibleJsonObject
{
    [JsonPropertyName("caption")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Caption { get; init; }

    [JsonPropertyName("fileName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> FileName { get; init; }

    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Kind { get; init; }

    [JsonPropertyName("mediaType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> MediaType { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ChannelMediaSource?> Source { get; init; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Text { get; init; }

}

/// <summary>Executable wire contract for ExtChannelSendParams.</summary>
[ContractModule("external-channel")]
public sealed class ExtChannelSendParams : ExtensibleJsonObject
{
    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ChannelOutboundMessage> Message { get; init; }

    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> Metadata { get; init; }

    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Target { get; init; }

}

/// <summary>Executable wire contract for ExtChannelSendResult.</summary>
[ContractModule("external-channel")]
public sealed class ExtChannelSendResult : ExtensibleJsonObject
{
    [JsonPropertyName("delivered")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Delivered { get; init; }

    [JsonPropertyName("errorCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ErrorCode { get; init; }

    [JsonPropertyName("errorMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ErrorMessage { get; init; }

    [JsonPropertyName("remoteMediaId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> RemoteMediaId { get; init; }

    [JsonPropertyName("remoteMessageId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> RemoteMessageId { get; init; }

}

/// <summary>Executable wire contract for ExtChannelToolCallContext.</summary>
[ContractModule("external-channel")]
public sealed class ExtChannelToolCallContext : ExtensibleJsonObject
{
    [JsonPropertyName("channelContext")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ChannelContext { get; init; }

    [JsonPropertyName("channelName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ChannelName { get; init; }

    [JsonPropertyName("groupId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> GroupId { get; init; }

    [JsonPropertyName("senderId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> SenderId { get; init; }

}

/// <summary>Executable wire contract for ExtChannelToolCallParams.</summary>
[ContractModule("external-channel")]
public sealed class ExtChannelToolCallParams : ExtensibleJsonObject
{
    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement> Arguments { get; init; }

    [JsonPropertyName("callId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> CallId { get; init; }

    [JsonPropertyName("context")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ExtChannelToolCallContext> Context { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("tool")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Tool { get; init; }

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TurnId { get; init; }

}

/// <summary>Executable wire contract for ExtChannelToolCallResult.</summary>
[ContractModule("external-channel")]
public sealed class ExtChannelToolCallResult : ExtensibleJsonObject
{
    [JsonPropertyName("contentItems")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<ExtChannelToolContentItem>?> ContentItems { get; init; }

    [JsonPropertyName("errorCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ErrorCode { get; init; }

    [JsonPropertyName("errorMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ErrorMessage { get; init; }

    [JsonPropertyName("structuredResult")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> StructuredResult { get; init; }

    [JsonPropertyName("success")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Success { get; init; }

}

/// <summary>Executable wire contract for ExtChannelToolContentItem.</summary>
[ContractModule("external-channel")]
public sealed class ExtChannelToolContentItem : ExtensibleJsonObject
{
    [JsonPropertyName("dataBase64")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DataBase64 { get; init; }

    [JsonPropertyName("mediaType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> MediaType { get; init; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Text { get; init; }

    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Type { get; init; }

    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Url { get; init; }

}

/// <summary>Executable wire contract for ExternalChannelConfigWire.</summary>
[ContractModule("external-channel")]
public sealed class ExternalChannelConfigWire : ExtensibleJsonObject
{
    [JsonPropertyName("args")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Args { get; init; }

    [JsonPropertyName("builtinModule")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> BuiltinModule { get; init; }

    [JsonPropertyName("command")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Command { get; init; }

    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("env")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, string>?> Env { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("transport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Transport { get; init; }

    [JsonPropertyName("workingDirectory")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> WorkingDirectory { get; init; }

}

/// <summary>Executable wire contract for ExternalChannelGetParams.</summary>
[ContractModule("external-channel")]
public sealed class ExternalChannelGetParams : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

/// <summary>Executable wire contract for ExternalChannelGetResult.</summary>
[ContractModule("external-channel")]
public sealed class ExternalChannelGetResult : ExtensibleJsonObject
{
    [JsonPropertyName("channel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ExternalChannelConfigWire> Channel { get; init; }

}

/// <summary>Executable wire contract for ExternalChannelListResult.</summary>
[ContractModule("external-channel")]
public sealed class ExternalChannelListResult : ExtensibleJsonObject
{
    [JsonPropertyName("channels")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<ExternalChannelConfigWire>> Channels { get; init; }

}

/// <summary>Executable wire contract for ExternalChannelLogsParams.</summary>
[ContractModule("external-channel")]
public sealed class ExternalChannelLogsParams : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("tail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> Tail { get; init; }

}

/// <summary>Executable wire contract for ExternalChannelLogsResult.</summary>
[ContractModule("external-channel")]
public sealed class ExternalChannelLogsResult : ExtensibleJsonObject
{
    [JsonPropertyName("lines")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Lines { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

/// <summary>Executable wire contract for ExternalChannelRemoveParams.</summary>
[ContractModule("external-channel")]
public sealed class ExternalChannelRemoveParams : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

/// <summary>Executable wire contract for ExternalChannelRemoveResult.</summary>
[ContractModule("external-channel")]
public sealed class ExternalChannelRemoveResult : ExtensibleJsonObject
{
    [JsonPropertyName("removed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Removed { get; init; }

}

/// <summary>Executable wire contract for ExternalChannelUpsertParams.</summary>
[ContractModule("external-channel")]
public sealed class ExternalChannelUpsertParams : ExtensibleJsonObject
{
    [JsonPropertyName("channel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ExternalChannelConfigWire> Channel { get; init; }

}

/// <summary>Executable wire contract for ExternalChannelUpsertResult.</summary>
[ContractModule("external-channel")]
public sealed class ExternalChannelUpsertResult : ExtensibleJsonObject
{
    [JsonPropertyName("channel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ExternalChannelConfigWire> Channel { get; init; }

}

/// <summary>Executable wire contract for MailboxDigestRecord.</summary>
[ContractModule("teams")]
public sealed class MailboxDigestRecord : ExtensibleJsonObject
{
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Content { get; init; }

    [JsonPropertyName("digestId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DigestId { get; init; }

    [JsonPropertyName("memberId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> MemberId { get; init; }

    [JsonPropertyName("updatedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> UpdatedAt { get; init; }

}

/// <summary>Executable wire contract for MissionRecord.</summary>
[ContractModule("teams")]
public sealed class MissionRecord : ExtensibleJsonObject
{
    [JsonPropertyName("archivedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> ArchivedAt { get; init; }

    [JsonPropertyName("completedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> CompletedAt { get; init; }

    [JsonPropertyName("completionNotifiedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> CompletionNotifiedAt { get; init; }

    [JsonPropertyName("completionQueuedInputId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> CompletionQueuedInputId { get; init; }

    [JsonPropertyName("completionSummary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> CompletionSummary { get; init; }

    [JsonPropertyName("createdAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> CreatedAt { get; init; }

    [JsonPropertyName("finalResponse")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> FinalResponse { get; init; }

    [JsonPropertyName("leaderContinuationQueuedInputId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> LeaderContinuationQueuedInputId { get; init; }

    [JsonPropertyName("leaderThreadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> LeaderThreadId { get; init; }

    [JsonPropertyName("missionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> MissionId { get; init; }

    [JsonPropertyName("originThreadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> OriginThreadId { get; init; }

    [JsonPropertyName("plan")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Plan { get; init; }

    [JsonPropertyName("prompt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Prompt { get; init; }

    [JsonPropertyName("scratchpadPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ScratchpadPath { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Status { get; init; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Title { get; init; }

    [JsonPropertyName("updatedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> UpdatedAt { get; init; }

}

/// <summary>Executable wire contract for MissionThreadView.</summary>
[ContractModule("teams")]
public sealed class MissionThreadView : ExtensibleJsonObject
{
    [JsonPropertyName("archivedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> ArchivedAt { get; init; }

    [JsonPropertyName("createdAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> CreatedAt { get; init; }

    [JsonPropertyName("currentTaskId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> CurrentTaskId { get; init; }

    [JsonPropertyName("memberId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> MemberId { get; init; }

    [JsonPropertyName("missionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> MissionId { get; init; }

    [JsonPropertyName("queuedInputCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> QueuedInputCount { get; init; }

    [JsonPropertyName("queuedInputId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> QueuedInputId { get; init; }

    [JsonPropertyName("running")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Running { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Status { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("updatedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> UpdatedAt { get; init; }

    [JsonPropertyName("waitingOnApproval")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> WaitingOnApproval { get; init; }

    [JsonPropertyName("waitingOnInput")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> WaitingOnInput { get; init; }

}

/// <summary>Executable wire contract for NodeReplBrowserSessionParams.</summary>
[ContractModule("node-repl")]
public sealed class NodeReplBrowserSessionParams : ExtensibleJsonObject
{
    [JsonPropertyName("evaluationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> EvaluationId { get; init; }

    [JsonPropertyName("protocolVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> ProtocolVersion { get; init; }

    [JsonPropertyName("sessionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> SessionId { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> TurnId { get; init; }

}

/// <summary>Executable wire contract for NodeReplCancelParams.</summary>
[ContractModule("node-repl")]
public sealed class NodeReplCancelParams : ExtensibleJsonObject
{
    [JsonPropertyName("evaluationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> EvaluationId { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for NodeReplEvaluateParams.</summary>
[ContractModule("node-repl")]
public sealed class NodeReplEvaluateParams : ExtensibleJsonObject
{
    [JsonPropertyName("browserSession")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<NodeReplBrowserSessionParams> BrowserSession { get; init; }

    [JsonPropertyName("code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Code { get; init; }

    [JsonPropertyName("evaluationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> EvaluationId { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("timeoutMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> TimeoutMs { get; init; }

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> TurnId { get; init; }

}

/// <summary>Executable wire contract for NodeReplEvaluateResult.</summary>
[ContractModule("node-repl")]
public sealed class NodeReplEvaluateResult : ExtensibleJsonObject
{
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Error { get; init; }

    [JsonPropertyName("images")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<NodeReplImageResult>> Images { get; init; }

    [JsonPropertyName("logs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Logs { get; init; }

    [JsonPropertyName("resultText")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ResultText { get; init; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Text { get; init; }

}

/// <summary>Executable wire contract for NodeReplImageResult.</summary>
[ContractModule("node-repl")]
public sealed class NodeReplImageResult : ExtensibleJsonObject
{
    [JsonPropertyName("dataBase64")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DataBase64 { get; init; }

    [JsonPropertyName("mediaType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> MediaType { get; init; }

}

/// <summary>Executable wire contract for SocialBindingAcceptParams.</summary>
[ContractModule("app-binding")]
public sealed class SocialBindingAcceptParams : ExtensibleJsonObject
{
    [JsonPropertyName("code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Code { get; init; }

    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SocialChannelTargetWire> Target { get; init; }

}

/// <summary>Executable wire contract for SocialBindingIntentWire.</summary>
[ContractModule("app-binding")]
public sealed class SocialBindingIntentWire : ExtensibleJsonObject
{
    [JsonPropertyName("channelName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ChannelName { get; init; }

    [JsonPropertyName("displayHint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DisplayHint { get; init; }

    [JsonPropertyName("targetSelection")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TargetSelection { get; init; }

}

/// <summary>Executable wire contract for SocialBindingRebindParams.</summary>
[ContractModule("app-binding")]
public sealed class SocialBindingRebindParams : ExtensibleJsonObject
{
    [JsonPropertyName("authorityRevision")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> AuthorityRevision { get; init; }

    [JsonPropertyName("bindingId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BindingId { get; init; }

    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SocialChannelTargetWire> Target { get; init; }

}

/// <summary>Executable wire contract for SocialBindingRequestGetParams.</summary>
[ContractModule("app-binding")]
public sealed class SocialBindingRequestGetParams : ExtensibleJsonObject
{
    [JsonPropertyName("code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Code { get; init; }

}

/// <summary>Executable wire contract for SocialChannelBoundByWire.</summary>
[ContractModule("app-binding")]
public sealed class SocialChannelBoundByWire : ExtensibleJsonObject
{
    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DisplayName { get; init; }

    [JsonPropertyName("platformUserId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> PlatformUserId { get; init; }

}

/// <summary>Executable wire contract for SocialChannelTargetWire.</summary>
[ContractModule("app-binding")]
public sealed class SocialChannelTargetWire : ExtensibleJsonObject
{
    [JsonPropertyName("accountId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AccountId { get; init; }

    [JsonPropertyName("boundBy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SocialChannelBoundByWire?> BoundBy { get; init; }

    [JsonPropertyName("channelName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ChannelName { get; init; }

    [JsonPropertyName("conversationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ConversationId { get; init; }

    [JsonPropertyName("conversationKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ConversationKind { get; init; }

    [JsonPropertyName("deliveryTarget")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DeliveryTarget { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DisplayName { get; init; }

}

/// <summary>Executable wire contract for TeamMemberAgentProfileDiagnostic.</summary>
[ContractModule("teams")]
public sealed class TeamMemberAgentProfileDiagnostic : ExtensibleJsonObject
{
    [JsonPropertyName("code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Code { get; init; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Message { get; init; }

    [JsonPropertyName("severity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Severity { get; init; }

}

/// <summary>Executable wire contract for TeamMemberAgentProfileView.</summary>
[ContractModule("teams")]
public sealed class TeamMemberAgentProfileView : ExtensibleJsonObject
{
    [JsonPropertyName("activeId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ActiveId { get; init; }

    [JsonPropertyName("diagnostics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<TeamMemberAgentProfileDiagnostic>> Diagnostics { get; init; }

    [JsonPropertyName("fallbackUsed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> FallbackUsed { get; init; }

    [JsonPropertyName("fingerprint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Fingerprint { get; init; }

    [JsonPropertyName("missing")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Missing { get; init; }

    [JsonPropertyName("requestedId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> RequestedId { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Source { get; init; }

    [JsonPropertyName("valid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Valid { get; init; }

}

/// <summary>Executable wire contract for TeamMemberView.</summary>
[ContractModule("teams")]
public sealed class TeamMemberView : ExtensibleJsonObject
{
    [JsonPropertyName("agentProfile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<TeamMemberAgentProfileView?> AgentProfile { get; init; }

    [JsonPropertyName("agentProfileId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AgentProfileId { get; init; }

    [JsonPropertyName("avatarAccent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AvatarAccent { get; init; }

    [JsonPropertyName("currentTaskId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> CurrentTaskId { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Description { get; init; }

    [JsonPropertyName("deskX")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<double> DeskX { get; init; }

    [JsonPropertyName("deskY")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<double> DeskY { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DisplayName { get; init; }

    [JsonPropertyName("memberId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> MemberId { get; init; }

    [JsonPropertyName("queuedInputCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> QueuedInputCount { get; init; }

    [JsonPropertyName("role")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Role { get; init; }

    [JsonPropertyName("running")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Running { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Status { get; init; }

    [JsonPropertyName("waitingOnApproval")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> WaitingOnApproval { get; init; }

    [JsonPropertyName("waitingOnInput")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> WaitingOnInput { get; init; }

}

/// <summary>Executable wire contract for TeamMessageRecord.</summary>
[ContractModule("teams")]
public sealed class TeamMessageRecord : ExtensibleJsonObject
{
    [JsonPropertyName("artifactIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> ArtifactIds { get; init; }

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Content { get; init; }

    [JsonPropertyName("createdAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> CreatedAt { get; init; }

    [JsonPropertyName("deliveredAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> DeliveredAt { get; init; }

    [JsonPropertyName("deliveredQueuedInputId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DeliveredQueuedInputId { get; init; }

    [JsonPropertyName("fromMemberId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> FromMemberId { get; init; }

    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Kind { get; init; }

    [JsonPropertyName("messageId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> MessageId { get; init; }

    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> Metadata { get; init; }

    [JsonPropertyName("missionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> MissionId { get; init; }

    [JsonPropertyName("requiresAction")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> RequiresAction { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Status { get; init; }

    [JsonPropertyName("taskId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> TaskId { get; init; }

    [JsonPropertyName("toMemberId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ToMemberId { get; init; }

}

/// <summary>Executable wire contract for TeamRecord.</summary>
[ContractModule("teams")]
public sealed class TeamRecord : ExtensibleJsonObject
{
    [JsonPropertyName("createdAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> CreatedAt { get; init; }

    [JsonPropertyName("teamId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TeamId { get; init; }

    [JsonPropertyName("updatedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> UpdatedAt { get; init; }

}

/// <summary>Executable wire contract for TeamTaskRecord.</summary>
[ContractModule("teams")]
public sealed class TeamTaskRecord : ExtensibleJsonObject
{
    [JsonPropertyName("alias")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Alias { get; init; }

    [JsonPropertyName("assigneeMemberId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AssigneeMemberId { get; init; }

    [JsonPropertyName("blockedOnTaskIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> BlockedOnTaskIds { get; init; }

    [JsonPropertyName("blockedReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> BlockedReason { get; init; }

    [JsonPropertyName("completionRecoveryAttempts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> CompletionRecoveryAttempts { get; init; }

    [JsonPropertyName("completionRecoveryPending")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> CompletionRecoveryPending { get; init; }

    [JsonPropertyName("completionRecoveryQueuedInputId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> CompletionRecoveryQueuedInputId { get; init; }

    [JsonPropertyName("createdAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> CreatedAt { get; init; }

    [JsonPropertyName("dependsOnTaskIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> DependsOnTaskIds { get; init; }

    [JsonPropertyName("digest")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Digest { get; init; }

    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Kind { get; init; }

    [JsonPropertyName("latestUpdate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> LatestUpdate { get; init; }

    [JsonPropertyName("leaderNotifiedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> LeaderNotifiedAt { get; init; }

    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> Metadata { get; init; }

    [JsonPropertyName("missionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> MissionId { get; init; }

    [JsonPropertyName("outputSummary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> OutputSummary { get; init; }

    [JsonPropertyName("prompt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Prompt { get; init; }

    [JsonPropertyName("queuedInputId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> QueuedInputId { get; init; }

    [JsonPropertyName("requiredForMission")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> RequiredForMission { get; init; }

    [JsonPropertyName("requiresLeaderSynthesis")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> RequiresLeaderSynthesis { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Status { get; init; }

    [JsonPropertyName("synthesisMessageId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> SynthesisMessageId { get; init; }

    [JsonPropertyName("taskId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TaskId { get; init; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Title { get; init; }

    [JsonPropertyName("updatedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> UpdatedAt { get; init; }

}

/// <summary>Executable wire contract for TeamsMemberOpenThreadParams.</summary>
[ContractModule("teams")]
public sealed class TeamsMemberOpenThreadParams : ExtensibleJsonObject
{
    [JsonPropertyName("memberId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> MemberId { get; init; }

    [JsonPropertyName("missionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> MissionId { get; init; }

    [JsonPropertyName("taskId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TaskId { get; init; }

}

/// <summary>Executable wire contract for TeamsMemberOpenThreadResult.</summary>
[ContractModule("teams")]
public sealed class TeamsMemberOpenThreadResult : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for TeamsMissionArchiveParams.</summary>
[ContractModule("teams")]
public sealed class TeamsMissionArchiveParams : ExtensibleJsonObject
{
    [JsonPropertyName("missionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> MissionId { get; init; }

}

/// <summary>Executable wire contract for TeamsMissionCancelParams.</summary>
[ContractModule("teams")]
public sealed class TeamsMissionCancelParams : ExtensibleJsonObject
{
    [JsonPropertyName("missionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> MissionId { get; init; }

}

/// <summary>Executable wire contract for TeamsMissionCreateParams.</summary>
[ContractModule("teams")]
public sealed class TeamsMissionCreateParams : ExtensibleJsonObject
{
    [JsonPropertyName("prompt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Prompt { get; init; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Title { get; init; }

}

/// <summary>Executable wire contract for TeamsMissionCreateResult.</summary>
[ContractModule("teams")]
public sealed class TeamsMissionCreateResult : ExtensibleJsonObject
{
    [JsonPropertyName("mission")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<MissionRecord> Mission { get; init; }

    [JsonPropertyName("queuedInput")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<QueuedTurnInput?> QueuedInput { get; init; }

    [JsonPropertyName("team")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<TeamsTeamViewResult> Team { get; init; }

}

/// <summary>Executable wire contract for TeamsTeamChangedNotification.</summary>
[ContractModule("teams")]
public sealed class TeamsTeamChangedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("missionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> MissionId { get; init; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Reason { get; init; }

}

/// <summary>Executable wire contract for TeamsTeamStats.</summary>
[ContractModule("teams")]
public sealed class TeamsTeamStats : ExtensibleJsonObject
{
    [JsonPropertyName("cachedInputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> CachedInputTokens { get; init; }

    [JsonPropertyName("completedTasks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> CompletedTasks { get; init; }

    [JsonPropertyName("inputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> InputTokens { get; init; }

    [JsonPropertyName("outputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> OutputTokens { get; init; }

    [JsonPropertyName("queuedInputs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> QueuedInputs { get; init; }

    [JsonPropertyName("runningMembers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> RunningMembers { get; init; }

    [JsonPropertyName("totalTasks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> TotalTasks { get; init; }

    [JsonPropertyName("totalTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> TotalTokens { get; init; }

}

/// <summary>Executable wire contract for TeamsTeamViewResult.</summary>
[ContractModule("teams")]
public sealed class TeamsTeamViewResult : ExtensibleJsonObject
{
    [JsonPropertyName("archivedMissions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<MissionRecord>> ArchivedMissions { get; init; }

    [JsonPropertyName("artifacts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<ArtifactRefRecord>> Artifacts { get; init; }

    [JsonPropertyName("mailboxDigests")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<MailboxDigestRecord>> MailboxDigests { get; init; }

    [JsonPropertyName("members")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<TeamMemberView>> Members { get; init; }

    [JsonPropertyName("messages")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<TeamMessageRecord>> Messages { get; init; }

    [JsonPropertyName("missionThreads")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<MissionThreadView>> MissionThreads { get; init; }

    [JsonPropertyName("missions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<MissionRecord>> Missions { get; init; }

    [JsonPropertyName("stats")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<TeamsTeamStats> Stats { get; init; }

    [JsonPropertyName("tasks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<TeamTaskRecord>> Tasks { get; init; }

    [JsonPropertyName("team")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<TeamRecord?> Team { get; init; }

}

/// <summary>Executable wire contract for ThreadAppBindingConfirmCapabilitiesParams.</summary>
[ContractModule("app-binding")]
public sealed class ThreadAppBindingConfirmCapabilitiesParams : ExtensibleJsonObject
{
    [JsonPropertyName("bindingId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BindingId { get; init; }

    [JsonPropertyName("candidateRevision")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> CandidateRevision { get; init; }

    [JsonPropertyName("decision")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Decision { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadAppBindingEnableParams.</summary>
[ContractModule("app-binding")]
public sealed class ThreadAppBindingEnableParams : ExtensibleJsonObject
{
    [JsonPropertyName("appId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AppId { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadAppBindingEnableResult.</summary>
[ContractModule("app-binding")]
public sealed class ThreadAppBindingEnableResult : ExtensibleJsonObject
{
    [JsonPropertyName("bindingId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BindingId { get; init; }

    [JsonPropertyName("bindingRequestId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BindingRequestId { get; init; }

    [JsonPropertyName("expiresAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> ExpiresAt { get; init; }

    [JsonPropertyName("handoff")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AppHandoffWire?> Handoff { get; init; }

    [JsonPropertyName("state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> State { get; init; }

}

/// <summary>Executable wire contract for ThreadAppBindingRevokeParams.</summary>
[ContractModule("app-binding")]
public sealed class ThreadAppBindingRevokeParams : ExtensibleJsonObject
{
    [JsonPropertyName("bindingId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BindingId { get; init; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Reason { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadAppBindingSummaryWire.</summary>
[ContractModule("app-binding")]
public sealed class ThreadAppBindingSummaryWire : ExtensibleJsonObject
{
    [JsonPropertyName("appId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AppId { get; init; }

    [JsonPropertyName("approvedCapabilityRevision")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> ApprovedCapabilityRevision { get; init; }

    [JsonPropertyName("approvedTools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<AppBindingToolCapabilityWire>> ApprovedTools { get; init; }

    [JsonPropertyName("authorityRevision")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> AuthorityRevision { get; init; }

    [JsonPropertyName("bindingId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BindingId { get; init; }

    [JsonPropertyName("bindingRequestId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> BindingRequestId { get; init; }

    [JsonPropertyName("candidateCapabilityRevision")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long?> CandidateCapabilityRevision { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DisplayName { get; init; }

    [JsonPropertyName("failureReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> FailureReason { get; init; }

    [JsonPropertyName("icon")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Icon { get; init; }

    [JsonPropertyName("managed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Managed { get; init; }

    [JsonPropertyName("pendingChanges")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<AppBindingCapabilityChangeWire>> PendingChanges { get; init; }

    [JsonPropertyName("requiresExternalConnection")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> RequiresExternalConnection { get; init; }

    [JsonPropertyName("socialTarget")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SocialChannelTargetWire?> SocialTarget { get; init; }

    [JsonPropertyName("state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> State { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadAppBindingsChangedNotification.</summary>
[ContractModule("app-binding")]
public sealed class ThreadAppBindingsChangedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("appId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AppId { get; init; }

    [JsonPropertyName("authorityRevision")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long?> AuthorityRevision { get; init; }

    [JsonPropertyName("bindingId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> BindingId { get; init; }

    [JsonPropertyName("changeKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ChangeKind { get; init; }

    [JsonPropertyName("failureReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> FailureReason { get; init; }

    [JsonPropertyName("previousState")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> PreviousState { get; init; }

    [JsonPropertyName("state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> State { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadAppBindingsListParams.</summary>
[ContractModule("app-binding")]
public sealed class ThreadAppBindingsListParams : ExtensibleJsonObject
{
    [JsonPropertyName("includeRevoked")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> IncludeRevoked { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadAppBindingsListResult.</summary>
[ContractModule("app-binding")]
public sealed class ThreadAppBindingsListResult : ExtensibleJsonObject
{
    [JsonPropertyName("bindings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<AppBindingWire>> Bindings { get; init; }

}

/// <summary>Executable wire contract for ThreadSocialBindingRequestCreateParams.</summary>
[ContractModule("app-binding")]
public sealed class ThreadSocialBindingRequestCreateParams : ExtensibleJsonObject
{
    [JsonPropertyName("channelName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ChannelName { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadSocialBindingRequestCreateResult.</summary>
[ContractModule("app-binding")]
public sealed class ThreadSocialBindingRequestCreateResult : ExtensibleJsonObject
{
    [JsonPropertyName("bindingId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BindingId { get; init; }

    [JsonPropertyName("bindingRequestId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BindingRequestId { get; init; }

    [JsonPropertyName("channelName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ChannelName { get; init; }

    [JsonPropertyName("code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Code { get; init; }

    [JsonPropertyName("expiresAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> ExpiresAt { get; init; }

}
