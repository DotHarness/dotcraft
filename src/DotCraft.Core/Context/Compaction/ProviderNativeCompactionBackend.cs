using DotCraft.Agents;

namespace DotCraft.Context.Compaction;

internal sealed class ProviderNativeCompactionBackend(
    string id,
    IProviderNativeCompactor compactor,
    Func<long, CompactionThreshold> evaluateThreshold) : ICompactionBackend
{
    public string Id { get; } = string.IsNullOrWhiteSpace(id)
        ? throw new ArgumentException("A compaction backend id is required.", nameof(id))
        : id;

    public async Task<CompactionExecutionResult> ExecuteAsync(
        CompactionExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var bridge = request.ProviderBridge
            ?? throw new InvalidOperationException(
                "provider_compaction_unavailable: Active provider history does not support native compaction.");
        var input = await bridge.CaptureInputAsync(
                ToProviderPhase(request.Phase),
                request.NeutralHistory,
                request.Options,
                cancellationToken)
            .ConfigureAwait(false);
        var before = evaluateThreshold(request.InputTokenHint);
        if (input.Items.Count == 0)
        {
            var skipped = new CompactionStatus(
                request.Trigger == CompactionTrigger.Auto && before.AboveBlocking
                    ? CompactionOutcome.Failed
                    : CompactionOutcome.Skipped,
                ToInt(request.InputTokenHint),
                ToInt(request.InputTokenHint),
                before,
                before,
                FailureReason: "provider_compaction_empty_input");
            return new CompactionExecutionResult(skipped, Id, Replacement: null);
        }

        var replacement = await compactor.CompactAsync(
                input,
                request.NeutralHistory,
                request.Options,
                cancellationToken)
            .ConfigureAwait(false);
        var after = evaluateThreshold(replacement.EstimatedTokensAfter);
        var status = new CompactionStatus(
            CompactionOutcome.Partial,
            ToInt(request.InputTokenHint),
            ToInt(replacement.EstimatedTokensAfter),
            before,
            after);
        return new CompactionExecutionResult(
            status,
            Id,
            new CompactionReplacement.ProviderNative(
                replacement.Protocol,
                replacement.Items.Select(static item => item.Payload).ToArray(),
                replacement.CoveredMessageCount,
                replacement.CoveredThroughTurnId,
                replacement.EstimatedTokensAfter));
    }

    private static ProviderCompactionPhase ToProviderPhase(CompactionPhase phase) => phase switch
    {
        CompactionPhase.PreTurn => ProviderCompactionPhase.PreTurn,
        CompactionPhase.MidTurn => ProviderCompactionPhase.MidTurn,
        CompactionPhase.Manual => ProviderCompactionPhase.Manual,
        CompactionPhase.Reactive => ProviderCompactionPhase.Reactive,
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null)
    };

    private static int ToInt(long value) => (int)Math.Clamp(value, 0, int.MaxValue);
}
