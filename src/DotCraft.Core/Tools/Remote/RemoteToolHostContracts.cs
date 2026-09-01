using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Tools;

/// <summary>Identifies one active remote execution target for a thread.</summary>
public sealed record RemoteToolRoute(
    string HostId,
    string WorkspaceId,
    string LeaseId,
    string HostInstanceId);

/// <summary>Safe environment information returned after acquiring a remote workspace.</summary>
public sealed record RemoteToolEnvironment(
    string HostName,
    string OperatingSystem,
    string UserName,
    string WorkspacePath);

/// <summary>The runtime state of one thread's Remote Tool Host connection.</summary>
public enum RemoteToolConnectionStatus
{
    Connected,
    LeaseLost
}

/// <summary>Model-safe, runtime-only connection information for one thread.</summary>
public sealed record RemoteToolConnectionSnapshot(
    RemoteToolConnectionStatus Status,
    string HostId,
    string WorkspaceId,
    RemoteToolEnvironment Environment);

/// <summary>One Host-local workspace that may be leased by an Agent Host.</summary>
public sealed record RemoteToolWorkspaceDescriptor(
    string WorkspaceId,
    string DisplayName,
    bool Available,
    string? BusyOwner = null,
    DateTimeOffset? LeaseExpiresAt = null);

/// <summary>Safe registration and live-catalog information for one Remote Tool Host.</summary>
public sealed record RemoteToolHostDescriptor(
    string HostId,
    string DisplayName,
    bool Online,
    IReadOnlyList<RemoteToolWorkspaceDescriptor> Workspaces,
    string? ErrorCode = null);

/// <summary>Model-safe result returned by RemoteToolHost.List.</summary>
public sealed record RemoteToolHostCatalog(
    IReadOnlyList<RemoteToolHostDescriptor> Hosts,
    RemoteToolRoute? ConnectedRoute = null);

/// <summary>Model-safe result returned after connecting to a remote workspace.</summary>
public sealed record RemoteToolConnectResult(
    RemoteToolRoute Route,
    RemoteToolEnvironment Environment,
    IReadOnlyList<string> MatchedTools,
    IReadOnlyList<string> UnavailableTools,
    bool AlreadyConnected = false);

/// <summary>Model-safe result returned after disconnecting one thread route.</summary>
public sealed record RemoteToolDisconnectResult(bool Disconnected, RemoteToolRoute? PreviousRoute = null);

/// <summary>
/// Agent-side Remote Tool Host client boundary. Implementations own credentials, MCP sessions,
/// workspace leases, and runtime-only thread routes.
/// </summary>
public interface IRemoteToolHostClient
{
    /// <summary>Replaces the current trusted set of RPC-eligible definitions.</summary>
    void UpdateRemoteToolDefinitions(IReadOnlyList<ToolDefinition> definitions);

    /// <summary>Lists registered Hosts and refreshes their safe workspace catalogs.</summary>
    ValueTask<RemoteToolHostCatalog> ListAsync(
        string threadId,
        CancellationToken cancellationToken = default);

    /// <summary>Acquires a workspace and atomically publishes the route for one thread.</summary>
    ValueTask<RemoteToolConnectResult> ConnectAsync(
        string threadId,
        string hostId,
        string workspaceId,
        CancellationToken cancellationToken = default);

    /// <summary>Clears one thread route and releases its route reference.</summary>
    ValueTask<RemoteToolDisconnectResult> DisconnectAsync(
        string threadId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the current runtime-only route without performing network work.</summary>
    bool TryGetRoute(string threadId, out RemoteToolRoute route);

    /// <summary>Gets model-safe runtime connection state without performing network work.</summary>
    bool TryGetConnectionSnapshot(string threadId, out RemoteToolConnectionSnapshot snapshot);

    /// <summary>Copies the parent's current route reference to a native child thread.</summary>
    bool TryForkRoute(string parentThreadId, string childThreadId);

    /// <summary>Invokes an exact mirrored definition through the active remote workspace lease.</summary>
    ValueTask<ToolExecutionResult> InvokeAsync(
        RemoteToolRoute route,
        ToolDefinition definition,
        string contractHash,
        ToolInvocationContext context,
        JsonObject arguments,
        CancellationToken cancellationToken = default);
}

/// <summary>Creates the process-shared Remote Tool Host client after runtime approval is available.</summary>
public interface IRemoteToolHostClientFactory
{
    /// <summary>Returns the shared client bound to the effective Agent-side approval service.</summary>
    IRemoteToolHostClient Create(DotCraft.Security.IApprovalService approvalService);
}

/// <summary>Stable Remote Tool Host failure codes used in common tool results.</summary>
public static class RemoteToolErrorCodes
{
    public const string HostNotRegistered = "remote_host_not_registered";
    public const string HostOffline = "remote_host_offline";
    public const string AuthenticationFailed = "remote_authentication_failed";
    public const string CertificateMismatch = "remote_certificate_mismatch";
    public const string ProtocolMismatch = "remote_protocol_mismatch";
    public const string WorkspaceNotFound = "remote_workspace_not_found";
    public const string WorkspaceBusy = "remote_workspace_busy";
    public const string LeaseLost = "remote_lease_lost";
    public const string ToolContractMismatch = "remote_tool_contract_mismatch";
    public const string RemoteToolUnavailable = "remote_tool_unavailable";
    public const string RemotePolicyDenied = "remote_policy_denied";
    public const string ApprovalDeclined = "remote_approval_declined";
    public const string RemoteOutcomeUnknown = "remote_outcome_unknown";
    public const string RemoteResultMaterializationFailed = "remote_result_materialization_failed";
}

/// <summary>JSON metadata attached to successful remote tool results for safe observability.</summary>
public sealed record RemoteToolInvocationProvenance(
    string ExecutionTarget,
    string HostId,
    string WorkspaceId,
    string HostInstanceId,
    string RemoteInvocationId,
    long RemoteLatencyMilliseconds,
    string? RemoteArtifactPath = null,
    long? RemoteArtifactCharacterCount = null)
{
    public JsonElement ToJson() => JsonSerializer.SerializeToElement(this, JsonSerializerOptions.Web);
}
