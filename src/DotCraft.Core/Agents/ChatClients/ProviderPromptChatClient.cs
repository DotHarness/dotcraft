using System.Runtime.CompilerServices;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

internal sealed class ProviderPromptChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var materialized = messages.ToArray();
        var snapshot = Capture(materialized, options);
        using var prompt = ProviderPromptSnapshotScope.Push(snapshot);
        using var request = PushContext(snapshot);
        return await InnerClient.GetResponseAsync(materialized, options, cancellationToken).ConfigureAwait(false);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var materialized = messages.ToArray();
        var snapshot = Capture(materialized, options);
        using var prompt = ProviderPromptSnapshotScope.Push(snapshot);
        using var request = PushContext(snapshot);
        await foreach (var update in InnerClient.GetStreamingResponseAsync(materialized, options, cancellationToken).ConfigureAwait(false))
            yield return update;
    }

    internal static ProviderPromptSnapshot Capture(IReadOnlyList<ChatMessage> messages, ChatOptions? options)
    {
        var fragments = new List<ProviderPromptFragment>();
        if (options?.Instructions is { Length: > 0 } instructions)
        {
            var known = ProviderRequestContextScope.Current?.PromptSources.Find(instructions);
            if (known is not null) fragments.AddRange(known);
            else fragments.Add(new ProviderPromptFragment("instructions", ProviderPromptSourceKind.Opaque, instructions, "instructions"));
        }
        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];
            if (AgentInstructionsHistory.IsInstructions(message))
                fragments.Add(new ProviderPromptFragment("agents-md", ProviderPromptSourceKind.ProjectInstructions, message.Text, "message", index));
            else if (message.Role == ChatRole.System || message.Role.Value == "developer")
                fragments.Add(new ProviderPromptFragment("message:" + index, ProviderPromptSourceKind.Opaque, message.Text, "message", index));
        }
        return new ProviderPromptSnapshot(fragments);
    }

    private static IDisposable? PushContext(ProviderPromptSnapshot snapshot) =>
        ProviderRequestContextScope.Current is { } context
            ? ProviderRequestContextScope.Push(context with { Prompt = snapshot }) : null;
}
