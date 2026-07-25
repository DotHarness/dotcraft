using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>
/// Projects persisted composite tool calls to their frozen flat aliases for provider
/// protocols that do not support native tool namespaces.
/// </summary>
internal sealed class FlatToolIdentityChatClient(IChatClient innerClient)
    : DelegatingChatClient(innerClient)
{
    private const string ProviderFlatNameMetadataKey = "dotcraft.tool.provider_flat_name";

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(ProjectMessages(messages), options, cancellationToken);

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(
                           ProjectMessages(messages),
                           options,
                           cancellationToken))
        {
            yield return update;
        }
    }

    private static IEnumerable<ChatMessage> ProjectMessages(IEnumerable<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            List<AIContent>? projected = null;
            for (var index = 0; index < message.Contents.Count; index++)
            {
                var content = message.Contents[index];
                if (content is not FunctionCallContent call
                    || !TryReadProviderFlatName(call, out var providerFlatName)
                    || string.Equals(call.Name, providerFlatName, StringComparison.Ordinal))
                {
                    projected?.Add(content);
                    continue;
                }

                projected ??= message.Contents.Take(index).ToList();
                projected.Add(new FunctionCallContent(call.CallId, providerFlatName, call.Arguments)
                {
                    AdditionalProperties = call.AdditionalProperties,
                    Exception = call.Exception,
                    InformationalOnly = call.InformationalOnly,
                    RawRepresentation = call.RawRepresentation
                });
            }

            if (projected is null)
            {
                yield return message;
                continue;
            }

            yield return new ChatMessage(message.Role, projected)
            {
                AdditionalProperties = message.AdditionalProperties,
                AuthorName = message.AuthorName,
                CreatedAt = message.CreatedAt,
                MessageId = message.MessageId,
                RawRepresentation = message.RawRepresentation
            };
        }
    }

    private static bool TryReadProviderFlatName(
        FunctionCallContent call,
        out string providerFlatName)
    {
        providerFlatName = string.Empty;
        if (call.AdditionalProperties is null
            || !call.AdditionalProperties.TryGetValue(ProviderFlatNameMetadataKey, out var value)
            || value is not string name
            || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        providerFlatName = name;
        return true;
    }
}
