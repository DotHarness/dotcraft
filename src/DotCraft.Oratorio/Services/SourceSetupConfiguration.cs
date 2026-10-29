using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Sources;

namespace DotCraft.Oratorio.Services;

public enum SourceCredentialGap
{
    None,
    GitHubAppId,
    GitHubPrivateKey,
    GitHubInstallation,
    GitLabToken
}

public sealed record SourceSetupProject(string Provider, string Project, string WorkspacePath, bool Enabled, bool AutoReview);

public static class SourceSetupConfiguration
{
    public const string GitHubProvider = "github";
    public const string GitLabProvider = "gitlab";

    public static string NormalizeProvider(string? provider)
    {
        var value = provider?.Trim().ToLowerInvariant();
        return value is GitHubProvider or GitLabProvider
            ? value
            : throw Invalid("invalidSourceProvider", "provider must be github or gitlab.");
    }

    public static string NormalizeProject(string provider, string? project)
    {
        var value = (project ?? "").Trim().Trim('/');
        var segments = value.Split('/');
        var valid = value.Length > 0 &&
            !value.Contains('\\') &&
            !value.Contains("..", StringComparison.Ordinal) &&
            !value.Any(char.IsWhiteSpace) &&
            segments.Length >= 2 &&
            segments.All(segment => segment.Length > 0 && segment != ".");
        if (!valid || provider == GitHubProvider && segments.Length != 2)
        {
            throw Invalid(
                "invalidProjectKey",
                provider == GitHubProvider
                    ? "project must be a GitHub repository in owner/repo form."
                    : "project must be a GitLab project path such as group/project or group/subgroup/project.");
        }

        return value;
    }

    public static string Endpoint(ServerConfigurationDto configuration, string provider) =>
        provider == GitHubProvider ? configuration.GitHub.Endpoint : configuration.GitLab.Endpoint;

    public static string Instance(string provider, string endpoint) =>
        provider == GitHubProvider
            ? SourceProjectKey.ResolveGitHubInstance(endpoint)
            : SourceProjectKey.ResolveGitLabInstance(endpoint);

    public static string CanonicalKey(string provider, string endpoint, string project) =>
        $"{provider}:{Instance(provider, endpoint)}/{project.ToLowerInvariant()}";

    public static string Owner(string project) => project.Split('/')[0];

    public static IReadOnlyList<SourceSetupProject> ListProjects(ServerConfigurationDto configuration)
    {
        var projects = new Dictionary<string, List<string>>
        {
            [GitHubProvider] = [.. configuration.GitHub.Repositories],
            [GitLabProvider] = [.. configuration.GitLab.Projects, .. configuration.GitLab.ProjectProfiles.Select(profile => profile.ProjectPath)]
        };
        foreach (var route in configuration.DotCraft.RepositoryWorkspaceRoutes)
        {
            if (SourceProjectKey.TryParse(route.Project, out var key) && projects.TryGetValue(key.Provider, out var list))
            {
                list.Add(key.ProjectPath);
            }
        }

        return projects
            .SelectMany(entry => entry.Value
                .Select(project => project.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(project => BuildProject(configuration, entry.Key, project)))
            .ToArray();
    }

    public static SourceSetupProject? FindProject(ServerConfigurationDto configuration, string provider, string project) =>
        ListProjects(configuration).FirstOrDefault(item =>
            item.Provider == provider && string.Equals(item.Project, project, StringComparison.OrdinalIgnoreCase));

    public static bool GitHubAppKeyConfigured(ServerConfigurationDto configuration) =>
        configuration.GitHub.Secrets?.PrivateKey.Configured == true ||
        configuration.GitHub.Secrets?.PrivateKeyPath.Configured == true;

    public static GitHubInstallationProfileDto? GitHubInstallation(ServerConfigurationDto configuration, string owner)
    {
        var instance = Instance(GitHubProvider, configuration.GitHub.Endpoint);
        return configuration.GitHub.InstallationProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Instance, instance, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(profile.Owner, owner, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(profile.InstallationId));
    }

    public static GitLabProjectProfileDto? GitLabProfile(ServerConfigurationDto configuration, string project)
    {
        var instance = Instance(GitLabProvider, configuration.GitLab.Endpoint);
        return configuration.GitLab.ProjectProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Instance, instance, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(profile.ProjectPath, project, StringComparison.OrdinalIgnoreCase));
    }

    public static SourceCredentialGap CredentialGap(ServerConfigurationDto configuration, string provider, string project)
    {
        if (provider == GitLabProvider)
        {
            return GitLabProfile(configuration, project)?.Secrets?.Token.Configured == true
                ? SourceCredentialGap.None
                : SourceCredentialGap.GitLabToken;
        }

        if (string.IsNullOrWhiteSpace(configuration.GitHub.AppId)) return SourceCredentialGap.GitHubAppId;
        if (!GitHubAppKeyConfigured(configuration)) return SourceCredentialGap.GitHubPrivateKey;
        return GitHubInstallation(configuration, Owner(project)) is null
            ? SourceCredentialGap.GitHubInstallation
            : SourceCredentialGap.None;
    }

    public static ServerConfigurationDto Connect(ServerConfigurationDto configuration, ConnectProjectRequest request)
    {
        var provider = request.Provider;
        var project = request.Project;
        var next = provider == GitHubProvider ? ConnectGitHub(configuration, request) : ConnectGitLab(configuration, request);
        var endpoint = Endpoint(next, provider);
        var canonical = CanonicalKey(provider, endpoint, project);

        var routes = next.DotCraft.RepositoryWorkspaceRoutes.ToList();
        var routeIndex = routes.FindIndex(route => SameProject(route.Project, provider, project));
        if (routeIndex >= 0) routes[routeIndex] = routes[routeIndex] with { WorkspacePath = request.WorkspacePath };
        else routes.Add(new DotCraftRepositoryWorkspaceRouteDto(canonical, request.WorkspacePath));

        var existing = FindProject(configuration, provider, project);
        var reviewed = next.Automation.AutoReviewRepositories
            .Where(entry => !ReviewEntryMatches(entry, provider, endpoint, canonical))
            .ToList();
        if (request.AutoReview ?? existing?.AutoReview ?? true) reviewed.Add(canonical);

        return next with
        {
            DotCraft = next.DotCraft with { RepositoryWorkspaceRoutes = routes },
            Automation = next.Automation with { AutoReviewRepositories = reviewed }
        };
    }

    public static ServerConfigurationDto ReplaceGitHubPrivateKey(ServerConfigurationDto configuration, string privateKey)
    {
        var secrets = configuration.GitHub.Secrets ?? UnconfiguredGitHubSecrets();
        return configuration with
        {
            GitHub = configuration.GitHub with
            {
                Secrets = secrets with { PrivateKey = new SecretConfigurationFieldDto(true, "replace", privateKey) }
            }
        };
    }

    public static ServerConfigurationDto ReplaceGitLabToken(
        ServerConfigurationDto configuration,
        string project,
        string token,
        string? tokenKind)
    {
        var profile = GitLabProfile(configuration, project)
            ?? throw ProjectNotConnected(GitLabProvider, project);
        var secrets = profile.Secrets ?? UnconfiguredGitLabSecrets();
        var replaced = profile with
        {
            TokenKind = tokenKind ?? profile.TokenKind,
            Secrets = secrets with { Token = new SecretConfigurationFieldDto(true, "replace", token) }
        };
        return configuration with
        {
            GitLab = configuration.GitLab with
            {
                ProjectProfiles = configuration.GitLab.ProjectProfiles.Select(item => item == profile ? replaced : item).ToArray()
            }
        };
    }

    public static OratorioApiException ProjectNotConnected(string provider, string project) =>
        new(
            StatusCodes.Status404NotFound,
            "sourceProjectNotConnected",
            $"{project} is not connected to Oratorio as a {provider} project. Call ConnectProject first.",
            new Dictionary<string, object?> { ["provider"] = provider, ["project"] = project });

    public static OratorioApiException Invalid(string code, string message) =>
        new(StatusCodes.Status400BadRequest, code, message);

    private static ServerConfigurationDto ConnectGitHub(ServerConfigurationDto configuration, ConnectProjectRequest request)
    {
        var gitHub = configuration.GitHub with
        {
            Endpoint = request.Endpoint ?? configuration.GitHub.Endpoint,
            WritesEnabled = request.AllowWrites ?? configuration.GitHub.WritesEnabled
        };
        if (request.GitHubAppId is not null)
        {
            gitHub = gitHub with { AppId = request.GitHubAppId };
        }

        if (request.GitHubPrivateKeyPath is not null)
        {
            var secrets = gitHub.Secrets ?? UnconfiguredGitHubSecrets();
            gitHub = gitHub with
            {
                // An inline key takes precedence over a key file, so a new key file replaces it.
                Secrets = secrets with
                {
                    PrivateKeyPath = new SecretConfigurationFieldDto(true, "replace", request.GitHubPrivateKeyPath),
                    PrivateKey = secrets.PrivateKey.Configured ? new SecretConfigurationFieldDto(false, "clear") : secrets.PrivateKey
                }
            };
        }

        if (request.GitHubInstallationId is not null)
        {
            var instance = Instance(GitHubProvider, gitHub.Endpoint);
            var owner = Owner(request.Project);
            gitHub = gitHub with
            {
                InstallationProfiles = gitHub.InstallationProfiles
                    .Where(profile => !(string.Equals(profile.Instance, instance, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(profile.Owner, owner, StringComparison.OrdinalIgnoreCase)))
                    .Append(new GitHubInstallationProfileDto(instance, owner, request.GitHubInstallationId, "manual"))
                    .ToArray()
            };
        }

        if (!gitHub.Repositories.Contains(request.Project, StringComparer.OrdinalIgnoreCase))
        {
            gitHub = gitHub with { Repositories = [.. gitHub.Repositories, request.Project] };
        }

        return configuration with { GitHub = gitHub };
    }

    private static ServerConfigurationDto ConnectGitLab(ServerConfigurationDto configuration, ConnectProjectRequest request)
    {
        var endpoint = request.Endpoint ?? configuration.GitLab.Endpoint;
        var gitLab = configuration.GitLab with
        {
            Enabled = true,
            Endpoint = endpoint,
            ApiBaseUrl = $"{endpoint.TrimEnd('/')}/api/v4",
            WritesEnabled = request.AllowWrites ?? configuration.GitLab.WritesEnabled
        };
        if (!gitLab.Projects.Contains(request.Project, StringComparer.OrdinalIgnoreCase))
        {
            gitLab = gitLab with { Projects = [.. gitLab.Projects, request.Project] };
        }

        var next = configuration with { GitLab = gitLab };
        if (GitLabProfile(next, request.Project) is null)
        {
            next = next with
            {
                GitLab = gitLab with
                {
                    ProjectProfiles =
                    [
                        .. gitLab.ProjectProfiles,
                        new GitLabProjectProfileDto(Instance(GitLabProvider, endpoint), request.Project, "accessToken", UnconfiguredGitLabSecrets())
                    ]
                }
            };
        }

        return next;
    }

    private static SourceSetupProject BuildProject(ServerConfigurationDto configuration, string provider, string project)
    {
        var endpoint = Endpoint(configuration, provider);
        var canonical = CanonicalKey(provider, endpoint, project);
        var enabledList = provider == GitHubProvider ? configuration.GitHub.Repositories : configuration.GitLab.Projects;
        var route = configuration.DotCraft.RepositoryWorkspaceRoutes.FirstOrDefault(item => SameProject(item.Project, provider, project));
        return new SourceSetupProject(
            provider,
            project,
            route?.WorkspacePath ?? "",
            enabledList.Contains(project, StringComparer.OrdinalIgnoreCase),
            configuration.Automation.AutoReviewRepositories.Any(entry => ReviewEntryMatches(entry, provider, endpoint, canonical)));
    }

    private static bool SameProject(string routeProject, string provider, string project) =>
        SourceProjectKey.TryParse(routeProject, out var key) &&
        key.Provider == provider &&
        string.Equals(key.ProjectPath, project, StringComparison.OrdinalIgnoreCase);

    private static bool ReviewEntryMatches(string entry, string provider, string endpoint, string canonical) =>
        SourceProjectKey.TryNormalizeForProvider(provider, entry, endpoint, out var key) &&
        string.Equals(key.Key, canonical, StringComparison.OrdinalIgnoreCase);

    private static GitHubSecretConfigurationDto UnconfiguredGitHubSecrets() =>
        new(new SecretConfigurationFieldDto(false), new SecretConfigurationFieldDto(false), new SecretConfigurationFieldDto(false));

    private static GitLabSecretConfigurationDto UnconfiguredGitLabSecrets() =>
        new(new SecretConfigurationFieldDto(false), new SecretConfigurationFieldDto(false), new SecretConfigurationFieldDto(false));
}
