using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace DotCraft.Agents;

internal sealed class OpenAIFastModeChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(messages, Prepare(options), cancellationToken);

    internal ChatOptions? PrepareOptions(ChatOptions? options) => Prepare(options);

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(messages, Prepare(options), cancellationToken))
            yield return update;
    }

    private static ChatOptions? Prepare(ChatOptions? options)
    {
        if (!string.Equals(
                ProviderPipelineOptionsScope.Current?.InferenceSpeed,
                "Fast",
                StringComparison.OrdinalIgnoreCase))
            return options;
        var prepared = options?.Clone() ?? new ChatOptions();
        var existingFactory = prepared.RawRepresentationFactory;
        prepared.RawRepresentationFactory = client =>
        {
            var raw = existingFactory?.Invoke(client);
            if (raw != null && raw is not CreateResponseOptions)
                return raw;
            var responseOptions = raw as CreateResponseOptions ?? new CreateResponseOptions();
            ResponsesToolSearchMapper.PatchResponseServiceTier(responseOptions, "priority");
            return responseOptions;
        };
        return prepared;
    }
}
