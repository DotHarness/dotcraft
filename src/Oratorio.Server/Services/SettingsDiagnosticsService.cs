using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Oratorio.Server.Api;
using Oratorio.Server.Data;
using Oratorio.Server.Domain;
using Oratorio.Server.DotCraft;
using Oratorio.Server.GitLab;
using Oratorio.Server.GitHub;

namespace Oratorio.Server.Services;

public sealed class SettingsDiagnosticsService(
    OratorioDbContext db,
    IClock clock,
    GitHubSourceService gitHubSourceService,
    DotCraftStatusService dotCraftStatusService,
    WorkspaceInventoryService workspaceInventoryService,
    IOptionsMonitor<GitHubOptions> gitHubOptions,
    IOptionsMonitor<GitLabOptions> gitLabOptions,
    IOptionsMonitor<DotCraftOptions> dotCraftOptions,
    IGitHubCredentialResolver gitHubCredentials,
    IGitLabCredentialResolver gitLabCredentials)
{
    private static readonly string[] RedactedFields =
    [
        "Oratorio:GitHub:PrivateKey",
        "Oratorio:GitHub:PrivateKeyPath",
        "Oratorio:GitHub:WebhookSecret",
        "Oratorio:GitLab:ProjectProfiles:*:Token",
        "Oratorio:GitLab:ProjectProfiles:*:WebhookSecret",
        "Oratorio:GitLab:ProjectProfiles:*:WebhookSigningToken",
        "Authorization",
        "X-Hub-Signature-256",
        "X-Gitlab-Token",
        "webhook-signature"
    ];

    private static readonly string[] UrlPartsRemoved =
    [
        "userinfo",
        "query",
        "fragment"
    ];

    public async Task<SettingsDiagnosticsResponse> GetDiagnosticsAsync(CancellationToken ct)
    {
        var gitHubStatus = await gitHubSourceService.GetStatusAsync(ct);
        var dotCraftStatus = await dotCraftStatusService.GetStatusAsync(ct);
        var dotCraft = dotCraftOptions.CurrentValue;
        var gitLab = gitLabOptions.CurrentValue;
        var gitLabCredentialStatus = gitLabCredentials.Resolve(gitLab);
        var gitLabLastSyncAt = await db.Items.AsNoTracking()
            .Where(x => x.Source == "gitlab" && x.LastSourceSyncAt != null)
            .MaxAsync(x => x.LastSourceSyncAt, ct);
        var gitLabSyncFailures = await db.GitLabSyncProjectRuns.AsNoTracking()
            .Where(x => x.Status == SourceSyncProjectStatus.Failed && x.ErrorMessage != null)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(5)
            .Select(x => $"{x.ProjectPath}: {x.ErrorMessage}")
            .ToArrayAsync(ct);
        var gitLabWriteFailures = await db.SourceWriteLogs.AsNoTracking()
            .Where(x => x.Source == "gitlab" && x.Status == SourceWriteStatus.Failed && x.ErrorMessage != null)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(5)
            .Select(x => $"{x.Kind}: {x.ErrorMessage}")
            .ToArrayAsync(ct);

        return new SettingsDiagnosticsResponse(
            clock.UtcNow,
            new SettingsServiceDiagnostics("Oratorio", "local-domain-api", workspaceInventoryService.GetWorkspaceMode()),
            BuildCapabilities(),
            new SettingsGitHubDiagnostics(
                true,
                gitHubStatus.Enabled,
                AuthenticationShape(gitHubCredentials.Resolve(gitHubOptions.CurrentValue)),
                gitHubStatus.WritesEnabled,
                gitHubStatus.WriteConfigured,
                gitHubStatus.WebhookSecretConfigured,
                SanitizeUrl(gitHubStatus.Endpoint),
                gitHubStatus.Repositories,
                gitHubStatus.LastSyncAt),
            new SettingsGitLabDiagnostics(
                true,
                gitLab.Enabled,
                GitLabAuthenticationShape(gitLabCredentialStatus),
                gitLab.WritesEnabled,
                gitLabCredentialStatus.HasToken,
                gitLabCredentialStatus.HasWebhookSecret,
                gitLabCredentialStatus.HasWebhookSigningToken,
                GitLabWebhookVerificationMode(gitLabCredentialStatus),
                SanitizeUrl(gitLab.EffectiveEndpoint),
                SanitizeUrl(gitLab.EffectiveApiBaseUrl),
                gitLab.Projects,
                gitLabLastSyncAt,
                gitLabSyncFailures,
                gitLabWriteFailures),
            new SettingsDotCraftDiagnostics(
                true,
                dotCraftStatus.Configured,
                dotCraftStatus.Connected,
                dotCraftStatus.Health,
                SanitizeUrl(dotCraftStatus.Endpoint),
                dotCraftStatus.EndpointSource,
                dotCraftStatus.WorkspacePath,
                dotCraftStatus.ApprovalPolicy,
                dotCraftStatus.RunTimeoutSeconds,
                dotCraft.HubDiscoveryEnabled,
                dotCraftStatus.Message),
            new SettingsRuntimeDiagnostics(
                dotCraft.ManagedWorktreesEnabled,
                dotCraftStatus.WorktreeRootPolicy,
                dotCraft.WorktreeBranchPrefix,
                dotCraftStatus.GlobalMaxActiveRuns,
                dotCraftStatus.MaxActiveRunsPerRepository,
                dotCraftStatus.MaxActiveRunsPerSource,
                dotCraft.EffectiveMaxRunAttempts,
                (int)dotCraft.RetryBackoff.TotalSeconds,
                (int)dotCraft.MaxRetryBackoff.TotalSeconds,
                (int)dotCraft.StallTimeout.TotalSeconds,
                (int)dotCraft.SucceededWorktreeRetention.TotalHours,
                (int)dotCraft.FailedWorktreeRetention.TotalHours,
                dotCraft.WorktreeCleanupEnabled,
                (int)dotCraft.WorktreeCleanupInterval.TotalSeconds),
            new SettingsRedactionDiagnostics(true, RedactedFields, UrlPartsRemoved));
    }

    private Dictionary<string, bool> BuildCapabilities() => new(StringComparer.Ordinal)
    {
        ["localDomainApi"] = true,
        ["gitHubReadSync"] = true,
        ["gitHubWebhooks"] = true,
        ["gitHubWrites"] = true,
        ["gitLabReadSync"] = true,
        ["gitLabWebhooks"] = true,
        ["gitLabWrites"] = true,
        ["dotCraftAppServerBridge"] = true,
        ["managedWorktrees"] = true,
        ["concurrencyLeases"] = true,
        ["multiWorkspaceRouting"] = true,
        ["settingsDiagnostics"] = true,
        ["serverConfigurationWrites"] = true
    };

    private static string AuthenticationShape(GitHubCredentialStatus status)
    {
        if (status.HasAppAuthentication)
        {
            return "githubApp";
        }

        return "none";
    }

    private static string GitLabWebhookVerificationMode(GitLabCredentialStatus status)
    {
        if (status.HasWebhookSigningToken)
        {
            return "signingToken";
        }

        if (status.HasWebhookSecret)
        {
            return "secretToken";
        }

        return status.AllowsUnsafeLocalWebhooks ? "localDevelopmentDisabled" : "none";
    }

    private static string GitLabAuthenticationShape(GitLabCredentialStatus status)
    {
        if (!status.HasToken)
        {
            return "none";
        }

        return "projectProfiles";
    }

    internal static string SanitizeUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            var builder = new UriBuilder(uri)
            {
                UserName = "",
                Password = "",
                Query = "",
                Fragment = ""
            };
            return builder.Uri.ToString();
        }

        var withoutFragment = value.Split('#', 2)[0];
        var withoutQuery = withoutFragment.Split('?', 2)[0];
        var schemeSeparator = withoutQuery.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator < 0)
        {
            return withoutQuery;
        }

        var prefix = withoutQuery[..(schemeSeparator + 3)];
        var rest = withoutQuery[(schemeSeparator + 3)..];
        var atIndex = rest.IndexOf('@', StringComparison.Ordinal);
        return atIndex >= 0 ? prefix + rest[(atIndex + 1)..] : withoutQuery;
    }
}
