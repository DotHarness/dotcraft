using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Oratorio.Server.DotCraft;

public interface IWorktreeManager
{
    Task<WorktreePrepareResult> PrepareAsync(WorktreePrepareRequest request, CancellationToken ct);

    Task CleanupAsync(WorktreeCleanupRequest request, CancellationToken ct);
}

public sealed record WorktreePrepareRequest(
    string RunId,
    string ItemId,
    string Source,
    string ExternalId,
    string? Repository,
    string? SourceBranch,
    string? HeadSha,
    string BaseWorkspacePath,
    string? StackOntoBranch = null,
    string? StackOntoSha = null,
    string? ReviewTargetFetchRef = null);

public sealed record WorktreePrepareResult(
    string BaseWorkspacePath,
    string WorktreePath,
    string WorktreeBranch,
    string BaseRef,
    string BaseSha,
    string WorktreeRoot);

public sealed record WorktreeCleanupRequest(
    string RunId,
    string? BaseWorkspacePath,
    string? WorktreePath);

public sealed class WorktreeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class WorktreeManager(IOptionsMonitor<DotCraftOptions> options, ILogger<WorktreeManager> logger) : IWorktreeManager
{
    private const string MetadataDirectoryName = ".metadata";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RepoLocks = new(StringComparer.OrdinalIgnoreCase);

    public async Task<WorktreePrepareResult> PrepareAsync(WorktreePrepareRequest request, CancellationToken ct)
    {
        var baseWorkspace = NormalizeExistingDirectory(request.BaseWorkspacePath, "baseWorkspaceMissing", "Base workspace is missing.");
        var repoRoot = await ResolveRepoRootAsync(baseWorkspace, ct);
        var worktreeRoot = ResolveWorktreeRoot(repoRoot);
        var key = BuildWorktreeKey(request);
        var branchName = BuildBranchName(key);
        var worktreePath = Path.Combine(worktreeRoot, key);
        var stackOntoExistingPr = !string.IsNullOrWhiteSpace(request.StackOntoBranch) || !string.IsNullOrWhiteSpace(request.StackOntoSha);
        var baseRef = stackOntoExistingPr
            ? request.StackOntoBranch ?? request.StackOntoSha!
            : ResolveBaseRef(request);

        var repoLock = RepoLocks.GetOrAdd(repoRoot, _ => new SemaphoreSlim(1, 1));
        await repoLock.WaitAsync(ct);
        try
        {
            var baseSha = stackOntoExistingPr
                ? await ResolveStackBaseShaAsync(repoRoot, request, ct)
                : await ResolveBaseShaAsync(repoRoot, request, baseRef, ct);
            if (string.IsNullOrWhiteSpace(baseSha))
            {
                throw new WorktreeException("baseRefUnresolved", $"Could not resolve base ref '{baseRef}'.");
            }

            Directory.CreateDirectory(worktreeRoot);
            if (Directory.Exists(worktreePath))
            {
                EnsureUnderRoot(worktreePath, worktreeRoot, "worktreePathInvalid");
                await EnsureCleanWorktreeAsync(worktreePath, ct);
                await GitAsync(worktreePath, ["checkout", "-B", branchName, baseSha], ct);
            }
            else
            {
                await GitAsync(repoRoot, ["worktree", "prune"], ct);
                await GitAsync(repoRoot, ["worktree", "add", "-B", branchName, worktreePath, baseSha], ct);
            }

            await WriteMetadataAsync(request, worktreeRoot, worktreePath, branchName, baseRef, baseSha, ct);
            logger.LogDebug("Prepared managed Oratorio worktree {WorktreePath} at {BaseSha}.", worktreePath, baseSha);
            return new WorktreePrepareResult(repoRoot, worktreePath, branchName, baseRef, baseSha, worktreeRoot);
        }
        finally
        {
            repoLock.Release();
        }
    }

    public async Task CleanupAsync(WorktreeCleanupRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BaseWorkspacePath) || string.IsNullOrWhiteSpace(request.WorktreePath))
        {
            return;
        }

        var baseWorkspace = Path.GetFullPath(request.BaseWorkspacePath);
        var repoRoot = await ResolveRepoRootAsync(baseWorkspace, ct);
        var worktreeRoot = ResolveWorktreeRoot(repoRoot);
        var worktreePath = Path.GetFullPath(request.WorktreePath);
        EnsureUnderRoot(worktreePath, worktreeRoot, "worktreeCleanupDenied");

        var metadataPath = Path.Combine(worktreeRoot, MetadataDirectoryName, Path.GetFileName(worktreePath) + ".json");
        if (!File.Exists(metadataPath))
        {
            throw new WorktreeException("worktreeMetadataMissing", "Worktree cleanup refused because Oratorio metadata is missing.");
        }

        var repoLock = RepoLocks.GetOrAdd(repoRoot, _ => new SemaphoreSlim(1, 1));
        await repoLock.WaitAsync(ct);
        try
        {
            if (Directory.Exists(worktreePath))
            {
                await GitAsync(repoRoot, ["worktree", "remove", "--force", worktreePath], ct);
                logger.LogInformation("Cleaned managed Oratorio worktree {WorktreePath}.", worktreePath);
            }
        }
        finally
        {
            repoLock.Release();
        }
    }

    private static string NormalizeExistingDirectory(string path, string code, string message)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            throw new WorktreeException(code, message);
        }

        return Path.GetFullPath(path);
    }

    private async Task<string> ResolveRepoRootAsync(string baseWorkspace, CancellationToken ct)
    {
        var root = (await GitAsync(baseWorkspace, ["rev-parse", "--show-toplevel"], ct)).Trim();
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new WorktreeException("baseWorkspaceNotGitRepository", "Base workspace is not a Git repository.");
        }

        return Path.GetFullPath(root);
    }

    private string ResolveWorktreeRoot(string repoRoot)
    {
        var configured = options.CurrentValue.WorktreeRoot;
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(repoRoot, ".craft", "oratorio", "worktrees")
            : Path.GetFullPath(configured);
    }

    private string BuildBranchName(string key)
    {
        var prefix = string.IsNullOrWhiteSpace(options.CurrentValue.WorktreeBranchPrefix)
            ? "oratorio/run"
            : SanitizeBranchSegment(options.CurrentValue.WorktreeBranchPrefix.Trim('/'));
        return $"{prefix}/{key}";
    }

    private static string ResolveBaseRef(WorktreePrepareRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.HeadSha))
        {
            return request.HeadSha;
        }

        return string.IsNullOrWhiteSpace(request.SourceBranch) ? "HEAD" : request.SourceBranch;
    }

    private static async Task<string> ResolveBaseShaAsync(string repoRoot, WorktreePrepareRequest request, string baseRef, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.HeadSha))
        {
            return await ResolveReviewTargetHeadShaAsync(repoRoot, request, ct);
        }

        return (await GitAsync(repoRoot, ["rev-parse", baseRef], ct)).Trim();
    }

    private static async Task<string> ResolveReviewTargetHeadShaAsync(string repoRoot, WorktreePrepareRequest request, CancellationToken ct)
    {
        var expectedHeadSha = request.HeadSha!.Trim();
        if (await CommitExistsAsync(repoRoot, expectedHeadSha, ct))
        {
            return expectedHeadSha;
        }

        if (!string.IsNullOrWhiteSpace(request.ReviewTargetFetchRef))
        {
            var fetchRef = request.ReviewTargetFetchRef!.Trim();
            try
            {
                await GitAsync(repoRoot, ["fetch", "origin", fetchRef], ct);
            }
            catch (WorktreeException ex)
            {
                throw new WorktreeException(
                    "reviewTargetFetchFailed",
                    $"Could not fetch review target ref '{fetchRef}' from origin: {ex.Message}");
            }

            string fetchedHead;
            try
            {
                fetchedHead = (await GitAsync(repoRoot, ["rev-parse", "FETCH_HEAD"], ct)).Trim();
            }
            catch (WorktreeException ex)
            {
                throw new WorktreeException(
                    "reviewTargetHeadUnresolved",
                    $"Fetched review target ref '{fetchRef}', but could not resolve FETCH_HEAD: {ex.Message}");
            }

            if (!string.Equals(fetchedHead, expectedHeadSha, StringComparison.OrdinalIgnoreCase))
            {
                throw new WorktreeException(
                    "reviewTargetHeadUnresolved",
                    $"Fetched review target ref '{fetchRef}' resolved to {fetchedHead}, but Oratorio expected {expectedHeadSha}.");
            }

            if (await CommitExistsAsync(repoRoot, expectedHeadSha, ct))
            {
                return expectedHeadSha;
            }

            throw new WorktreeException(
                "reviewTargetHeadUnresolved",
                $"Fetched review target ref '{fetchRef}', but expected head {expectedHeadSha} is not available in the mapped repository checkout.");
        }

        try
        {
            return (await GitAsync(repoRoot, ["rev-parse", expectedHeadSha], ct)).Trim();
        }
        catch (WorktreeException ex)
        {
            throw new WorktreeException(
                "reviewTargetHeadUnresolved",
                $"Could not resolve review target head '{expectedHeadSha}' in the mapped repository checkout: {ex.Message}");
        }
    }

    private static Task<bool> CommitExistsAsync(string repoRoot, string sha, CancellationToken ct) =>
        TryGitAsync(repoRoot, ["cat-file", "-e", $"{sha}^{{commit}}"], ct);

    // Implementation follow-up rounds must stack on the existing generated PR head so prior
    // delivered commits are retained (design spec §5.5). Prefer the locally available head SHA,
    // then a best-effort fetch of the PR branch, then a retained local branch ref.
    private static async Task<string> ResolveStackBaseShaAsync(string repoRoot, WorktreePrepareRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.StackOntoSha) &&
            await TryGitAsync(repoRoot, ["cat-file", "-e", request.StackOntoSha!], ct))
        {
            return request.StackOntoSha!.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.StackOntoBranch))
        {
            if (await TryGitAsync(repoRoot, ["fetch", "origin", request.StackOntoBranch!], ct))
            {
                var fetched = (await GitAsync(repoRoot, ["rev-parse", "FETCH_HEAD"], ct)).Trim();
                if (!string.IsNullOrWhiteSpace(fetched))
                {
                    return fetched;
                }
            }

            if (await TryGitAsync(repoRoot, ["rev-parse", "--verify", "--quiet", request.StackOntoBranch!], ct))
            {
                return (await GitAsync(repoRoot, ["rev-parse", request.StackOntoBranch!], ct)).Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(request.StackOntoSha))
        {
            return (await GitAsync(repoRoot, ["rev-parse", request.StackOntoSha!], ct)).Trim();
        }

        throw new WorktreeException("followUpBaseUnresolved", "Could not resolve the existing pull request head for the implementation follow-up worktree.");
    }

    private static async Task<bool> TryGitAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        try
        {
            await GitAsync(workingDirectory, arguments, ct);
            return true;
        }
        catch (WorktreeException)
        {
            return false;
        }
    }

    private static async Task EnsureCleanWorktreeAsync(string worktreePath, CancellationToken ct)
    {
        var status = await GitAsync(worktreePath, ["status", "--porcelain"], ct);
        if (!string.IsNullOrWhiteSpace(status))
        {
            throw new WorktreeException("worktreeDirty", "Managed worktree has local modifications and will not be reused destructively.");
        }
    }

    private static string BuildWorktreeKey(WorktreePrepareRequest request)
    {
        var readable = SanitizePathSegment($"{request.Source}-{request.Repository ?? "local"}-{request.ExternalId}");
        var hashInput = $"{request.Source}\n{request.ExternalId}\n{request.Repository}\n{request.ItemId}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)))[..12].ToLowerInvariant();
        return $"{readable[..Math.Min(readable.Length, 48)]}-{hash}";
    }

    private static string SanitizePathSegment(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.')
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append('-');
            }
        }

        var result = builder.ToString().Trim('-', '.');
        return string.IsNullOrWhiteSpace(result) ? "work-item" : result;
    }

    private static string SanitizeBranchSegment(string value)
    {
        var normalized = value.Replace('\\', '/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizePathSegment)
            .Where(x => !string.IsNullOrWhiteSpace(x));
        return string.Join("/", parts);
    }

    private static void EnsureUnderRoot(string path, string root, string code)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new WorktreeException(code, "Managed worktree path is outside the configured Oratorio worktree root.");
        }
    }

    private static async Task WriteMetadataAsync(
        WorktreePrepareRequest request,
        string worktreeRoot,
        string worktreePath,
        string branchName,
        string baseRef,
        string baseSha,
        CancellationToken ct)
    {
        var metadata = new
        {
            owner = "oratorio",
            request.RunId,
            request.ItemId,
            request.Source,
            request.ExternalId,
            request.Repository,
            WorktreeRoot = worktreeRoot,
            WorktreePath = worktreePath,
            WorktreeBranch = branchName,
            BaseRef = baseRef,
            BaseSha = baseSha,
            PreparedAt = DateTimeOffset.UtcNow
        };
        Directory.CreateDirectory(Path.Combine(worktreeRoot, MetadataDirectoryName));
        await File.WriteAllTextAsync(
            Path.Combine(worktreeRoot, MetadataDirectoryName, Path.GetFileName(worktreePath) + ".json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
            ct);
    }

    private static async Task<string> GitAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(workingDirectory);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new WorktreeException("gitStartFailed", "Could not start git.");
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
        {
            throw new WorktreeException("gitCommandFailed", stderr.Trim().Length == 0 ? "Git command failed." : stderr.Trim());
        }

        return stdout;
    }
}
