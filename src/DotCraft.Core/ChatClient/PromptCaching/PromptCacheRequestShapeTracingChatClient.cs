using System.Runtime.CompilerServices;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

internal sealed class PromptCacheRequestShapeTracingChatClient(
    IChatClient innerClient,
    TraceCollector traceCollector,
    string model,
    bool removesUnsupportedOAuthResponsesFields = false) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var preparedResult = await PrepareAndRecordAsync(messages, options, cancellationToken)
            .ConfigureAwait(false);
        using var traceScope = OpenAIResponsesToolSearchChatClient.UseTraceCollector(traceCollector);
        return await base.GetResponseAsync(
                preparedResult.Messages,
                preparedResult.Options,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var preparedResult = await PrepareAndRecordAsync(messages, options, cancellationToken)
            .ConfigureAwait(false);
        using var traceScope = OpenAIResponsesToolSearchChatClient.UseTraceCollector(traceCollector);
        await foreach (var update in base.GetStreamingResponseAsync(
                               preparedResult.Messages,
                               preparedResult.Options,
                               cancellationToken)
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return update;
        }
    }

    private async Task<PreparedTracingRequest> PrepareAndRecordAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        var preparedMessages = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        var preparedOptions = ResponsesToolSearchMapper.PreparePromptCacheOptions(options);
        var providerHistory = OpenAIResponsesProviderHistoryRuntimeScope.Current;
        var canonicalInput = providerHistory == null
            ? null
            : await providerHistory.PrepareInputAsync(
                    preparedMessages,
                    preparedOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        var request = ResponsesToolSearchMapper.CreateResponseRequest(
            model,
            preparedMessages,
            preparedOptions,
            removesUnsupportedOAuthResponsesFields: removesUnsupportedOAuthResponsesFields,
            canonicalInput: canonicalInput?.Input,
            canonicalItemIdentity: canonicalInput?.ItemIdentity);
        var sessionKey = TracingChatClient.CurrentSessionKey ?? TracingChatClient.GetActiveSessionKey();
        if (!string.IsNullOrWhiteSpace(sessionKey))
        {
            traceCollector.RecordPromptCacheRequestShape(
                sessionKey,
                request.Shape,
                PromptCacheRequestShapeTraceScope.RequestIndex);
        }

        return new PreparedTracingRequest(preparedMessages, options);
    }

    private sealed record PreparedTracingRequest(
        IReadOnlyList<ChatMessage> Messages,
        ChatOptions? Options);
}
