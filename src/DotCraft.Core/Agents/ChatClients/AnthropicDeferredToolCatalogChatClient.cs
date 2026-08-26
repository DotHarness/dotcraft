using System.Runtime.CompilerServices;
using System.Text;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>Adds the Anthropic names-only deferred tool inventory to each sampling request.</summary>
internal sealed class AnthropicDeferredToolCatalogChatClient(
    IChatClient innerClient,
    DeferredToolActivationIndex registry)
    : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(PrepareMessages(messages), options, cancellationToken);

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(
                           PrepareMessages(messages),
                           options,
                           cancellationToken))
        {
            yield return update;
        }
    }

    private IReadOnlyList<ChatMessage> PrepareMessages(IEnumerable<ChatMessage> messages)
    {
        var history = messages as IReadOnlyList<ChatMessage> ?? messages.ToArray();
        registry.ActivateByName(history
            .SelectMany(static message => message.Contents)
            .OfType<FunctionResultContent>()
            .SelectMany(static result => result.Result is IEnumerable<AIContent> contents
                ? contents
                : [])
            .OfType<DeferredToolReferenceContent>()
            .Select(static reference => reference.ToolName));

        var prepared = new List<ChatMessage>
        {
            new(ChatRole.User, BuildCatalog())
        };
        prepared.AddRange(history);
        return prepared;
    }

    private string BuildCatalog()
    {
        var builder = new StringBuilder("<available-deferred-tools>\n");
        foreach (var identity in registry.Entries.Keys.Order(StringComparer.Ordinal))
        {
            builder.Append(identity);
            builder.Append('\n');
        }
        builder.Append("</available-deferred-tools>");
        return builder.ToString();
    }
}
