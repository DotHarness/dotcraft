using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

/// <summary>Params for listing paired Remote Tool Hosts.</summary>
public sealed class RemoteToolHostListParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }
}

/// <summary>Safe catalog of paired Remote Tool Hosts, plus one thread's current route.</summary>
public sealed class RemoteToolHostListResult : ExtensibleJsonObject
{
    [JsonPropertyName("hosts")]
    public required IReadOnlyList<RemoteToolHostInfo> Hosts { get; init; }

    [JsonPropertyName("route")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<RemoteToolRouteInfo?> Route { get; init; }
}

/// <summary>One paired machine and the folders it exports.</summary>
public sealed class RemoteToolHostInfo : ExtensibleJsonObject
{
    [JsonPropertyName("hostId")]
    public required string HostId { get; init; }

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("online")]
    public required bool Online { get; init; }

    [JsonPropertyName("workspaces")]
    public required IReadOnlyList<RemoteToolHostWorkspaceInfo> Workspaces { get; init; }

    [JsonPropertyName("errorCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ErrorCode { get; init; }
}

/// <summary>One folder a paired machine exports, with its lease availability.</summary>
public sealed class RemoteToolHostWorkspaceInfo : ExtensibleJsonObject
{
    [JsonPropertyName("workspaceId")]
    public required string WorkspaceId { get; init; }

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("available")]
    public required bool Available { get; init; }

    [JsonPropertyName("busyOwner")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> BusyOwner { get; init; }

    [JsonPropertyName("leaseExpiresAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> LeaseExpiresAt { get; init; }
}

/// <summary>One thread's runtime-only route to a remote folder.</summary>
public sealed class RemoteToolRouteInfo : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("hostId")]
    public required string HostId { get; init; }

    [JsonPropertyName("workspaceId")]
    public required string WorkspaceId { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("environment")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<RemoteToolEnvironmentInfo?> Environment { get; init; }
}

/// <summary>Where a routed thread's tools run.</summary>
public sealed class RemoteToolEnvironmentInfo : ExtensibleJsonObject
{
    [JsonPropertyName("hostName")]
    public required string HostName { get; init; }

    [JsonPropertyName("operatingSystem")]
    public required string OperatingSystem { get; init; }

    [JsonPropertyName("userName")]
    public required string UserName { get; init; }

    [JsonPropertyName("workspacePath")]
    public required string WorkspacePath { get; init; }
}

/// <summary>Params for routing one thread to a remote folder.</summary>
public sealed class RemoteToolHostConnectParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("hostId")]
    public required string HostId { get; init; }

    [JsonPropertyName("workspaceId")]
    public required string WorkspaceId { get; init; }
}

/// <summary>The published route and the tools it covers.</summary>
public sealed class RemoteToolHostConnectResult : ExtensibleJsonObject
{
    [JsonPropertyName("route")]
    public required RemoteToolRouteInfo Route { get; init; }

    [JsonPropertyName("matchedTools")]
    public required IReadOnlyList<string> MatchedTools { get; init; }

    [JsonPropertyName("unavailableTools")]
    public required IReadOnlyList<string> UnavailableTools { get; init; }

    [JsonPropertyName("alreadyConnected")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> AlreadyConnected { get; init; }
}

/// <summary>Params for returning one thread to local execution.</summary>
public sealed class RemoteToolHostDisconnectParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }
}

/// <summary>The route a disconnect removed, when there was one.</summary>
public sealed class RemoteToolHostDisconnectResult : ExtensibleJsonObject
{
    [JsonPropertyName("disconnected")]
    public required bool Disconnected { get; init; }

    [JsonPropertyName("previousRoute")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<RemoteToolRouteInfo?> PreviousRoute { get; init; }
}

/// <summary>Announces that one thread's remote route changed.</summary>
public sealed class RemoteToolHostRouteChangedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("initiator")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Initiator { get; init; }

    [JsonPropertyName("route")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<RemoteToolRouteInfo?> Route { get; init; }
}
