using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using Microsoft.EntityFrameworkCore;

namespace DotCraft.Oratorio.Integrations;

public sealed partial class AppServerRunWorker
{
    private async Task<string> PrepareExecutionWorkspaceAsync(string runId, string baseWorkspacePath, CancellationToken ct)
    {
        if (!options.CurrentValue.ManagedWorktreesEnabled)
        {
            await MarkWorktreeNotRequiredAsync(runId, baseWorkspacePath, ct);
            return baseWorkspacePath;
        }

        try
        {
            WorktreePrepareRequest request;
            using (var scope = scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
                var run = await LoadRunAsync(db, runId, ct);
                var now = clock.UtcNow;
                run.BaseWorkspacePath = baseWorkspacePath;
                run.WorktreeStatus = WorktreeStatus.Preparing;
                run.WorktreeErrorCode = null;
                run.WorktreeErrorMessage = null;
                run.ProgressPercent = Math.Max(run.ProgressPercent, 8);
                run.StatusMessage = "Preparing managed Git worktree.";
                run.LastHeartbeatAt = now;
                run.Item!.UpdatedAt = now;
                await db.SaveChangesAsync(ct);

                var item = run.Item;
                var headSha = item.HeadSha;
                if (RequiresReviewDraft(run))
                {
                    headSha = run.TargetHeadSha;
                    if (string.IsNullOrWhiteSpace(headSha))
                        throw new WorktreeException("reviewTargetHeadUnresolved", "The review run has no target commit SHA.");
                }

                string? stackOntoBranch = null;
                string? stackOntoSha = null;
                if (run.Purpose == RunPurpose.Implementation && run.DispatchTrigger == RunDispatchTrigger.AutoFollowUp)
                {
                    var generatedPr = await db.Items.AsNoTracking()
                        .Where(x =>
                            x.ParentItemId == item.ItemId &&
                            x.Kind == ItemKind.PullRequest &&
                            (x.Source == "github" || x.Source == "gitlab") &&
                            x.SourceState == SourceState.Open)
                        .OrderByDescending(x => x.CreatedAt)
                        .FirstOrDefaultAsync(ct);
                    if (generatedPr is not null)
                    {
                        stackOntoBranch = generatedPr.Branch;
                        stackOntoSha = generatedPr.HeadSha;
                    }
                }

                request = new WorktreePrepareRequest(
                    run.RunId,
                    item.ItemId,
                    item.Source,
                    item.ExternalId,
                    item.Repository,
                    item.Branch,
                    headSha,
                    baseWorkspacePath,
                    stackOntoBranch,
                    stackOntoSha);
            }

            var result = await worktreeManager.PrepareAsync(request, ct);
            await MarkWorktreeReadyAsync(runId, result, ct);
            return result.WorktreePath;
        }
        catch (WorktreeException ex)
        {
            await MarkWorktreeFailedAsync(runId, baseWorkspacePath, ex.Code, ex.Message, ct);
            throw;
        }
    }

    private async Task MarkWorktreeNotRequiredAsync(string runId, string baseWorkspacePath, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await LoadRunAsync(db, runId, ct);
        if (IsTerminal(run.Status))
        {
            return;
        }

        run.BaseWorkspacePath = baseWorkspacePath;
        run.WorktreeStatus = WorktreeStatus.NotRequired;
        run.LastHeartbeatAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task MarkWorktreeReadyAsync(string runId, WorktreePrepareResult result, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await LoadRunAsync(db, runId, ct);
        if (IsTerminal(run.Status))
        {
            return;
        }

        var now = clock.UtcNow;
        run.BaseWorkspacePath = result.BaseWorkspacePath;
        run.WorktreePath = result.WorktreePath;
        run.WorktreeBranch = result.WorktreeBranch;
        run.BaseRef = result.BaseRef;
        run.BaseSha = result.BaseSha;
        run.WorktreeStatus = WorktreeStatus.Ready;
        run.WorktreeErrorCode = null;
        run.WorktreeErrorMessage = null;
        run.ProgressPercent = Math.Max(run.ProgressPercent, 15);
        run.StatusMessage = "Managed Git worktree is ready.";
        run.LastHeartbeatAt = now;
        run.Item!.UpdatedAt = now;
        AddTimeline(run, TimelineEventKind.RunStarted, ActorKind.System, "oratorio/worktree", "Managed worktree ready", result.WorktreePath, now);
        await db.SaveChangesAsync(ct);
    }

    private async Task MarkWorktreeFailedAsync(string runId, string baseWorkspacePath, string code, string message, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await LoadRunAsync(db, runId, ct);
        if (IsTerminal(run.Status))
        {
            return;
        }

        var now = clock.UtcNow;
        run.BaseWorkspacePath = baseWorkspacePath;
        run.WorktreeStatus = WorktreeStatus.Failed;
        run.WorktreeErrorCode = code;
        run.WorktreeErrorMessage = message;
        run.ProgressPercent = 100;
        run.StatusMessage = message;
        run.LastHeartbeatAt = now;
        run.Item!.UpdatedAt = now;
        AddTimeline(run, TimelineEventKind.RunFailed, ActorKind.System, "oratorio/worktree", "Managed worktree preparation failed", message, now);
        await db.SaveChangesAsync(ct);
    }
}
