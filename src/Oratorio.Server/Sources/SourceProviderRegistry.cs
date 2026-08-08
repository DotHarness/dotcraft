using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oratorio.Server.Api;
using Oratorio.Server.Data;
using Oratorio.Server.Domain;
using Oratorio.Server.GitLab;
using Oratorio.Server.GitHub;
using Oratorio.Server.Services;

namespace Oratorio.Server.Sources;

/// <summary>
/// Provides source-neutral status for one external source provider.
/// </summary>
public interface ISourceProvider
{
    string Provider { get; }
    Task<SourceProviderStatusDto> GetStatusAsync(CancellationToken ct);
}

/// <summary>
/// Resolves configured source providers for source-neutral API surfaces.
/// </summary>
public sealed class SourceProviderRegistry(IEnumerable<ISourceProvider> providers)
{
    private readonly IReadOnlyDictionary<string, ISourceProvider> _providers = providers
        .ToDictionary(x => x.Provider, StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<SourceProviderStatusDto>> GetStatusesAsync(CancellationToken ct)
    {
        var statuses = new List<SourceProviderStatusDto>();
        foreach (var provider in _providers.Values.OrderBy(x => x.Provider, StringComparer.OrdinalIgnoreCase))
        {
            statuses.Add(await provider.GetStatusAsync(ct));
        }

        return statuses;
    }

    public bool TryGetProvider(string provider, out ISourceProvider sourceProvider) =>
        _providers.TryGetValue(provider, out sourceProvider!);
}

/// <summary>
/// Projects the existing GitHub integration as a source-neutral provider.
/// </summary>
public sealed class GitHubSourceProvider(
    OratorioDbContext db,
    IOptionsMonitor<GitHubOptions> options,
    IGitHubCredentialResolver credentials) : ISourceProvider
{
    public string Provider => "github";

    public async Task<SourceProviderStatusDto> GetStatusAsync(CancellationToken ct)
    {
        var current = options.CurrentValue;
        var credentialStatus = credentials.Resolve(current);
        var endpoint = string.IsNullOrWhiteSpace(current.Endpoint) ? "https://api.github.com" : current.Endpoint;
        var projects = current.Repositories
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(repository => ToProject(repository, endpoint))
            .ToArray();
        var lastSyncAt = await db.Items.AsNoTracking()
            .Where(x => x.Source == "github" && x.LastSourceSyncAt != null)
            .MaxAsync(x => x.LastSourceSyncAt, ct);

        var read = projects.Length == 0
            ? new SourceProviderCapabilityDto(false, "unconfigured", "No GitHub repositories are configured.")
            : credentialStatus.HasAppAuthentication
                ? new SourceProviderCapabilityDto(true, "available", null)
                : new SourceProviderCapabilityDto(false, "credentialsMissing", "GitHub read sync requires GitHub App authentication.");
        var write = current.WritesEnabled
            ? credentialStatus.CanWrite
                ? new SourceProviderCapabilityDto(true, "available", null)
                : new SourceProviderCapabilityDto(false, "credentialsMissing", "GitHub writes require GitHub App authentication.")
            : new SourceProviderCapabilityDto(false, "disabled", "GitHub writes are disabled.");
        var webhook = credentialStatus.HasWebhookSecret
            ? new SourceProviderCapabilityDto(true, "available", null)
            : new SourceProviderCapabilityDto(false, "unconfigured", "GitHub webhook secret is not configured.");

        return new SourceProviderStatusDto(
            Provider,
            "GitHub",
            endpoint,
            projects.Length > 0,
            AuthenticationShape(credentialStatus),
            read,
            write,
            webhook,
            projects.Length,
            lastSyncAt,
            read.Available ? null : read.Reason,
            projects);
    }

    private static SourceProjectDto ToProject(string repository, string endpoint)
    {
        var key = SourceProjectKey.FromGitHubRepository(repository, endpoint);
        return new SourceProjectDto(
            key.Provider,
            key.Instance,
            key.ProjectPath,
            key.Key,
            repository);
    }

    private static string AuthenticationShape(GitHubCredentialStatus status)
    {
        if (status.HasAppAuthentication)
        {
            return "githubApp";
        }

        return "none";
    }
}

/// <summary>
/// Projects the GitLab read integration as a source-neutral provider.
/// </summary>
public sealed class GitLabSourceProvider(
    OratorioDbContext db,
    IOptionsMonitor<GitLabOptions> options,
    IGitLabCredentialResolver credentials) : ISourceProvider
{
    public string Provider => "gitlab";

    public async Task<SourceProviderStatusDto> GetStatusAsync(CancellationToken ct)
    {
        var current = options.CurrentValue;
        var endpoint = current.EffectiveEndpoint;
        var projects = current.Projects
            .Select(SourceProjectKey.NormalizeGitLabProjectPath)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(project => ToProject(project!, endpoint, current, credentials))
            .ToArray();
        var lastSyncAt = await db.Items.AsNoTracking()
            .Where(x => x.Source == "gitlab" && x.LastSourceSyncAt != null)
            .MaxAsync(x => x.LastSourceSyncAt, ct);

        var projectRead = projects.Select(x => x.ReadCapability).Where(x => x is not null).Cast<SourceProviderCapabilityDto>().ToArray();
        var projectWrite = projects.Select(x => x.WriteCapability).Where(x => x is not null).Cast<SourceProviderCapabilityDto>().ToArray();
        var projectWebhook = projects.Select(x => x.WebhookCapability).Where(x => x is not null).Cast<SourceProviderCapabilityDto>().ToArray();
        var read = !current.Enabled
            ? new SourceProviderCapabilityDto(false, "disabled", "GitLab read sync is disabled.")
            : projects.Length == 0
                ? new SourceProviderCapabilityDto(false, "unconfigured", "No GitLab projects are configured.")
                : AggregateProjectCapabilities(projectRead, "GitLab read sync requires a project profile token.");
        var write = !current.Enabled
            ? new SourceProviderCapabilityDto(false, "disabled", "GitLab provider is disabled.")
            : !current.WritesEnabled
                ? new SourceProviderCapabilityDto(false, "disabled", "GitLab writes are disabled.")
                : projects.Length == 0
                    ? new SourceProviderCapabilityDto(false, "unconfigured", "No GitLab projects are configured.")
                    : AggregateProjectCapabilities(projectWrite, "GitLab writes require a project profile token.");
        var webhook = projects.Length == 0
            ? new SourceProviderCapabilityDto(false, "unconfigured", "No GitLab projects are configured.")
            : AggregateProjectCapabilities(projectWebhook, "GitLab webhook verification is not configured.");

        return new SourceProviderStatusDto(
            Provider,
            "GitLab",
            endpoint,
            current.Enabled && projects.Length > 0,
            AuthenticationShape(projects),
            read,
            write,
            webhook,
            projects.Length,
            lastSyncAt,
            read.Available ? null : read.Reason,
            projects);
    }

    private static SourceProjectDto ToProject(
        string projectPath,
        string endpoint,
        GitLabOptions options,
        IGitLabCredentialResolver credentials)
    {
        var key = SourceProjectKey.FromGitLabProject(projectPath, endpoint);
        var project = new GitLabProjectRef(projectPath);
        var status = credentials.ResolveProject(options, project);
        var read = !options.Enabled
            ? new SourceProviderCapabilityDto(false, "disabled", "GitLab read sync is disabled.")
            : status.HasToken
                ? new SourceProviderCapabilityDto(true, "available", null)
                : new SourceProviderCapabilityDto(false, "credentialsMissing", "GitLab project profile token is missing.");
        var write = !options.Enabled
            ? new SourceProviderCapabilityDto(false, "disabled", "GitLab provider is disabled.")
            : !options.WritesEnabled
                ? new SourceProviderCapabilityDto(false, "disabled", "GitLab writes are disabled.")
                : status.HasToken
                    ? new SourceProviderCapabilityDto(true, "available", null)
                    : new SourceProviderCapabilityDto(false, "credentialsMissing", "GitLab project profile token is missing.");
        var webhook = status.HasWebhookSigningToken
            ? new SourceProviderCapabilityDto(true, "available", "Signing token verification is configured.")
            : status.HasWebhookSecret
                ? new SourceProviderCapabilityDto(true, "available", "Secret token verification is configured.")
                : status.AllowsUnsafeLocalWebhooks
                    ? new SourceProviderCapabilityDto(true, "localDevelopmentOnly", "Webhook verification is disabled for local development.")
                    : new SourceProviderCapabilityDto(false, "unconfigured", "GitLab project webhook verification is not configured.");
        return new SourceProjectDto(
            key.Provider,
            key.Instance,
            key.ProjectPath,
            key.Key,
            projectPath,
            read,
            write,
            webhook);
    }

    private static SourceProviderCapabilityDto AggregateProjectCapabilities(
        IReadOnlyList<SourceProviderCapabilityDto> capabilities,
        string missingReason)
    {
        var available = capabilities.Count(x => x.Available);
        if (available == capabilities.Count)
        {
            return new SourceProviderCapabilityDto(true, "available", null);
        }

        if (available > 0)
        {
            return new SourceProviderCapabilityDto(true, "partial", missingReason);
        }

        var unavailableStates = capabilities
            .Select(x => x.State)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unavailableStates.Length == 1)
        {
            return new SourceProviderCapabilityDto(false, unavailableStates[0], missingReason);
        }

        return new SourceProviderCapabilityDto(false, "credentialsMissing", missingReason);
    }

    private static string AuthenticationShape(IReadOnlyList<SourceProjectDto> projects)
    {
        var tokenKinds = projects
            .Where(project => project.ReadCapability?.Available == true)
            .Select(_ => "token")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (tokenKinds.Length == 0)
        {
            return "none";
        }

        return projects.Any(project => project.ReadCapability?.Available != true) ? "partial" : "token";
    }
}

/// <summary>
/// Implements source-neutral provider status and sync job orchestration.
/// </summary>
public sealed class SourceProviderService(
    SourceProviderRegistry registry,
    GitHubSyncCoordinator gitHubSync,
    IOptionsMonitor<GitHubOptions> gitHubOptions,
    GitLabSyncCoordinator gitLabSync,
    IOptionsMonitor<GitLabOptions> gitLabOptions,
    IClock clock)
{
    public async Task<SourcesResponse> GetSourcesAsync(CancellationToken ct) =>
        new(clock.UtcNow, await registry.GetStatusesAsync(ct));

    public async Task<SourceProviderStatusDto> GetStatusAsync(string provider, CancellationToken ct)
    {
        if (!registry.TryGetProvider(provider, out var sourceProvider))
        {
            throw UnknownProvider(provider);
        }

        return await sourceProvider.GetStatusAsync(ct);
    }

    public async Task<SourceSyncJobDto> EnqueueSyncJobAsync(SourceSyncJobRequest request, CancellationToken ct)
    {
        var provider = NormalizeProvider(request.Provider);
        if (provider == "github")
        {
            var repositories = ResolveGitHubRepositories(request.Projects);
            var job = await gitHubSync.EnqueueAsync(
                GitHubSyncTrigger.Manual,
                SourceSyncMapper.ToGitHubMode(request.Mode ?? SourceSyncMode.Incremental),
                repositories,
                ct);
            return SourceSyncMapper.FromGitHub(job, gitHubOptions.CurrentValue.Endpoint);
        }

        if (provider == "gitlab")
        {
            var projects = ResolveGitLabProjects(request.Projects);
            return await gitLabSync.EnqueueAsync(
                SourceSyncTrigger.Manual,
                request.Mode ?? SourceSyncMode.Incremental,
                projects,
                ct);
        }

        throw UnknownProvider(provider);
    }

    public async Task<SourceSyncJobDto?> GetActiveSyncJobAsync(string? provider, CancellationToken ct)
    {
        var normalized = NormalizeProvider(provider);
        if (normalized == "github")
        {
            var job = await gitHubSync.GetActiveJobAsync(ct);
            return job is null ? null : SourceSyncMapper.FromGitHub(job, gitHubOptions.CurrentValue.Endpoint);
        }

        if (normalized == "gitlab")
        {
            return await gitLabSync.GetActiveJobAsync(ct);
        }

        throw UnknownProvider(normalized);
    }

    public async Task<SourceSyncJobDto?> GetSyncJobAsync(string? provider, string jobId, CancellationToken ct)
    {
        var normalized = NormalizeProvider(provider);
        if (normalized == "github")
        {
            var job = await gitHubSync.GetJobAsync(jobId, ct);
            return job is null ? null : SourceSyncMapper.FromGitHub(job, gitHubOptions.CurrentValue.Endpoint);
        }

        if (normalized == "gitlab")
        {
            return await gitLabSync.GetJobAsync(jobId, ct);
        }

        throw UnknownProvider(normalized);
    }

    private IReadOnlyList<string>? ResolveGitHubRepositories(IReadOnlyList<string>? projects)
    {
        if (projects is null || projects.Count == 0)
        {
            return null;
        }

        var repositories = new List<string>();
        foreach (var project in projects)
        {
            if (!SourceProjectKey.TryNormalizeForProvider("github", project, gitHubOptions.CurrentValue.Endpoint, out var key))
            {
                throw OratorioApiException.Validation(
                    "projects must contain GitHub source project keys or owner/name repository names.",
                    new Dictionary<string, object?> { ["project"] = project });
            }

            repositories.Add(key.ProjectPath);
        }

        return repositories
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<string>? ResolveGitLabProjects(IReadOnlyList<string>? projects)
    {
        if (projects is null || projects.Count == 0)
        {
            return null;
        }

        var projectPaths = new List<string>();
        foreach (var project in projects)
        {
            if (!SourceProjectKey.TryNormalizeForProvider("gitlab", project, gitLabOptions.CurrentValue.Endpoint, out var key))
            {
                throw OratorioApiException.Validation(
                    "projects must contain GitLab source project keys or group/project paths.",
                    new Dictionary<string, object?> { ["project"] = project });
            }

            projectPaths.Add(key.ProjectPath);
        }

        return projectPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeProvider(string? provider) =>
        string.IsNullOrWhiteSpace(provider) ? "" : provider.Trim().ToLowerInvariant();

    private static OratorioApiException UnknownProvider(string provider) =>
        new(
            StatusCodes.Status404NotFound,
            "sourceProviderNotFound",
            $"Source provider '{provider}' is not configured.",
            new Dictionary<string, object?> { ["provider"] = provider });
}
