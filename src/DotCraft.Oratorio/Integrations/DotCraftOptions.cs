namespace DotCraft.Oratorio.Integrations;

public sealed class DotCraftOptions
{
    public List<DotCraftRepositoryWorkspaceRoute> RepositoryWorkspaceRoutes { get; set; } = [];
    public string AppServerUrl { get; set; } = "ws://127.0.0.1:9100/ws";

    /// <summary>
    /// Bearer token presented to a configured (non-Hub) DotCraft AppServer. May be stored
    /// in plaintext or as an <c>enc:v1:</c> value protected by <see cref="global::DotCraft.Oratorio.Services.IConfigurationSecretProtector"/>;
    /// it is unprotected at connection time. Hub-discovered endpoints carry their token in the URL instead.
    /// </summary>
    public string AppServerToken { get; set; } = "";
    public bool HubDiscoveryEnabled { get; set; } = true;
    public string HubLockPath { get; set; } = "";
    public bool AutoStart { get; set; }
    public string Command { get; set; } = "dotnet";
    public List<string> Arguments { get; set; } = [];
    public int RunTimeoutSeconds { get; set; } = 30 * 60;
    public string ApprovalPolicy { get; set; } = "interrupt";
    public int ConnectTimeoutSeconds { get; set; } = 5;
    public bool ManagedWorktreesEnabled { get; set; } = true;
    public string WorktreeRoot { get; set; } = "";
    public string WorktreeBranchPrefix { get; set; } = "oratorio/run";
    public int GlobalMaxActiveRuns { get; set; } = 2;
    public int MaxActiveRunsPerRepository { get; set; } = 1;
    public int MaxActiveRunsPerSource { get; set; } = 2;
    public int MaxRunAttempts { get; set; } = 3;
    public int RetryBackoffSeconds { get; set; } = 10;
    public int MaxRetryBackoffSeconds { get; set; } = 300;
    public int StallTimeoutSeconds { get; set; } = 300;
    public int SucceededWorktreeRetentionHours { get; set; } = 24;
    public int FailedWorktreeRetentionHours { get; set; } = 168;
    public bool WorktreeCleanupEnabled { get; set; } = true;
    public int WorktreeCleanupIntervalSeconds { get; set; } = 60;

    public TimeSpan RunTimeout => TimeSpan.FromSeconds(Math.Clamp(RunTimeoutSeconds, 30, 7200));
    public TimeSpan ConnectTimeout => TimeSpan.FromSeconds(Math.Clamp(ConnectTimeoutSeconds, 1, 30));
    public int EffectiveGlobalMaxActiveRuns => Math.Max(1, GlobalMaxActiveRuns);
    public int EffectiveMaxActiveRunsPerRepository => Math.Max(1, MaxActiveRunsPerRepository);
    public int EffectiveMaxActiveRunsPerSource => Math.Max(1, MaxActiveRunsPerSource);
    public int EffectiveMaxRunAttempts => Math.Max(1, MaxRunAttempts);
    public TimeSpan RetryBackoff => TimeSpan.FromSeconds(Math.Clamp(RetryBackoffSeconds, 1, 300));
    public TimeSpan MaxRetryBackoff => TimeSpan.FromSeconds(Math.Clamp(MaxRetryBackoffSeconds, 1, 1800));
    public TimeSpan StallTimeout => TimeSpan.FromSeconds(Math.Clamp(StallTimeoutSeconds, 5, 7200));
    public TimeSpan SucceededWorktreeRetention => TimeSpan.FromHours(Math.Clamp(SucceededWorktreeRetentionHours, 0, 24 * 30));
    public TimeSpan FailedWorktreeRetention => TimeSpan.FromHours(Math.Clamp(FailedWorktreeRetentionHours, 1, 24 * 60));
    public TimeSpan WorktreeCleanupInterval => TimeSpan.FromSeconds(Math.Clamp(WorktreeCleanupIntervalSeconds, 5, 3600));
}

public sealed class DotCraftRepositoryWorkspaceRoute
{
    public string Project { get; set; } = "";
    public string WorkspacePath { get; set; } = "";
}
