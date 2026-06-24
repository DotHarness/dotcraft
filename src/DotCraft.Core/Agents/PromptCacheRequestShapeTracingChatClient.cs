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
        var prepared = PrepareAndRecord(messages, options, out var preparedMessages);
        using var traceScope = OpenAIResponsesToolSearchChatClient.UseTraceCollector(traceCollector);
        return await base.GetResponseAsync(preparedMessages, prepared, cancellationToken).ConfigureAwait(false);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var prepared = PrepareAndRecord(messages, options, out var preparedMessages);
        using var traceScope = OpenAIResponsesToolSearchChatClient.UseTraceCollector(traceCollector);
        await foreach (var update in base.GetStreamingResponseAsync(preparedMessages, prepared, cancellationToken)
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return update;
        }
    }

    private ChatOptions? PrepareAndRecord(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        out IReadOnlyList<ChatMessage> preparedMessages)
    {
        preparedMessages = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        var preparedOptions = ResponsesToolSearchMapper.PreparePromptCacheOptions(options);
        var request = ResponsesToolSearchMapper.CreateResponseRequest(
            model,
            preparedMessages,
            preparedOptions,
            removesUnsupportedOAuthResponsesFields: removesUnsupportedOAuthResponsesFields);
        var sessionKey = TracingChatClient.CurrentSessionKey ?? TracingChatClient.GetActiveSessionKey();
        if (!string.IsNullOrWhiteSpace(sessionKey))
        {
            traceCollector.RecordPromptCacheRequestShape(
                sessionKey,
                request.Shape,
                PromptCacheRequestShapeTraceScope.RequestIndex);
        }

        return options;
    }
}
