namespace DotCraft.Oratorio.Services;

public sealed record SourceSetupDto(
    bool Writable,
    string? DisabledReason,
    SourceSetupGitHubDto GitHub,
    SourceSetupGitLabDto GitLab,
    IReadOnlyList<SourceSetupProjectDto> Projects,
    IReadOnlyList<SourceSetupWorkspaceDto> Workspaces);

public sealed record SourceSetupGitHubDto(
    string Endpoint,
    string? AppId,
    bool AppKeyConfigured,
    IReadOnlyList<SourceSetupInstallationDto> Installations,
    bool WritesEnabled,
    int? SyncIntervalMinutes);

public sealed record SourceSetupInstallationDto(string Owner, string InstallationId);

public sealed record SourceSetupGitLabDto(string Endpoint, bool WritesEnabled, int? SyncIntervalMinutes);

public sealed record SourceSetupProjectDto(
    string Provider,
    string Project,
    string WorkspacePath,
    bool Enabled,
    bool AutoReview,
    string Credentials,
    SourceSetupLastSyncDto? LastSync);

public sealed record SourceSetupLastSyncDto(
    string Status,
    DateTimeOffset? FinishedAt,
    int IssuesImported,
    int ReviewTargetsImported,
    string? Error);

public sealed record SourceSetupWorkspaceDto(string Path, string Name);

public sealed record SourceSetupSyncDto(
    string JobId,
    string Status,
    int IssuesImported,
    int ReviewTargetsImported,
    string? Error);

public sealed record SourceSetupWarningDto(string Code, string Message);

public sealed record ConnectProjectResultDto(
    SourceSetupProjectDto Project,
    string Credentials,
    SourceSetupSyncDto? Sync,
    IReadOnlyList<SourceSetupWarningDto> Warnings);

public sealed record SourceCredentialRequestDto(
    string Provider,
    string Project,
    string Instance,
    string Credential,
    bool Configured,
    string? TokenKind,
    string? AppId);

public sealed record SourceCredentialSavedDto(string Provider, string Project, bool Configured);

public sealed record ConnectProjectRequest(
    string Provider,
    string Project,
    string WorkspacePath,
    string? Endpoint,
    bool? AutoReview,
    bool? AllowWrites,
    int? SyncIntervalMinutes,
    string? GitHubAppId,
    string? GitHubPrivateKeyPath,
    string? GitHubInstallationId);
