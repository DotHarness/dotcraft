namespace DotCraft.Hub;

public static class HubAppServerStates
{
    public const string Stopped = "stopped";
    public const string Starting = "starting";
    public const string Running = "running";
    public const string Unhealthy = "unhealthy";
    public const string Stopping = "stopping";
    public const string Exited = "exited";
}

public sealed class EnsureAppServerRequest
{
    public string WorkspacePath { get; set; } = string.Empty;

    public HubClientInfo? Client { get; set; }

    public bool StartIfMissing { get; set; } = true;

    public HubRuntimeToolsRequest? RuntimeTools { get; set; }
}

public sealed class WorkspacePathRequest
{
    public string WorkspacePath { get; set; } = string.Empty;

    public HubRuntimeToolsRequest? RuntimeTools { get; set; }
}

public sealed class HubClientInfo
{
    public string? Name { get; set; }

    public string? Version { get; set; }
}

public sealed class HubRuntimeToolsRequest
{
    public string? RipgrepPath { get; set; }

    public string? NodeBin { get; set; }

    public bool? NodeRunAsNode { get; set; }

    public string? ModulesDir { get; set; }

    public string? BuiltInPluginRoots { get; set; }

    public string? BuiltInPluginCatalogs { get; set; }
}

public sealed class HubNotificationRequest
{
    public string? WorkspacePath { get; set; }

    public string? ThreadId { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string? TitleKey { get; set; }

    public string? BodyKey { get; set; }

    public object? Params { get; set; }

    public string? FallbackTitle { get; set; }

    public string? FallbackBody { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Body { get; set; }

    public string? Severity { get; set; }

    public string? Source { get; set; }

    public string? ActionUrl { get; set; }

    public bool? OpenDesktopOnClick { get; set; }
}

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

public sealed record HubServiceStatus(
    string State,
    string? Url = null,
    string? Reason = null);

public sealed record HubEvent(
    string Kind,
    DateTimeOffset At,
    string? WorkspacePath,
    object? Data);
