using DotCraft.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCraft.Agents;

public sealed class OpenRouterBackend(IProviderHttpTransport? transport = null) : IModelBackend
{
    public const string Id = "openrouter";
    public const string OAuth = "openrouterOAuth";
    public static readonly string[] SupportedProtocols = ["auto", ModelProviderProtocols.Anthropic,
        ModelProviderProtocols.OpenAIChatCompletions, ModelProviderProtocols.OpenAIResponses];

    public ModelBackendDescriptor Descriptor { get; } = new(Id, "OpenRouter", "auto", SupportedProtocols,
        [new("apiKey", "API key", "apiKey"), new(OAuth, "Sign in with OpenRouter", "oauth")],
        new ModelProviderCapabilities { CachedInputUsageReporting = true, PromptCacheRequestShaping = true,
            ExtendedThinking = true, ToolChoiceControls = true, RawMetadataPassthrough = true });

    public IModelProvider CreateProvider(ModelBackendContext context) => new OpenRouterProvider(context, transport);
}

public static class OpenRouterServiceCollectionExtensions
{
    public static IServiceCollection AddOpenRouterModelProvider(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelBackend, OpenRouterBackend>());
        return services;
    }
}
