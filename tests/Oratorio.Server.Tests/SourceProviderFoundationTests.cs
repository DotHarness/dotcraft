using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Oratorio.Server.Api;
using Oratorio.Server.Data;
using Oratorio.Server.Domain;
using Oratorio.Server.Services;
using Oratorio.Server.Sources;

namespace Oratorio.Server.Tests;

public sealed class SourceProviderFoundationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-19T09:00:00Z");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public void SourceProjectKey_NormalizesCanonicalAndUnqualifiedGitHubKeys()
    {
        Assert.True(SourceProjectKey.TryParse("gitlab:gitlab.company.test/group/subgroup/project", out var gitLab));
        Assert.Equal("gitlab", gitLab.Provider);
        Assert.Equal("gitlab.company.test", gitLab.Instance);
        Assert.Equal("group/subgroup/project", gitLab.ProjectPath);
        Assert.Equal("gitlab:gitlab.company.test/group/subgroup/project", gitLab.Key);

        var gitHub = SourceProjectKey.FromGitHubRepository("example-owner/oratorio", "https://api.github.com");
        Assert.Equal("github:github.com/example-owner/oratorio", gitHub.Key);
        Assert.Equal("example-owner/oratorio", SourceProjectKey.NormalizeGitHubRepository("https://github.com/example-owner/oratorio.git"));
        Assert.True(SourceProjectKey.AreEquivalent("example-owner/oratorio", "github:github.com/example-owner/oratorio"));
    }

    [Fact]
    public async Task GitLabShapedFakeProvider_PassesProviderContractShape()
    {
        var provider = new FakeSourceProvider("gitlab", "https://gitlab.company.test", "group/subgroup/project");
        var registry = new SourceProviderRegistry([provider]);

        var statuses = await registry.GetStatusesAsync(CancellationToken.None);
        var status = Assert.Single(statuses);
        var project = Assert.Single(status.Projects);

        Assert.Equal("gitlab", status.Provider);
        Assert.True(status.Configured);
        Assert.True(status.ReadCapability.Available);
        Assert.False(status.WriteCapability.Available);
        Assert.Equal("group/subgroup/project", project.ProjectPath);
        Assert.Equal("gitlab:gitlab.company.test/group/subgroup/project", project.Key);
    }

    [Fact]
    public void SourceSyncMapper_ProjectsGitHubJobAsSourceNeutralReviewTargets()
    {
        var run = new GitHubSyncRepositoryRunDto(
            "run-1",
            "job-1",
            "example-owner/oratorio",
            GitHubSyncRepositoryStatus.Succeeded,
            GitHubSyncRepositoryPhase.Done,
            2,
            3,
            1,
            2,
            4,
            0,
            null,
            null,
            Now,
            Now,
            Now,
            Now);
        var job = new GitHubSyncJobDto(
            "job-1",
            GitHubSyncTrigger.Manual,
            GitHubSyncMode.Incremental,
            GitHubSyncStatus.Succeeded,
            1,
            1,
            0,
            1,
            2,
            4,
            0,
            null,
            null,
            Now,
            Now,
            Now,
            Now,
            [run]);

        var sourceJob = SourceSyncMapper.FromGitHub(job, "https://api.github.com");
        var sourceRun = Assert.Single(sourceJob.Projects);

        Assert.Equal("github", sourceJob.Provider);
        Assert.Equal(SourceSyncTrigger.Manual, sourceJob.Trigger);
        Assert.Equal(SourceSyncStatus.Succeeded, sourceJob.Status);
        Assert.Equal(2, sourceJob.ReviewTargetsImported);
        Assert.Equal("github:github.com/example-owner/oratorio", sourceRun.SourceProjectKey);
        Assert.Equal(3, sourceRun.ReviewTargetsDiscovered);
        Assert.Equal(2, sourceRun.ReviewTargetsImported);
    }

    [Fact]
    public void SourceWriteDto_ExposesCanonicalKind_ForProviderPayloads()
    {
        var write = new OratorioSourceWriteLog
        {
            WriteId = "write-1",
            ItemId = "item-1",
            Source = "github",
            Kind = SourceWriteKind.CheckRun,
            Intent = "approve",
            Status = SourceWriteStatus.Succeeded,
            RequestJson = """{"name":"oratorio/review","conclusion":"success"}""",
            CreatedAt = Now,
            UpdatedAt = Now
        };

        var dto = write.ToDto();

        Assert.Equal(SourceWriteKind.CheckRun, dto.Kind);
        Assert.Equal(SourceWriteCanonicalKinds.ExternalStatus, dto.CanonicalKind);
        Assert.Equal("approve", dto.Intent);
    }

    [Fact]
    public async Task SourcesEndpoint_ReturnsGitHubProviderProjection()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        var sources = await client.GetFromJsonAsync<SourcesResponse>("/api/v1/sources", JsonOptions);

        Assert.NotNull(sources);
        var gitHub = Assert.Single(sources!.Providers, x => x.Provider == "github");
        Assert.Equal("GitHub", gitHub.DisplayName);
        Assert.Equal("githubApp", gitHub.AuthenticationState);
        Assert.True(gitHub.ReadCapability.Available);
        Assert.True(gitHub.WriteCapability.Available);
        Assert.Equal(1, gitHub.ConfiguredProjectCount);
        var project = Assert.Single(gitHub.Projects);
        Assert.Equal("github:github.com/example-owner/oratorio", project.Key);
    }

    [Fact]
    public async Task SourceNeutralSyncJob_EnqueuesGitHubJob_WithCanonicalProjectKey()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        var job = await client.PostAsJsonAsync(
            "/api/v1/sources/sync-jobs",
            new SourceSyncJobRequest("github", SourceSyncMode.Incremental, ["github:github.com/example-owner/oratorio"]),
            JsonOptions);
        job.EnsureSuccessStatusCode();
        var body = await job.Content.ReadFromJsonAsync<SourceSyncJobDto>(JsonOptions);

        Assert.NotNull(body);
        Assert.Equal("github", body!.Provider);
        Assert.Equal(SourceSyncStatus.Queued, body.Status);
        Assert.Equal(1, body.ProjectsTotal);
        var project = Assert.Single(body.Projects);
        Assert.Equal("github:github.com/example-owner/oratorio", project.SourceProjectKey);
        Assert.Equal("example-owner/oratorio", project.ProjectPath);
    }

    [Fact]
    public void SourceSyncMapper_PreservesScheduledGitHubTrigger()
    {
        var job = new GitHubSyncJobDto(
            "job-1",
            GitHubSyncTrigger.Scheduled,
            GitHubSyncMode.Incremental,
            GitHubSyncStatus.Queued,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            null,
            null,
            Now,
            Now,
            null,
            null,
            []);

        var sourceJob = SourceSyncMapper.FromGitHub(job, "https://api.github.com");

        Assert.Equal(SourceSyncTrigger.Scheduled, sourceJob.Trigger);
    }

    [Fact]
    public async Task SourceSyncSchedules_ReturnDefaultsAndEnableWithFiveMinuteNextRun()
    {
        var clock = new MutableClock(DateTimeOffset.Parse("2026-05-20T08:00:00Z"));
        await using var app = CreateScheduleTestApp(clock);
        var client = app.CreateClient();

        var schedules = await client.GetFromJsonAsync<SourceSyncSchedulesResponse>(
            "/api/v1/sources/sync-schedules",
            JsonOptions);

        Assert.NotNull(schedules);
        var gitHub = Assert.Single(schedules!.Schedules, x => x.Provider == "github");
        Assert.False(gitHub.Enabled);
        Assert.Equal(300, gitHub.IntervalSeconds);
        Assert.Null(gitHub.NextRunAt);

        var response = await client.PutAsJsonAsync(
            "/api/v1/sources/github/sync-schedule",
            new SourceSyncScheduleUpdateRequest(true),
            JsonOptions);
        response.EnsureSuccessStatusCode();
        var enabled = await response.Content.ReadFromJsonAsync<SourceSyncScheduleDto>(JsonOptions);

        Assert.NotNull(enabled);
        Assert.True(enabled!.Enabled);
        Assert.Equal(300, enabled.IntervalSeconds);
        Assert.Equal(clock.UtcNow.AddMinutes(5), enabled.NextRunAt);
        Assert.True(enabled.ReadAvailable);
    }

    [Fact]
    public async Task SourceSyncSchedule_RejectsIntervalsBelowOneMinute()
    {
        await using var app = CreateScheduleTestApp(new MutableClock(Now));
        var client = app.CreateClient();

        var response = await client.PutAsJsonAsync(
            "/api/v1/sources/github/sync-schedule",
            new SourceSyncScheduleUpdateRequest(true, 59),
            JsonOptions);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validationFailed", error?.Error.Code);
    }

    [Fact]
    public async Task SourceSyncScheduler_EnqueuesScheduledIncrementalJobWhenDue()
    {
        var clock = new MutableClock(DateTimeOffset.Parse("2026-05-20T08:00:00Z"));
        await using var app = CreateScheduleTestApp(clock);
        using var scope = app.Services.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<SourceSyncSchedulerService>();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();

        await scheduler.UpdateScheduleAsync("github", new SourceSyncScheduleUpdateRequest(true), CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(5));

        var processed = await scheduler.ProcessDueSchedulesAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        var job = await db.GitHubSyncJobs.AsNoTracking().SingleAsync();
        Assert.Equal(GitHubSyncTrigger.Scheduled, job.Trigger);
        Assert.Equal(GitHubSyncMode.Incremental, job.Mode);
        var schedule = await db.SourceSyncSchedules.AsNoTracking().SingleAsync(x => x.Provider == "github");
        Assert.Equal(job.JobId, schedule.LastJobId);
        Assert.Equal(clock.UtcNow, schedule.LastScheduledAt);
        Assert.Equal(clock.UtcNow.AddMinutes(5), schedule.NextRunAt);
        Assert.Null(schedule.LastErrorCode);
    }

    [Fact]
    public async Task SourceSyncScheduler_CoalescesDueScheduleWithActiveProviderJob()
    {
        var clock = new MutableClock(DateTimeOffset.Parse("2026-05-20T08:00:00Z"));
        await using var app = CreateScheduleTestApp(clock);
        using var scope = app.Services.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<SourceSyncSchedulerService>();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();

        await scheduler.UpdateScheduleAsync("github", new SourceSyncScheduleUpdateRequest(true), CancellationToken.None);
        db.GitHubSyncJobs.Add(new OratorioGitHubSyncJob
        {
            JobId = "active-job",
            Trigger = GitHubSyncTrigger.Manual,
            Mode = GitHubSyncMode.Full,
            Status = GitHubSyncStatus.Running,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
            StartedAt = clock.UtcNow
        });
        await db.SaveChangesAsync();
        clock.Advance(TimeSpan.FromMinutes(5));

        var processed = await scheduler.ProcessDueSchedulesAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.Equal(1, await db.GitHubSyncJobs.CountAsync());
        var schedule = await db.SourceSyncSchedules.AsNoTracking().SingleAsync(x => x.Provider == "github");
        Assert.Equal("active-job", schedule.LastJobId);
        Assert.Equal(clock.UtcNow.AddMinutes(5), schedule.NextRunAt);
    }

    [Fact]
    public async Task SourceSyncScheduler_RecordsFailureWhenReadCapabilityIsUnavailable()
    {
        var clock = new MutableClock(DateTimeOffset.Parse("2026-05-20T08:00:00Z"));
        await using var app = CreateScheduleTestApp(
            clock,
            settings: new Dictionary<string, string?>
            {
                ["Oratorio:GitHub:Repositories:0"] = ""
            });
        using var scope = app.Services.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<SourceSyncSchedulerService>();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        db.SourceSyncSchedules.Add(new OratorioSourceSyncSchedule
        {
            Provider = "github",
            Enabled = true,
            IntervalSeconds = 300,
            NextRunAt = clock.UtcNow,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        });
        await db.SaveChangesAsync();

        var processed = await scheduler.ProcessDueSchedulesAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.False(await db.GitHubSyncJobs.AnyAsync());
        var schedule = await db.SourceSyncSchedules.AsNoTracking().SingleAsync(x => x.Provider == "github");
        Assert.Equal("sourceSyncScheduleUnavailable", schedule.LastErrorCode);
        Assert.Equal(clock.UtcNow.AddMinutes(5), schedule.NextRunAt);
    }

    private sealed class FakeSourceProvider(string provider, string endpoint, string projectPath) : ISourceProvider
    {
        public string Provider { get; } = provider;

        public Task<SourceProviderStatusDto> GetStatusAsync(CancellationToken ct)
        {
            var uri = new Uri(endpoint);
            var key = new SourceProjectKey(Provider, uri.Host, projectPath);
            return Task.FromResult(new SourceProviderStatusDto(
                Provider,
                "GitLab",
                endpoint,
                true,
                "token",
                new SourceProviderCapabilityDto(true, "available", null),
                new SourceProviderCapabilityDto(false, "disabled", "Writes are not enabled in the fake provider."),
                new SourceProviderCapabilityDto(false, "unconfigured", "Webhook verification is not configured."),
                1,
                null,
                null,
                [new SourceProjectDto(Provider, key.Instance, key.ProjectPath, key.Key, key.ProjectPath)]));
        }
    }

    private static TestOratorioApp CreateScheduleTestApp(
        MutableClock clock,
        IReadOnlyDictionary<string, string?>? settings = null) =>
        new(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(clock);
        }, settings);
}
