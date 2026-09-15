using Microsoft.Extensions.AI;
using DotCraft.Sessions;

namespace DotCraft.Context.Compaction;

internal sealed class LocalSummaryCompactionBackend(CompactionPipeline pipeline) : ICompactionBackend
{
    public string Id => CompactionBackendIds.LocalSummary;

    public async Task<CompactionExecutionResult> ExecuteAsync(
        CompactionExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        request = Prepare(request);
        var result = request.Trigger switch
        {
            CompactionTrigger.Auto => await pipeline.TryAutoCompactHistoryAsync(
                    request.NeutralHistory,
                    request.ThreadId,
                    request.InputTokenHint,
                    request.LastAssistantTimestampUtc,
                    cancellationToken,
                    request.PromptSnapshot)
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

    private static CompactionExecutionRequest Prepare(CompactionExecutionRequest request)
    {
        var history = WithoutInstructions(request.NeutralHistory);
        var snapshot = request.PromptSnapshot;
        if (snapshot is not null && snapshot.Messages.Any(AgentInstructionsHistory.IsInstructions))
        {
            var messages = WithoutInstructions(snapshot.Messages);
            snapshot = snapshot with
            {
                Messages = messages,
                MessageFingerprint = MessageTokenEstimator.ComputePrefixFingerprint(messages, messages.Count)
            };
        }

        return request with { NeutralHistory = history, PromptSnapshot = snapshot };
    }

    private static List<ChatMessage> WithoutInstructions(IEnumerable<ChatMessage> history) =>
        history.Where(static message => !AgentInstructionsHistory.IsInstructions(message))
            .Select(static message => message.Clone()).ToList();
}
