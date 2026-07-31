using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DotCraft.Context.Compaction;

internal static class CompactionBackendIds
{
    public const string LocalSummary = "local_summary";
    public const string ChatGptResponsesCompact = "chatgpt_responses_compact";
}

internal enum CompactionTrigger
{
    Auto,
    Manual,
    Reactive
}

internal enum CompactionPhase
{
    PreTurn,
    MidTurn,
    Manual,
    Reactive
}

internal sealed record CompactionExecutionRequest(
    CompactionTrigger Trigger,
    CompactionPhase Phase,
    IReadOnlyList<ChatMessage> NeutralHistory,
    string ThreadId,
    long InputTokenHint,
    DateTimeOffset? LastAssistantTimestampUtc,
    PromptRequestSnapshot? PromptSnapshot = null,
    IReadOnlyList<AITool>? FallbackTools = null,
    bool CarryRequestOverhead = true,
    ChatOptions? Options = null,
    IProviderHistoryCompactionBridge? ProviderBridge = null);

internal sealed record CompactionExecutionResult(
    CompactionStatus Status,
    string BackendId,
    CompactionReplacement? Replacement);

internal abstract record CompactionReplacement
{
    internal sealed record Neutral(
        IReadOnlyList<ChatMessage> Messages) : CompactionReplacement;

    internal sealed record ProviderNative(
        string Protocol,
        IReadOnlyList<JsonElement> Items,
        int CoveredMessageCount,
        string? CoveredThroughTurnId,
        long EstimatedTokensAfter) : CompactionReplacement;
}

internal sealed record ProviderCompactionInput(
    IReadOnlyList<JsonElement> Items,
    int CoveredMessageCount,
    string? CoveredThroughTurnId);

internal sealed record ProviderNativeSnapshot(
    IReadOnlyList<JsonElement> Items,
    int CoveredMessageCount,
    string? CoveredThroughTurnId);

internal interface IProviderHistoryCompactionBridge
{
    ValueTask<ProviderCompactionInput> CaptureCompactionInputAsync(
        CompactionPhase phase,
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken);

    ValueTask ReplaceNativeAsync(
        CompactionReplacement.ProviderNative replacement,
        CancellationToken cancellationToken);

    long EstimateNativeContextTokens(
        ProviderNativeSnapshot snapshot,
        IReadOnlyList<ChatMessage> pendingTail,
        ChatOptions? options);
}

internal interface ICompactionBackend
{
    string Id { get; }

    Task<CompactionExecutionResult> ExecuteAsync(
        CompactionExecutionRequest request,
        CancellationToken cancellationToken);
}

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
}

internal sealed class CompactionCoordinator
{
    private readonly CompactionPipeline _thresholdPipeline;
    private readonly Func<CompactionExecutionRequest, ICompactionBackend> _resolveBackend;

    public CompactionCoordinator(
        CompactionPipeline localPipeline,
        Func<CompactionExecutionRequest, ICompactionBackend>? resolveBackend = null)
    {
        _thresholdPipeline = localPipeline ?? throw new ArgumentNullException(nameof(localPipeline));
        var localBackend = new LocalSummaryCompactionBackend(localPipeline);
        _resolveBackend = resolveBackend ?? (_ => localBackend);
    }

    public CompactionThreshold EvaluateThreshold(long tokens) =>
        _thresholdPipeline.EvaluateThreshold(tokens);

    public async Task<CompactionExecutionResult> ExecuteAsync(
        CompactionExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var backend = _resolveBackend(request)
            ?? throw new InvalidOperationException("The compaction backend resolver returned no backend.");
        return await backend.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
