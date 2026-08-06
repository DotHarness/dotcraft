using DotCraft.Configuration;
using DotCraft.Tracing;
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
        TraceCollector? traceCollector,
        bool includePromptCaching = true,
        bool useDefaultReasoning = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (includePromptCaching)
            UsePromptCaching(builder, runtime.Protocol, runtime.Model, promptCaching, traceCollector);

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
        TraceCollector? traceCollector,
        bool includePromptCaching = true,
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
            false,
            ModelProviderCapabilities.ForProtocol(normalizedProtocol));
        UseProviderAdapters(
            builder,
            config,
            runtime,
            reasoningConfig,
            speed,
            promptCaching,
            traceCollector,
            includePromptCaching,
            useDefaultReasoning);
    }

    private static void UsePromptCaching(
        ChatClientBuilder builder,
        string protocol,
        string model,
        AppConfig.PromptCachingConfig promptCaching,
        TraceCollector? traceCollector)
    {
        var normalized = NormalizeProtocolOrNull(protocol);
        if (normalized == null)
            return;
        if (!ModelProviderProtocols.IsOpenAIResponses(normalized))
            builder.Use(innerClient => new FlatToolIdentityChatClient(innerClient));

        builder.Use(innerClient => new PromptCachingChatClient(
            innerClient,
            promptCaching,
            model,
            traceCollector,
            dialect: innerClient.GetService(typeof(IPromptCacheDialect)) as IPromptCacheDialect
                     ?? (string.Equals(normalized, ModelProviderProtocols.Anthropic, StringComparison.Ordinal)
                         ? AdditionalPropertiesPromptCacheDialect.Anthropic
                         : AdditionalPropertiesPromptCacheDialect.Instance)));
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
            promptCaching.Enabled,
            promptCaching.Ttl);

    private static string? NormalizeProtocolOrNull(string? protocol)
    {
        try { return ModelProviderProtocols.Normalize(protocol); }
        catch (ArgumentException) { return null; }
    }
}
