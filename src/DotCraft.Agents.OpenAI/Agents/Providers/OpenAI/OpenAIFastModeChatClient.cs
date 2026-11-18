using System.Runtime.CompilerServices;
using DotCraft.Configuration;
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
        if (ProviderPipelineOptionsScope.Current is not { } pipeline || ResolveServiceTier(pipeline) is not { } tier)
            return options;
        var prepared = options?.Clone() ?? new ChatOptions();
        var existingFactory = prepared.RawRepresentationFactory;
        prepared.RawRepresentationFactory = client =>
        {
            var raw = existingFactory?.Invoke(client);
            if (raw != null && raw is not CreateResponseOptions)
                return raw;
            var responseOptions = raw as CreateResponseOptions ?? new CreateResponseOptions();
            ResponsesToolSearchMapper.PatchResponseServiceTier(responseOptions, tier);
            return responseOptions;
        };
        return prepared;
    }

    private static string? ResolveServiceTier(ProviderPipelineOptions pipeline)
    {
        var tier = pipeline.InferenceSpeed.ToLowerInvariant() switch
        {
            "fast" => "priority",
            "ultrafast" => "ultrafast",
            _ => null
        };
        var runtime = pipeline.Runtime;
        return tier is null || !runtime.IsChatGptOAuth
               || ChatGptCodexModelCatalog.AdvertisesServiceTier(runtime, runtime.AccountId, tier)
            ? tier
            : null;
    }
}
