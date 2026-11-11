using System.Net.Http.Json;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static DotCraft.Oratorio.Tests.OratorioApiTestSupport;

namespace DotCraft.Oratorio.Tests;

public sealed class GitHubSyncJobConcurrencyTests
{
    [Fact]
    public async Task GitHubSyncJob_ImportsRepositoriesIndependently_WhenFirstRepositoryIsBlocked()
    {
        var firstRepositoryRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fakeGitHub = new FakeGitHubApiClient();
        fakeGitHub.ListIssueGates["example-owner/oratorio"] = firstRepositoryRelease.Task;
        fakeGitHub.IssuesByRepository["example-owner/second-repo"] = [TestGitHubIssue(3001, 7, "Second repository issue", "example-owner/second-repo")];
        fakeGitHub.PullRequestsByRepository["example-owner/second-repo"] = [];
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IGitHubApiClient>();
                services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:GitHub:Repositories:1"] = "example-owner/second-repo"
            });
        var client = app.CreateClient();

        var job = await PostAsync<GitHubSyncJobDto>(client, "/api/v1/sources/github/sync-jobs", new { });

        GitHubSyncJobDto queued;
        try
        {
            GitHubSyncJobDto? active = null;
            await WaitUntilAsync(async () =>
            {
                active = await client.GetFromJsonAsync<GitHubSyncJobDto>($"/api/v1/sources/github/sync-jobs/{job.JobId}", JsonOptions);
                return active!.Repositories.Any(x => x.Repository == "example-owner/second-repo" && x.Status == GitHubSyncRepositoryStatus.Succeeded);
            });
            Assert.NotEqual(GitHubSyncRepositoryStatus.Succeeded,
                Assert.Single(active!.Repositories, x => x.Repository == "example-owner/oratorio").Status);
            queued = await PostAsync<GitHubSyncJobDto>(client, "/api/v1/sources/github/sync-jobs",
                new GitHubSyncJobRequest(GitHubSyncMode.Full, ["example-owner/second-repo"]));
            Assert.NotEqual(job.JobId, queued.JobId);
            Assert.Equal(GitHubSyncStatus.Queued,
                (await client.GetFromJsonAsync<GitHubSyncJobDto>($"/api/v1/sources/github/sync-jobs/{queued.JobId}", JsonOptions))!.Status);
        }
        finally
        {
            firstRepositoryRelease.TrySetResult();
        }

        var completed = await WaitForGitHubSyncJobAsync(client, job.JobId);
        Assert.Equal(GitHubSyncStatus.Succeeded, completed.Status);
        Assert.Equal(["example-owner/oratorio", "example-owner/second-repo"], completed.Repositories.Select(x => x.Repository).ToArray());
        Assert.Equal(GitHubSyncStatus.Succeeded, (await WaitForGitHubSyncJobAsync(client, queued.JobId)).Status);
    }
}
