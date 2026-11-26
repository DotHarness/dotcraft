using DotCraft.Configuration;

namespace DotCraft.Agents;

public sealed record ProviderRequestAdaptation(
    bool UseResponsesLite,
    bool DeepThinking,
    ModelThinkingAdapterCatalog.AnthropicThinkingAdapterData? AnthropicThinking,
    ModelThinkingAdapterCatalog.AnthropicMessageContentAdapterData? AnthropicMessageContent,
    ModelCatalogEntry? Model = null,
    string? Protocol = null)
{
    public ProviderContextBudget? ContextBudget { get; init; }
    public ModelProviderCapabilities? Capabilities { get; init; }
    public int? MaxOutputTokens { get; init; }
}
