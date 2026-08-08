using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oratorio.Server.Data;
using Oratorio.Server.Domain;
using Oratorio.Server.Services;

namespace Oratorio.Server.DotCraft;

public sealed class WorktreeCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IWorktreeManager worktreeManager,
    IClock clock,
    IOptionsMonitor<DotCraftOptions> options,
    ILogger<WorktreeCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var current = options.CurrentValue;
            var delay = current.WorktreeCleanupEnabled
                ? current.WorktreeCleanupInterval
                : TimeSpan.FromSeconds(1);
            try
            {
                await Task.Delay(delay, stoppingToken);
                if (options.CurrentValue.WorktreeCleanupEnabled)
                {
                    await CleanupDueWorktreesAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Managed worktree cleanup tick failed.");
            }
        }
    }

    private async Task CleanupDueWorktreesAsync(CancellationToken ct)
    {
        List<string> runIds;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            var now = clock.UtcNow;
            runIds = await db.Runs.AsNoTracking()
                .Where(x =>
                    x.RunnerKind == "appServer" &&
                    x.WorktreePath != null &&
                    x.WorktreeStatus == WorktreeStatus.CleanupPending &&
                    x.WorktreeCleanupAfterAt != null &&
                    x.WorktreeCleanupAfterAt <= now)
                .OrderBy(x => x.WorktreeCleanupAfterAt)
                .Select(x => x.RunId)
                .Take(10)
                .ToListAsync(ct);
        }

        foreach (var runId in runIds)
        {
            await CleanupRunWorktreeAsync(runId, ct);
        }
    }

    private async Task CleanupRunWorktreeAsync(string runId, CancellationToken ct)
    {
        WorktreeCleanupRequest request;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            var run = await db.Runs.FirstOrDefaultAsync(x => x.RunId == runId, ct);
            if (run is null || run.WorktreeStatus != WorktreeStatus.CleanupPending)
            {
                return;
            }

            request = new WorktreeCleanupRequest(run.RunId, run.BaseWorkspacePath, run.WorktreePath);
        }

        try
        {
            await worktreeManager.CleanupAsync(request, ct);
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            var run = await db.Runs.FirstOrDefaultAsync(x => x.RunId == runId, ct);
            if (run is null)
            {
                return;
            }

            run.WorktreeStatus = WorktreeStatus.Cleaned;
            run.WorktreeCleanedAt = clock.UtcNow;
            run.WorktreeErrorCode = null;
            run.WorktreeErrorMessage = null;
            await db.SaveChangesAsync(ct);
        }
        catch (WorktreeException ex)
        {
            await MarkCleanupFailedAsync(runId, ex.Code, ex.Message, ct);
        }
    }

    private async Task MarkCleanupFailedAsync(string runId, string code, string message, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await db.Runs.FirstOrDefaultAsync(x => x.RunId == runId, ct);
        if (run is null)
        {
            return;
        }

        run.WorktreeStatus = WorktreeStatus.Failed;
        run.WorktreeErrorCode = code;
        run.WorktreeErrorMessage = message;
        await db.SaveChangesAsync(ct);
    }
}
