namespace DotCraft.Sdk.Hub;

using System.Text.Json;

/// <summary>Policy applied when a live Hub uses a different executable.</summary>
public enum HubBinaryMatchPolicy
{
    Ignore,
    RestartIfMismatch,
    ErrorIfMismatch
}

/// <summary>
/// DotCraft Hub client options.
/// </summary>
public sealed class DotCraftHubClientOptions
{
    /// <summary>
    /// Optional dotcraft executable or dll used when starting Hub.
    /// </summary>
    public string? Executable { get; set; }

    /// <summary>Optional executable path expected for an already running Hub.</summary>
    public string? ExpectedExecutable { get; set; }

    /// <summary>Action taken when <see cref="ExpectedExecutable"/> does not match.</summary>
    public HubBinaryMatchPolicy BinaryMatchPolicy { get; set; } = HubBinaryMatchPolicy.Ignore;

    /// <summary>
    /// Optional user profile directory used to resolve .craft paths.
    /// </summary>
    public string? UserProfilePath { get; set; }

    /// <summary>
    /// Optional explicit Hub lock path.
    /// </summary>
    public string? HubLockPath { get; set; }

    /// <summary>
    /// Whether EnsureHubAsync may start dotcraft hub when no live lock exists.
    /// </summary>
    public bool StartHubIfMissing { get; set; } = true;

    /// <summary>
    /// Timeout while waiting for a newly started Hub.
    /// </summary>
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Timeout while waiting for a mismatched Hub to stop.</summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Optional factory for test or host-provided HTTP clients.
    /// </summary>
    public Func<HttpClient>? HttpClientFactory { get; set; }
}

/// <summary>
/// Published DotCraft Hub lock metadata.
/// </summary>
public sealed record HubLockInfo(
    int Pid,
    string ApiBaseUrl,
    string Token,
    DateTimeOffset? StartedAt = null,
    string? Version = null,
    string? BinaryPath = null);

/// <summary>
/// AppServer states returned by Hub.
/// </summary>
public static class HubAppServerStates
{
    public const string Stopped = "stopped";
    public const string Starting = "starting";
    public const string Running = "running";
    public const string Unhealthy = "unhealthy";
    public const string Stopping = "stopping";
    public const string Exited = "exited";
}

/// <summary>
/// Client identity sent to Hub management APIs.
/// </summary>
public sealed class HubClientInfo
{
    public string? Name { get; set; }
    public string? Version { get; set; }
}

/// <summary>
/// AppServer ensure request options.
/// </summary>
public sealed class HubEnsureAppServerOptions
{
    public HubClientInfo? Client { get; set; }
    public bool StartIfMissing { get; set; } = true;
    public HubRuntimeToolsRequest? RuntimeTools { get; set; }
}

/// <summary>Host runtime tool paths forwarded to Hub-managed AppServers.</summary>
public sealed class HubRuntimeToolsRequest
{
    public string? RipgrepPath { get; set; }
    public string? NodeBin { get; set; }
    public bool? NodeRunAsNode { get; set; }
    public string? ModulesDir { get; set; }
    public string? BuiltInPluginRoots { get; set; }
    public string? DefaultPluginRegistryUrl { get; set; }
}

/// <summary>Hub capability flags.</summary>
public sealed record HubCapabilities(
    bool AppServerManagement,
    bool PortManagement,
    bool Events,
    bool Notifications,
    bool Tray);

/// <summary>Hub status response.</summary>
public sealed record HubStatusResponse(
    string HubVersion,
    int Pid,
    DateTimeOffset StartedAt,
    string StatePath,
    string ApiBaseUrl,
    string? BinaryPath,
    HubCapabilities Capabilities);

/// <summary>One Hub SSE lifecycle event.</summary>
public sealed record HubEvent(
    string Kind,
    DateTimeOffset At,
    string? WorkspacePath,
    JsonElement? Data);

/// <summary>
/// Hub AppServer response.
/// </summary>
public sealed record HubAppServerResponse(
    string WorkspacePath,
    string CanonicalWorkspacePath,
    string State,
    int? Pid,
    IReadOnlyDictionary<string, string> Endpoints,
    IReadOnlyDictionary<string, HubServiceStatus> ServiceStatus,
    string? ServerVersion,
    bool StartedByHub,
    int? ExitCode,
    string? LastError,
    string? RecentStderr);

/// <summary>
/// Hub service status entry.
/// </summary>
public sealed record HubServiceStatus(string State, string? Url = null, string? Reason = null);

/// <summary>
/// Exception raised by Hub discovery or management calls.
/// </summary>
public sealed class HubClientException(
    string code,
    string message,
    JsonElement? details = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    /// <summary>
    /// Stable Hub error code.
    /// </summary>
    public string Code { get; } = code;

    /// <summary>Structured Hub error details, when supplied by the server.</summary>
    public JsonElement? Details { get; } = details;
}
