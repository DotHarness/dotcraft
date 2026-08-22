using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using Oratorio.Server.GitHub;
using Oratorio.Server.GitLab;

namespace Oratorio.Server.Sources;

/// <summary>
/// Credentials for one Git transport host, resolved from the same provider
/// configuration Oratorio uses for its REST calls.
/// </summary>
public sealed record GitTransportCredential(string Host, string Username, string Secret)
{
    /// <summary>
    /// Supplies the credential to a git child process through an
    /// <c>http.&lt;url&gt;.extraheader</c> entry in its environment.
    /// </summary>
    /// <remarks>
    /// The secret travels in the environment rather than in argv because
    /// <c>/proc/&lt;pid&gt;/cmdline</c> is world-readable, and because git echoes
    /// remote URLs back in its error output.
    /// </remarks>
    public void ApplyTo(ProcessStartInfo startInfo)
    {
        // ProcessStartInfo.Environment is pre-populated from this process, so the
        // index continues any inherited sequence. A host may already pass git
        // config through these variables; overwriting entry 0 would drop it.
        var index = 0;
        if (startInfo.Environment.TryGetValue("GIT_CONFIG_COUNT", out var inherited) &&
            int.TryParse(inherited, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
            parsed > 0)
        {
            index = parsed;
        }

        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Secret}"));
        startInfo.Environment[$"GIT_CONFIG_KEY_{index}"] = $"http.https://{Host}/.extraheader";
        startInfo.Environment[$"GIT_CONFIG_VALUE_{index}"] = $"Authorization: Basic {basic}";
        startInfo.Environment["GIT_CONFIG_COUNT"] = (index + 1).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Builds an authenticated remote URL for commands that must target an
    /// explicit remote rather than a configured one.
    /// </summary>
    public string ToRemoteUrl(string projectPath) =>
        $"https://{Username}:{Uri.EscapeDataString(Secret)}@{Host}/{projectPath}.git";
}

public interface IGitTransportCredentialProvider
{
    /// <summary>
    /// Normalizes a run's source and repository fields into a project identity.
    /// </summary>
    bool TryResolveProject(string? provider, string? repository, out SourceProjectKey project);

    /// <summary>
    /// Resolves transport credentials for a project, or <c>null</c> when the
    /// project has no configured credential. Provider errors propagate, so
    /// callers that can tolerate anonymous transport must handle them; callers
    /// that require a credential surface their own message for <c>null</c>.
    /// </summary>
    Task<GitTransportCredential?> ResolveAsync(SourceProjectKey project, CancellationToken ct);
}

public sealed class GitTransportCredentialProvider(
    IGitHubTokenProvider gitHubTokens,
    IOptionsMonitor<GitHubOptions> gitHubOptions,
    IGitLabCredentialResolver gitLabCredentials,
    IOptionsMonitor<GitLabOptions> gitLabOptions) : IGitTransportCredentialProvider
{
    public bool TryResolveProject(string? provider, string? repository, out SourceProjectKey project)
    {
        project = new SourceProjectKey("", "", "");
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(repository))
        {
            return false;
        }

        var endpoint = provider switch
        {
            "github" => gitHubOptions.CurrentValue.Endpoint,
            "gitlab" => gitLabOptions.CurrentValue.Endpoint,
            _ => null
        };
        if (endpoint is null)
        {
            return false;
        }

        return SourceProjectKey.TryNormalizeForProvider(provider, repository, endpoint, out project);
    }

    public async Task<GitTransportCredential?> ResolveAsync(SourceProjectKey project, CancellationToken ct) =>
        project.Provider switch
        {
            "github" => await ResolveGitHubAsync(project, ct),
            "gitlab" => ResolveGitLab(project),
            _ => null
        };

    private async Task<GitTransportCredential?> ResolveGitHubAsync(SourceProjectKey project, CancellationToken ct)
    {
        if (!GitHubRepositoryRef.TryParse(project.ProjectPath, out var repository))
        {
            return null;
        }

        var token = await gitHubTokens.GetBearerTokenAsync(repository, ct);
        return string.IsNullOrWhiteSpace(token)
            ? null
            : new GitTransportCredential(ResolveGitHubHost(), "x-access-token", token);
    }

    private GitTransportCredential? ResolveGitLab(SourceProjectKey project)
    {
        var token = gitLabCredentials.ResolveToken(gitLabOptions.CurrentValue, new GitLabProjectRef(project.ProjectPath));
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var endpoint = gitLabOptions.CurrentValue.EffectiveEndpoint;
        var host = Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ? uri.Authority : project.Instance;
        return new GitTransportCredential(host, "oauth2", token);
    }

    private string ResolveGitHubHost()
    {
        // The API endpoint and the git endpoint differ on github.com but match on
        // GitHub Enterprise.
        var endpoint = gitHubOptions.CurrentValue.Endpoint;
        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
               !string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase)
            ? uri.Authority
            : "github.com";
    }
}
