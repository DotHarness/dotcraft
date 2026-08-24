using System.Runtime.CompilerServices;
using DotCraft.Agents;
using DotCraft.Contributions;
using Microsoft.Extensions.AI;

namespace Acme.ReviewCore;

/// <summary>An observing wrapper around the model call; returning <c>inner</c> unchanged declines a pipeline.</summary>
internal sealed class ReviewObserverMiddleware(ReviewJournal journal) : IChatMiddleware
{
    /// <inheritdoc />
    public string Name => "review-observer";

    /// <inheritdoc />
    public IChatClient Wrap(IChatClient inner, ChatPipelineContext context) =>
        context.Kind == ChatPipelineKind.SubAgent
            ? inner
            : new ObservingChatClient(inner, journal, context);
}
/// <summary>Counts model calls without altering a single request or response.</summary>
internal sealed class ObservingChatClient(
    IChatClient innerClient,
    ReviewJournal journal,
    ChatPipelineContext context) : DelegatingChatClient(innerClient)
{
    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        journal.Write($"model call on {context.Kind} pipeline");
        return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        journal.Write($"streaming call on {context.Kind} pipeline");
        await foreach (var update in base
                           .GetStreamingResponseAsync(messages, options, cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return update;
        }
    }
}
