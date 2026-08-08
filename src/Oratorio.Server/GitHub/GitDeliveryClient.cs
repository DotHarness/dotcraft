using System.Diagnostics;
using Microsoft.Extensions.Options;
using Oratorio.Server.GitLab;
using Oratorio.Server.Sources;

namespace Oratorio.Server.GitHub;

public interface IGitDeliveryClient
{
    Task<IReadOnlyList<string>> GetChangedFilesAsync(string worktreePath, CancellationToken ct);
    Task<string> CommitAllAsync(string worktreePath, string message, CancellationToken ct);
    Task PushBranchAsync(string worktreePath, SourceProjectKey project, string branchName, CancellationToken ct);
}

public sealed class GitDeliveryClient(
    IGitHubTokenProvider tokenProvider,
    IOptionsMonitor<GitHubOptions> options,
    IOptionsMonitor<GitLabOptions> gitLabOptions,
    IGitLabCredentialResolver gitLabCredentials) : IGitDeliveryClient
{
    public async Task<IReadOnlyList<string>> GetChangedFilesAsync(string worktreePath, CancellationToken ct)
    {
        var output = await GitAsync(worktreePath, ["status", "--porcelain"], ct);
        return output
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Length > 3 ? line[3..].Trim().Replace('\\', '/') : line.Trim().Replace('\\', '/'))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<string> CommitAllAsync(string worktreePath, string message, CancellationToken ct)
    {
        await GitAsync(worktreePath, ["add", "-A"], ct);
        await GitAsync(worktreePath, ["commit", "-m", message], ct);
        return (await GitAsync(worktreePath, ["rev-parse", "HEAD"], ct)).Trim();
    }

    public async Task PushBranchAsync(string worktreePath, SourceProjectKey project, string branchName, CancellationToken ct)
    {
        var remote = project.Provider switch
        {
            "github" => await BuildGitHubAuthenticatedRemoteAsync(project, ct),
            "gitlab" => BuildGitLabAuthenticatedRemote(project),
            _ => throw new InvalidOperationException($"Unsupported git delivery provider '{project.Provider}'.")
        };
        await GitAsync(worktreePath, ["push", remote, $"HEAD:refs/heads/{branchName}", "--force-with-lease"], ct);
    }

    private async Task<string> BuildGitHubAuthenticatedRemoteAsync(SourceProjectKey project, CancellationToken ct)
    {
        if (!GitHubRepositoryRef.TryParse(project.ProjectPath, out var repository))
        {
            throw new InvalidOperationException("GitHub branch push target is not in owner/name form.");
        }

        var token = await tokenProvider.GetBearerTokenAsync(repository, ct);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("GitHub App installation token is not available for branch push.");
        }

        return BuildGitHubAuthenticatedRemote(project, token);
    }

    private string BuildGitHubAuthenticatedRemote(SourceProjectKey project, string token)
    {
        var endpoint = options.CurrentValue.Endpoint;
        var host = "github.com";
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && !string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase))
        {
            host = uri.Authority;
        }

        var escaped = Uri.EscapeDataString(token);
        return $"https://x-access-token:{escaped}@{host}/{project.ProjectPath}.git";
    }

    private string BuildGitLabAuthenticatedRemote(SourceProjectKey project)
    {
        var token = gitLabCredentials.ResolveToken(gitLabOptions.CurrentValue, new GitLabProjectRef(project.ProjectPath));
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException($"GitLab project profile token is not available for branch push to {project.ProjectPath}.");
        }

        var endpoint = gitLabOptions.CurrentValue.EffectiveEndpoint;
        var host = Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            ? uri.Authority
            : project.Instance;
        var escaped = Uri.EscapeDataString(token);
        return $"https://oauth2:{escaped}@{host}/{project.ProjectPath}.git";
    }

    private static async Task<string> GitAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            throw new InvalidOperationException("Managed worktree path is missing.");
        }

        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(stderr.Trim().Length == 0 ? "Git command failed." : stderr.Trim());
        }

        return stdout;
    }
}
