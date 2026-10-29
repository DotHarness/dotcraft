using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Realtime;
using DotCraft.Oratorio.Sources;
using static DotCraft.Oratorio.Services.SourceSetupConfiguration;

namespace DotCraft.Oratorio.Services;

public sealed class SourceSetupService(
    ServerConfigurationService configuration,
    SourceSyncSchedulerService schedules,
    SourceProviderService sources,
    WorkspaceInventoryService workspaces,
    OratorioDbContext db,
    BoardEventHub boardEvents,
    IClock clock)
{
    public const string AuditActor = "app-binding";
    private const string WritesDisabledReason = "Server configuration writes are disabled for this deployment.";
    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const int DefaultSyncIntervalMinutes = 15;

    public TimeSpan SyncWait { get; set; } = TimeSpan.FromSeconds(30);

    public async Task<SourceSetupDto> GetSetupAsync(CancellationToken ct)
    {
        var current = configuration.GetSnapshot().Configuration;
        var schedule = (await schedules.GetSchedulesAsync(ct)).Schedules;
        int? Minutes(string provider) =>
            schedule.FirstOrDefault(x => x.Provider == provider && x.Enabled)?.IntervalSeconds / 60;

        var projects = new List<SourceSetupProjectDto>();
        foreach (var project in ListProjects(current))
        {
            projects.Add(await ToDtoAsync(current, project, ct));
        }

        return new SourceSetupDto(
            configuration.DeploymentWritable,
            configuration.DeploymentWritable ? null : WritesDisabledReason,
            new SourceSetupGitHubDto(
                current.GitHub.Endpoint,
                current.GitHub.AppId,
                GitHubAppKeyConfigured(current),
                current.GitHub.InstallationProfiles
                    .Where(profile => string.Equals(profile.Instance, Instance(GitHubProvider, current.GitHub.Endpoint), StringComparison.OrdinalIgnoreCase))
                    .Select(profile => new SourceSetupInstallationDto(profile.Owner, profile.InstallationId))
                    .ToArray(),
                current.GitHub.WritesEnabled,
                Minutes(GitHubProvider)),
            new SourceSetupGitLabDto(current.GitLab.Endpoint, current.GitLab.WritesEnabled, Minutes(GitLabProvider)),
            projects,
            workspaces.ListWorkspacePaths().Select(x => new SourceSetupWorkspaceDto(x.Path, x.Label)).ToArray());
    }

    public async Task<ConnectProjectResultDto> ConnectAsync(ConnectProjectRequest request, CancellationToken ct)
    {
        ThrowIfNotWritable();
        var snapshot = configuration.GetSnapshot();
        var next = Connect(snapshot.Configuration, request);
        var detect = request.Provider == GitHubProvider &&
            !string.IsNullOrWhiteSpace(next.GitHub.AppId) &&
            GitHubAppKeyConfigured(next) &&
            GitHubInstallation(next, Owner(request.Project)) is null;
        var applied = await ApplyAsync(snapshot.Revision, next, detect, ct);

        var warnings = applied.GitHubInstallationWarnings
            .Where(warning => string.Equals(warning.Owner, Owner(request.Project), StringComparison.OrdinalIgnoreCase))
            .Select(warning => new SourceSetupWarningDto(warning.Code, warning.Message))
            .ToList();
        var saved = applied.Configuration;
        var gap = CredentialGap(saved, request.Provider, request.Project);
        if (gap != SourceCredentialGap.None)
        {
            warnings.Add(GapWarning(gap, request.Project));
        }

        var minutes = request.SyncIntervalMinutes ?? await CurrentIntervalAsync(request.Provider, ct);
        var scheduled = await TrySaveScheduleAsync(request.Provider, minutes, warnings, ct);
        SourceSetupSyncDto? sync = null;
        if (gap == SourceCredentialGap.None)
        {
            sync = await RunSyncAsync(request.Provider, request.Project, warnings, ct);
        }
        else if (!scheduled)
        {
            await PublishScheduleAsync(request.Provider, ct);
        }

        var project = FindProject(saved, request.Provider, request.Project)
            ?? throw ProjectNotConnected(request.Provider, request.Project);
        return new ConnectProjectResultDto(
            await ToDtoAsync(saved, project, ct),
            gap == SourceCredentialGap.None ? "ready" : "missing",
            sync,
            warnings);
    }

    public async Task<SourceSetupSyncDto> SyncAsync(string provider, string project, CancellationToken ct)
    {
        var current = configuration.GetSnapshot().Configuration;
        var connected = FindProject(current, provider, project);
        if (connected is null || !connected.Enabled)
        {
            throw ProjectNotConnected(provider, project);
        }

        var gap = CredentialGap(current, provider, connected.Project);
        if (gap != SourceCredentialGap.None)
        {
            var warning = GapWarning(gap, connected.Project);
            throw new OratorioApiException(StatusCodes.Status409Conflict, "sourceCredentialsMissing", warning.Message);
        }

        List<SourceSetupWarningDto> warnings = [];
        return await RunSyncAsync(provider, connected.Project, warnings, ct)
            ?? throw new OratorioApiException(StatusCodes.Status409Conflict, warnings[0].Code, warnings[0].Message);
    }

    public SourceCredentialRequestDto RequestCredential(string provider, string project)
    {
        var current = configuration.GetSnapshot().Configuration;
        var connected = FindProject(current, provider, project) ?? throw ProjectNotConnected(provider, project);
        var instance = Instance(provider, Endpoint(current, provider));
        if (provider == GitLabProvider)
        {
            var profile = GitLabProfile(current, connected.Project) ?? throw ProjectNotConnected(provider, project);
            return new SourceCredentialRequestDto(
                provider,
                connected.Project,
                instance,
                "gitlabToken",
                profile.Secrets?.Token.Configured == true,
                profile.TokenKind,
                null);
        }

        if (string.IsNullOrWhiteSpace(current.GitHub.AppId))
        {
            throw new OratorioApiException(
                StatusCodes.Status409Conflict,
                "githubAppIdMissing",
                "The GitHub App id is not configured. Ask the user for it and call ConnectProject with githubAppId first.");
        }

        return new SourceCredentialRequestDto(
            provider,
            connected.Project,
            instance,
            "githubAppPrivateKey",
            GitHubAppKeyConfigured(current),
            null,
            current.GitHub.AppId);
    }

    public async Task<SourceCredentialSavedDto> SaveCredentialAsync(
        string provider,
        string project,
        string secret,
        string? tokenKind,
        CancellationToken ct)
    {
        ThrowIfNotWritable();
        var request = RequestCredential(provider, project);
        var snapshot = configuration.GetSnapshot();
        ServerConfigurationDto next;
        if (provider == GitLabProvider)
        {
            next = ReplaceGitLabToken(snapshot.Configuration, request.Project, RequireSecret(secret.Trim()), tokenKind);
        }
        else
        {
            if (!secret.Contains("-----BEGIN", StringComparison.Ordinal) ||
                !secret.Contains("PRIVATE KEY-----", StringComparison.Ordinal))
            {
                throw Invalid("invalidPrivateKey", "The GitHub App private key must be the PEM file content.");
            }

            next = ReplaceGitHubPrivateKey(snapshot.Configuration, secret.Trim());
        }

        await ApplyAsync(snapshot.Revision, next, detectGitHubInstallations: false, ct);
        return new SourceCredentialSavedDto(provider, request.Project, true);
    }

    private Task<ServerConfigurationApplyResult> ApplyAsync(
        string revision,
        ServerConfigurationDto next,
        bool detectGitHubInstallations,
        CancellationToken ct) =>
        configuration.ApplyAsync(
            new ServerConfigurationApplyRequest(revision, next, ConfirmImpact: true, detectGitHubInstallations, AuditActor, RemoteAddress: null),
            ct);

    /// <summary>A schedule that has never run counts as unset, so a connection saved before its credentials still gets the default.</summary>
    private async Task<int> CurrentIntervalAsync(string provider, CancellationToken ct)
    {
        var schedule = await schedules.GetScheduleAsync(provider, ct);
        if (schedule.Enabled) return schedule.IntervalSeconds / 60;
        return schedule.LastScheduledAt is null && schedule.LastJobId is null ? DefaultSyncIntervalMinutes : 0;
    }

    private async Task<bool> TrySaveScheduleAsync(
        string provider,
        int minutes,
        List<SourceSetupWarningDto> warnings,
        CancellationToken ct)
    {
        try
        {
            await schedules.UpdateScheduleAsync(
                provider,
                new SourceSyncScheduleUpdateRequest(minutes > 0, minutes > 0 ? minutes * 60 : null),
                ct);
            return true;
        }
        catch (OratorioApiException ex) when (ex.Code == "sourceSyncScheduleUnavailable")
        {
            warnings.Add(new SourceSetupWarningDto(
                "syncScheduleNotStarted",
                "Scheduled sync starts once the credentials are saved and ConnectProject runs again."));
            return false;
        }
    }

    private async Task PublishScheduleAsync(string provider, CancellationToken ct)
    {
        var schedule = await schedules.GetScheduleAsync(provider, ct);
        boardEvents.PublishSourceSync(new SourceSyncEvent(
            SourceSyncEvent.SourceSyncScheduleUpdatedType,
            JsonSerializer.SerializeToElement(schedule, EventJsonOptions),
            clock.UtcNow));
    }

    private async Task<SourceSetupSyncDto?> RunSyncAsync(
        string provider,
        string project,
        List<SourceSetupWarningDto> warnings,
        CancellationToken ct)
    {
        var job = await sources.EnqueueSyncJobAsync(
            new SourceSyncJobRequest(provider, SourceSyncMode.Incremental, [project]),
            ct);
        using (var wait = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            wait.CancelAfter(SyncWait);
            try
            {
                while (job.Status is SourceSyncStatus.Queued or SourceSyncStatus.Running)
                {
                    await Task.Delay(250, wait.Token);
                    job = await sources.GetSyncJobAsync(provider, job.JobId, wait.Token) ?? job;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
            }
        }

        var run = job.Projects.FirstOrDefault(x => string.Equals(x.ProjectPath, project, StringComparison.OrdinalIgnoreCase));
        if (run is null)
        {
            warnings.Add(new SourceSetupWarningDto(
                "sourceSyncNotStarted",
                $"Another sync is running without {project}, so {project} has not synced. Call SyncProject again once it finishes."));
            return null;
        }

        return new SourceSetupSyncDto(
            job.JobId,
            Wire(job.Status),
            run.IssuesImported,
            run.ReviewTargetsImported,
            run.ErrorMessage ?? job.ErrorMessage);
    }

    private async Task<SourceSetupProjectDto> ToDtoAsync(ServerConfigurationDto current, SourceSetupProject project, CancellationToken ct) =>
        new(
            project.Provider,
            project.Project,
            project.WorkspacePath,
            project.Enabled,
            project.AutoReview,
            CredentialGap(current, project.Provider, project.Project) == SourceCredentialGap.None ? "ready" : "missing",
            await LoadLastSyncAsync(project.Provider, project.Project, ct));

    private async Task<SourceSetupLastSyncDto?> LoadLastSyncAsync(string provider, string project, CancellationToken ct)
    {
        var path = project.ToLowerInvariant();
        if (provider == GitHubProvider)
        {
            var run = await db.GitHubSyncRepositoryRuns.AsNoTracking()
                .Where(x => x.Repository.ToLower() == path)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(ct);
            return run is null
                ? null
                : new SourceSetupLastSyncDto(Wire(run.Status), run.CompletedAt, run.IssuesImported, run.PullRequestsImported, run.ErrorMessage);
        }

        var gitLabRun = await db.GitLabSyncProjectRuns.AsNoTracking()
            .Where(x => x.ProjectPath.ToLower() == path)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return gitLabRun is null
            ? null
            : new SourceSetupLastSyncDto(Wire(gitLabRun.Status), gitLabRun.CompletedAt, gitLabRun.IssuesImported, gitLabRun.MergeRequestsImported, gitLabRun.ErrorMessage);
    }

    private void ThrowIfNotWritable()
    {
        if (!configuration.DeploymentWritable)
        {
            throw new OratorioApiException(StatusCodes.Status403Forbidden, "configWritesForbidden", WritesDisabledReason);
        }
    }

    private static string RequireSecret(string secret) =>
        secret.Length > 0 ? secret : throw Invalid("invalidSecret", "The secret must not be empty.");

    private static SourceSetupWarningDto GapWarning(SourceCredentialGap gap, string project) => gap switch
    {
        SourceCredentialGap.GitHubAppId => new("githubAppIdMissing", "The GitHub App id is not configured. Ask the user for it and call ConnectProject again with githubAppId."),
        SourceCredentialGap.GitHubPrivateKey => new("githubPrivateKeyMissing", "The GitHub App private key is not configured. Call RequestSourceCredential so the user can enter it."),
        SourceCredentialGap.GitHubInstallation => new("githubInstallationMissing", $"The GitHub App is not installed for {Owner(project)}, or its installation could not be found. Ask the user to install the App there, or call ConnectProject again with githubInstallationId."),
        _ => new("gitlabTokenMissing", "The GitLab access token for this project is not configured. Call RequestSourceCredential so the user can enter it.")
    };

    private static string Wire<T>(T value) where T : struct, Enum =>
        JsonSerializer.Serialize(value, EventJsonOptions).Trim('"');
}
