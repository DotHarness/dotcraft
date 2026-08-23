using System.Diagnostics;
using Oratorio.Server.Sources;

namespace Oratorio.Server.GitHub;

public interface IGitDeliveryClient
{
    Task<IReadOnlyList<string>> GetChangedFilesAsync(string worktreePath, CancellationToken ct);
    Task<string> CommitAllAsync(string worktreePath, string message, CancellationToken ct);
    Task PushBranchAsync(string worktreePath, SourceProjectKey project, string branchName, CancellationToken ct);
}

public sealed class GitDeliveryClient(IGitTransportCredentialProvider credentials) : IGitDeliveryClient
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
        if (project.Provider is not ("github" or "gitlab"))
        {
            throw new InvalidOperationException($"Unsupported git delivery provider '{project.Provider}'.");
        }

        if (project.Provider == "github" && !GitHubRepositoryRef.TryParse(project.ProjectPath, out _))
        {
            throw new InvalidOperationException("GitHub branch push target is not in owner/name form.");
        }

        var credential = await credentials.ResolveAsync(project, ct)
            ?? throw new InvalidOperationException(project.Provider == "github"
                ? "GitHub App installation token is not available for branch push."
                : $"GitLab project profile token is not available for branch push to {project.ProjectPath}.");

        var remote = credential.ToRemoteUrl(project.ProjectPath);
        await GitAsync(worktreePath, ["push", remote, $"HEAD:refs/heads/{branchName}", "--force-with-lease"], ct);
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
        // Never block on an interactive credential prompt; fail fast instead.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
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
