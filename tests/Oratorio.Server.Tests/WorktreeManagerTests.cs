using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Oratorio.Server.DotCraft;

namespace Oratorio.Server.Tests;

public sealed class WorktreeManagerTests
{
    [Fact]
    public async Task PrepareAsync_GitHubPullRequestFetchesProviderHeadRef_WhenLocalShaIsMissing()
    {
        var repository = await CreateReviewTargetRepositoryAsync("refs/pull/184/head", pushReviewRef: true, mismatchReviewRef: false);
        try
        {
            Assert.False(await GitSucceedsAsync(repository.CheckoutPath, ["cat-file", "-e", $"{repository.ExpectedHeadSha}^{{commit}}"]));

            var manager = CreateManager(repository.Root);
            var result = await manager.PrepareAsync(new WorktreePrepareRequest(
                "run-github-pr-fetch",
                "item-github-pr-fetch",
                "github",
                "pr:example-owner/oratorio#184",
                "example-owner/oratorio",
                "feature/review-target",
                repository.ExpectedHeadSha,
                repository.CheckoutPath,
                ReviewTargetFetchRef: "refs/pull/184/head"), CancellationToken.None);

            Assert.Equal(repository.ExpectedHeadSha, result.BaseRef);
            Assert.Equal(repository.ExpectedHeadSha, result.BaseSha);
            Assert.Equal(repository.ExpectedHeadSha, (await GitAsync(result.WorktreePath, ["rev-parse", "HEAD"])).Trim());
        }
        finally
        {
            DeleteDirectory(repository.Root);
        }
    }

    [Fact]
    public async Task PrepareAsync_GitLabMergeRequestFetchesProviderHeadRef_WhenLocalShaIsMissing()
    {
        var repository = await CreateReviewTargetRepositoryAsync("refs/merge-requests/7/head", pushReviewRef: true, mismatchReviewRef: false);
        try
        {
            Assert.False(await GitSucceedsAsync(repository.CheckoutPath, ["cat-file", "-e", $"{repository.ExpectedHeadSha}^{{commit}}"]));

            var manager = CreateManager(repository.Root);
            var result = await manager.PrepareAsync(new WorktreePrepareRequest(
                "run-gitlab-mr-fetch",
                "item-gitlab-mr-fetch",
                "gitlab",
                "mr:gitlab.example.test/group/project!7",
                "gitlab:gitlab.example.test/group/project",
                "feature/review-target",
                repository.ExpectedHeadSha,
                repository.CheckoutPath,
                ReviewTargetFetchRef: "refs/merge-requests/7/head"), CancellationToken.None);

            Assert.Equal(repository.ExpectedHeadSha, result.BaseRef);
            Assert.Equal(repository.ExpectedHeadSha, result.BaseSha);
            Assert.Equal(repository.ExpectedHeadSha, (await GitAsync(result.WorktreePath, ["rev-parse", "HEAD"])).Trim());
        }
        finally
        {
            DeleteDirectory(repository.Root);
        }
    }

    [Fact]
    public async Task PrepareAsync_ReviewTargetFetchRefMissing_FailsWithStableCode()
    {
        var repository = await CreateReviewTargetRepositoryAsync("refs/pull/184/head", pushReviewRef: false, mismatchReviewRef: false);
        try
        {
            var manager = CreateManager(repository.Root);
            var error = await Assert.ThrowsAsync<WorktreeException>(() => manager.PrepareAsync(new WorktreePrepareRequest(
                "run-missing-pr-ref",
                "item-missing-pr-ref",
                "github",
                "pr:example-owner/oratorio#184",
                "example-owner/oratorio",
                "feature/review-target",
                repository.ExpectedHeadSha,
                repository.CheckoutPath,
                ReviewTargetFetchRef: "refs/pull/184/head"), CancellationToken.None));

            Assert.Equal("reviewTargetFetchFailed", error.Code);
        }
        finally
        {
            DeleteDirectory(repository.Root);
        }
    }

    [Fact]
    public async Task PrepareAsync_ReviewTargetFetchRefMismatch_FailsWithStableCode()
    {
        var repository = await CreateReviewTargetRepositoryAsync("refs/pull/184/head", pushReviewRef: true, mismatchReviewRef: true);
        try
        {
            var manager = CreateManager(repository.Root);
            var error = await Assert.ThrowsAsync<WorktreeException>(() => manager.PrepareAsync(new WorktreePrepareRequest(
                "run-mismatched-pr-ref",
                "item-mismatched-pr-ref",
                "github",
                "pr:example-owner/oratorio#184",
                "example-owner/oratorio",
                "feature/review-target",
                repository.ExpectedHeadSha,
                repository.CheckoutPath,
                ReviewTargetFetchRef: "refs/pull/184/head"), CancellationToken.None));

            Assert.Equal("reviewTargetHeadUnresolved", error.Code);
            Assert.Contains(repository.ExpectedHeadSha, error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(repository.MismatchedHeadSha, error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(repository.Root);
        }
    }

    private static WorktreeManager CreateManager(string root) =>
        new(
            new StaticOptionsMonitor<DotCraftOptions>(new DotCraftOptions
            {
                WorktreeRoot = Path.Combine(root, "managed-worktrees"),
                WorktreeBranchPrefix = "oratorio/run"
            }),
            NullLogger<WorktreeManager>.Instance);

    private static async Task<ReviewTargetRepository> CreateReviewTargetRepositoryAsync(string reviewRef, bool pushReviewRef, bool mismatchReviewRef)
    {
        var root = Path.Combine(Path.GetTempPath(), $"oratorio-worktree-{Guid.NewGuid():n}");
        var sourcePath = Path.Combine(root, "source");
        var remotePath = Path.Combine(root, "remote.git");
        var checkoutPath = Path.Combine(root, "checkout");
        Directory.CreateDirectory(sourcePath);

        await GitAsync(sourcePath, ["init"]);
        await GitAsync(sourcePath, ["config", "user.email", "oratorio-tests@example.test"]);
        await GitAsync(sourcePath, ["config", "user.name", "Oratorio Tests"]);
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "README.md"), "base\n");
        await GitAsync(sourcePath, ["add", "README.md"]);
        await GitAsync(sourcePath, ["commit", "-m", "Initial commit"]);
        await GitAsync(sourcePath, ["branch", "-M", "main"]);

        await GitAsync(sourcePath, ["checkout", "-b", "review-target"]);
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "README.md"), "base\nreview target\n");
        await GitAsync(sourcePath, ["commit", "-am", "Review target"]);
        var expectedHeadSha = (await GitAsync(sourcePath, ["rev-parse", "HEAD"])).Trim();

        await GitAsync(sourcePath, ["checkout", "main"]);
        await GitAsync(sourcePath, ["checkout", "-b", "different-review-target"]);
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "README.md"), "base\ndifferent review target\n");
        await GitAsync(sourcePath, ["commit", "-am", "Different review target"]);
        var mismatchedHeadSha = (await GitAsync(sourcePath, ["rev-parse", "HEAD"])).Trim();

        await GitAsync(root, ["init", "--bare", remotePath]);
        await GitAsync(sourcePath, ["remote", "add", "origin", remotePath]);
        await GitAsync(sourcePath, ["push", "origin", "main"]);
        if (pushReviewRef)
        {
            var reviewSha = mismatchReviewRef ? mismatchedHeadSha : expectedHeadSha;
            await GitAsync(sourcePath, ["push", "origin", $"{reviewSha}:{reviewRef}"]);
        }

        await GitAsync(root, ["clone", "--no-local", "--no-tags", "--single-branch", "--branch", "main", remotePath, checkoutPath]);
        return new ReviewTargetRepository(root, checkoutPath, expectedHeadSha, mismatchedHeadSha);
    }

    private static async Task<string> GitAsync(string workingDirectory, IReadOnlyList<string> arguments)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
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
        var stdout = await process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = await process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(stderr.Trim().Length == 0 ? "Git command failed." : stderr.Trim());
        }

        return stdout;
    }

    private static async Task<bool> GitSucceedsAsync(string workingDirectory, IReadOnlyList<string> arguments)
    {
        try
        {
            await GitAsync(workingDirectory, arguments);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temporary git repositories.
        }
    }

    private sealed record ReviewTargetRepository(
        string Root,
        string CheckoutPath,
        string ExpectedHeadSha,
        string MismatchedHeadSha);
}
