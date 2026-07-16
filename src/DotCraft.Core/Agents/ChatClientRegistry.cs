using System.Collections.Concurrent;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using OpenAI.Images;

namespace DotCraft.Agents;

/// <summary>
/// Central provider-neutral registry for resolving model runtimes and cached chat clients.
/// </summary>
public sealed class ChatClientRegistry(
    OpenAIClientProvider? openAIClientProvider = null,
    AnthropicClientProvider? anthropicClientProvider = null)
{
    private readonly OpenAIClientProvider _openAIClientProvider = openAIClientProvider ?? new OpenAIClientProvider();
    private readonly AnthropicClientProvider _anthropicClientProvider = anthropicClientProvider ?? new AnthropicClientProvider();
    private readonly ConcurrentDictionary<ModelRuntimeKey, IChatClient> _chatClients = new();

    /// <summary>
    /// Resolves the effective MainAgent runtime for a workspace or thread.
    /// </summary>
    public EffectiveModelRuntime ResolveMainRuntime(
        AppConfig config,
        string? providerIdOverride = null,
        string? modelOverride = null) =>
        ModelProviderResolver.ResolveMain(config, providerIdOverride, modelOverride);

    /// <summary>
    /// Resolves the effective MainAgent model for a workspace or thread.
    /// </summary>
    public string ResolveMainModel(AppConfig config, string? modelOverride = null) =>
        ResolveMainRuntime(config, modelOverride: modelOverride).Model;

    /// <summary>
    /// Resolves the effective MainAgent provider id for a workspace or thread.
    /// </summary>
    public string ResolveMainProviderId(AppConfig config, string? providerIdOverride = null) =>
        ResolveMainRuntime(config, providerIdOverride).ProviderId;

    /// <summary>
    /// Resolves the effective native SubAgent runtime for the current thread context.
    /// </summary>
    public EffectiveModelRuntime ResolveSubAgentRuntime(
        AppConfig config,
        string effectiveMainProviderId,
        string effectiveMainModel) =>
        ModelProviderResolver.ResolveSubAgent(config, effectiveMainProviderId, effectiveMainModel);

    /// <summary>
    /// Resolves the effective native SubAgent model for the current thread context.
    /// </summary>
    public string ResolveSubAgentModel(
        AppConfig config,
        string effectiveMainProviderId,
        string effectiveMainModel) =>
        ResolveSubAgentRuntime(config, effectiveMainProviderId, effectiveMainModel).Model;

    /// <summary>
    /// Resolves the effective memory consolidation runtime.
    /// </summary>
    public EffectiveModelRuntime ResolveConsolidationRuntime(
        AppConfig config,
        string? providerIdOverride = null,
        string? mainModelOverride = null) =>
        ModelProviderResolver.ResolveConsolidation(config, providerIdOverride, mainModelOverride);

    /// <summary>
    /// Resolves the effective memory consolidation model.
    /// </summary>
    public string ResolveConsolidationModel(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return string.IsNullOrWhiteSpace(config.ConsolidationModel)
            ? ResolveMainModel(config)
            : config.ConsolidationModel.Trim();
    }

    /// <summary>
    /// Gets a cached provider-neutral chat client for the workspace's effective MainAgent model.
    /// </summary>
    public IChatClient GetMainChatClient(AppConfig config, string? providerIdOverride = null, string? modelOverride = null) =>
        GetChatClient(ResolveMainRuntime(config, providerIdOverride, modelOverride));

    /// <summary>
    /// Gets a cached provider-neutral chat client for the effective native SubAgent model.
    /// </summary>
    public IChatClient GetSubAgentChatClient(
        AppConfig config,
        string effectiveMainProviderId,
        string effectiveMainModel) =>
        GetChatClient(ResolveSubAgentRuntime(config, effectiveMainProviderId, effectiveMainModel));

    /// <summary>
    /// Gets a cached provider-neutral chat client for the effective memory consolidation model.
    /// </summary>
    public IChatClient GetConsolidationChatClient(
        AppConfig config,
        string? providerIdOverride = null,
        string? mainModelOverride = null) =>
        GetChatClient(ResolveConsolidationRuntime(config, providerIdOverride, mainModelOverride));

    /// <summary>
    /// Gets a cached provider-neutral chat client for a specific provider runtime.
    /// </summary>
    public IChatClient GetChatClient(EffectiveModelRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var normalizedRuntime = runtime with
        {
            Model = NormalizeRequiredModel(runtime.Model),
            Protocol = ModelProviderProtocols.Normalize(runtime.Protocol),
            EndPoint = runtime.EndPoint.Trim(),
            ApiKey = runtime.ApiKey.Trim(),
            NetworkTimeoutSeconds = Math.Max(1, runtime.NetworkTimeoutSeconds),
            MaxOutputTokens = NormalizeMaxOutputTokens(runtime.Protocol, runtime.MaxOutputTokens),
            StreamMaxRetries = Math.Clamp(runtime.StreamMaxRetries, 0, ModelProviderDefaults.MaxStreamMaxRetries),
            StreamIdleTimeoutMs = Math.Max(1, runtime.StreamIdleTimeoutMs),
            AuthMethod = ModelProviderAuthMethods.Normalize(runtime.AuthMethod),
            ChatGptAccountId = string.IsNullOrWhiteSpace(runtime.ChatGptAccountId) ? null : runtime.ChatGptAccountId.Trim()
        };
        var key = ModelRuntimeKey.From(normalizedRuntime);
        return _chatClients.GetOrAdd(key, static (runtimeKey, registry) =>
        {
            var runtime = runtimeKey.ToRuntime();
            var client = runtimeKey.Protocol switch
            {
                ModelProviderProtocols.OpenAIChatCompletions => registry._openAIClientProvider
                    .GetOpenAIChatClient(runtime)
                    .AsIChatClient(),
                ModelProviderProtocols.OpenAIResponses => registry._openAIClientProvider
                    .GetOpenAIResponsesChatClient(runtime),
                ModelProviderProtocols.Anthropic => registry._anthropicClientProvider
                    .GetChatClient(runtime),
                _ => throw new ArgumentException($"Unsupported model provider protocol '{runtimeKey.Protocol}'.")
            };

            return new StreamRetryingChatClient(
                client,
                new StreamRetryOptions(
                    runtime.StreamMaxRetries,
                    TimeSpan.FromMilliseconds(runtime.StreamIdleTimeoutMs)));
        }, this);
    }

    /// <summary>
    /// Gets a cached OpenAI SDK image client for a provider runtime and image model.
    /// </summary>
    public ImageClient GetOpenAIImageClient(EffectiveModelRuntime runtime, string imageModel) =>
        _openAIClientProvider.GetOpenAIImageClient(runtime, imageModel);

    /// <summary>
    /// Sends a multipart image edit request for SDK image-edit gaps.
    /// </summary>
    internal Task<byte[]> GenerateOpenAIImageEditAsync(
        EffectiveModelRuntime runtime,
        string imageModel,
        string prompt,
        IReadOnlyList<OpenAIImageEditInput> images,
        CancellationToken cancellationToken) =>
        _openAIClientProvider.GenerateOpenAIImageEditAsync(runtime, imageModel, prompt, images, cancellationToken);

    /// <summary>
    /// Gets a cached provider-neutral chat client for a provider/model pair.
    /// </summary>
    public IChatClient GetChatClient(AppConfig config, string providerId, string model) =>
        GetChatClient(ResolveMainRuntime(config, providerId, model));

    /// <summary>
    /// Tries to get a cached chat client for a provider/model pair without throwing on invalid configuration.
    /// </summary>
    public bool TryGetChatClient(AppConfig config, string providerId, string model, out IChatClient? chatClient)
    {
        try
        {
            chatClient = GetChatClient(config, providerId, model);
            return true;
        }
        catch (ArgumentException)
        {
            chatClient = null;
            return false;
        }
        catch (UriFormatException)
        {
            chatClient = null;
            return false;
        }
    }

    private static string NormalizeRequiredModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model must be configured.", nameof(model));

        return model.Trim();
    }

    private readonly record struct ModelRuntimeKey(
        string ProviderId,
        string Protocol,
        string Model,
        string ApiKey,
        string EndPoint,
        int NetworkTimeoutSeconds,
        int? MaxOutputTokens,
        int StreamMaxRetries,
        int StreamIdleTimeoutMs,
        string AuthMethod,
        string? ChatGptAccountId)
    {
        public static ModelRuntimeKey From(EffectiveModelRuntime runtime) =>
            new(
                runtime.ProviderId,
                ModelProviderProtocols.Normalize(runtime.Protocol),
                NormalizeRequiredModel(runtime.Model),
                runtime.ApiKey.Trim(),
                runtime.EndPoint.Trim(),
                Math.Max(1, runtime.NetworkTimeoutSeconds),
                NormalizeMaxOutputTokens(runtime.Protocol, runtime.MaxOutputTokens),
                Math.Clamp(runtime.StreamMaxRetries, 0, ModelProviderDefaults.MaxStreamMaxRetries),
                Math.Max(1, runtime.StreamIdleTimeoutMs),
                ModelProviderAuthMethods.Normalize(runtime.AuthMethod),
                string.IsNullOrWhiteSpace(runtime.ChatGptAccountId) ? null : runtime.ChatGptAccountId.Trim());

        public EffectiveModelRuntime ToRuntime() => new(
            ProviderId,
            Model,
            Protocol,
            DisplayName: ProviderId,
            ApiKey,
            EndPoint,
            NetworkTimeoutSeconds,
            MaxOutputTokens,
            IsImplicit: ModelProviderResolver.IsImplicitProviderId(ProviderId),
            ModelProviderCapabilities.ForProtocol(Protocol),
            StreamMaxRetries,
            StreamIdleTimeoutMs,
            AuthMethod,
            ChatGptAccountId);
    }

    private static int? NormalizeMaxOutputTokens(string protocol, int? value)
    {
        if (value is > 0)
            return value.Value;

        return string.Equals(ModelProviderProtocols.Normalize(protocol), ModelProviderProtocols.Anthropic, StringComparison.OrdinalIgnoreCase)
            ? AnthropicClientProvider.DefaultMaxOutputTokens
            : null;
    }
}
