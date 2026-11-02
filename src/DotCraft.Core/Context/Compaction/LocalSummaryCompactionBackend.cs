using Microsoft.Extensions.AI;

namespace DotCraft.Context.Compaction;

internal sealed class LocalSummaryCompactionBackend(CompactionPipeline pipeline) : ICompactionBackend
{
    public string Id => CompactionBackendIds.LocalSummary;

    public async Task<CompactionExecutionResult> ExecuteAsync(
        CompactionExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = request.Trigger switch
        {
            CompactionTrigger.Auto => await pipeline.TryAutoCompactHistoryAsync(
                    request.NeutralHistory,
                    request.ThreadId,
                    request.InputTokenHint,
                    request.LastAssistantTimestampUtc,
                    cancellationToken,
                    request.PromptSnapshot,
                    turnEnd: request.Phase == CompactionPhase.PostTurn)
                .ConfigureAwait(false),
            CompactionTrigger.Manual => await pipeline.TryManualCompactHistoryAsync(
                    request.NeutralHistory,
                    request.ThreadId,
                    request.LastAssistantTimestampUtc,
                    cancellationToken,
                    request.InputTokenHint > 0 ? request.InputTokenHint : null,
                    request.PromptSnapshot,
                    request.FallbackTools,
                    request.CarryRequestOverhead)
                .ConfigureAwait(false),
            CompactionTrigger.Reactive => await pipeline.TryReactiveCompactHistoryAsync(
                    request.NeutralHistory,
                    request.ThreadId,
                    request.LastAssistantTimestampUtc,
                    cancellationToken)
                .ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Trigger, "Unsupported compaction trigger.")
        };

        var replacement = result.Status.Success
            ? new CompactionReplacement.Neutral(result.Messages)
            : null;
        return new CompactionExecutionResult(result.Status, Id, replacement);
    }
}
