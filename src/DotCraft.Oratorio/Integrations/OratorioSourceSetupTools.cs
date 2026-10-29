using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotCraft.Protocol.AppServer;
using DotCraft.Sdk;
using DotCraft.Sdk.DynamicTools;
using DotCraft.Oratorio.Services;
using static DotCraft.Oratorio.Services.SourceSetupConfiguration;

namespace DotCraft.Oratorio.Integrations;

internal sealed partial class OratorioDynamicToolFacade
{
    private static readonly JsonSerializerOptions SetupJsonOptions = new(DynamicToolJson.Options)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    [DynamicTool(
        OratorioDynamicToolCatalog.GetSourceSetupName,
        "Read Oratorio's source setup: the GitHub and GitLab connections, each connected repository with its workspace, automatic review, credential state and last sync, and the workspaces already in use. Call it before connecting or changing a repository.",
        Order = 100)]
    public Task<DynamicToolCallResult> GetSourceSetupAsync(
        OratorioToolInvocationContext context,
        CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            var setup = await Setup(context).GetSetupAsync(ct);
            var missing = setup.Projects.Count(x => x.Credentials == "missing");
            var summary = $"{setup.Projects.Count} connected project(s), {missing} missing credentials.";
            return SetupSuccess(setup.Writable ? summary : $"{summary} {setup.DisabledReason}", setup);
        });

    [DynamicTool(
        OratorioDynamicToolCatalog.ConnectProjectName,
        "Connect a GitHub repository or GitLab project to Oratorio, or update an existing connection in place. Saves the workspace, automatic review, write access and sync schedule, then runs a first sync when credentials are ready. Never pass a token or key content; when credentials are missing, call RequestSourceCredential.",
        Order = 110)]
    public Task<DynamicToolCallResult> ConnectProjectAsync(
        ConnectProjectToolArgs args,
        OratorioToolInvocationContext context,
        CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            var result = await Setup(context).ConnectAsync(ToRequest(args), ct);
            var project = result.Project;
            var message = $"Connected {project.Provider} project {project.Project} to {project.WorkspacePath}.";
            if (result.Sync is not null)
            {
                message += $" {SyncMessage(result.Sync)}";
            }
            else
            {
                message += " It cannot sync until its credentials are configured.";
            }

            message = string.Join(" ", [message, .. result.Warnings.Select(x => x.Message)]);
            if (result.Warnings.Any(x => x.Code is "githubPrivateKeyMissing" or "gitlabTokenMissing"))
            {
                message += " After the user confirms the credential is saved, call ConnectProject again with the same arguments to start syncing.";
            }

            return SetupSuccess(message, result);
        });

    [DynamicTool(
        OratorioDynamicToolCatalog.SyncProjectName,
        "Sync one connected repository's issues and pull or merge requests into Oratorio now and report what was imported.",
        Order = 120)]
    public Task<DynamicToolCallResult> SyncProjectAsync(
        SourceProjectToolArgs args,
        OratorioToolInvocationContext context,
        CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            var provider = NormalizeProvider(args.Provider);
            var sync = await Setup(context).SyncAsync(provider, NormalizeProject(provider, args.Project), ct);
            return SetupSuccess(SyncMessage(sync), sync);
        });

    [DynamicTool(
        OratorioDynamicToolCatalog.RequestSourceCredentialName,
        "Show the user a secure card for entering a connected project's credential: the GitLab project access token, or the GitHub App private key. The secret goes straight to Oratorio and never through you.",
        Order = 130)]
    public Task<DynamicToolCallResult> RequestSourceCredentialAsync(
        SourceProjectToolArgs args,
        OratorioToolInvocationContext context,
        CancellationToken ct) =>
        ExecuteAsync(() =>
        {
            var provider = NormalizeProvider(args.Provider);
            var request = Setup(context).RequestCredential(provider, NormalizeProject(provider, args.Project));
            var credential = request.Credential == "gitlabToken" ? "GitLab access token" : "GitHub App private key";
            var message = $"The user enters the {credential} for {request.Project} in the card shown with this result, and it is saved directly to Oratorio. Stop here and wait for the user's next message before continuing.";
            return Task.FromResult(SetupSuccess(message, request));
        });

    [DynamicTool(
        OratorioDynamicToolCatalog.SaveSourceCredentialName,
        "Save the source credential the user entered in the Oratorio credential card.",
        Order = 140)]
    public Task<DynamicToolCallResult> SaveSourceCredentialAsync(
        SaveSourceCredentialToolArgs args,
        OratorioToolInvocationContext context,
        CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            var provider = NormalizeProvider(args.Provider);
            var saved = await Setup(context).SaveCredentialAsync(
                provider,
                NormalizeProject(provider, args.Project),
                args.Secret,
                ToWire(args.TokenKind),
                ct);
            return SetupSuccess($"Saved the credential for {saved.Project}.", saved);
        });

    private static SourceSetupService Setup(OratorioToolInvocationContext context) =>
        context.Services.GetRequiredService<SourceSetupService>();

    private static ConnectProjectRequest ToRequest(ConnectProjectToolArgs args)
    {
        var provider = NormalizeProvider(args.Provider);
        if (provider == GitLabProvider && (args.GithubAppId ?? args.GithubPrivateKeyPath ?? args.GithubInstallationId) is not null)
        {
            throw Invalid("invalidArguments", "githubAppId, githubPrivateKeyPath and githubInstallationId apply only to GitHub.");
        }

        if (args.SyncIntervalMinutes is < 0 or > 1440)
        {
            throw Invalid("invalidSyncInterval", "syncIntervalMinutes must be 0 or between 1 and 1440.");
        }

        var installationId = Optional(args.GithubInstallationId);
        if (installationId is not null && !installationId.All(char.IsAsciiDigit))
        {
            throw Invalid("invalidInstallationId", "githubInstallationId must contain only digits.");
        }

        var keyPath = Optional(args.GithubPrivateKeyPath);
        if (keyPath is not null && (!Path.IsPathFullyQualified(keyPath) || !File.Exists(keyPath)))
        {
            throw Invalid("privateKeyPathNotFound", "githubPrivateKeyPath must be the absolute path of a key file that exists on the Oratorio server.");
        }

        var endpoint = Optional(args.Endpoint);
        if (endpoint is not null &&
            !(Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0))
        {
            throw Invalid("invalidEndpoint", "endpoint must be an absolute http or https URL.");
        }

        return new ConnectProjectRequest(
            provider,
            NormalizeProject(provider, args.Project),
            NormalizeWorkspacePath(args.WorkspacePath),
            endpoint,
            args.AutoReview,
            args.AllowWrites,
            args.SyncIntervalMinutes,
            Optional(args.GithubAppId),
            keyPath,
            installationId);
    }

    private static string NormalizeWorkspacePath(string value)
    {
        var path = value.Trim();
        if (!Path.IsPathFullyQualified(path) || !Directory.Exists(path))
        {
            throw Invalid("workspaceNotFound", "workspacePath must be the absolute path of a directory that exists on the Oratorio server.");
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string SyncMessage(SourceSetupSyncDto sync)
    {
        var message = sync.Status is "queued" or "running"
            ? "The first sync is still running; call GetSourceSetup later to see its result."
            : $"Sync {sync.Status}: {sync.IssuesImported} issue(s) and {sync.ReviewTargetsImported} pull or merge request(s) imported.";
        return sync.Error is null ? message : $"{message} Error: {sync.Error}";
    }

    private static DynamicToolCallResult SetupSuccess(string message, object structuredContent) =>
        new()
        {
            Success = true,
            ContentItems = [new DynamicToolContentItem { Type = "text", Text = message }],
            StructuredContent = JsonSerializer.SerializeToElement(structuredContent, SetupJsonOptions)
        };
}

public class SourceProjectToolArgs
{
    [Description("github or gitlab.")]
    public required string Provider { get; init; }

    [Description("owner/repo for GitHub; group/project or group/subgroup/project for GitLab.")]
    public required string Project { get; init; }
}

public sealed class ConnectProjectToolArgs : SourceProjectToolArgs
{
    [Description("Absolute path of the repository checkout on the Oratorio server. Oratorio runs work in this workspace.")]
    public required string WorkspacePath { get; init; }

    [Description("GitHub API endpoint such as https://api.github.com, or GitLab instance URL such as https://gitlab.example.com. Defaults to the provider's current endpoint.")]
    public string? Endpoint { get; init; }

    [Description("Review new pull or merge requests automatically. Omit to keep the current setting; a new project defaults to true.")]
    public bool? AutoReview { get; init; }

    [Description("Let Oratorio write comments, reviews and statuses back to the provider. Applies to every project of this provider. Omit to keep the current setting, which starts off.")]
    public bool? AllowWrites { get; init; }

    [Description("Minutes between scheduled syncs for this provider; 0 turns scheduled sync off. Omit to keep the current schedule; a provider's first project defaults to 15.")]
    [SchemaMinimum(0)]
    [SchemaMaximum(1440)]
    public int? SyncIntervalMinutes { get; init; }

    [Description("GitHub App id.")]
    public string? GithubAppId { get; init; }

    [Description("Absolute path of the GitHub App private key file on the Oratorio server. Never key content.")]
    public string? GithubPrivateKeyPath { get; init; }

    [Description("GitHub App installation id for the repository owner, when Oratorio cannot detect it.")]
    public string? GithubInstallationId { get; init; }
}

public sealed class SaveSourceCredentialToolArgs : SourceProjectToolArgs
{
    [Description("The GitLab access token, or the GitHub App private key PEM content.")]
    public required string Secret { get; init; }

    [Description("Kind of GitLab token.")]
    public GitLabTokenKind? TokenKind { get; init; }
}

public enum GitLabTokenKind
{
    AccessToken,
    PersonalAccessToken,
    GroupAccessToken
}
