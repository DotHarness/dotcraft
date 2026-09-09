using DotCraft.Automations.Protocol;
using DotCraft.Sessions;
using Microsoft.Extensions.Logging;

namespace DotCraft.Automations;

public sealed partial class AutomationService
{
    private DateTimeOffset _lastRetentionSweep;
    private async Task SweepWorktreesAsync(CancellationToken ct)
    {
        if (!config.WorktreeRetentionEnabled || _client is not IAutomationWorktreeRetentionClient retention
            || DateTimeOffset.UtcNow - _lastRetentionSweep < TimeSpan.FromDays(1)) return;
        _lastRetentionSweep = DateTimeOffset.UtcNow;
        var threshold = DateTimeOffset.UtcNow - (config.WorktreeRetentionIdlePeriod < TimeSpan.FromDays(14)
            ? TimeSpan.FromDays(14) : config.WorktreeRetentionIdlePeriod);
        foreach (var worktree in await retention.ListManagedWorktreesAsync(ct))
        {
            if (worktree.Worktree.OwnerKind != "automationRun" || worktree.Worktree.OwnerId == null
                || worktree.Worktree.CreatedAt > threshold || worktree.HasUncommittedChanges || worktree.HasCommitsAheadOfBase
                || !worktree.Exists || !worktree.IsGitWorktree) continue;
            var thread = await _client.TryGetThreadAsync(worktree.ThreadId, ct);
            if (thread == null || thread.LastActiveAt > threshold
                || thread.Turns.Any(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput)) continue;
            try { await retention.RemoveRunWorktreeAsync(worktree.Worktree.OwnerId, worktree.ThreadId, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogWarning(ex, "Unable to remove idle automation worktree {Id}", worktree.Worktree.Id); }
        }
    }
}
