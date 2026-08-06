using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

internal sealed class ProviderPipelineOptionsChatClient(
    IChatClient innerClient,
    ProviderPipelineOptions pipelineOptions) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = ProviderPipelineOptionsScope.Push(pipelineOptions);
        return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var scope = ProviderPipelineOptionsScope.Push(pipelineOptions);
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken)
                           .WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return update;
    }
}
