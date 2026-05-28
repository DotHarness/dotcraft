using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using DotCraft.Tracing;

namespace DotCraft.Agents;

/// <summary>
/// Lightweight <see cref="DelegatingChatClient"/> placed inside
/// <see cref="FunctionInvokingChatClient"/> in the SubAgent pipeline.
/// Accumulates token usage from LLM responses so the Live Table can
/// display per-SubAgent token counts. Tool activity tracking is handled
/// separately via <see cref="FunctionInvokingChatClient.FunctionInvoker"/>.
/// All stream content is passed through unchanged.
/// </summary>
internal sealed class SubAgentProgressChatClient(
    IChatClient innerClient,
    SubAgentProgressBridge.ProgressEntry progressEntry) : DelegatingChatClient(innerClient)
{
    private readonly TokenUsageRequestAccumulator _usageAccumulator = new();
    private int _nextSyntheticRequestIndex;
    private int? _currentUsageRequestIndex;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(chatMessages, options, cancellationToken);

        if (response.Usage != null)
        {
            _currentUsageRequestIndex = Interlocked.Increment(ref _nextSyntheticRequestIndex);
            ApplyUsage(TokenUsageExtractor.FromResponse(response));
        }

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _currentUsageRequestIndex = Interlocked.Increment(ref _nextSyntheticRequestIndex);
        await foreach (var update in base.GetStreamingResponseAsync(chatMessages, options, cancellationToken))
        {
            var updateRequestIndex = TokenUsageRequestMetadata.TryGetRequestIndex(update);
            if (updateRequestIndex.HasValue)
                _currentUsageRequestIndex = updateRequestIndex;

            foreach (var content in update.Contents)
            {
                if (content is UsageContent usage)
                {
                    ApplyUsage(TokenUsageExtractor.FromUsageContent(usage));
                }
            }

            yield return update;
        }
    }

    private void ApplyUsage(TokenUsageSnapshot snapshot)
    {
        if (snapshot.InputTokens <= 0 && snapshot.OutputTokens <= 0)
            return;

        var usageDelta = _usageAccumulator.ApplySnapshot(snapshot, _currentUsageRequestIndex);
        var delta = usageDelta.Usage;

        if (delta.InputTokens > 0
            || delta.OutputTokens > 0
            || delta.CachedInputTokens > 0
            || delta.CacheWriteInputTokens > 0
            || delta.ReasoningOutputTokens > 0)
        {
            progressEntry.AddTokens(
                delta.InputTokens,
                delta.OutputTokens,
                delta.CachedInputTokens,
                delta.CacheWriteInputTokens,
                delta.ReasoningOutputTokens,
                usageDelta.LlmCallDelta);
        }
    }
}
