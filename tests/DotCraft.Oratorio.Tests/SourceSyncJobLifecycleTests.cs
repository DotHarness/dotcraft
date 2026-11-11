using System.Net;
using System.Net.Http.Json;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.GitLab;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using static DotCraft.Oratorio.Tests.OratorioApiTestSupport;

namespace DotCraft.Oratorio.Tests;

public sealed class SourceSyncJobLifecycleTests
{
    private const string FirstProject = "example-owner/oratorio";
    private const string SecondProject = "example-owner/second-repo";

    [Theory]
    [InlineData("github")]
    [InlineData("gitlab")]
    public async Task NewRequest_RunsItsOwnProjectsAndModeDespiteStaleRunningJob(string provider)
    {
        await using var app = CreateApp();
        var client = app.CreateClient();
        var stale = await PostAsync<SourceSyncJobDto>(client, "/api/v1/sources/sync-jobs",
            new SourceSyncJobRequest(provider, SourceSyncMode.Incremental, [FirstProject]));
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            if (provider == "github")
                await db.GitHubSyncJobs.Where(x => x.JobId == stale.JobId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, GitHubSyncStatus.Running));
            else
                await db.GitLabSyncJobs.Where(x => x.JobId == stale.JobId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SourceSyncStatus.Running));
        }

        var requested = await PostAsync<SourceSyncJobDto>(client, "/api/v1/sources/sync-jobs",
            new SourceSyncJobRequest(provider, SourceSyncMode.Full, [SecondProject]));

        Assert.NotEqual(stale.JobId, requested.JobId);
        Assert.Equal(SourceSyncMode.Full, requested.Mode);
        Assert.Equal(SecondProject, Assert.Single(requested.Projects).ProjectPath);
        await ProcessNextAsync(app, provider);
        var completed = await ReadJobAsync(client, provider, requested.JobId);
        Assert.Equal(SourceSyncStatus.Succeeded, completed.Status);
        Assert.NotNull(completed.CompletedAt);
    }

    [Theory]
    [InlineData("github")]
    [InlineData("gitlab")]
    public async Task JobFailure_PreservesCompletedProjectsAndAllowsNextJob(string provider)
    {
        var failure = new ClaimFailure();
        await using var app = CreateApp(failure);
        var client = app.CreateClient();
        var job = await PostAsync<SourceSyncJobDto>(client, "/api/v1/sources/sync-jobs",
            new SourceSyncJobRequest(provider, SourceSyncMode.Full, [FirstProject, SecondProject]));
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            if (provider == "github")
            {
                var run = await db.GitHubSyncRepositoryRuns.SingleAsync(x => x.JobId == job.JobId && x.Repository == FirstProject);
                run.Status = GitHubSyncRepositoryStatus.Succeeded;
                run.Phase = GitHubSyncRepositoryPhase.Done;
                run.IssuesImported = 3;
                run.CompletedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                var run = await db.GitLabSyncProjectRuns.SingleAsync(x => x.JobId == job.JobId && x.ProjectPath == FirstProject);
                run.Status = SourceSyncProjectStatus.Succeeded;
                run.Phase = SourceSyncProjectPhase.Done;
                run.IssuesImported = 3;
                run.CompletedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync();
        }
        failure.JobId = job.JobId;

        await ProcessNextAsync(app, provider);

        var failed = await ReadJobAsync(client, provider, job.JobId);
        Assert.Equal(SourceSyncStatus.PartialFailed, failed.Status);
        Assert.Equal(2, failed.ProjectsCompleted);
        Assert.Equal(1, failed.ProjectsFailed);
        Assert.Equal(3, failed.IssuesImported);
        Assert.Equal(SourceSyncProjectStatus.Succeeded, Assert.Single(failed.Projects, x => x.ProjectPath == FirstProject).Status);
        var unfinished = Assert.Single(failed.Projects, x => x.ProjectPath == SecondProject);
        Assert.Equal(SourceSyncProjectStatus.Failed, unfinished.Status);
        Assert.Equal("Synthetic job claim failure.", unfinished.ErrorMessage);
        Assert.Equal(provider == "github" ? "githubSyncFailed" : "gitLabSyncFailed", failed.ErrorCode);
        Assert.All(failed.Projects, x => Assert.NotNull(x.CompletedAt));

        var next = await PostAsync<SourceSyncJobDto>(client, "/api/v1/sources/sync-jobs",
            new SourceSyncJobRequest(provider, SourceSyncMode.Incremental, [SecondProject]));
        await ProcessNextAsync(app, provider);
        Assert.Equal(SourceSyncStatus.Succeeded, (await ReadJobAsync(client, provider, next.JobId)).Status);
    }

    private static TestOratorioApp CreateApp(ClaimFailure? failure = null) =>
        new(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IGitHubApiClient>();
            var gitHub = new FakeGitHubApiClient();
            gitHub.Issues.Clear();
            gitHub.PullRequests.Clear();
            services.AddSingleton<IGitHubApiClient>(gitHub);
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new StaticHttpClientFactory(new HttpClient(new CapturingHandler(request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                var project = Uri.UnescapeDataString(path.Split('/').Last());
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = path.EndsWith("/issues") || path.EndsWith("/merge_requests")
                        ? JsonContent.Create(Array.Empty<object>())
                        : JsonContent.Create(new { id = 1, path_with_namespace = project })
                };
            }))));
            if (failure is not null)
                services.AddDbContext<OratorioDbContext>(options => options.AddInterceptors(failure));
        }, new Dictionary<string, string?>
        {
            ["Oratorio:GitLab:Enabled"] = "true",
            ["Oratorio:GitLab:Endpoint"] = "https://gitlab.example.test",
            ["Oratorio:GitLab:Projects:0"] = FirstProject,
            ["Oratorio:GitLab:Projects:1"] = SecondProject,
            ["Oratorio:GitLab:ProjectProfiles:0:Instance"] = "gitlab.example.test",
            ["Oratorio:GitLab:ProjectProfiles:0:ProjectPath"] = FirstProject,
            ["Oratorio:GitLab:ProjectProfiles:0:Token"] = "test-token",
            ["Oratorio:GitLab:ProjectProfiles:1:Instance"] = "gitlab.example.test",
            ["Oratorio:GitLab:ProjectProfiles:1:ProjectPath"] = SecondProject,
            ["Oratorio:GitLab:ProjectProfiles:1:Token"] = "test-token"
        });

    private static async Task ProcessNextAsync(TestOratorioApp app, string provider)
    {
        if (provider == "github")
            await app.Services.GetRequiredService<GitHubSyncCoordinator>().ProcessNextQueuedJobAsync(CancellationToken.None);
        else
            await app.Services.GetRequiredService<GitLabSyncCoordinator>().ProcessNextQueuedJobAsync(CancellationToken.None);
    }

    private static async Task<SourceSyncJobDto> ReadJobAsync(HttpClient client, string provider, string jobId) =>
        (await client.GetFromJsonAsync<SourceSyncJobDto>($"/api/v1/sources/sync-jobs/{jobId}?provider={provider}", JsonOptions))!;

    private sealed class ClaimFailure : SaveChangesInterceptor
    {
        public string? JobId { get; set; }

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            var claimed = eventData.Context!.ChangeTracker.Entries<OratorioGitHubSyncJob>()
                .Any(x => x.Entity.JobId == JobId && x.Entity.Status == GitHubSyncStatus.Running) ||
                eventData.Context.ChangeTracker.Entries<OratorioGitLabSyncJob>()
                .Any(x => x.Entity.JobId == JobId && x.Entity.Status == SourceSyncStatus.Running);
            if (claimed)
            {
                JobId = null;
                throw new InvalidOperationException("Synthetic job claim failure.");
            }
            return ValueTask.FromResult(result);
        }
    }
}
