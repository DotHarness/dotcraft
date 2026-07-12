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
        var builder = new ChatClientBuilder(innerClient);
        UseRequestAdapters(
            builder,
            config,
            runtime.Protocol,
            runtime.Model,
            runtime.EndPoint,
            runtime.MaxOutputTokens,
            reasoningConfig,
            useDefaultReasoning);
        return builder.Build();
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
        UseProviderAdapters(
            builder,
            config,
            runtime.Protocol,
            runtime.Model,
            runtime.EndPoint,
            runtime.MaxOutputTokens,
            reasoningConfig,
            speed,
            promptCaching,
            traceCollector,
            includePromptCaching,
            useDefaultReasoning,
            removesUnsupportedOAuthResponsesFields: ModelProviderAuthMethods.Normalize(runtime.AuthMethod) == ModelProviderAuthMethods.ChatGptOAuth);
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
        var normalizedProtocol = NormalizeProtocolOrNull(protocol);
        if (includePromptCaching)
            UsePromptCaching(builder, protocol, model, promptCaching, traceCollector);

        UseRequestAdapters(
            builder,
            config,
            protocol,
            model,
            endpoint,
            maxOutputTokens,
            reasoningConfig,
            useDefaultReasoning);

        if (normalizedProtocol != null && ModelCatalog.SupportsFast(config, normalizedProtocol, model))
        {
            builder.Use(innerClient => new FastModeChatClient(
                innerClient,
                config,
                normalizedProtocol,
                model,
                speed));
        }

        UsePromptCacheRequestShapeTracing(
            builder,
            normalizedProtocol,
            model,
            traceCollector,
            removesUnsupportedOAuthResponsesFields);
    }

    private static void UsePromptCaching(
        ChatClientBuilder builder,
        string? protocol,
        string? model,
        AppConfig.PromptCachingConfig promptCaching,
        TraceCollector? traceCollector)
    {
        var normalizedProtocol = NormalizeProtocolOrNull(protocol);
        if (normalizedProtocol is null)
            return;

        if (ModelProviderProtocols.IsOpenAIProtocol(normalizedProtocol))
        {
            builder.Use(innerClient => new PromptCachingChatClient(
                innerClient,
                promptCaching,
                model ?? string.Empty,
                traceCollector));
        }
        else if (string.Equals(normalizedProtocol, ModelProviderProtocols.Anthropic, StringComparison.Ordinal))
        {
            builder.Use(innerClient => new PromptCachingChatClient(
                innerClient,
                promptCaching,
                model ?? string.Empty,
                PromptCacheMarkerStrategy.AnthropicNative,
                traceCollector));
        }
    }

    private static void UseRequestAdapters(
        ChatClientBuilder builder,
        AppConfig config,
        string? protocol,
        string? model,
        string? endpoint,
        int? maxOutputTokens,
        AppConfig.ReasoningConfig? reasoningConfig,
        bool useDefaultReasoning)
    {
        var normalizedProtocol = NormalizeProtocolOrNull(protocol);
        if (normalizedProtocol is null)
            return;

        if (ModelProviderProtocols.IsOpenAIProtocol(normalizedProtocol))
        {
            builder.Use(innerClient => new DeepThinkingChatClient(
                innerClient,
                config,
                model,
                endpoint,
                reasoningConfig,
                useDefaultReasoning));
        }
        else if (string.Equals(normalizedProtocol, ModelProviderProtocols.Anthropic, StringComparison.Ordinal))
        {
            var messageContentAdapter = ModelThinkingAdapterCatalog.ResolveAnthropicMessageContentAdapter(
                config,
                endpoint,
                model);
            if (messageContentAdapter != null)
            {
                builder.Use(innerClient => new DeepSeekAnthropicReasoningHistoryChatClient(
                    innerClient,
                    messageContentAdapter));
            }

            builder.Use(innerClient => new AnthropicThinkingChatClient(
                innerClient,
                config,
                model,
                endpoint,
                maxOutputTokens,
                reasoningConfig,
                useDefaultReasoning));
        }
    }

    private static void UsePromptCacheRequestShapeTracing(
        ChatClientBuilder builder,
        string? normalizedProtocol,
        string? model,
        TraceCollector? traceCollector,
        bool removesUnsupportedOAuthResponsesFields)
    {
        if (traceCollector == null)
            return;
        if (normalizedProtocol is null)
            return;
        if (!ModelProviderProtocols.IsOpenAIResponses(normalizedProtocol))
            return;

        var modelId = model ?? string.Empty;
        builder.Use(innerClient => new PromptCacheRequestShapeTracingChatClient(
            innerClient,
            traceCollector,
            modelId,
            removesUnsupportedOAuthResponsesFields));
    }

    private static string? NormalizeProtocolOrNull(string? protocol)
    {
        try
        {
            return ModelProviderProtocols.Normalize(protocol);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
