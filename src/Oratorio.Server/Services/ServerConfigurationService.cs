using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oratorio.Server.Api;
using Oratorio.Server.Data;
using Oratorio.Server.Domain;
using Oratorio.Server.DotCraft;
using Oratorio.Server.GitLab;
using Oratorio.Server.GitHub;
using Oratorio.Server.Sources;

namespace Oratorio.Server.Services;

public sealed class ServerConfigurationService(
    OratorioDbContext db,
    IClock clock,
    IWebHostEnvironment environment,
    IOptionsMonitor<GitHubOptions> gitHubOptions,
    IOptionsMonitor<GitLabOptions> gitLabOptions,
    IOptionsMonitor<DotCraftOptions> dotCraftOptions,
    IOptionsMonitor<OratorioAutomationOptions> automationOptions,
    IOptionsMonitor<SettingsWriteOptions> settingsOptions,
    IConfigurationSecretProtector secretProtector,
    IGitHubInstallationResolver gitHubInstallations)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly HashSet<string> RootProperties = new(StringComparer.Ordinal)
    {
        "baseRevision",
        "confirmImpact",
        "configuration",
        "detectGitHubInstallations"
    };

    private static readonly HashSet<string> ConfigurationProperties = new(StringComparer.Ordinal)
    {
        "gitHub",
        "gitLab",
        "dotCraft",
        "runtime",
        "automation"
    };

    private static readonly HashSet<string> GitHubProperties = new(StringComparer.Ordinal)
    {
        "endpoint",
        "appId",
        "installationProfiles",
        "repositories",
        "writesEnabled",
        "secrets"
    };

    private static readonly HashSet<string> GitHubSecretProperties = new(StringComparer.Ordinal)
    {
        "privateKey",
        "privateKeyPath",
        "webhookSecret"
    };

    private static readonly HashSet<string> GitLabProperties = new(StringComparer.Ordinal)
    {
        "enabled",
        "writesEnabled",
        "endpoint",
        "apiBaseUrl",
        "projects",
        "projectProfiles",
        "allowLocalDevelopmentUnsafeWebhooks",
    };

    private static readonly HashSet<string> GitLabSecretProperties = new(StringComparer.Ordinal)
    {
        "token",
        "webhookSecret",
        "webhookSigningToken"
    };

    private static readonly HashSet<string> GitLabProjectProfileProperties = new(StringComparer.Ordinal)
    {
        "instance",
        "projectPath",
        "tokenKind",
        "secrets"
    };

    private static readonly HashSet<string> SecretFieldProperties = new(StringComparer.Ordinal)
    {
        "configured",
        "mode",
        "value"
    };

    private static readonly HashSet<string> DotCraftProperties = new(StringComparer.Ordinal)
    {
        "repositoryWorkspaceRoutes",
        "appServerUrl",
        "hubDiscoveryEnabled",
        "hubLockPath",
        "approvalPolicy",
        "runTimeoutSeconds"
    };

    private static readonly HashSet<string> RepositoryWorkspaceRouteProperties = new(StringComparer.Ordinal)
    {
        "project",
        "workspacePath"
    };

    private static readonly HashSet<string> RuntimeProperties = new(StringComparer.Ordinal)
    {
        "managedWorktreesEnabled",
        "worktreeRoot",
        "worktreeBranchPrefix",
        "globalMaxActiveRuns",
        "maxActiveRunsPerRepository",
        "maxActiveRunsPerSource",
        "maxRunAttempts",
        "retryBackoffSeconds",
        "maxRetryBackoffSeconds",
        "stallTimeoutSeconds",
        "succeededWorktreeRetentionHours",
        "failedWorktreeRetentionHours",
        "worktreeCleanupEnabled",
        "worktreeCleanupIntervalSeconds"
    };

    private static readonly HashSet<string> AutomationProperties = new(StringComparer.Ordinal)
    {
        "autoDispatchEnabled",
        "autoDispatchAllowLabels",
        "autoDispatchBlockLabels",
        "deliveryPolicy",
        "maxImplementationTurns",
        "autoReviewRepositories",
        "autoReviewPublishEnabled",
        "autoReviewPublishRepositories",
        "autoFollowUpEnabled",
        "autoFollowUpRepositories",
        "maxFollowUpRounds"
    };

    private static readonly HashSet<string> BlockedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "token",
        "webhookSecret",
        "privateKey",
        "privateKeyPath",
        "autoStart",
        "command",
        "arguments"
    };

    private static readonly HashSet<string> ImpactfulFields = new(StringComparer.Ordinal)
    {
        "github.writesEnabled",
        "github.installationProfiles",
        "github.secrets.privateKey",
        "github.secrets.privateKeyPath",
        "github.secrets.webhookSecret",
        "gitlab.enabled",
        "gitlab.writesEnabled",
        "gitlab.endpoint",
        "gitlab.apiBaseUrl",
        "gitlab.projects",
        "gitlab.projectProfiles",
        "dotCraft.repositoryWorkspaceRoutes",
        "dotCraft.appServerUrl",
        "dotCraft.hubDiscoveryEnabled",
        "dotCraft.hubLockPath",
        "dotCraft.approvalPolicy",
        "automation.autoDispatchEnabled",
        "automation.autoDispatchAllowLabels",
        "automation.autoDispatchBlockLabels",
        "automation.deliveryPolicy",
        "automation.maxImplementationTurns",
        "automation.autoReviewRepositories",
        "automation.autoReviewPublishEnabled",
        "automation.autoReviewPublishRepositories",
        "automation.autoFollowUpEnabled",
        "automation.autoFollowUpRepositories",
        "automation.maxFollowUpRounds",
        "runtime.managedWorktreesEnabled",
        "runtime.worktreeRoot",
        "runtime.worktreeBranchPrefix",
        "runtime.globalMaxActiveRuns",
        "runtime.maxActiveRunsPerRepository",
        "runtime.maxActiveRunsPerSource"
    };

    public async Task<ServerConfigurationResponse> GetAsync(HttpContext context, CancellationToken ct)
    {
        var recent = await GetRecentChangesAsync(5, ct);
        return BuildResponse(context, recent);
    }

    public async Task<IReadOnlyList<ConfigurationChangeDto>> GetChangesAsync(int? limit, CancellationToken ct) =>
        await GetRecentChangesAsync(Math.Clamp(limit ?? 20, 1, 100), ct);

    public async Task<ServerConfigurationUpdateResponse> UpdateAsync(JsonElement requestElement, HttpContext context, CancellationToken ct)
    {
        ThrowIfNotWritable(context);
        ValidateRequestShape(requestElement);

        var request = requestElement.Deserialize<ServerConfigurationUpdateRequest>(JsonOptions)
            ?? throw ConfigurationValidationFailed("Configuration request is empty.");
        if (request.Configuration is null)
        {
            throw ConfigurationValidationFailed("Configuration payload is required.");
        }

        ValidateConfiguration(request.Configuration);
        var (nextConfiguration, gitHubInstallationWarnings) = request.DetectGitHubInstallations
            ? await DetectGitHubInstallationProfilesAsync(request.Configuration, ct)
            : (request.Configuration, Array.Empty<GitHubInstallationProfileDetectionWarningDto>());
        nextConfiguration = NormalizeConfigurationForSave(nextConfiguration);
        ValidateConfiguration(nextConfiguration);

        var overlayPath = ResolveOverlayPath();
        var currentRevision = ComputeRevision(overlayPath);
        if (!string.Equals(request.BaseRevision, currentRevision, StringComparison.Ordinal))
        {
            throw new OratorioApiException(
                StatusCodes.Status409Conflict,
                "configurationRevisionMismatch",
                "Server configuration changed after this Settings page loaded. Refresh and try again.",
                new Dictionary<string, object?>
                {
                    ["expectedRevision"] = currentRevision,
                    ["actualRevision"] = request.BaseRevision
                });
        }

        var before = BuildConfiguration();
        var changedFields = ChangedFields(before, nextConfiguration);
        var impactWarnings = BuildImpactWarnings(changedFields);
        if (impactWarnings.Count > 0 && !request.ConfirmImpact)
        {
            throw new OratorioApiException(
                StatusCodes.Status400BadRequest,
                "configurationConfirmationRequired",
                "This configuration change can affect new dispatches, source writes, or managed worktrees and must be confirmed.",
                new Dictionary<string, object?>
                {
                    ["impactWarnings"] = impactWarnings,
                    ["changedFields"] = changedFields
                });
        }

        await PersistOverlayAsync(nextConfiguration, overlayPath, ct);

        var newRevision = ComputeRevision(overlayPath);
        var savedConfiguration = BuildSavedConfiguration(nextConfiguration, before.GitHub.Secrets, before.GitLab.ProjectProfiles);
        const bool restartRequired = false;
        string? restartSignature = null;
        var now = clock.UtcNow;
        var change = new OratorioConfigurationChange
        {
            CreatedAt = now,
            Actor = "local-admin",
            RemoteAddress = context.Connection.RemoteIpAddress?.ToString(),
            BaseRevision = currentRevision,
            NewRevision = newRevision,
            ChangedFieldsJson = JsonSerializer.Serialize(changedFields, JsonOptions),
            ImpactWarningsJson = JsonSerializer.Serialize(impactWarnings, JsonOptions),
            BeforeJson = SerializeRedacted(before),
            AfterJson = SerializeRedacted(savedConfiguration)
        };
        db.ConfigurationChanges.Add(change);
        await db.SaveChangesAsync(ct);

        var recent = await GetRecentChangesAsync(5, ct);
        var response = BuildResponse(context, recent, savedConfiguration, newRevision, restartRequired, restartSignature);
        return new ServerConfigurationUpdateResponse(response, change.ChangeId, changedFields, gitHubInstallationWarnings, restartRequired, restartSignature);
    }

    private ServerConfigurationResponse BuildResponse(
        HttpContext context,
        IReadOnlyList<ConfigurationChangeDto> recentChanges,
        ServerConfigurationDto? configurationOverride = null,
        string? revisionOverride = null,
        bool restartRequired = false,
        string? restartSignature = null)
    {
        var overlayPath = ResolveOverlayPath();
        var (writable, disabledReason) = ResolveWritable(context);
        return new ServerConfigurationResponse(
            clock.UtcNow,
            writable,
            disabledReason,
            revisionOverride ?? ComputeRevision(overlayPath),
            overlayPath,
            configurationOverride ?? BuildConfiguration(),
            [],
            recentChanges,
            restartRequired,
            restartSignature);
    }

    private ServerConfigurationDto BuildConfiguration()
    {
        var gitHub = gitHubOptions.CurrentValue;
        var gitLab = gitLabOptions.CurrentValue;
        var dotCraft = dotCraftOptions.CurrentValue;
        var automation = automationOptions.CurrentValue;
        return new ServerConfigurationDto(
            new GitHubServerConfigurationDto(
                string.IsNullOrWhiteSpace(gitHub.Endpoint) ? "https://api.github.com" : gitHub.Endpoint,
                NullIfWhiteSpace(gitHub.AppId),
                BuildGitHubInstallationProfiles(gitHub),
                gitHub.Repositories,
                gitHub.WritesEnabled,
                new GitHubSecretConfigurationDto(
                    SecretStatus(gitHub.PrivateKey),
                    SecretStatus(gitHub.PrivateKeyPath),
                    SecretStatus(gitHub.WebhookSecret))),
            new GitLabServerConfigurationDto(
                gitLab.Enabled,
                gitLab.WritesEnabled,
                gitLab.EffectiveEndpoint,
                gitLab.EffectiveApiBaseUrl,
                gitLab.Projects,
                BuildGitLabProjectProfiles(gitLab),
                gitLab.AllowLocalDevelopmentUnsafeWebhooks),
            new DotCraftServerConfigurationDto(
                dotCraft.RepositoryWorkspaceRoutes
                    .Select(route => new DotCraftRepositoryWorkspaceRouteDto(route.Project, route.WorkspacePath))
                    .ToList(),
                dotCraft.AppServerUrl,
                dotCraft.HubDiscoveryEnabled,
                dotCraft.HubLockPath,
                string.IsNullOrWhiteSpace(dotCraft.ApprovalPolicy) ? "interrupt" : dotCraft.ApprovalPolicy,
                dotCraft.RunTimeoutSeconds),
            new RuntimeServerConfigurationDto(
                dotCraft.ManagedWorktreesEnabled,
                dotCraft.WorktreeRoot,
                dotCraft.WorktreeBranchPrefix,
                dotCraft.GlobalMaxActiveRuns,
                dotCraft.MaxActiveRunsPerRepository,
                dotCraft.MaxActiveRunsPerSource,
                dotCraft.MaxRunAttempts,
                dotCraft.RetryBackoffSeconds,
                dotCraft.MaxRetryBackoffSeconds,
                dotCraft.StallTimeoutSeconds,
                dotCraft.SucceededWorktreeRetentionHours,
                dotCraft.FailedWorktreeRetentionHours,
                dotCraft.WorktreeCleanupEnabled,
                dotCraft.WorktreeCleanupIntervalSeconds),
            new AutomationServerConfigurationDto(
                automation.AutoDispatchEnabled,
                automation.AutoDispatchAllowLabels,
                automation.AutoDispatchBlockLabels,
                automation.DeliveryPolicy,
                automation.MaxImplementationTurns,
                automation.AutoReviewRepositories,
                automation.AutoReviewPublishEnabled,
                automation.AutoReviewPublishRepositories,
                automation.AutoFollowUpEnabled,
                automation.AutoFollowUpRepositories,
                automation.MaxFollowUpRounds));
    }

    private static IReadOnlyList<GitHubInstallationProfileDto> BuildGitHubInstallationProfiles(GitHubOptions options)
        => NormalizeGitHubInstallationProfiles(
            (options.InstallationProfiles ?? [])
                .Select(profile => new GitHubInstallationProfileDto(
                    profile.Instance,
                    profile.Owner,
                    profile.InstallationId,
                    string.IsNullOrWhiteSpace(profile.Source) ? "manual" : profile.Source)));

    private async Task<(ServerConfigurationDto Configuration, IReadOnlyList<GitHubInstallationProfileDetectionWarningDto> Warnings)> DetectGitHubInstallationProfilesAsync(
        ServerConfigurationDto configuration,
        CancellationToken ct)
    {
        var profiles = NormalizeGitHubInstallationProfiles(configuration.GitHub.InstallationProfiles).ToList();
        var profileKeys = profiles
            .Select(ProfileKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var attempted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<GitHubInstallationProfileDetectionWarningDto>();
        var discoveryOptions = BuildEffectiveGitHubOptions(configuration.GitHub);
        var instance = SourceProjectKey.ResolveGitHubInstance(configuration.GitHub.Endpoint);

        foreach (var repositoryName in configuration.GitHub.Repositories)
        {
            if (!GitHubRepositoryRef.TryParse(repositoryName, out var repository))
            {
                continue;
            }

            var key = $"{instance}/{repository.Owner}";
            if (profileKeys.Contains(key) || !attempted.Add(key))
            {
                continue;
            }

            var result = await gitHubInstallations.DiscoverAsync(discoveryOptions, repository, ct);
            if (result.Succeeded && !string.IsNullOrWhiteSpace(result.InstallationId))
            {
                var profile = new GitHubInstallationProfileDto(
                    result.Instance,
                    result.Owner,
                    result.InstallationId,
                    "detected");
                profiles.Add(profile);
                profileKeys.Add(ProfileKey(profile));
                continue;
            }

            warnings.Add(new GitHubInstallationProfileDetectionWarningDto(
                result.Instance,
                result.Owner,
                result.Repository,
                result.Code ?? "githubInstallationDiscoveryFailed",
                result.Message ?? "GitHub installation profile could not be detected."));
        }

        var normalizedProfiles = NormalizeGitHubInstallationProfiles(profiles);
        return (
            configuration with { GitHub = configuration.GitHub with { InstallationProfiles = normalizedProfiles } },
            warnings);
    }

    private GitHubOptions BuildEffectiveGitHubOptions(GitHubServerConfigurationDto gitHub)
    {
        var current = gitHubOptions.CurrentValue;
        return new GitHubOptions
        {
            Endpoint = gitHub.Endpoint,
            AppId = gitHub.AppId,
            InstallationProfiles = NormalizeGitHubInstallationProfiles(gitHub.InstallationProfiles)
                .Select(profile => new GitHubInstallationProfileOptions
                {
                    Instance = profile.Instance,
                    Owner = profile.Owner,
                    InstallationId = profile.InstallationId,
                    Source = profile.Source
                })
                .ToArray(),
            PrivateKey = EffectiveSecret(gitHub.Secrets?.PrivateKey, current.PrivateKey),
            PrivateKeyPath = EffectiveSecret(gitHub.Secrets?.PrivateKeyPath, current.PrivateKeyPath),
            WebhookSecret = EffectiveSecret(gitHub.Secrets?.WebhookSecret, current.WebhookSecret),
            Repositories = gitHub.Repositories.ToArray(),
            WritesEnabled = gitHub.WritesEnabled
        };
    }

    private static string? EffectiveSecret(SecretConfigurationFieldDto? request, string? current) =>
        NormalizeSecretMode(request) switch
        {
            "replace" => request?.Value,
            "clear" => null,
            _ => current
        };

    private static ServerConfigurationDto NormalizeConfigurationForSave(ServerConfigurationDto configuration)
    {
        var gitLabInstance = SourceProjectKey.ResolveGitLabInstance(configuration.GitLab.Endpoint);
        var gitLabProjects = configuration.GitLab.Projects
            .Select(SourceProjectKey.NormalizeGitLabProjectPath)
            .Where(project => !string.IsNullOrWhiteSpace(project))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var configuredGitLabProjects = gitLabProjects.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return configuration with
        {
            GitLab = configuration.GitLab with
            {
                Projects = gitLabProjects,
                ProjectProfiles = NormalizeGitLabProjectProfiles(
                    configuration.GitLab.ProjectProfiles,
                    gitLabInstance,
                    configuredGitLabProjects)
            }
        };
    }

    private static IReadOnlyList<GitHubInstallationProfileDto> NormalizeGitHubInstallationProfiles(IEnumerable<GitHubInstallationProfileDto> profiles)
    {
        var normalized = new List<GitHubInstallationProfileDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            var instance = string.IsNullOrWhiteSpace(profile.Instance) ? "github.com" : profile.Instance.Trim().ToLowerInvariant();
            var owner = profile.Owner?.Trim() ?? "";
            var installationId = profile.InstallationId?.Trim() ?? "";
            var source = profile.Source == "detected" ? "detected" : "manual";
            var normalizedProfile = new GitHubInstallationProfileDto(instance, owner, installationId, source);
            if (!seen.Add(ProfileKey(normalizedProfile)))
            {
                continue;
            }

            normalized.Add(normalizedProfile);
        }

        return normalized
            .OrderBy(profile => profile.Instance, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.Owner, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ProfileKey(GitHubInstallationProfileDto profile) =>
        $"{profile.Instance}/{profile.Owner}";

    private static IReadOnlyList<GitLabProjectProfileDto> BuildGitLabProjectProfiles(GitLabOptions options)
    {
        var endpointInstance = SourceProjectKey.ResolveGitLabInstance(options.Endpoint);
        var configuredProjects = (options.Projects ?? [])
            .Select(SourceProjectKey.NormalizeGitLabProjectPath)
            .Where(project => !string.IsNullOrWhiteSpace(project))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return NormalizeGitLabProjectProfiles(
            (options.ProjectProfiles ?? [])
                .Select(profile => new GitLabProjectProfileDto(
                    string.IsNullOrWhiteSpace(profile.Instance) ? endpointInstance : profile.Instance,
                    profile.ProjectPath,
                    string.IsNullOrWhiteSpace(profile.TokenKind) ? "accessToken" : profile.TokenKind,
                    new GitLabSecretConfigurationDto(
                        SecretStatus(profile.Token),
                        SecretStatus(profile.WebhookSecret),
                        SecretStatus(profile.WebhookSigningToken)))),
            endpointInstance,
            configuredProjects);
    }

    private static IReadOnlyList<GitLabProjectProfileDto> NormalizeGitLabProjectProfiles(
        IEnumerable<GitLabProjectProfileDto> profiles,
        string endpointInstance,
        IReadOnlySet<string> configuredProjects)
    {
        var normalized = new List<GitLabProjectProfileDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            var projectPath = SourceProjectKey.NormalizeGitLabProjectPath(profile.ProjectPath);
            if (string.IsNullOrWhiteSpace(projectPath) ||
                configuredProjects.Count > 0 && !configuredProjects.Contains(projectPath))
            {
                continue;
            }

            var instance = string.IsNullOrWhiteSpace(profile.Instance)
                ? endpointInstance
                : profile.Instance.Trim().ToLowerInvariant();
            if (!string.Equals(instance, endpointInstance, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = GitLabProfileKey(instance, projectPath);
            if (!seen.Add(key))
            {
                continue;
            }

            normalized.Add(new GitLabProjectProfileDto(
                instance,
                projectPath,
                string.IsNullOrWhiteSpace(profile.TokenKind) ? "accessToken" : profile.TokenKind.Trim(),
                NormalizeGitLabProfileSecrets(profile.Secrets)));
        }

        return normalized
            .OrderBy(profile => profile.Instance, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.ProjectPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static GitLabSecretConfigurationDto NormalizeGitLabProfileSecrets(GitLabSecretConfigurationDto? secrets) =>
        new(
            NormalizeSecretField(secrets?.Token),
            NormalizeSecretField(secrets?.WebhookSecret),
            NormalizeSecretField(secrets?.WebhookSigningToken));

    private static SecretConfigurationFieldDto NormalizeSecretField(SecretConfigurationFieldDto? field)
    {
        var mode = NormalizeSecretMode(field);
        if (mode is not ("replace" or "clear"))
        {
            mode = "unchanged";
        }

        return new SecretConfigurationFieldDto(
            field?.Configured ?? false,
            mode,
            mode == "replace" ? field?.Value : null);
    }

    private static string GitLabProfileKey(string instance, string projectPath) =>
        $"{instance.Trim().ToLowerInvariant()}/{projectPath.Trim()}";

    private async Task<IReadOnlyList<ConfigurationChangeDto>> GetRecentChangesAsync(int limit, CancellationToken ct)
    {
        var changes = await db.ConfigurationChanges.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
        return changes.Select(ToDto).ToArray();
    }

    private static ConfigurationChangeDto ToDto(OratorioConfigurationChange change) =>
        new(
            change.ChangeId,
            change.CreatedAt,
            change.Actor,
            change.RemoteAddress,
            change.BaseRevision,
            change.NewRevision,
            DeserializeStringList(change.ChangedFieldsJson),
            DeserializeStringList(change.ImpactWarningsJson),
            change.BeforeJson,
            change.AfterJson);

    private static IReadOnlyList<string> DeserializeStringList(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<string>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void ThrowIfNotWritable(HttpContext context)
    {
        var (writable, reason) = ResolveWritable(context);
        if (writable)
        {
            return;
        }

        throw new OratorioApiException(
            StatusCodes.Status403Forbidden,
            "configWritesForbidden",
            reason ?? "Server configuration writes require a loopback request.");
    }

    private (bool Writable, string? DisabledReason) ResolveWritable(HttpContext context)
    {
        if (!settingsOptions.CurrentValue.Writable)
        {
            return (false, "Server configuration writes are disabled for this deployment.");
        }

        if (!IsLoopback(context.Connection.RemoteIpAddress))
        {
            return (false, "Server configuration writes require a loopback request.");
        }

        return (true, null);
    }

    private bool IsLoopback(IPAddress? address) =>
        address is null && environment.IsEnvironment("Testing") ||
        address is not null && IPAddress.IsLoopback(address);

    private string ResolveOverlayPath()
    {
        var configured = settingsOptions.CurrentValue.ConfigPath;

        return string.IsNullOrWhiteSpace(configured)
            ? OratorioStatePaths.ResolveDefaultConfigurationOverlayPath(
                environment.ContentRootPath,
                Environment.GetEnvironmentVariable("DOTCRAFT_MANAGED_SERVICE_STATE_ROOT"))
            : Path.GetFullPath(configured);
    }

    private static string ComputeRevision(string path)
    {
        if (!File.Exists(path))
        {
            return HashString("{}");
        }

        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashString(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private async Task PersistOverlayAsync(ServerConfigurationDto next, string overlayPath, CancellationToken ct)
    {
        var existingGitHubSecrets = ReadExistingSecretNodes(overlayPath, "GitHub", ["PrivateKey", "PrivateKeyPath", "WebhookSecret"]);
        var existingGitLabProfileSecrets = ReadExistingGitLabProfileSecretNodes(overlayPath);
        var existingDotCraftSecrets = ReadExistingSecretNodes(overlayPath, "DotCraft", ["AppServerToken"]);
        var gitHubNode = JsonSerializer.SerializeToNode(new
        {
            next.GitHub.Endpoint,
            next.GitHub.AppId,
            InstallationProfiles = next.GitHub.InstallationProfiles,
            Repositories = next.GitHub.Repositories,
            next.GitHub.WritesEnabled
        }, JsonOptions)!.AsObject();
        ApplySecret(gitHubNode, "PrivateKey", next.GitHub.Secrets?.PrivateKey, existingGitHubSecrets);
        ApplySecret(gitHubNode, "PrivateKeyPath", next.GitHub.Secrets?.PrivateKeyPath, existingGitHubSecrets);
        ApplySecret(gitHubNode, "WebhookSecret", next.GitHub.Secrets?.WebhookSecret, existingGitHubSecrets);

        var gitLabNode = JsonSerializer.SerializeToNode(new
        {
            next.GitLab.Enabled,
            next.GitLab.WritesEnabled,
            next.GitLab.Endpoint,
            next.GitLab.ApiBaseUrl,
            Projects = next.GitLab.Projects,
            ProjectProfiles = BuildGitLabProjectProfileOverlay(next.GitLab.ProjectProfiles, existingGitLabProfileSecrets),
            next.GitLab.AllowLocalDevelopmentUnsafeWebhooks
        }, JsonOptions)!.AsObject();

        var dotCraftNode = JsonSerializer.SerializeToNode(new
        {
            RepositoryWorkspaceRoutes = next.DotCraft.RepositoryWorkspaceRoutes,
            next.DotCraft.AppServerUrl,
            next.DotCraft.HubDiscoveryEnabled,
            next.DotCraft.HubLockPath,
            next.DotCraft.ApprovalPolicy,
            next.DotCraft.RunTimeoutSeconds,
            next.Runtime.ManagedWorktreesEnabled,
            next.Runtime.WorktreeRoot,
            next.Runtime.WorktreeBranchPrefix,
            next.Runtime.GlobalMaxActiveRuns,
            next.Runtime.MaxActiveRunsPerRepository,
            next.Runtime.MaxActiveRunsPerSource,
            next.Runtime.MaxRunAttempts,
            next.Runtime.RetryBackoffSeconds,
            next.Runtime.MaxRetryBackoffSeconds,
            next.Runtime.StallTimeoutSeconds,
            next.Runtime.SucceededWorktreeRetentionHours,
            next.Runtime.FailedWorktreeRetentionHours,
            next.Runtime.WorktreeCleanupEnabled,
            next.Runtime.WorktreeCleanupIntervalSeconds
        }, JsonOptions)!.AsObject();
        // AppServerToken is managed out-of-band (overlay file or environment), not through the Settings DTO.
        // Preserve any existing value so a Settings save never drops the AppServer credential.
        if (existingDotCraftSecrets.TryGetValue("AppServerToken", out var appServerTokenNode) && appServerTokenNode is not null)
        {
            dotCraftNode["AppServerToken"] = appServerTokenNode.DeepClone();
        }

        var root = new JsonObject
        {
            ["Oratorio"] = new JsonObject
            {
                ["GitHub"] = gitHubNode,
                ["GitLab"] = gitLabNode,
                ["DotCraft"] = dotCraftNode,
                ["Automation"] = JsonSerializer.SerializeToNode(new
                {
                    next.Automation.AutoDispatchEnabled,
                    AutoDispatchAllowLabels = next.Automation.AutoDispatchAllowLabels,
                    AutoDispatchBlockLabels = next.Automation.AutoDispatchBlockLabels,
                    next.Automation.DeliveryPolicy,
                    next.Automation.MaxImplementationTurns,
                    AutoReviewRepositories = next.Automation.AutoReviewRepositories,
                    next.Automation.AutoReviewPublishEnabled,
                    AutoReviewPublishRepositories = next.Automation.AutoReviewPublishRepositories,
                    next.Automation.AutoFollowUpEnabled,
                    AutoFollowUpRepositories = next.Automation.AutoFollowUpRepositories,
                    next.Automation.MaxFollowUpRounds
                }, JsonOptions)
            }
        };

        var directory = Path.GetDirectoryName(overlayPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = overlayPath + "." + Guid.NewGuid().ToString("n") + ".tmp";
        await File.WriteAllTextAsync(tempPath, root.ToJsonString(JsonOptions), Encoding.UTF8, ct);
        File.Move(tempPath, overlayPath, overwrite: true);
    }

    private JsonArray BuildGitLabProjectProfileOverlay(
        IReadOnlyList<GitLabProjectProfileDto> profiles,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonNode?>> existingSecrets)
    {
        var array = new JsonArray();
        foreach (var profile in profiles.OrderBy(x => x.Instance, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.ProjectPath, StringComparer.OrdinalIgnoreCase))
        {
            var node = JsonSerializer.SerializeToNode(new
            {
                profile.Instance,
                profile.ProjectPath,
                profile.TokenKind
            }, JsonOptions)!.AsObject();
            var existing = existingSecrets.TryGetValue(GitLabProfileKey(profile.Instance, profile.ProjectPath), out var values)
                ? values
                : new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
            ApplySecret(node, "Token", profile.Secrets?.Token, existing);
            ApplySecret(node, "WebhookSecret", profile.Secrets?.WebhookSecret, existing);
            ApplySecret(node, "WebhookSigningToken", profile.Secrets?.WebhookSigningToken, existing);
            array.Add(node);
        }

        return array;
    }

    private void ApplySecret(
        JsonObject gitHubNode,
        string propertyName,
        SecretConfigurationFieldDto? request,
        IReadOnlyDictionary<string, JsonNode?> existingSecrets)
    {
        var mode = NormalizeSecretMode(request);
        if (mode == "replace")
        {
            var value = request?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                gitHubNode[propertyName] = secretProtector.Protect(value);
            }

            return;
        }

        if (mode == "clear")
        {
            gitHubNode[propertyName] = null;
            return;
        }

        if (existingSecrets.TryGetValue(propertyName, out var existing))
        {
            gitHubNode[propertyName] = existing?.DeepClone();
        }
    }

    private static IReadOnlyDictionary<string, JsonNode?> ReadExistingSecretNodes(
        string overlayPath,
        string sectionName,
        IReadOnlyList<string> propertyNames)
    {
        var result = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(overlayPath))
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(overlayPath));
            if (!TryGetProperty(document.RootElement, "Oratorio", out var oratorio) ||
                !TryGetProperty(oratorio, sectionName, out var section))
            {
                return result;
            }

            foreach (var propertyName in propertyNames)
            {
                if (!TryGetProperty(section, propertyName, out var secret))
                {
                    continue;
                }

                result[propertyName] = secret.ValueKind == JsonValueKind.Null
                    ? null
                    : JsonValue.Create(secret.GetString());
            }
        }
        catch (JsonException)
        {
            return result;
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonNode?>> ReadExistingGitLabProfileSecretNodes(string overlayPath)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, JsonNode?>>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(overlayPath))
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(overlayPath));
            if (!TryGetProperty(document.RootElement, "Oratorio", out var oratorio) ||
                !TryGetProperty(oratorio, "GitLab", out var gitLab) ||
                !TryGetProperty(gitLab, "ProjectProfiles", out var profiles) ||
                profiles.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var profile in profiles.EnumerateArray())
            {
                if (profile.ValueKind != JsonValueKind.Object ||
                    !TryGetProperty(profile, "Instance", out var instanceElement) ||
                    !TryGetProperty(profile, "ProjectPath", out var projectElement))
                {
                    continue;
                }

                var instance = instanceElement.GetString() ?? "";
                var projectPath = SourceProjectKey.NormalizeGitLabProjectPath(projectElement.GetString());
                if (string.IsNullOrWhiteSpace(instance) || string.IsNullOrWhiteSpace(projectPath))
                {
                    continue;
                }

                var secrets = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
                foreach (var propertyName in new[] { "Token", "WebhookSecret", "WebhookSigningToken" })
                {
                    if (!TryGetProperty(profile, propertyName, out var secret))
                    {
                        continue;
                    }

                    secrets[propertyName] = secret.ValueKind == JsonValueKind.Null
                        ? null
                        : JsonValue.Create(secret.GetString());
                }

                result[GitLabProfileKey(instance, projectPath)] = secrets;
            }
        }
        catch (JsonException)
        {
            return result;
        }

        return result;
    }

    private static void ValidateRequestShape(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw ConfigurationValidationFailed("Configuration request must be a JSON object.");
        }

        ValidateObject(root, RootProperties, "request");
        if (root.TryGetProperty("configuration", out var config))
        {
            ValidateObject(config, ConfigurationProperties, "configuration");
            if (config.TryGetProperty("gitHub", out var gitHub))
            {
                ValidateObject(gitHub, GitHubProperties, "configuration.gitHub");
                if (gitHub.TryGetProperty("secrets", out var secrets))
                {
                    ValidateSecretsObject(secrets, "configuration.gitHub.secrets", GitHubSecretProperties);
                }
            }

            if (config.TryGetProperty("gitLab", out var gitLab))
            {
                ValidateObject(gitLab, GitLabProperties, "configuration.gitLab");
                if (gitLab.TryGetProperty("projectProfiles", out var profiles))
                {
                    if (profiles.ValueKind != JsonValueKind.Array)
                    {
                        throw ConfigurationValidationFailed("configuration.gitLab.projectProfiles must be a JSON array.");
                    }

                    var index = 0;
                    foreach (var profile in profiles.EnumerateArray())
                    {
                        var path = $"configuration.gitLab.projectProfiles.{index}";
                        ValidateObject(profile, GitLabProjectProfileProperties, path);
                        if (profile.TryGetProperty("secrets", out var secrets))
                        {
                            ValidateSecretsObject(secrets, $"{path}.secrets", GitLabSecretProperties);
                        }

                        index++;
                    }
                }
            }

            if (config.TryGetProperty("dotCraft", out var dotCraft))
            {
                ValidateObject(dotCraft, DotCraftProperties, "configuration.dotCraft");
                if (dotCraft.TryGetProperty("repositoryWorkspaceRoutes", out var routes))
                {
                    if (routes.ValueKind != JsonValueKind.Array)
                    {
                        throw ConfigurationValidationFailed("configuration.dotCraft.repositoryWorkspaceRoutes must be a JSON array.");
                    }

                    var index = 0;
                    foreach (var route in routes.EnumerateArray())
                    {
                        ValidateObject(route, RepositoryWorkspaceRouteProperties, $"configuration.dotCraft.repositoryWorkspaceRoutes.{index}");
                        index++;
                    }
                }
            }

            if (config.TryGetProperty("automation", out var automation))
            {
                ValidateObject(automation, AutomationProperties, "configuration.automation");
            }

            if (config.TryGetProperty("runtime", out var runtime))
            {
                ValidateObject(runtime, RuntimeProperties, "configuration.runtime");
            }
        }
    }

    private static void ValidateObject(JsonElement element, HashSet<string> allowed, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw ConfigurationValidationFailed($"{path} must be a JSON object.");
        }

        foreach (var property in element.EnumerateObject())
        {
            if (BlockedProperties.Contains(property.Name))
            {
                throw ConfigurationValidationFailed($"Field '{property.Name}' is not writable from Settings.");
            }

            if (!allowed.Contains(property.Name))
            {
                throw ConfigurationValidationFailed($"Unknown configuration field '{path}.{property.Name}'.");
            }
        }
    }

    private static void ValidateSecretsObject(JsonElement element, string path, HashSet<string> allowedSecrets)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw ConfigurationValidationFailed($"{path} must be a JSON object.");
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!allowedSecrets.Contains(property.Name))
            {
                throw ConfigurationValidationFailed($"Unknown configuration field '{path}.{property.Name}'.");
            }

            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                throw ConfigurationValidationFailed($"{path}.{property.Name} must be a JSON object.");
            }

            foreach (var field in property.Value.EnumerateObject())
            {
                if (!SecretFieldProperties.Contains(field.Name))
                {
                    throw ConfigurationValidationFailed($"Unknown configuration field '{path}.{property.Name}.{field.Name}'.");
                }
            }
        }
    }

    private static void ValidateConfiguration(ServerConfigurationDto configuration)
    {
        var errors = new Dictionary<string, object?>();
        if (!IsAbsoluteUrl(configuration.GitHub.Endpoint, ["http", "https"]))
        {
            errors["github.endpoint"] = "GitHub endpoint must be an absolute http or https URL.";
        }

        if (!IsAbsoluteUrl(configuration.GitLab.Endpoint, ["http", "https"]))
        {
            errors["gitlab.endpoint"] = "GitLab endpoint must be an absolute http or https URL.";
        }

        if (!IsAbsoluteUrl(configuration.GitLab.ApiBaseUrl, ["http", "https"]))
        {
            errors["gitlab.apiBaseUrl"] = "GitLab API base URL must be an absolute http or https URL.";
        }

        var repositories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var repository in configuration.GitHub.Repositories)
        {
            if (string.IsNullOrWhiteSpace(repository) ||
                !repository.Contains('/', StringComparison.Ordinal) ||
                repository.Contains('\\', StringComparison.Ordinal) ||
                repository.Contains("..", StringComparison.Ordinal))
            {
                errors[$"github.repositories.{repository}"] = "Repository must be in owner/name form.";
                continue;
            }

            if (!repositories.Add(repository.Trim()))
            {
                errors[$"github.repositories.{repository}"] = "Repository entries must be unique.";
            }
        }

        var gitHubProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in configuration.GitHub.InstallationProfiles)
        {
            var key = $"{profile.Instance}/{profile.Owner}";
            if (string.IsNullOrWhiteSpace(profile.Instance) ||
                profile.Instance.Contains('/', StringComparison.Ordinal) ||
                profile.Instance.Contains('\\', StringComparison.Ordinal) ||
                profile.Instance.Contains("..", StringComparison.Ordinal))
            {
                errors[$"github.installationProfiles.{key}.instance"] = "GitHub installation profile instance must be a non-empty host label.";
            }

            if (string.IsNullOrWhiteSpace(profile.Owner) ||
                profile.Owner.Contains('/', StringComparison.Ordinal) ||
                profile.Owner.Contains('\\', StringComparison.Ordinal) ||
                profile.Owner.Contains("..", StringComparison.Ordinal))
            {
                errors[$"github.installationProfiles.{key}.owner"] = "GitHub installation profile owner must be a non-empty account name.";
            }

            if (string.IsNullOrWhiteSpace(profile.InstallationId))
            {
                errors[$"github.installationProfiles.{key}.installationId"] = "GitHub installation profile installation id is required.";
            }

            if (profile.Source is not ("detected" or "manual"))
            {
                errors[$"github.installationProfiles.{key}.source"] = "GitHub installation profile source must be detected or manual.";
            }

            if (!gitHubProfiles.Add(key))
            {
                errors[$"github.installationProfiles.{key}"] = "GitHub installation profiles must be unique per instance and owner.";
            }
        }

        var gitLabProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in configuration.GitLab.Projects)
        {
            var normalized = SourceProjectKey.NormalizeGitLabProjectPath(project);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                errors[$"gitlab.projects.{project}"] = "Project must be a GitLab group/project path and may include subgroups.";
                continue;
            }

            if (!gitLabProjects.Add(normalized))
            {
                errors[$"gitlab.projects.{project}"] = "Project entries must be unique.";
            }
        }

        var gitLabProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in configuration.GitLab.ProjectProfiles)
        {
            var projectPath = SourceProjectKey.NormalizeGitLabProjectPath(profile.ProjectPath);
            var key = $"{profile.Instance}/{profile.ProjectPath}";
            if (string.IsNullOrWhiteSpace(profile.Instance) ||
                profile.Instance.Contains('/', StringComparison.Ordinal) ||
                profile.Instance.Contains('\\', StringComparison.Ordinal) ||
                profile.Instance.Contains("..", StringComparison.Ordinal))
            {
                errors[$"gitlab.projectProfiles.{key}.instance"] = "GitLab project profile instance must be a non-empty host label.";
            }

            if (string.IsNullOrWhiteSpace(projectPath))
            {
                errors[$"gitlab.projectProfiles.{key}.projectPath"] = "GitLab project profile project path must be a group/project path and may include subgroups.";
            }

            if (string.IsNullOrWhiteSpace(profile.TokenKind) ||
                profile.TokenKind.Contains("..", StringComparison.Ordinal) ||
                profile.TokenKind.Contains('\\', StringComparison.Ordinal))
            {
                errors[$"gitlab.projectProfiles.{key}.tokenKind"] = "GitLab project profile token kind must be a non-empty label.";
            }

            if (!string.IsNullOrWhiteSpace(projectPath) && !gitLabProfiles.Add(GitLabProfileKey(profile.Instance, projectPath)))
            {
                errors[$"gitlab.projectProfiles.{key}"] = "GitLab project profiles must be unique per instance and project.";
            }

            ValidateSecretUpdate(profile.Secrets?.Token, $"gitlab.projectProfiles.{key}.secrets.token", errors);
            ValidateSecretUpdate(profile.Secrets?.WebhookSecret, $"gitlab.projectProfiles.{key}.secrets.webhookSecret", errors);
            ValidateSecretUpdate(profile.Secrets?.WebhookSigningToken, $"gitlab.projectProfiles.{key}.secrets.webhookSigningToken", errors);
        }

        foreach (var route in configuration.DotCraft.RepositoryWorkspaceRoutes)
        {
            if (!SourceProjectKey.TryParse(route.Project, out _))
            {
                errors[$"dotCraft.repositoryWorkspaceRoutes.{route.Project}"] = "Project must be a canonical source project key.";
            }

            ValidateExistingAbsoluteDirectory(route.WorkspacePath, $"dotCraft.repositoryWorkspaceRoutes.{route.Project}", errors);
        }

        if (!IsAbsoluteUrl(configuration.DotCraft.AppServerUrl, ["ws", "wss"]))
        {
            errors["dotCraft.appServerUrl"] = "AppServer URL must be an absolute ws or wss URL.";
        }

        if (!string.IsNullOrWhiteSpace(configuration.DotCraft.HubLockPath) && !Path.IsPathFullyQualified(configuration.DotCraft.HubLockPath))
        {
            errors["dotCraft.hubLockPath"] = "Hub lock path must be absolute when set.";
        }

        if (configuration.DotCraft.ApprovalPolicy is not ("default" or "autoApprove" or "interrupt"))
        {
            errors["dotCraft.approvalPolicy"] = "Approval policy must be default, autoApprove, or interrupt.";
        }

        ValidateRange(configuration.DotCraft.RunTimeoutSeconds, 30, 7200, "dotCraft.runTimeoutSeconds", errors);
        if (!string.IsNullOrWhiteSpace(configuration.Runtime.WorktreeRoot) && !Path.IsPathFullyQualified(configuration.Runtime.WorktreeRoot))
        {
            errors["runtime.worktreeRoot"] = "Worktree root must be absolute when set.";
        }

        if (string.IsNullOrWhiteSpace(configuration.Runtime.WorktreeBranchPrefix) ||
            configuration.Runtime.WorktreeBranchPrefix.Contains("..", StringComparison.Ordinal) ||
            configuration.Runtime.WorktreeBranchPrefix.Contains('\\', StringComparison.Ordinal))
        {
            errors["runtime.worktreeBranchPrefix"] = "Branch prefix must be a non-empty slash-separated branch namespace.";
        }

        ValidateRange(configuration.Runtime.GlobalMaxActiveRuns, 1, 50, "runtime.globalMaxActiveRuns", errors);
        ValidateRange(configuration.Runtime.MaxActiveRunsPerRepository, 1, 50, "runtime.maxActiveRunsPerRepository", errors);
        ValidateRange(configuration.Runtime.MaxActiveRunsPerSource, 1, 50, "runtime.maxActiveRunsPerSource", errors);
        ValidateRange(configuration.Runtime.MaxRunAttempts, 1, 10, "runtime.maxRunAttempts", errors);
        ValidateRange(configuration.Runtime.RetryBackoffSeconds, 1, 300, "runtime.retryBackoffSeconds", errors);
        ValidateRange(configuration.Runtime.MaxRetryBackoffSeconds, 1, 1800, "runtime.maxRetryBackoffSeconds", errors);
        ValidateRange(configuration.Runtime.StallTimeoutSeconds, 5, 7200, "runtime.stallTimeoutSeconds", errors);
        ValidateRange(configuration.Runtime.SucceededWorktreeRetentionHours, 0, 24 * 30, "runtime.succeededWorktreeRetentionHours", errors);
        ValidateRange(configuration.Runtime.FailedWorktreeRetentionHours, 1, 24 * 60, "runtime.failedWorktreeRetentionHours", errors);
        ValidateRange(configuration.Runtime.WorktreeCleanupIntervalSeconds, 5, 3600, "runtime.worktreeCleanupIntervalSeconds", errors);
        ValidateRange(configuration.Automation.MaxImplementationTurns, 1, 10, "automation.maxImplementationTurns", errors);
        if (configuration.Automation.DeliveryPolicy is not (DeliveryPolicy.ManualDelivery or DeliveryPolicy.AutoPr))
        {
            errors["automation.deliveryPolicy"] = "Delivery policy must be manualDelivery or autoPr.";
        }

        ValidateLabels(configuration.Automation.AutoDispatchAllowLabels, "automation.autoDispatchAllowLabels", errors);
        ValidateLabels(configuration.Automation.AutoDispatchBlockLabels, "automation.autoDispatchBlockLabels", errors);
        ValidateRepositories(configuration.Automation.AutoReviewRepositories, "automation.autoReviewRepositories", errors);
        ValidateRepositories(configuration.Automation.AutoReviewPublishRepositories, "automation.autoReviewPublishRepositories", errors);
        ValidateRepositories(configuration.Automation.AutoFollowUpRepositories, "automation.autoFollowUpRepositories", errors);
        ValidateRange(configuration.Automation.MaxFollowUpRounds, 1, 20, "automation.maxFollowUpRounds", errors);
        ValidateSecretUpdate(configuration.GitHub.Secrets?.PrivateKey, "github.secrets.privateKey", errors);
        ValidateSecretUpdate(configuration.GitHub.Secrets?.PrivateKeyPath, "github.secrets.privateKeyPath", errors);
        ValidateSecretUpdate(configuration.GitHub.Secrets?.WebhookSecret, "github.secrets.webhookSecret", errors);
        if (errors.Count > 0)
        {
            throw ConfigurationValidationFailed("Server configuration validation failed.", errors);
        }
    }

    private static void ValidateLabels(IReadOnlyList<string> labels, string field, Dictionary<string, object?> errors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in labels)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                errors[field] = "Labels must be non-empty strings.";
                return;
            }

            if (!seen.Add(label.Trim()))
            {
                errors[field] = "Labels must be unique.";
                return;
            }
        }
    }

    private static void ValidateRepositories(IReadOnlyList<string> repositories, string field, Dictionary<string, object?> errors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var repository in repositories)
        {
            if (!IsValidReviewProjectKey(repository))
            {
                errors[$"{field}.{repository}"] = "Entry must be an owner/name GitHub repository or canonical source project key.";
                continue;
            }

            if (!seen.Add(repository.Trim()))
            {
                errors[$"{field}.{repository}"] = "Project entries must be unique.";
            }
        }
    }

    private static bool IsValidReviewProjectKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Contains('\\', StringComparison.Ordinal) ||
            value.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        if (SourceProjectKey.TryParse(value, out var key))
        {
            return !string.IsNullOrWhiteSpace(key.ProjectPath) &&
                key.ProjectPath.Contains('/', StringComparison.Ordinal);
        }

        return SourceProjectKey.NormalizeGitHubRepository(value) is not null;
    }

    private static void ValidateSecretUpdate(SecretConfigurationFieldDto? field, string name, Dictionary<string, object?> errors)
    {
        var mode = NormalizeSecretMode(field);
        if (mode is not ("unchanged" or "replace" or "clear"))
        {
            errors[name] = "Secret mode must be unchanged, replace, or clear.";
            return;
        }

        if (mode == "replace" && string.IsNullOrWhiteSpace(field?.Value))
        {
            errors[name] = "Replacement secret value must be non-empty.";
        }
    }

    private static bool IsAbsoluteUrl(string value, IReadOnlyList<string> schemes) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        schemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase) &&
        string.IsNullOrWhiteSpace(uri.UserInfo);

    private static void ValidateExistingAbsoluteDirectory(string value, string field, Dictionary<string, object?> errors)
    {
        if (!Path.IsPathFullyQualified(value))
        {
            errors[field] = "Path must be absolute.";
            return;
        }

        if (!Directory.Exists(value))
        {
            errors[field] = "Directory must exist.";
        }
    }

    private static void ValidateRange(int value, int min, int max, string field, Dictionary<string, object?> errors)
    {
        if (value < min || value > max)
        {
            errors[field] = $"Value must be between {min} and {max}.";
        }
    }

    private static IReadOnlyList<string> ChangedFields(ServerConfigurationDto before, ServerConfigurationDto after)
    {
        var beforeMap = Flatten(before);
        var afterMap = Flatten(after);
        var changed = beforeMap.Keys
            .Concat(afterMap.Keys)
            .Distinct(StringComparer.Ordinal)
            .Where(key => !string.Equals(beforeMap.GetValueOrDefault(key), afterMap.GetValueOrDefault(key), StringComparison.Ordinal))
            .ToList();

        AddSecretChange(changed, "github.secrets.privateKey", after.GitHub.Secrets?.PrivateKey);
        AddSecretChange(changed, "github.secrets.privateKeyPath", after.GitHub.Secrets?.PrivateKeyPath);
        AddSecretChange(changed, "github.secrets.webhookSecret", after.GitHub.Secrets?.WebhookSecret);
        AddGitLabProfileSecretChanges(changed, after.GitLab.ProjectProfiles);
        return changed
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddSecretChange(List<string> changed, string field, SecretConfigurationFieldDto? secret)
    {
        if (NormalizeSecretMode(secret) != "unchanged")
        {
            changed.Add(field);
        }
    }

    private static void AddGitLabProfileSecretChanges(List<string> changed, IReadOnlyList<GitLabProjectProfileDto> profiles)
    {
        foreach (var profile in profiles)
        {
            var key = GitLabProfileKey(profile.Instance, profile.ProjectPath);
            AddSecretChange(changed, $"gitlab.projectProfiles.{key}.secrets.token", profile.Secrets?.Token);
            AddSecretChange(changed, $"gitlab.projectProfiles.{key}.secrets.webhookSecret", profile.Secrets?.WebhookSecret);
            AddSecretChange(changed, $"gitlab.projectProfiles.{key}.secrets.webhookSigningToken", profile.Secrets?.WebhookSigningToken);
        }
    }

    private static Dictionary<string, string?> Flatten(ServerConfigurationDto value) => new(StringComparer.Ordinal)
    {
        ["github.endpoint"] = value.GitHub.Endpoint,
        ["github.appId"] = value.GitHub.AppId,
        ["github.installationProfiles"] = string.Join("\n", value.GitHub.InstallationProfiles
            .OrderBy(x => x.Instance, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Owner, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{x.Instance}/{x.Owner}={x.InstallationId}:{x.Source}")),
        ["github.repositories"] = string.Join("\n", value.GitHub.Repositories.Order(StringComparer.OrdinalIgnoreCase)),
        ["github.writesEnabled"] = value.GitHub.WritesEnabled.ToString(),
        ["gitlab.enabled"] = value.GitLab.Enabled.ToString(),
        ["gitlab.writesEnabled"] = value.GitLab.WritesEnabled.ToString(),
        ["gitlab.endpoint"] = value.GitLab.Endpoint,
        ["gitlab.apiBaseUrl"] = value.GitLab.ApiBaseUrl,
        ["gitlab.projects"] = string.Join("\n", value.GitLab.Projects.Order(StringComparer.OrdinalIgnoreCase)),
        ["gitlab.projectProfiles"] = string.Join("\n", value.GitLab.ProjectProfiles
            .OrderBy(x => x.Instance, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ProjectPath, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{x.Instance}/{x.ProjectPath}:{x.TokenKind}:{x.Secrets?.Token.Configured}:{x.Secrets?.WebhookSecret.Configured}:{x.Secrets?.WebhookSigningToken.Configured}")),
        ["gitlab.allowLocalDevelopmentUnsafeWebhooks"] = value.GitLab.AllowLocalDevelopmentUnsafeWebhooks.ToString(),
        ["dotCraft.repositoryWorkspaceRoutes"] = string.Join("\n", value.DotCraft.RepositoryWorkspaceRoutes.OrderBy(x => x.Project, StringComparer.OrdinalIgnoreCase).Select(x => $"{x.Project}={x.WorkspacePath}")),
        ["dotCraft.appServerUrl"] = value.DotCraft.AppServerUrl,
        ["dotCraft.hubDiscoveryEnabled"] = value.DotCraft.HubDiscoveryEnabled.ToString(),
        ["dotCraft.hubLockPath"] = value.DotCraft.HubLockPath,
        ["dotCraft.approvalPolicy"] = value.DotCraft.ApprovalPolicy,
        ["dotCraft.runTimeoutSeconds"] = value.DotCraft.RunTimeoutSeconds.ToString(),
        ["automation.autoDispatchEnabled"] = value.Automation.AutoDispatchEnabled.ToString(),
        ["automation.autoDispatchAllowLabels"] = string.Join("\n", value.Automation.AutoDispatchAllowLabels.Order(StringComparer.OrdinalIgnoreCase)),
        ["automation.autoDispatchBlockLabels"] = string.Join("\n", value.Automation.AutoDispatchBlockLabels.Order(StringComparer.OrdinalIgnoreCase)),
        ["automation.deliveryPolicy"] = value.Automation.DeliveryPolicy.ToString(),
        ["automation.maxImplementationTurns"] = value.Automation.MaxImplementationTurns.ToString(),
        ["automation.autoReviewRepositories"] = string.Join("\n", value.Automation.AutoReviewRepositories.Order(StringComparer.OrdinalIgnoreCase)),
        ["automation.autoReviewPublishEnabled"] = value.Automation.AutoReviewPublishEnabled.ToString(),
        ["automation.autoReviewPublishRepositories"] = string.Join("\n", value.Automation.AutoReviewPublishRepositories.Order(StringComparer.OrdinalIgnoreCase)),
        ["automation.autoFollowUpEnabled"] = value.Automation.AutoFollowUpEnabled.ToString(),
        ["automation.autoFollowUpRepositories"] = string.Join("\n", value.Automation.AutoFollowUpRepositories.Order(StringComparer.OrdinalIgnoreCase)),
        ["automation.maxFollowUpRounds"] = value.Automation.MaxFollowUpRounds.ToString(),
        ["runtime.managedWorktreesEnabled"] = value.Runtime.ManagedWorktreesEnabled.ToString(),
        ["runtime.worktreeRoot"] = value.Runtime.WorktreeRoot,
        ["runtime.worktreeBranchPrefix"] = value.Runtime.WorktreeBranchPrefix,
        ["runtime.globalMaxActiveRuns"] = value.Runtime.GlobalMaxActiveRuns.ToString(),
        ["runtime.maxActiveRunsPerRepository"] = value.Runtime.MaxActiveRunsPerRepository.ToString(),
        ["runtime.maxActiveRunsPerSource"] = value.Runtime.MaxActiveRunsPerSource.ToString(),
        ["runtime.maxRunAttempts"] = value.Runtime.MaxRunAttempts.ToString(),
        ["runtime.retryBackoffSeconds"] = value.Runtime.RetryBackoffSeconds.ToString(),
        ["runtime.maxRetryBackoffSeconds"] = value.Runtime.MaxRetryBackoffSeconds.ToString(),
        ["runtime.stallTimeoutSeconds"] = value.Runtime.StallTimeoutSeconds.ToString(),
        ["runtime.succeededWorktreeRetentionHours"] = value.Runtime.SucceededWorktreeRetentionHours.ToString(),
        ["runtime.failedWorktreeRetentionHours"] = value.Runtime.FailedWorktreeRetentionHours.ToString(),
        ["runtime.worktreeCleanupEnabled"] = value.Runtime.WorktreeCleanupEnabled.ToString(),
        ["runtime.worktreeCleanupIntervalSeconds"] = value.Runtime.WorktreeCleanupIntervalSeconds.ToString()
    };

    private static IReadOnlyList<string> BuildImpactWarnings(IReadOnlyList<string> changedFields) =>
        changedFields
            .Where(field => ImpactfulFields.Contains(field) || field.StartsWith("gitlab.projectProfiles.", StringComparison.Ordinal))
            .Select(field => field switch
            {
                "github.writesEnabled" => "GitHub write enablement changes affect future source-write attempts.",
                "github.installationProfiles" => "GitHub installation profile changes affect future GitHub source sync, source writes, branch pushes, and PR delivery.",
                "github.secrets.privateKey" or "github.secrets.privateKeyPath" or "github.secrets.webhookSecret" => "Credential changes affect future GitHub source sync, webhooks, and source-write attempts.",
                "gitlab.enabled" or "gitlab.writesEnabled" or "gitlab.endpoint" or "gitlab.apiBaseUrl" or "gitlab.projects" or "gitlab.projectProfiles" => "GitLab configuration changes affect future GitLab source sync, webhooks, and source-write attempts.",
                var dynamicField when dynamicField.StartsWith("gitlab.projectProfiles.", StringComparison.Ordinal) => "GitLab project profile changes affect future GitLab source sync, webhooks, and source-write attempts.",
                "dotCraft.repositoryWorkspaceRoutes" => "Workspace routing changes affect new AppServer dispatches.",
                "dotCraft.appServerUrl" or "dotCraft.hubDiscoveryEnabled" or "dotCraft.hubLockPath" => "DotCraft endpoint discovery changes affect new AppServer dispatches and status checks.",
                "dotCraft.approvalPolicy" => "Approval policy changes affect new DotCraft threads.",
                "automation.autoDispatchEnabled" or "automation.autoDispatchAllowLabels" or "automation.autoDispatchBlockLabels" or "automation.deliveryPolicy" or "automation.maxImplementationTurns" => "Automation policy changes affect future implementation dispatches and handoffs.",
                "automation.autoReviewRepositories" => "Automatic review changes affect future PR/MR review dispatches.",
                "automation.autoReviewPublishEnabled" or "automation.autoReviewPublishRepositories" => "Draft auto-publish changes affect future PR/MR review draft source writes.",
                "automation.autoFollowUpEnabled" or "automation.autoFollowUpRepositories" or "automation.maxFollowUpRounds" => "Implementation follow-up changes affect future automatic re-implementation of items whose generated pull request gains review feedback.",
                "runtime.managedWorktreesEnabled" or "runtime.worktreeRoot" or "runtime.worktreeBranchPrefix" => "Managed worktree changes affect new worktree preparation and cleanup boundaries.",
                "runtime.globalMaxActiveRuns" or "runtime.maxActiveRunsPerRepository" or "runtime.maxActiveRunsPerSource" => "Concurrency changes affect new run scheduling.",
                _ => "This change affects future runtime behavior."
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static ServerConfigurationDto BuildSavedConfiguration(
        ServerConfigurationDto request,
        GitHubSecretConfigurationDto? previousGitHubSecrets,
        IReadOnlyList<GitLabProjectProfileDto> previousGitLabProfiles) =>
        request with
        {
            GitHub = request.GitHub with
            {
                Secrets = new GitHubSecretConfigurationDto(
                    SavedSecret(request.GitHub.Secrets?.PrivateKey, previousGitHubSecrets?.PrivateKey),
                    SavedSecret(request.GitHub.Secrets?.PrivateKeyPath, previousGitHubSecrets?.PrivateKeyPath),
                    SavedSecret(request.GitHub.Secrets?.WebhookSecret, previousGitHubSecrets?.WebhookSecret))
            },
            GitLab = request.GitLab with
            {
                ProjectProfiles = BuildSavedGitLabProjectProfiles(request.GitLab.ProjectProfiles, previousGitLabProfiles)
            }
        };

    private static IReadOnlyList<GitLabProjectProfileDto> BuildSavedGitLabProjectProfiles(
        IReadOnlyList<GitLabProjectProfileDto> requestProfiles,
        IReadOnlyList<GitLabProjectProfileDto> previousProfiles)
    {
        var previousByKey = previousProfiles.ToDictionary(
            profile => GitLabProfileKey(profile.Instance, profile.ProjectPath),
            StringComparer.OrdinalIgnoreCase);
        return requestProfiles
            .Select(profile =>
            {
                previousByKey.TryGetValue(GitLabProfileKey(profile.Instance, profile.ProjectPath), out var previous);
                return profile with
                {
                    Secrets = new GitLabSecretConfigurationDto(
                        SavedSecret(profile.Secrets?.Token, previous?.Secrets?.Token),
                        SavedSecret(profile.Secrets?.WebhookSecret, previous?.Secrets?.WebhookSecret),
                        SavedSecret(profile.Secrets?.WebhookSigningToken, previous?.Secrets?.WebhookSigningToken))
                };
            })
            .ToArray();
    }

    private static SecretConfigurationFieldDto SavedSecret(SecretConfigurationFieldDto? request, SecretConfigurationFieldDto? previous)
    {
        var configured = NormalizeSecretMode(request) switch
        {
            "replace" => true,
            "clear" => false,
            _ => previous?.Configured ?? request?.Configured ?? false
        };
        return new SecretConfigurationFieldDto(configured);
    }

    private static SecretConfigurationFieldDto SecretStatus(string? value) =>
        new(!string.IsNullOrWhiteSpace(value));

    private static string NormalizeSecretMode(SecretConfigurationFieldDto? field) =>
        string.IsNullOrWhiteSpace(field?.Mode) ? "unchanged" : field.Mode.Trim();

    private static string BuildRestartSignature(string revision, IReadOnlyList<string> changedFields) =>
        HashString(revision + "\n" + string.Join("\n", changedFields.Order(StringComparer.Ordinal)));

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string SerializeRedacted(ServerConfigurationDto value) =>
        JsonSerializer.Serialize(BuildSavedConfiguration(value, value.GitHub.Secrets, value.GitLab.ProjectProfiles), JsonOptions);

    private static OratorioApiException ConfigurationValidationFailed(string message, IReadOnlyDictionary<string, object?>? details = null) =>
        new(StatusCodes.Status400BadRequest, "configurationValidationFailed", message, details);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
