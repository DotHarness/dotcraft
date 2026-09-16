using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

internal static class ProviderChatClientAdapters
{
    public static IChatClient CreateRequestAdaptedClient(
        IChatClient innerClient,
        AppConfig config,
        EffectiveModelRuntime runtime,
        AppConfig.ReasoningConfig? reasoningConfig = null,
        bool useDefaultReasoning = true)
    {
        _ = useDefaultReasoning;
        return new ProviderPipelineOptionsChatClient(
            innerClient,
            CreatePipelineOptions(
                runtime,
                reasoningConfig ?? config.Reasoning,
                ModelCatalog.SupportsFast(config, runtime.Protocol, runtime.Model)
                    ? config.Speed
                    : InferenceSpeed.Standard,
                config.PromptCaching));
    }

    public static void UseProviderAdapters(
        ChatClientBuilder builder,
        AppConfig config,
        EffectiveModelRuntime runtime,
        AppConfig.ReasoningConfig? reasoningConfig,
        InferenceSpeed speed,
        AppConfig.PromptCachingConfig promptCaching,
        bool useDefaultReasoning = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        UseFlatToolIdentity(builder, runtime.Protocol);

        _ = useDefaultReasoning;
        builder.Use(innerClient => new ProviderPipelineOptionsChatClient(
            innerClient,
            CreatePipelineOptions(
                runtime,
                reasoningConfig ?? config.Reasoning,
                ModelCatalog.SupportsFast(config, runtime.Protocol, runtime.Model)
                    ? speed
                    : InferenceSpeed.Standard,
                promptCaching)));
    }

    public static void UseProviderAdapters(
        ChatClientBuilder builder,
        AppConfig config,
        string? protocol,
        string? model,
        string? endpoint,
        int? maxOutputTokens,
        AppConfig.ReasoningConfig? reasoningConfig,
        InferenceSpeed speed,
        AppConfig.PromptCachingConfig promptCaching,
        bool useDefaultReasoning = true,
        bool removesUnsupportedOAuthResponsesFields = false)
    {
        _ = removesUnsupportedOAuthResponsesFields;
        var normalizedProtocol = NormalizeProtocolOrNull(protocol);
        if (normalizedProtocol == null)
            return;

        var runtime = new EffectiveModelRuntime(
            "request",
            model ?? string.Empty,
            normalizedProtocol,
            "request",
            string.Empty,
            endpoint ?? string.Empty,
            config.NetworkTimeoutSeconds,
            maxOutputTokens,
            ModelProviderCapabilities.ForProtocol(normalizedProtocol));
        UseProviderAdapters(
            builder,
            config,
            runtime,
            reasoningConfig,
            speed,
            promptCaching,
            useDefaultReasoning);
    }

    private static void UseFlatToolIdentity(ChatClientBuilder builder, string protocol)
    {
        var normalized = NormalizeProtocolOrNull(protocol);
        if (normalized != null && !ModelProviderProtocols.IsOpenAIResponses(normalized))
            builder.Use(innerClient => new FlatToolIdentityChatClient(innerClient));
    }

    private static ProviderPipelineOptions CreatePipelineOptions(
        EffectiveModelRuntime runtime,
        AppConfig.ReasoningConfig reasoning,
        InferenceSpeed speed,
        AppConfig.PromptCachingConfig promptCaching) =>
        new(
            runtime,
            reasoning.Effort.ToString(),
            reasoning.Output.ToString(),
            reasoning.Enabled,
            speed.ToString(),
            promptCaching.ShouldApply(runtime.Model),
            promptCaching.Ttl);

    private static string? NormalizeProtocolOrNull(string? protocol)
    {
        try { return ModelProviderProtocols.Normalize(protocol); }
        catch (ArgumentException) { return null; }
    }
}
