using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Sources;

namespace DotCraft.Oratorio.Tests;

public sealed class WorktreeManagerTests
{
    [Fact]
    public async Task PrepareAsync_GitHubPullRequestFetchesExactCommitWithResolvedCredential_WhenLocalShaIsMissing()
    {
        var repository = await CreateReviewTargetRepositoryAsync("refs/pull/184/head", pushReviewRef: true, mismatchReviewRef: false);
        try
        {
            Assert.False(await GitSucceedsAsync(repository.CheckoutPath, ["cat-file", "-e", $"{repository.ExpectedHeadSha}^{{commit}}"]));

            var credentials = new RecordingGitTransportCredentialProvider(
                new GitTransportCredential("github.com", "x-access-token", "ghs-test-token"));
            var manager = CreateManager(repository.Root, credentials);
            var result = await manager.PrepareAsync(new WorktreePrepareRequest(
                "run-github-pr-fetch",
                "item-github-pr-fetch",
                "github",
                "pr:example-owner/oratorio#184",
                "example-owner/oratorio",
                "feature/review-target",
                repository.ExpectedHeadSha,
                repository.CheckoutPath), CancellationToken.None);

            Assert.Equal(("github", "example-owner/oratorio"), credentials.RequestedItem);
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
    public async Task CleanupAsync_LeavesWorktreeALaterOwnerPrepared()
    {
        var repository = await CreateReviewTargetRepositoryAsync("refs/pull/184/head", pushReviewRef: true, mismatchReviewRef: false);
        try
        {
            var manager = CreateManager(repository.Root);
            WorktreePrepareRequest Request(string ownerId) => new(
                ownerId,
                "item-shared-path",
                "github",
                "pr:example-owner/oratorio#184",
                "example-owner/oratorio",
                "feature/review-target",
                repository.ExpectedHeadSha,
                repository.CheckoutPath);
            var first = await manager.PrepareAsync(Request("run-earlier"), CancellationToken.None);
            var second = await manager.PrepareAsync(Request("discussion-later"), CancellationToken.None);
            Assert.Equal(first.WorktreePath, second.WorktreePath);

            await manager.CleanupAsync(new WorktreeCleanupRequest("run-earlier", repository.CheckoutPath, first.WorktreePath), CancellationToken.None);
            Assert.True(Directory.Exists(second.WorktreePath));

            await manager.CleanupAsync(new WorktreeCleanupRequest("discussion-later", repository.CheckoutPath, second.WorktreePath), CancellationToken.None);
            Assert.False(Directory.Exists(second.WorktreePath));
        }
        finally
        {
            DeleteDirectory(repository.Root);
        }
    }

    [Fact]
    public async Task PrepareAsync_GitLabMergeRequestFetchesExactCommit_WhenLocalShaIsMissing()
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
                repository.CheckoutPath), CancellationToken.None);

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
    public async Task PrepareAsync_FetchesExactCommit_WhenPullRequestRefIsStale()
    {
        var repository = await CreateReviewTargetRepositoryAsync("refs/pull/184/head", pushReviewRef: true, mismatchReviewRef: true);
        try
        {
            Assert.False(await GitSucceedsAsync(repository.CheckoutPath, ["cat-file", "-e", $"{repository.ExpectedHeadSha}^{{commit}}"]));
            var manager = CreateManager(repository.Root);
            var result = await manager.PrepareAsync(new WorktreePrepareRequest(
                "run-stale-pr-ref",
                "item-stale-pr-ref",
                "github",
                "pr:example-owner/oratorio#184",
                "example-owner/oratorio",
                "review-target",
                repository.ExpectedHeadSha,
                repository.CheckoutPath), CancellationToken.None);

            Assert.Equal(repository.ExpectedHeadSha, result.BaseSha);
            Assert.Equal(repository.ExpectedHeadSha, (await GitAsync(result.WorktreePath, ["rev-parse", "HEAD"])).Trim());
            Assert.Equal(repository.MismatchedHeadSha, (await GitAsync(Path.Combine(repository.Root, "remote.git"),
                ["rev-parse", "refs/pull/184/head"])).Trim());
        }
        finally
        {
            DeleteDirectory(repository.Root);
        }
    }

    [Fact]
    public async Task PrepareAsync_CredentialLookupFailure_FallsBackToAnonymousFetch()
    {
        var repository = await CreateReviewTargetRepositoryAsync("refs/pull/184/head", pushReviewRef: true, mismatchReviewRef: false);
        try
        {
            var credentials = new RecordingGitTransportCredentialProvider(
                failure: new HttpRequestException("github is unreachable"));

            var result = await CreateManager(repository.Root, credentials).PrepareAsync(new WorktreePrepareRequest(
                "run-credential-lookup-failure",
                "item-credential-lookup-failure",
                "github",
                "pr:example-owner/oratorio#184",
                "example-owner/oratorio",
                "feature/review-target",
                repository.ExpectedHeadSha,
                repository.CheckoutPath), CancellationToken.None);

            Assert.Equal(repository.ExpectedHeadSha, result.BaseSha);
        }
        finally
        {
            DeleteDirectory(repository.Root);
        }
    }

    [Fact]
    public async Task PrepareAsync_ReviewTargetFetchFailure_NamesMissingCredentials()
    {
        var repository = await CreateReviewTargetRepositoryAsync("refs/pull/184/head", pushReviewRef: false, mismatchReviewRef: false);
        try
        {
            var manager = CreateManager(repository.Root, new RecordingGitTransportCredentialProvider());
            var error = await Assert.ThrowsAsync<WorktreeException>(() => manager.PrepareAsync(new WorktreePrepareRequest(
                "run-uncredentialed-fetch",
                "item-uncredentialed-fetch",
                "github",
                "pr:example-owner/oratorio#184",
                "example-owner/oratorio",
                "feature/review-target",
                repository.ExpectedHeadSha,
                repository.CheckoutPath), CancellationToken.None));

            Assert.Equal("reviewTargetFetchFailed", error.Code);
            Assert.Contains("No source credentials were available", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(repository.Root);
        }
    }

    private static WorktreeManager CreateManager(string root, IGitTransportCredentialProvider? credentials = null) =>
        new(
            new StaticOptionsMonitor<DotCraftOptions>(new DotCraftOptions
            {
                WorktreeRoot = Path.Combine(root, "managed-worktrees"),
                WorktreeBranchPrefix = "oratorio/run"
            }),
            credentials ?? new RecordingGitTransportCredentialProvider(),
            NullLogger<WorktreeManager>.Instance);

    private sealed class RecordingGitTransportCredentialProvider(
        GitTransportCredential? credential = null,
        Exception? failure = null)
        : IGitTransportCredentialProvider
    {
        public (string? Provider, string? Repository)? RequestedItem { get; private set; }

        public bool TryResolveProject(string? provider, string? repository, out SourceProjectKey project)
        {
            RequestedItem = (provider, repository);
            project = new SourceProjectKey(provider ?? "", "instance", repository ?? "");
            return true;
        }

        public Task<GitTransportCredential?> ResolveAsync(SourceProjectKey project, CancellationToken ct) =>
            failure is null
                ? Task.FromResult(credential)
                : Task.FromException<GitTransportCredential?>(failure);
    }

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
            await GitAsync(sourcePath, ["push", "origin", "review-target"]);
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
        }
    }

    private sealed record ReviewTargetRepository(
        string Root,
        string CheckoutPath,
        string ExpectedHeadSha,
        string MismatchedHeadSha);
}
