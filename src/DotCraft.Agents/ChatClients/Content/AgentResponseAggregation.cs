using System.Text;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

internal static class AgentResponseAggregation
{
    internal const string ReasoningGroupKey = "dotcraft.reasoning.group";

    public static ChatResponse ToAgentResponse(this IEnumerable<ChatResponseUpdate> updates)
    {
        var response = updates.Select(ProtectGroups).ToChatResponse();
        foreach (var message in response.Messages)
        {
            var contents = new List<AIContent>();
            for (var i = 0; i < message.Contents.Count; i++)
            {
                if (message.Contents[i] is not GroupedReasoning first)
                {
                    contents.Add(message.Contents[i]);
                    continue;
                }

                var text = new StringBuilder(first.Content.Text);
                var protectedData = first.Content.ProtectedData;
                while (i + 1 < message.Contents.Count
                       && message.Contents[i + 1] is GroupedReasoning next
                       && next.Group == first.Group)
                {
                    text.Append(next.Content.Text);
                    protectedData = next.Content.ProtectedData ?? protectedData;
                    i++;
                }
                contents.Add(new TextReasoningContent(text.ToString())
                {
                    ProtectedData = protectedData,
                    RawRepresentation = first.Content.RawRepresentation,
                    AdditionalProperties = first.Content.AdditionalProperties is { } properties
                        ? new AdditionalPropertiesDictionary(properties)
                        : null,
                    Annotations = first.Content.Annotations
                });
            }
            message.Contents = contents;
        }
        return response;
    }

    public static async Task<ChatResponse> ToAgentResponseAsync(
        this IAsyncEnumerable<ChatResponseUpdate> updates, CancellationToken cancellationToken = default)
    {
        var collected = new List<ChatResponseUpdate>();
        await foreach (var update in updates.WithCancellation(cancellationToken).ConfigureAwait(false))
            collected.Add(update);
        return collected.ToAgentResponse();
    }

    private static ChatResponseUpdate ProtectGroups(ChatResponseUpdate update)
    {
        if (!update.Contents.OfType<TextReasoningContent>().Any(content => Group(content) != null))
            return update;

        // The SDK merges adjacent reasoning regardless of item identity and keeps only the first metadata.
        var copy = update.Clone();
        copy.Contents = update.Contents.Select(content =>
            content is TextReasoningContent reasoning && Group(reasoning) is { } group
                ? (AIContent)new GroupedReasoning(reasoning, group)
                : content).ToList();
        return copy;
    }

    private static string? Group(AIContent content) =>
        content.AdditionalProperties?.TryGetValue(ReasoningGroupKey, out var value) == true
            ? value as string
            : null;

    private sealed class GroupedReasoning(TextReasoningContent content, string group) : AIContent
    {
        public TextReasoningContent Content { get; } = content;
        public string Group { get; } = group;
    }
}
