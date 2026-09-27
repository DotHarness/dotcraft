using System.Runtime.CompilerServices;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

#pragma warning disable OPENAI001

namespace DotCraft.Agents;

internal sealed class OpenAIMaxReasoningChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(messages, Prepare(options), cancellationToken);

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(messages, Prepare(options), cancellationToken))
            yield return update;
    }

    private static ChatOptions? Prepare(ChatOptions? options)
    {
        if (options == null)
            return null;
        var max = ProviderReasoningOptions.Resolve(options) == ProviderReasoningEffort.Max;
        var prepared = ProviderReasoningOptions.WithoutMetadata(options);
        if (!max)
            return prepared;
        var factory = prepared.RawRepresentationFactory;
        prepared.RawRepresentationFactory = client =>
        {
            var raw = factory?.Invoke(client);
            if (raw != null && raw is not ChatCompletionOptions)
                return raw;
            var native = raw as ChatCompletionOptions ?? new ChatCompletionOptions();
            native.ReasoningEffortLevel ??= new ChatReasoningEffortLevel("max");
            return native;
        };
        return prepared;
    }
}
