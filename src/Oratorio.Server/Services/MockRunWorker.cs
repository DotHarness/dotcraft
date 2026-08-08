using Microsoft.EntityFrameworkCore;
using Oratorio.Server.Data;
using Oratorio.Server.Domain;
using Oratorio.Server.GitHub;
using Oratorio.Server.Realtime;

namespace Oratorio.Server.Services;

public sealed class MockRunWorker(IServiceScopeFactory scopeFactory, IClock clock, BoardEventHub boardEvents, ILogger<MockRunWorker> logger) : BackgroundService
{
    private static readonly RunStatus[] ActiveStatuses = [RunStatus.Queued, RunStatus.Dispatching, RunStatus.Running];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedRunsAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await AdvanceRunsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Mock run worker tick failed.");
            }
        }
    }

    private async Task RecoverInterruptedRunsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var activeRuns = await LoadActiveRuns(db, ct);
        if (activeRuns.Count == 0)
        {
            return;
        }

        var now = clock.UtcNow;
        foreach (var run in activeRuns)
        {
            FailRun(
                run,
                now,
                RunStatus.Failed,
                "runnerInterrupted",
                "The mock runner was interrupted before it completed.",
                "Mock run interrupted");
        }

        await RecordFailedReviewGatesAsync(scope, activeRuns, ct);
        await db.SaveChangesAsync(ct);
        PublishRunItems(activeRuns, now);
    }

    private async Task AdvanceRunsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var activeRuns = await LoadActiveRuns(db, ct);
        if (activeRuns.Count == 0)
        {
            return;
        }

        var now = clock.UtcNow;
        foreach (var run in activeRuns)
        {
            AdvanceRun(run, now);
        }

        var runsStillOwnedByThisTick = await FilterRunsStillOwnedByThisTickAsync(db, activeRuns, ct);
        if (runsStillOwnedByThisTick.Count == 0)
        {
            return;
        }

        await RecordCompletedReviewGatesAsync(scope, runsStillOwnedByThisTick, ct);
        await RecordFailedReviewGatesAsync(scope, runsStillOwnedByThisTick, ct);
        await db.SaveChangesAsync(ct);
        PublishRunItems(runsStillOwnedByThisTick, now);
    }

    private static async Task<List<OratorioRun>> LoadActiveRuns(OratorioDbContext db, CancellationToken ct) =>
        await db.Runs
            .Include(x => x.Item)
            .Include(x => x.Round)
            .Where(x => x.RunnerKind == "mock" && ActiveStatuses.Contains(x.Status))
            .ToListAsync(ct);

    private static async Task<IReadOnlyList<OratorioRun>> FilterRunsStillOwnedByThisTickAsync(
        OratorioDbContext db,
        IReadOnlyList<OratorioRun> runs,
        CancellationToken ct)
    {
        var runIds = runs.Select(x => x.RunId).ToArray();
        var currentStatuses = await db.Runs
            .AsNoTracking()
            .Where(x => runIds.Contains(x.RunId))
            .ToDictionaryAsync(x => x.RunId, x => x.Status, ct);
        var retained = new List<OratorioRun>(runs.Count);
        foreach (var run in runs)
        {
            if (!currentStatuses.TryGetValue(run.RunId, out var currentStatus) || !ActiveStatuses.Contains(currentStatus))
            {
                db.Entry(run).State = EntityState.Unchanged;
                if (run.Item is not null)
                {
                    db.Entry(run.Item).State = EntityState.Unchanged;
                }

                if (run.Round is not null)
                {
                    db.Entry(run.Round).State = EntityState.Unchanged;
                }

                continue;
            }

            retained.Add(run);
        }

        return retained;
    }

    private void PublishRunItems(IEnumerable<OratorioRun> runs, DateTimeOffset timestamp)
    {
        foreach (var item in runs.Select(x => x.Item).OfType<OratorioItem>().DistinctBy(x => x.ItemId))
        {
            boardEvents.PublishTaskUpdated(item, timestamp);
        }
    }

    private static async Task RecordFailedReviewGatesAsync(IServiceScope scope, IEnumerable<OratorioRun> runs, CancellationToken ct)
    {
        var writes = scope.ServiceProvider.GetRequiredService<GitHubWriteService>();
        foreach (var run in runs.Where(x => x.Item?.State == ItemState.Failed))
        {
            await writes.RecordReviewGateRunFailedAsync(run, ct);
        }
    }

    private static async Task RecordCompletedReviewGatesAsync(IServiceScope scope, IEnumerable<OratorioRun> runs, CancellationToken ct)
    {
        var writes = scope.ServiceProvider.GetRequiredService<GitHubWriteService>();
        foreach (var run in runs.Where(x => x.Item?.State == ItemState.AwaitingReview))
        {
            await writes.RecordReviewGateRunCompletedAsync(run, ct);
        }
    }

    private static void AdvanceRun(OratorioRun run, DateTimeOffset now)
    {
        var item = run.Item;
        var round = run.Round;
        if (item is null || round is null)
        {
            return;
        }

        var startedAt = run.StartedAt ?? now;
        var duration = TimeSpan.FromSeconds(Math.Clamp(run.MockDurationSeconds, 1, 120));
        var elapsedRatio = Math.Clamp((now - startedAt).TotalMilliseconds / duration.TotalMilliseconds, 0, 1);

        if (elapsedRatio >= 1)
        {
            CompleteRun(run, now);
            return;
        }

        if (run.Status == RunStatus.Queued && elapsedRatio >= 0.2)
        {
            run.Status = RunStatus.Dispatching;
            run.StatusMessage = "Preparing mock workspace.";
            run.ProgressPercent = Math.Max(run.ProgressPercent, 12);
        }
        else if (run.Status is RunStatus.Queued)
        {
            run.StatusMessage = "Queued for mock runner.";
            run.ProgressPercent = Math.Max(run.ProgressPercent, 5);
        }

        if (run.Status is RunStatus.Dispatching && elapsedRatio >= 0.35)
        {
            run.Status = RunStatus.Running;
            item.State = ItemState.Running;
            round.Status = RoundStatus.Running;
            AddTimeline(item, round, run, TimelineEventKind.RunStarted, ActorKind.MockRunner, "Mock runner", "Run started", "Mock execution started.", now);
        }

        if (run.Status == RunStatus.Running)
        {
            run.StatusMessage = "Mock runner is reviewing changes.";
            run.ProgressPercent = Math.Clamp((int)Math.Round(25 + elapsedRatio * 70), 25, 95);
        }

        item.UpdatedAt = now;
        run.LastHeartbeatAt = now;
    }

    private static void CompleteRun(OratorioRun run, DateTimeOffset now)
    {
        switch (run.MockOutcome)
        {
            case MockOutcome.Fail:
                FailRun(
                    run,
                    now,
                    RunStatus.Failed,
                    "mockFailed",
                    "The mock runner failed this validation pass.",
                    "Mock run failed");
                break;
            case MockOutcome.Timeout:
                FailRun(
                    run,
                    now,
                    RunStatus.TimedOut,
                    "mockTimedOut",
                    "The mock runner timed out before producing review output.",
                    "Mock run timed out");
                break;
            default:
                SucceedRun(run, now);
                break;
        }
    }

    private static void SucceedRun(OratorioRun run, DateTimeOffset now)
    {
        var item = run.Item!;
        var round = run.Round!;
        var summary = $"Mock review completed for round {round.RoundNumber}. Review output is ready for operator judgment.";

        run.Status = RunStatus.Succeeded;
        run.CompletedAt = now;
        run.Summary = summary;
        run.ProgressPercent = 100;
        run.StatusMessage = "Review output is ready.";
        run.LastHeartbeatAt = now;

        round.Status = RoundStatus.AwaitingReview;
        round.Summary = summary;
        round.CompletedAt = now;

        item.State = ItemState.AwaitingReview;
        item.CurrentRunId = null;
        item.LatestSummary = summary;
        item.CheckState = CheckState.Attention;
        item.UpdatedAt = now;

        AddTimeline(item, round, run, TimelineEventKind.RunCompleted, ActorKind.MockRunner, "Mock runner", "Run completed", summary, now);
        AddTimeline(item, round, run, TimelineEventKind.CheckUpdated, ActorKind.System, "oratorio/review", "Check waiting on review", "The mock run completed and needs operator review.", now);
    }

    private static void FailRun(OratorioRun run, DateTimeOffset now, RunStatus status, string errorCode, string errorMessage, string title)
    {
        var item = run.Item!;
        var round = run.Round!;

        run.Status = status;
        run.CompletedAt = now;
        run.ErrorCode = errorCode;
        run.ErrorMessage = errorMessage;
        run.Summary = errorMessage;
        run.ProgressPercent = 100;
        run.StatusMessage = errorMessage;
        run.LastHeartbeatAt = now;

        round.Status = RoundStatus.Failed;
        round.Summary = errorMessage;
        round.CompletedAt = now;

        item.State = ItemState.Failed;
        item.CurrentRunId = null;
        item.LatestSummary = errorMessage;
        item.CheckState = CheckState.Failing;
        item.UpdatedAt = now;

        AddTimeline(item, round, run, TimelineEventKind.RunFailed, ActorKind.MockRunner, "Mock runner", title, errorMessage, now);
        AddTimeline(item, round, run, TimelineEventKind.CheckUpdated, ActorKind.System, "oratorio/review", "Check failed", errorMessage, now);
    }

    private static void AddTimeline(
        OratorioItem item,
        OratorioRound round,
        OratorioRun run,
        TimelineEventKind kind,
        ActorKind actorKind,
        string actorName,
        string title,
        string? body,
        DateTimeOffset createdAt)
    {
        item.TimelineEvents.Add(new OratorioTimelineEvent
        {
            ItemId = item.ItemId,
            RoundId = round.RoundId,
            RunId = run.RunId,
            Kind = kind,
            ActorKind = actorKind,
            ActorName = actorName,
            Title = title,
            Body = body,
            CreatedAt = createdAt
        });
    }
}
