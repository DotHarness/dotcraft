using System.Text.Json;
using DotCraft.Agents;
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
    IProviderCompactionBridge? ProviderBridge = null);

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
    private readonly Func<string, CompactionFailureTracker?> _resolveFailureTracker;

    public CompactionCoordinator(
        CompactionPipeline localPipeline,
        Func<CompactionExecutionRequest, ICompactionBackend>? resolveBackend = null,
        Func<string, CompactionFailureTracker?>? resolveFailureTracker = null)
    {
        _thresholdPipeline = localPipeline ?? throw new ArgumentNullException(nameof(localPipeline));
        var localBackend = new LocalSummaryCompactionBackend(localPipeline);
        _resolveBackend = resolveBackend ?? (_ => localBackend);
        _resolveFailureTracker = resolveFailureTracker ?? (_ => null);
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
        var failureTracker = _resolveFailureTracker(backend.Id);
        if (failureTracker?.IsTripped(request.ThreadId) == true)
            return CreateCircuitBreakerResult(request, backend.Id);

        try
        {
            var result = await backend.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.Status.Outcome == CompactionOutcome.Failed
                && !string.Equals(
                    result.Status.FailureReason,
                    "provider_compaction_empty_input",
                    StringComparison.Ordinal))
            {
                failureTracker?.RecordFailure(request.ThreadId);
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            failureTracker?.RecordFailure(request.ThreadId);
            throw;
        }
    }

    public async ValueTask InstallProviderNativeAsync(
        string threadId,
        string backendId,
        IProviderCompactionBridge bridge,
        CompactionReplacement.ProviderNative replacement,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(backendId);
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(replacement);

        var failureTracker = _resolveFailureTracker(backendId);
        try
        {
            var items = replacement.Items
                .Select((item, index) => new ProviderHistoryItem($"compact-output:{index}", item))
                .ToArray();
            await bridge.ReplaceAsync(
                    new ProviderNativeCompactionReplacement(
                        replacement.Protocol,
                        items,
                        replacement.CoveredMessageCount,
                        replacement.CoveredThroughTurnId,
                        replacement.EstimatedTokensAfter),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            failureTracker?.RecordFailure(threadId);
            throw;
        }

        failureTracker?.RecordSuccess(threadId);
    }

    private CompactionExecutionResult CreateCircuitBreakerResult(
        CompactionExecutionRequest request,
        string backendId)
    {
        var tokens = (int)Math.Clamp(request.InputTokenHint, 0, int.MaxValue);
        var threshold = EvaluateThreshold(tokens);
        var status = new CompactionStatus(
            request.Trigger == CompactionTrigger.Auto
                ? CompactionOutcome.Skipped
                : CompactionOutcome.Failed,
            tokens,
            tokens,
            threshold,
            threshold,
            FailureReason: "circuit_breaker_tripped");
        return new CompactionExecutionResult(status, backendId, Replacement: null);
    }
}
