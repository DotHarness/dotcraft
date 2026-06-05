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
            promptCaching,
            traceCollector,
            includePromptCaching,
            useDefaultReasoning);
    }

    public static void UseProviderAdapters(
        ChatClientBuilder builder,
        AppConfig config,
        string? protocol,
        string? model,
        string? endpoint,
        int? maxOutputTokens,
        AppConfig.ReasoningConfig? reasoningConfig,
        AppConfig.PromptCachingConfig promptCaching,
        TraceCollector? traceCollector,
        bool includePromptCaching = true,
        bool useDefaultReasoning = true)
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

        UsePromptCacheRequestShapeTracing(builder, normalizedProtocol, model, traceCollector);
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
        TraceCollector? traceCollector)
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
            modelId));
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
