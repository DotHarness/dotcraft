using System.Runtime.CompilerServices;
using Anthropic.Models.Beta;
using AnthropicBetaMessageCreateParams = Anthropic.Models.Beta.Messages.MessageCreateParams;
using AnthropicSpeed = Anthropic.Models.Beta.Messages.Speed;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001, MEAI001

namespace DotCraft.Agents;

internal sealed class FastModeChatClient(
    IChatClient innerClient,
    AppConfig config,
    string protocol,
    string? model,
    InferenceSpeed speed) : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(messages, PrepareOptions(options), cancellationToken);

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(messages, PrepareOptions(options), cancellationToken))
            yield return update;
    }

    internal ChatOptions? PrepareOptions(ChatOptions? options)
        => PrepareOptions(options, config, protocol, model, speed);

    internal static ChatOptions? PrepareOptions(
        ChatOptions? options,
        AppConfig config,
        string protocol,
        string? model,
        InferenceSpeed speed)
    {
        if (speed != InferenceSpeed.Fast || !ModelCatalog.SupportsFast(config, protocol, model))
            return options;

        var prepared = options?.Clone() ?? new ChatOptions();
        var existingFactory = prepared.RawRepresentationFactory;
        prepared.RawRepresentationFactory = client =>
        {
            var raw = existingFactory?.Invoke(client);
            if (ModelProviderProtocols.IsOpenAIResponses(protocol))
            {
                var responseOptions = raw as CreateResponseOptions ?? new CreateResponseOptions();
                ResponsesToolSearchMapper.PatchResponseServiceTier(responseOptions, "priority");
                return responseOptions;
            }

            if (string.Equals(ModelProviderProtocols.Normalize(protocol), ModelProviderProtocols.Anthropic, StringComparison.Ordinal))
            {
                if (raw != null && raw is not AnthropicBetaMessageCreateParams)
                    return raw;
                return PatchAnthropic(raw as AnthropicBetaMessageCreateParams, prepared, model);
            }

            return raw;
        };
        return prepared;
    }

    private static AnthropicBetaMessageCreateParams PatchAnthropic(
        AnthropicBetaMessageCreateParams? existing,
        ChatOptions options,
        string? model)
    {
        var betas = existing?.Betas?.ToList() ?? [];
        if (!betas.Contains(AnthropicBeta.FastMode2026_02_01))
            betas.Add(AnthropicBeta.FastMode2026_02_01);

        if (existing != null)
        {
            return new AnthropicBetaMessageCreateParams(existing)
            {
                Speed = AnthropicSpeed.Fast,
                Betas = betas
            };
        }

        return new AnthropicBetaMessageCreateParams
        {
            Model = string.IsNullOrWhiteSpace(options.ModelId) ? model ?? string.Empty : options.ModelId.Trim(),
            MaxTokens = options.MaxOutputTokens ?? 4096,
            Messages = [],
            Speed = AnthropicSpeed.Fast,
            Betas = betas
        };
    }
}
