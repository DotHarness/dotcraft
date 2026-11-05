namespace DotCraft.Hub;

public sealed partial class ManagedAppServerRegistry
{
    private sealed record ServicePlan(
        IReadOnlyDictionary<string, string?> Environment,
        IReadOnlyDictionary<string, string> ResponseEndpoints,
        IReadOnlyDictionary<string, HubServiceStatus> ServiceStatus,
        string? TypeScriptNodeBin,
        bool TypeScriptNodeRunAsNode,
        string? TypeScriptModulesDir,
        string? BuiltInPluginRoots,
        string? DefaultPluginRegistryUrl);

    private sealed class ManagedEntry
    {
        public ManagedEntry(string workspacePath, string canonicalWorkspacePath)
        {
            WorkspacePath = workspacePath;
            CanonicalWorkspacePath = canonicalWorkspacePath;
        }

        public SemaphoreSlim Mutex { get; } = new(1, 1);

        public string WorkspacePath { get; }

        public string CanonicalWorkspacePath { get; }

        public string State { get; set; } = HubAppServerStates.Stopped;

        public IManagedAppServerProcess? Process { get; set; }

        public bool AdoptedExternalLock { get; set; }

        public string? TypeScriptNodeBin { get; set; }

        public bool TypeScriptNodeRunAsNode { get; set; }

        public string? TypeScriptModulesDir { get; set; }

        public string? BuiltInPluginRoots { get; set; }

        public string? DefaultPluginRegistryUrl { get; set; }

        public int? Pid { get; set; }

        public string? ServerVersion { get; set; }

        public bool StartedByHub { get; set; }

        public bool StartedInCurrentHubProcess { get; set; }

        public int? ExitCode { get; set; }

        public string? LastError { get; set; }

        public string? RecentStderr { get; set; }

        public DateTimeOffset? LastStartedAt { get; set; }

        public DateTimeOffset? LastSeenAt { get; set; }

        public IReadOnlyDictionary<string, string> Endpoints { get; set; } =
            new Dictionary<string, string>();

        public IReadOnlyDictionary<string, HubServiceStatus> ServiceStatus { get; set; } =
            new Dictionary<string, HubServiceStatus>();

        public HubAppServerResponse ToResponse() => new(
            WorkspacePath,
            CanonicalWorkspacePath,
            State,
            Pid,
            Endpoints,
            ServiceStatus,
            ServerVersion,
            StartedByHub,
            ExitCode,
            LastError,
            RecentStderr);

        public HubAppServerRegistryRecord ToRegistryRecord() => new(
            WorkspacePath,
            CanonicalWorkspacePath,
            Path.GetFileName(CanonicalWorkspacePath),
            State,
            Pid,
            Endpoints,
            ServiceStatus,
            ServerVersion,
            StartedByHub,
            LastStartedAt,
            LastSeenAt);
    }
}
