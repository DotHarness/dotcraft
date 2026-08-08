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

namespace Oratorio.Server.GitLab;

public sealed class GitLabSyncCoordinator(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<GitLabOptions> options,
    IClock clock,
    BoardEventHub boardEvents,
    ILogger<GitLabSyncCoordinator> logger)
{
    private const int MaxConcurrentProjects = 2;
    private static readonly SourceSyncStatus[] ActiveStatuses = [SourceSyncStatus.Queued, SourceSyncStatus.Running];
    private static readonly SourceSyncProjectStatus[] TerminalProjectStatuses = [SourceSyncProjectStatus.Succeeded, SourceSyncProjectStatus.Failed];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<SourceSyncJobDto?> GetActiveJobAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var job = await LoadJobQuery(db)
            .Where(x => ActiveStatuses.Contains(x.Status))
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return job is null ? null : ToDto(job, options.CurrentValue.Endpoint);
    }

    public async Task<SourceSyncJobDto?> GetJobAsync(string jobId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var job = await LoadJobQuery(db).FirstOrDefaultAsync(x => x.JobId == jobId, ct);
        return job is null ? null : ToDto(job, options.CurrentValue.Endpoint);
    }

    public async Task<SourceSyncJobDto> EnqueueAsync(
        SourceSyncTrigger trigger,
        SourceSyncMode mode,
        IReadOnlyList<string>? projectPaths,
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
            return ToDto(active, options.CurrentValue.Endpoint);
        }

        var now = clock.UtcNow;
        var projects = ResolveProjects(projectPaths).ToArray();
        var job = new OratorioGitLabSyncJob
        {
            Trigger = trigger,
            Mode = mode,
            Status = projects.Length == 0 ? SourceSyncStatus.Succeeded : SourceSyncStatus.Queued,
            ProjectsTotal = projects.Length,
            CreatedAt = now,
            UpdatedAt = now,
            CompletedAt = projects.Length == 0 ? now : null
        };

        foreach (var project in projects)
        {
            job.ProjectRuns.Add(new OratorioGitLabSyncProjectRun
            {
                JobId = job.JobId,
                ProjectPath = project.ProjectPath,
                Status = SourceSyncProjectStatus.Queued,
                Phase = SourceSyncProjectPhase.Queued,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        db.GitLabSyncJobs.Add(job);
        await db.SaveChangesAsync(ct);
        var dto = ToDto(job, options.CurrentValue.Endpoint);
        PublishJob(dto);
        return dto;
    }

    public async Task<SourceSyncJobDto?> ProcessNextQueuedJobAsync(CancellationToken ct)
    {
        OratorioGitLabSyncJob? job;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            job = await LoadJobQuery(db)
                .Where(x => x.Status == SourceSyncStatus.Queued)
                .OrderBy(x => x.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (job is null)
            {
                return null;
            }

            var now = clock.UtcNow;
            job.Status = SourceSyncStatus.Running;
            job.StartedAt = now;
            job.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            PublishJob(ToDto(job, options.CurrentValue.Endpoint));
        }

        await ProcessJobAsync(job.JobId, job.Mode, ct);
        return await GetJobAsync(job.JobId, ct);
    }

    public async Task<SourceSyncJobDto> WaitForCompletionAsync(string jobId, CancellationToken ct)
    {
        while (true)
        {
            var job = await GetJobAsync(jobId, ct)
                ?? throw new InvalidOperationException($"GitLab sync job {jobId} was not found.");
            if (!ActiveStatuses.Contains(job.Status))
            {
                return job;
            }

            await Task.Delay(200, ct);
        }
    }

    public async Task RecoverInterruptedJobsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var jobs = await db.GitLabSyncJobs
            .Include(x => x.ProjectRuns)
            .Where(x => x.Status == SourceSyncStatus.Running)
            .ToListAsync(ct);
        if (jobs.Count == 0)
        {
            return;
        }

        var now = clock.UtcNow;
        foreach (var job in jobs)
        {
            foreach (var run in job.ProjectRuns.Where(x => !TerminalProjectStatuses.Contains(x.Status)))
            {
                run.Status = SourceSyncProjectStatus.Failed;
                run.Phase = SourceSyncProjectPhase.Failed;
                run.ErrorCode = "syncInterrupted";
                run.ErrorMessage = "GitLab sync was interrupted before this project completed.";
                run.UpdatedAt = now;
                run.CompletedAt = now;
            }

            ApplyAggregate(job, now);
            PublishJob(ToDto(job, options.CurrentValue.Endpoint));
            foreach (var run in job.ProjectRuns)
            {
                PublishProjectRun(ToDto(run, options.CurrentValue.Endpoint));
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public static SourceSyncJobDto ToDto(OratorioGitLabSyncJob job, string? endpoint) =>
        new(
            job.JobId,
            "gitlab",
            job.Trigger,
            job.Mode,
            job.Status,
            job.ProjectsTotal,
            job.ProjectsCompleted,
            job.ProjectsFailed,
            job.IssuesImported,
            job.MergeRequestsImported,
            job.CommentsImported,
            job.Skipped,
            job.ErrorCode,
            job.ErrorMessage,
            job.CreatedAt,
            job.UpdatedAt,
            job.StartedAt,
            job.CompletedAt,
            job.ProjectRuns.OrderBy(x => x.CreatedAt).ThenBy(x => x.ProjectPath).Select(x => ToDto(x, endpoint)).ToArray());

    public static SourceSyncProjectRunDto ToDto(OratorioGitLabSyncProjectRun run, string? endpoint)
    {
        var key = SourceProjectKey.FromGitLabProject(run.ProjectPath, endpoint);
        return new SourceSyncProjectRunDto(
            run.ProjectRunId,
            run.JobId,
            "gitlab",
            key.Key,
            key.ProjectPath,
            run.ProjectPath,
            run.Status,
            run.Phase,
            run.IssuesDiscovered,
            run.MergeRequestsDiscovered,
            run.IssuesImported,
            run.MergeRequestsImported,
            run.CommentsImported,
            run.Skipped,
            run.ErrorCode,
            run.ErrorMessage,
            run.CreatedAt,
            run.UpdatedAt,
            run.StartedAt,
            run.CompletedAt);
    }

    private async Task ProcessJobAsync(string jobId, SourceSyncMode mode, CancellationToken ct)
    {
        IReadOnlyList<string> runIds;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            runIds = await db.GitLabSyncProjectRuns
                .Where(x => x.JobId == jobId)
                .OrderBy(x => x.CreatedAt)
                .Select(x => x.ProjectRunId)
                .ToArrayAsync(ct);
        }

        using var gate = new SemaphoreSlim(MaxConcurrentProjects, MaxConcurrentProjects);
        var tasks = runIds.Select(async runId =>
        {
            await gate.WaitAsync(ct);
            try
            {
                await ProcessProjectRunAsync(jobId, runId, mode, ct);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        await UpdateJobAggregateAsync(jobId, ct);
    }

    private async Task ProcessProjectRunAsync(string jobId, string projectRunId, SourceSyncMode mode, CancellationToken ct)
    {
        try
        {
            GitLabProjectRef project;
            DateTimeOffset? since;
            using (var scope = scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
                var run = await db.GitLabSyncProjectRuns.FirstAsync(x => x.ProjectRunId == projectRunId, ct);
                if (!GitLabProjectRef.TryParse(run.ProjectPath, out project))
                {
                    throw new InvalidOperationException($"Project '{run.ProjectPath}' is not a valid GitLab path.");
                }

                var now = clock.UtcNow;
                run.Status = SourceSyncProjectStatus.Running;
                run.Phase = SourceSyncProjectPhase.Fetching;
                run.StartedAt = now;
                run.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
                PublishProjectRun(ToDto(run, options.CurrentValue.Endpoint));

                since = await ResolveIncrementalSinceAsync(db, jobId, run.ProjectPath, mode, ct);
            }

            using (var scope = scopeFactory.CreateScope())
            {
                var source = scope.ServiceProvider.GetRequiredService<GitLabSourceService>();
                var progress = new ProjectProgress(this, projectRunId);
                var result = await source.SyncProjectAsync(project, clock.UtcNow, since, mode, progress, ct);
                await CompleteProjectRunAsync(projectRunId, result, ct);
            }
        }
        catch (GitLabCredentialException ex)
        {
            logger.LogWarning(ex, "GitLab sync credentials failed for project run {ProjectRunId}.", projectRunId);
            await FailProjectRunAsync(projectRunId, ex.Code, ex.Message, ct);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "GitLab sync was canceled for project run {ProjectRunId}.", projectRunId);
            await FailProjectRunAsync(projectRunId, "gitLabSyncFailed", ex.Message, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "GitLab sync failed for project run {ProjectRunId}.", projectRunId);
            await FailProjectRunAsync(projectRunId, "gitLabSyncFailed", ex.Message, ct);
        }
        finally
        {
            await UpdateJobAggregateAsync(jobId, ct);
        }
    }

    private static async Task<DateTimeOffset?> ResolveIncrementalSinceAsync(
        OratorioDbContext db,
        string jobId,
        string projectPath,
        SourceSyncMode mode,
        CancellationToken ct)
    {
        if (mode == SourceSyncMode.Full)
        {
            return null;
        }

        var priorRun = await db.GitLabSyncProjectRuns
            .Where(x => x.JobId != jobId &&
                x.ProjectPath == projectPath &&
                x.Status == SourceSyncProjectStatus.Succeeded &&
                x.CompletedAt != null)
            .OrderByDescending(x => x.CompletedAt)
            .Select(x => new { x.StartedAt, x.CompletedAt })
            .FirstOrDefaultAsync(ct);
        var since = priorRun?.StartedAt ?? priorRun?.CompletedAt;
        return since?.AddMinutes(-5);
    }

    private async Task CompleteProjectRunAsync(string projectRunId, GitLabProjectSyncResult result, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await db.GitLabSyncProjectRuns.FirstAsync(x => x.ProjectRunId == projectRunId, ct);
        var now = clock.UtcNow;
        run.Status = SourceSyncProjectStatus.Succeeded;
        run.Phase = SourceSyncProjectPhase.Done;
        run.IssuesDiscovered = result.IssuesDiscovered;
        run.MergeRequestsDiscovered = result.MergeRequestsDiscovered;
        run.IssuesImported = result.Issues;
        run.MergeRequestsImported = result.MergeRequests;
        run.CommentsImported = result.Comments;
        run.Skipped = result.Skipped;
        run.ErrorCode = null;
        run.ErrorMessage = null;
        run.UpdatedAt = now;
        run.CompletedAt = now;
        await db.SaveChangesAsync(ct);
        PublishProjectRun(ToDto(run, options.CurrentValue.Endpoint));
    }

    private async Task FailProjectRunAsync(string projectRunId, string code, string message, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await db.GitLabSyncProjectRuns.FirstAsync(x => x.ProjectRunId == projectRunId, ct);
        var now = clock.UtcNow;
        run.Status = SourceSyncProjectStatus.Failed;
        run.Phase = SourceSyncProjectPhase.Failed;
        run.ErrorCode = code;
        run.ErrorMessage = message;
        run.UpdatedAt = now;
        run.CompletedAt = now;
        await db.SaveChangesAsync(ct);
        PublishProjectRun(ToDto(run, options.CurrentValue.Endpoint));
    }

    private async Task UpdateProjectProgressAsync(
        string projectRunId,
        SourceSyncProjectPhase? phase,
        int? issuesDiscovered,
        int? mergeRequestsDiscovered,
        int? issuesImported,
        int? mergeRequestsImported,
        int? commentsImported,
        int? skipped,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await db.GitLabSyncProjectRuns.FirstAsync(x => x.ProjectRunId == projectRunId, ct);
        if (phase is not null)
        {
            run.Phase = phase.Value;
        }

        run.IssuesDiscovered = issuesDiscovered ?? run.IssuesDiscovered;
        run.MergeRequestsDiscovered = mergeRequestsDiscovered ?? run.MergeRequestsDiscovered;
        run.IssuesImported = issuesImported ?? run.IssuesImported;
        run.MergeRequestsImported = mergeRequestsImported ?? run.MergeRequestsImported;
        run.CommentsImported = commentsImported ?? run.CommentsImported;
        run.Skipped = skipped ?? run.Skipped;
        run.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        PublishProjectRun(ToDto(run, options.CurrentValue.Endpoint));
    }

    private async Task UpdateJobAggregateAsync(string jobId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var job = await db.GitLabSyncJobs.Include(x => x.ProjectRuns).FirstAsync(x => x.JobId == jobId, ct);
        ApplyAggregate(job, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        PublishJob(ToDto(job, options.CurrentValue.Endpoint));
    }

    private static void ApplyAggregate(OratorioGitLabSyncJob job, DateTimeOffset now)
    {
        var runs = job.ProjectRuns;
        job.ProjectsCompleted = runs.Count(x => TerminalProjectStatuses.Contains(x.Status));
        job.ProjectsFailed = runs.Count(x => x.Status == SourceSyncProjectStatus.Failed);
        job.IssuesImported = runs.Sum(x => x.IssuesImported);
        job.MergeRequestsImported = runs.Sum(x => x.MergeRequestsImported);
        job.CommentsImported = runs.Sum(x => x.CommentsImported);
        job.Skipped = runs.Sum(x => x.Skipped);
        job.UpdatedAt = now;

        if (job.ProjectsCompleted < job.ProjectsTotal)
        {
            job.Status = SourceSyncStatus.Running;
            return;
        }

        job.CompletedAt ??= now;
        job.Status = job.ProjectsFailed switch
        {
            0 => SourceSyncStatus.Succeeded,
            var failed when failed == job.ProjectsTotal => SourceSyncStatus.Failed,
            _ => SourceSyncStatus.PartialFailed
        };
        var firstFailure = runs.FirstOrDefault(x => x.Status == SourceSyncProjectStatus.Failed);
        job.ErrorCode = firstFailure?.ErrorCode;
        job.ErrorMessage = firstFailure?.ErrorMessage;
    }

    private IReadOnlyList<GitLabProjectRef> ResolveProjects(IReadOnlyList<string>? projectPaths)
    {
        var values = projectPaths is { Count: > 0 }
            ? projectPaths
            : options.CurrentValue.Projects;
        return values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => GitLabProjectRef.TryParse(x, out var project) ? project : null)
            .Where(x => x is not null)
            .Cast<GitLabProjectRef>()
            .DistinctBy(x => x.ProjectPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IQueryable<OratorioGitLabSyncJob> LoadJobQuery(OratorioDbContext db) =>
        db.GitLabSyncJobs.Include(x => x.ProjectRuns);

    private void PublishJob(SourceSyncJobDto job) =>
        boardEvents.PublishSourceSync(new SourceSyncEvent(
            SourceSyncEvent.SourceSyncJobUpdatedType,
            JsonSerializer.SerializeToElement(job, JsonOptions),
            clock.UtcNow));

    private void PublishProjectRun(SourceSyncProjectRunDto run) =>
        boardEvents.PublishSourceSync(new SourceSyncEvent(
            SourceSyncEvent.SourceSyncProjectUpdatedType,
            JsonSerializer.SerializeToElement(run, JsonOptions),
            clock.UtcNow));

    private sealed class ProjectProgress(GitLabSyncCoordinator coordinator, string projectRunId) : IGitLabProjectSyncProgress
    {
        private int _lastImportedTotal;

        public Task SetPhaseAsync(SourceSyncProjectPhase phase, CancellationToken ct) =>
            coordinator.UpdateProjectProgressAsync(projectRunId, phase, null, null, null, null, null, null, ct);

        public Task SetDiscoveredAsync(int issues, int mergeRequests, int skipped, CancellationToken ct) =>
            coordinator.UpdateProjectProgressAsync(projectRunId, null, issues, mergeRequests, null, null, null, skipped, ct);

        public Task SetImportedAsync(int issues, int mergeRequests, int comments, int skipped, CancellationToken ct)
        {
            var total = issues + mergeRequests;
            if (_lastImportedTotal > 0 && total < _lastImportedTotal + 10)
            {
                return Task.CompletedTask;
            }

            _lastImportedTotal = total;
            return coordinator.UpdateProjectProgressAsync(projectRunId, null, null, null, issues, mergeRequests, comments, skipped, ct);
        }
    }
}
