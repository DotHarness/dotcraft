using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oratorio.Server.Api;
using Oratorio.Server.Data;
using Oratorio.Server.Domain;
using Oratorio.Server.Realtime;
using Oratorio.Server.Services;
using Oratorio.Server.Sources;

namespace Oratorio.Server.GitHub;

public sealed class GitHubSyncCoordinator(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<GitHubOptions> options,
    IClock clock,
    BoardEventHub boardEvents,
    ILogger<GitHubSyncCoordinator> logger)
{
    private const int MaxConcurrentRepositories = 2;
    private static readonly GitHubSyncStatus[] ActiveStatuses = [GitHubSyncStatus.Queued, GitHubSyncStatus.Running];
    private static readonly GitHubSyncRepositoryStatus[] TerminalRepositoryStatuses = [GitHubSyncRepositoryStatus.Succeeded, GitHubSyncRepositoryStatus.Failed];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<GitHubSyncJobDto?> GetActiveJobAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var job = await LoadJobQuery(db)
            .Where(x => ActiveStatuses.Contains(x.Status))
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return job is null ? null : ToDto(job);
    }

    public async Task<GitHubSyncJobDto?> GetJobAsync(string jobId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var job = await LoadJobQuery(db).FirstOrDefaultAsync(x => x.JobId == jobId, ct);
        return job is null ? null : ToDto(job);
    }

    public async Task<GitHubSyncJobDto> EnqueueAsync(
        GitHubSyncTrigger trigger,
        GitHubSyncMode mode,
        IReadOnlyList<string>? repositoryFullNames,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var active = await LoadJobQuery(db)
            .Where(x => ActiveStatuses.Contains(x.Status))
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (active is not null)
        {
            return ToDto(active);
        }

        var now = clock.UtcNow;
        var repositories = ResolveRepositories(repositoryFullNames).ToArray();
        var job = new OratorioGitHubSyncJob
        {
            Trigger = trigger,
            Mode = mode,
            Status = repositories.Length == 0 ? GitHubSyncStatus.Succeeded : GitHubSyncStatus.Queued,
            RepositoriesTotal = repositories.Length,
            RepositoriesCompleted = repositories.Length == 0 ? 0 : 0,
            CreatedAt = now,
            UpdatedAt = now,
            CompletedAt = repositories.Length == 0 ? now : null
        };

        foreach (var repository in repositories)
        {
            job.RepositoryRuns.Add(new OratorioGitHubSyncRepositoryRun
            {
                JobId = job.JobId,
                Repository = repository.FullName,
                Status = GitHubSyncRepositoryStatus.Queued,
                Phase = GitHubSyncRepositoryPhase.Queued,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        db.GitHubSyncJobs.Add(job);
        await db.SaveChangesAsync(ct);
        var dto = ToDto(job);
        PublishJob(dto);
        return dto;
    }

    public async Task<GitHubSyncJobDto> WaitForCompletionAsync(string jobId, CancellationToken ct)
    {
        while (true)
        {
            var job = await GetJobAsync(jobId, ct)
                ?? throw new InvalidOperationException($"GitHub sync job {jobId} was not found.");
            if (!ActiveStatuses.Contains(job.Status))
            {
                return job;
            }

            await Task.Delay(200, ct);
        }
    }

    public async Task<GitHubSyncJobDto?> ProcessNextQueuedJobAsync(CancellationToken ct)
    {
        OratorioGitHubSyncJob? job;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            job = await LoadJobQuery(db)
                .Where(x => x.Status == GitHubSyncStatus.Queued)
                .OrderBy(x => x.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (job is null)
            {
                return null;
            }

            var now = clock.UtcNow;
            job.Status = GitHubSyncStatus.Running;
            job.StartedAt = now;
            job.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            PublishJob(ToDto(job));
        }

        await ProcessJobAsync(job.JobId, job.Mode, ct);
        return await GetJobAsync(job.JobId, ct);
    }

    public async Task RecoverInterruptedJobsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var jobs = await db.GitHubSyncJobs
            .Include(x => x.RepositoryRuns)
            .Where(x => x.Status == GitHubSyncStatus.Running)
            .ToListAsync(ct);
        if (jobs.Count == 0)
        {
            return;
        }

        var now = clock.UtcNow;
        foreach (var job in jobs)
        {
            foreach (var run in job.RepositoryRuns.Where(x => !TerminalRepositoryStatuses.Contains(x.Status)))
            {
                run.Status = GitHubSyncRepositoryStatus.Failed;
                run.Phase = GitHubSyncRepositoryPhase.Failed;
                run.ErrorCode = "syncInterrupted";
                run.ErrorMessage = "GitHub sync was interrupted before this repository completed.";
                run.UpdatedAt = now;
                run.CompletedAt = now;
            }

            ApplyAggregate(job, now);
            PublishJob(ToDto(job));
            foreach (var run in job.RepositoryRuns)
            {
                PublishRepositoryRun(ToDto(run));
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public static GitHubSyncResponse ToSyncResponse(GitHubSyncJobDto job) =>
        new(
            job.Repositories.Select(x => x.Repository).ToArray(),
            job.IssuesImported,
            job.PullRequestsImported,
            job.CommentsImported,
            job.Skipped,
            job.Repositories
                .Where(x => x.Status == GitHubSyncRepositoryStatus.Failed)
                .Select(x => new GitHubSyncErrorDto(x.Repository, x.ErrorCode ?? "githubSyncFailed", x.ErrorMessage ?? "GitHub sync failed."))
                .ToArray(),
            job.CompletedAt ?? job.UpdatedAt);

    public static GitHubSyncJobDto ToDto(OratorioGitHubSyncJob job) =>
        new(
            job.JobId,
            job.Trigger,
            job.Mode,
            job.Status,
            job.RepositoriesTotal,
            job.RepositoriesCompleted,
            job.RepositoriesFailed,
            job.IssuesImported,
            job.PullRequestsImported,
            job.CommentsImported,
            job.Skipped,
            job.ErrorCode,
            job.ErrorMessage,
            job.CreatedAt,
            job.UpdatedAt,
            job.StartedAt,
            job.CompletedAt,
            job.RepositoryRuns.OrderBy(x => x.CreatedAt).ThenBy(x => x.Repository).Select(ToDto).ToArray());

    public static GitHubSyncRepositoryRunDto ToDto(OratorioGitHubSyncRepositoryRun run) =>
        new(
            run.RepositoryRunId,
            run.JobId,
            run.Repository,
            run.Status,
            run.Phase,
            run.IssuesDiscovered,
            run.PullRequestsDiscovered,
            run.IssuesImported,
            run.PullRequestsImported,
            run.CommentsImported,
            run.Skipped,
            run.ErrorCode,
            run.ErrorMessage,
            run.CreatedAt,
            run.UpdatedAt,
            run.StartedAt,
            run.CompletedAt);

    private async Task ProcessJobAsync(string jobId, GitHubSyncMode mode, CancellationToken ct)
    {
        IReadOnlyList<string> runIds;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            runIds = await db.GitHubSyncRepositoryRuns
                .Where(x => x.JobId == jobId)
                .OrderBy(x => x.CreatedAt)
                .Select(x => x.RepositoryRunId)
                .ToArrayAsync(ct);
        }

        using var gate = new SemaphoreSlim(MaxConcurrentRepositories, MaxConcurrentRepositories);
        var tasks = runIds.Select(async runId =>
        {
            await gate.WaitAsync(ct);
            try
            {
                await ProcessRepositoryRunAsync(jobId, runId, mode, ct);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        await UpdateJobAggregateAsync(jobId, ct);
    }

    private async Task ProcessRepositoryRunAsync(string jobId, string repositoryRunId, GitHubSyncMode mode, CancellationToken ct)
    {
        try
        {
            GitHubRepositoryRef repository;
            DateTimeOffset? since;
            using (var scope = scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
                var run = await db.GitHubSyncRepositoryRuns.FirstAsync(x => x.RepositoryRunId == repositoryRunId, ct);
                if (!GitHubRepositoryRef.TryParse(run.Repository, out repository))
                {
                    throw new InvalidOperationException($"Repository '{run.Repository}' is not in owner/name form.");
                }

                var now = clock.UtcNow;
                run.Status = GitHubSyncRepositoryStatus.Running;
                run.Phase = GitHubSyncRepositoryPhase.Fetching;
                run.StartedAt = now;
                run.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
                PublishRepositoryRun(ToDto(run));

                since = await ResolveIncrementalSinceAsync(db, jobId, run.Repository, mode, ct);
            }

            using (var scope = scopeFactory.CreateScope())
            {
                var source = scope.ServiceProvider.GetRequiredService<GitHubSourceService>();
                var progress = new RepositoryProgress(this, repositoryRunId);
                var result = await source.SyncRepositoryAsync(repository, clock.UtcNow, since, mode, progress, ct);
                await CompleteRepositoryRunAsync(repositoryRunId, result, ct);
            }
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "GitHub sync was canceled for repository run {RepositoryRunId}.", repositoryRunId);
            await FailRepositoryRunAsync(repositoryRunId, "githubSyncFailed", ex.Message, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "GitHub sync failed for repository run {RepositoryRunId}.", repositoryRunId);
            var code = ex is GitHubAppAuthenticationRequiredException authRequired
                ? authRequired.ErrorCode
                : "githubSyncFailed";
            await FailRepositoryRunAsync(repositoryRunId, code, ex.Message, ct);
        }
        finally
        {
            await UpdateJobAggregateAsync(jobId, ct);
        }
    }

    private static async Task<DateTimeOffset?> ResolveIncrementalSinceAsync(
        OratorioDbContext db,
        string jobId,
        string repository,
        GitHubSyncMode mode,
        CancellationToken ct)
    {
        if (mode == GitHubSyncMode.Full)
        {
            return null;
        }

        var priorRun = await db.GitHubSyncRepositoryRuns
            .Where(x => x.JobId != jobId &&
                x.Repository == repository &&
                x.Status == GitHubSyncRepositoryStatus.Succeeded &&
                x.CompletedAt != null)
            .OrderByDescending(x => x.CompletedAt)
            .Select(x => new { x.StartedAt, x.CompletedAt })
            .FirstOrDefaultAsync(ct);
        var since = priorRun?.StartedAt ?? priorRun?.CompletedAt;
        return since?.AddMinutes(-5);
    }

    private async Task CompleteRepositoryRunAsync(string repositoryRunId, GitHubRepositorySyncResult result, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await db.GitHubSyncRepositoryRuns.FirstAsync(x => x.RepositoryRunId == repositoryRunId, ct);
        var now = clock.UtcNow;
        run.Status = GitHubSyncRepositoryStatus.Succeeded;
        run.Phase = GitHubSyncRepositoryPhase.Done;
        run.IssuesDiscovered = result.IssuesDiscovered;
        run.PullRequestsDiscovered = result.PullRequestsDiscovered;
        run.IssuesImported = result.Issues;
        run.PullRequestsImported = result.PullRequests;
        run.CommentsImported = result.Comments;
        run.Skipped = result.Skipped;
        run.ErrorCode = null;
        run.ErrorMessage = null;
        run.UpdatedAt = now;
        run.CompletedAt = now;
        await db.SaveChangesAsync(ct);
        PublishRepositoryRun(ToDto(run));
    }

    private async Task FailRepositoryRunAsync(string repositoryRunId, string code, string message, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await db.GitHubSyncRepositoryRuns.FirstAsync(x => x.RepositoryRunId == repositoryRunId, ct);
        var now = clock.UtcNow;
        run.Status = GitHubSyncRepositoryStatus.Failed;
        run.Phase = GitHubSyncRepositoryPhase.Failed;
        run.ErrorCode = code;
        run.ErrorMessage = message;
        run.UpdatedAt = now;
        run.CompletedAt = now;
        await db.SaveChangesAsync(ct);
        PublishRepositoryRun(ToDto(run));
    }

    private async Task UpdateRepositoryProgressAsync(
        string repositoryRunId,
        GitHubSyncRepositoryPhase? phase,
        int? issuesDiscovered,
        int? pullRequestsDiscovered,
        int? issuesImported,
        int? pullRequestsImported,
        int? commentsImported,
        int? skipped,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await db.GitHubSyncRepositoryRuns.FirstAsync(x => x.RepositoryRunId == repositoryRunId, ct);
        if (phase is not null)
        {
            run.Phase = phase.Value;
        }

        run.IssuesDiscovered = issuesDiscovered ?? run.IssuesDiscovered;
        run.PullRequestsDiscovered = pullRequestsDiscovered ?? run.PullRequestsDiscovered;
        run.IssuesImported = issuesImported ?? run.IssuesImported;
        run.PullRequestsImported = pullRequestsImported ?? run.PullRequestsImported;
        run.CommentsImported = commentsImported ?? run.CommentsImported;
        run.Skipped = skipped ?? run.Skipped;
        run.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        PublishRepositoryRun(ToDto(run));
    }

    private async Task UpdateJobAggregateAsync(string jobId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var job = await db.GitHubSyncJobs.Include(x => x.RepositoryRuns).FirstAsync(x => x.JobId == jobId, ct);
        ApplyAggregate(job, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        PublishJob(ToDto(job));
    }

    private static void ApplyAggregate(OratorioGitHubSyncJob job, DateTimeOffset now)
    {
        var runs = job.RepositoryRuns;
        job.RepositoriesCompleted = runs.Count(x => TerminalRepositoryStatuses.Contains(x.Status));
        job.RepositoriesFailed = runs.Count(x => x.Status == GitHubSyncRepositoryStatus.Failed);
        job.IssuesImported = runs.Sum(x => x.IssuesImported);
        job.PullRequestsImported = runs.Sum(x => x.PullRequestsImported);
        job.CommentsImported = runs.Sum(x => x.CommentsImported);
        job.Skipped = runs.Sum(x => x.Skipped);
        job.UpdatedAt = now;

        if (job.RepositoriesCompleted < job.RepositoriesTotal)
        {
            job.Status = GitHubSyncStatus.Running;
            return;
        }

        job.CompletedAt ??= now;
        job.Status = job.RepositoriesFailed switch
        {
            0 => GitHubSyncStatus.Succeeded,
            var failed when failed == job.RepositoriesTotal => GitHubSyncStatus.Failed,
            _ => GitHubSyncStatus.PartialFailed
        };
        var firstFailure = runs.FirstOrDefault(x => x.Status == GitHubSyncRepositoryStatus.Failed);
        job.ErrorCode = firstFailure?.ErrorCode;
        job.ErrorMessage = firstFailure?.ErrorMessage;
    }

    private IReadOnlyList<GitHubRepositoryRef> ResolveRepositories(IReadOnlyList<string>? repositoryFullNames)
    {
        var values = repositoryFullNames is { Count: > 0 }
            ? repositoryFullNames
            : options.CurrentValue.Repositories;
        return values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => GitHubRepositoryRef.TryParse(x, out var repository) ? repository : null)
            .Where(x => x is not null)
            .Cast<GitHubRepositoryRef>()
            .DistinctBy(x => x.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IQueryable<OratorioGitHubSyncJob> LoadJobQuery(OratorioDbContext db) =>
        db.GitHubSyncJobs.Include(x => x.RepositoryRuns);

    private void PublishJob(GitHubSyncJobDto job)
    {
        boardEvents.PublishSourceSync(new SourceSyncEvent(
            SourceSyncEvent.GitHubSyncJobUpdatedType,
            JsonSerializer.SerializeToElement(job, JsonOptions),
            clock.UtcNow));
        boardEvents.PublishSourceSync(new SourceSyncEvent(
            SourceSyncEvent.SourceSyncJobUpdatedType,
            JsonSerializer.SerializeToElement(SourceSyncMapper.FromGitHub(job, options.CurrentValue.Endpoint), JsonOptions),
            clock.UtcNow));
    }

    private void PublishRepositoryRun(GitHubSyncRepositoryRunDto run)
    {
        boardEvents.PublishSourceSync(new SourceSyncEvent(
            SourceSyncEvent.GitHubSyncRepositoryUpdatedType,
            JsonSerializer.SerializeToElement(run, JsonOptions),
            clock.UtcNow));
        boardEvents.PublishSourceSync(new SourceSyncEvent(
            SourceSyncEvent.SourceSyncProjectUpdatedType,
            JsonSerializer.SerializeToElement(SourceSyncMapper.FromGitHub(run, options.CurrentValue.Endpoint), JsonOptions),
            clock.UtcNow));
    }

    private sealed class RepositoryProgress(GitHubSyncCoordinator coordinator, string repositoryRunId) : IGitHubRepositorySyncProgress
    {
        private int _lastImportedTotal;

        public Task SetPhaseAsync(GitHubSyncRepositoryPhase phase, CancellationToken ct) =>
            coordinator.UpdateRepositoryProgressAsync(repositoryRunId, phase, null, null, null, null, null, null, ct);

        public Task SetDiscoveredAsync(int issues, int pullRequests, int skipped, CancellationToken ct) =>
            coordinator.UpdateRepositoryProgressAsync(repositoryRunId, null, issues, pullRequests, null, null, null, skipped, ct);

        public Task SetImportedAsync(int issues, int pullRequests, int comments, int skipped, CancellationToken ct)
        {
            var total = issues + pullRequests;
            if (_lastImportedTotal > 0 && total < _lastImportedTotal + 10)
            {
                return Task.CompletedTask;
            }

            _lastImportedTotal = total;
            return coordinator.UpdateRepositoryProgressAsync(repositoryRunId, null, null, null, issues, pullRequests, comments, skipped, ct);
        }
    }
}
