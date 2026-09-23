using DotCraft.Configuration;

namespace DotCraft.Agents;

public sealed record ProviderRequestAdaptation(
    bool UseResponsesLite,
    bool DeepThinking,
    ModelThinkingAdapterCatalog.AnthropicThinkingAdapterData? AnthropicThinking,
    ModelThinkingAdapterCatalog.AnthropicMessageContentAdapterData? AnthropicMessageContent);
