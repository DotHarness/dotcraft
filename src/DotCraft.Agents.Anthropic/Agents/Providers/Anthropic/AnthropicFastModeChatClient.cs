using System.Runtime.CompilerServices;
using Anthropic.Models.Beta;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using BetaMessageCreateParams = Anthropic.Models.Beta.Messages.MessageCreateParams;
using AnthropicSpeed = Anthropic.Models.Beta.Messages.Speed;

namespace DotCraft.Agents;

internal sealed class AnthropicFastModeChatClient(
    IChatClient innerClient,
    EffectiveModelRuntime runtime) : DelegatingChatClient(innerClient)
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

    private ChatOptions? Prepare(ChatOptions? options)
    {
        if (!string.Equals(ProviderPipelineOptionsScope.Current?.InferenceSpeed, "Fast", StringComparison.OrdinalIgnoreCase))
            return options;
        var prepared = options?.Clone() ?? new ChatOptions();
        var existingFactory = prepared.RawRepresentationFactory;
        prepared.RawRepresentationFactory = client =>
        {
            var raw = existingFactory?.Invoke(client);
            if (raw != null && raw is not BetaMessageCreateParams)
                return raw;
            var existing = raw as BetaMessageCreateParams;
            var betas = existing?.Betas?.ToList() ?? [];
            if (!betas.Contains(AnthropicBeta.FastMode2026_02_01))
                betas.Add(AnthropicBeta.FastMode2026_02_01);
            return existing == null
                ? new BetaMessageCreateParams
                {
                    Model = prepared.ModelId ?? runtime.Model,
                    MaxTokens = prepared.MaxOutputTokens ?? runtime.MaxOutputTokens ?? AnthropicClientProvider.DefaultMaxOutputTokens,
                    Messages = [],
                    Speed = AnthropicSpeed.Fast,
                    Betas = betas
                }
                : new BetaMessageCreateParams(existing) { Speed = AnthropicSpeed.Fast, Betas = betas };
        };
        return prepared;
    }
}
