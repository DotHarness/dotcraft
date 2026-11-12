using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Oratorio.Integrations;

public sealed partial class AppServerRunWorker
{
    private async Task<AppServerThreadReuseCandidate?> FindCompatibleThreadAsync(
        string runId,
        string baseWorkspacePath,
        string executionWorkspacePath,
        IReadOnlyList<string> requiredDynamicTools,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await db.Runs.AsNoTracking().FirstAsync(x => x.RunId == runId, ct);

        var recoveringImplementation = run.Purpose == RunPurpose.Implementation && run.Attempt > 1;
        if (recoveringImplementation || run.RetryCount > 0)
        {
            var attempts = db.Runs.AsNoTracking()
                .Where(x => x.RoundId == run.RoundId && x.Attempt < run.Attempt &&
                    x.RunnerKind == "appServer" && x.ThreadId != null && x.PromptContextJson != null);
            attempts = recoveringImplementation
                ? attempts.Where(x => x.Purpose == RunPurpose.Implementation &&
                    (x.Status == RunStatus.Failed || x.Status == RunStatus.TimedOut))
                : attempts.Where(x => x.Attempt == run.Attempt - 1);
            var previousAttempt = await attempts.OrderByDescending(x => x.Attempt).FirstOrDefaultAsync(ct);
            if (previousAttempt is not null &&
                (recoveringImplementation || IsReviewThreadRecoveryError(previousAttempt.ErrorCode) &&
                    SameHead(previousAttempt.TargetHeadSha, run.TargetHeadSha)) &&
                ConversationThreadContext.IsReusable(previousAttempt.PromptContextJson!,
                    baseWorkspacePath, executionWorkspacePath, requiredDynamicTools))
            {
                return new AppServerThreadReuseCandidate(previousAttempt.ThreadId!,
                    $"failed attempt {ShortId(previousAttempt.RunId)}", IsRetryRecovery: true);
            }

            if (run.RetryCount > 0)
                return null;
        }

        var candidates = await scope.ServiceProvider.GetRequiredService<ConversationThreadLocator>()
            .ListAsync(run.ItemId, excludeRunId: runId, ct);
        return candidates
            .Where(x => ConversationThreadContext.IsReusable(x.ContextJson, baseWorkspacePath, executionWorkspacePath, requiredDynamicTools))
            .Select(x => new AppServerThreadReuseCandidate(x.ThreadId, x.Origin, IsRetryRecovery: false))
            .FirstOrDefault();
    }
}
