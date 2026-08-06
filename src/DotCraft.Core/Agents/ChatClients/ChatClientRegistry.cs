using System.Collections.Concurrent;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Agents;

/// <summary>
/// Central provider-neutral registry for resolving model runtimes and cached chat clients.
/// </summary>
public sealed class ChatClientRegistry
{
    // Responses Lite currently disables parallel tool calls. Keep the dialect available for
    // targeted development, but leave normal runtime resolution on standard Responses until the
    // Lite endpoint can support the tool concurrency expected by current Codex models.
    private const bool EnableChatGptResponsesLite = false;

    private readonly ModelProviderRegistry _providerRegistry;
    private readonly ConcurrentDictionary<ModelRuntimeKey, IChatClient> _chatClients = new();

    [ActivatorUtilitiesConstructor]
    public ChatClientRegistry(ModelProviderRegistry providerRegistry)
    {
        _providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
    }

    /// <summary>Creates an empty registry for hosts and tests that only resolve configuration metadata.</summary>
    public ChatClientRegistry() : this(new ModelProviderRegistry([]))
    {
    }

    /// <summary>Creates a registry from explicitly supplied providers.</summary>
    public ChatClientRegistry(IEnumerable<IModelProvider> providers)
        : this(new ModelProviderRegistry(providers))
    {
    }

    /// <summary>Creates a registry from one explicitly supplied provider.</summary>
    public ChatClientRegistry(IModelProvider provider)
        : this([provider])
    {
    }

    /// <summary>
    /// Resolves the effective MainAgent runtime for a workspace or thread.
    /// </summary>
    public EffectiveModelRuntime ResolveMainRuntime(
        AppConfig config,
        string? providerIdOverride = null,
        string? modelOverride = null) =>
        ResolveRuntimeMetadata(config, ModelProviderResolver.ResolveMain(config, providerIdOverride, modelOverride));

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
        ResolveRuntimeMetadata(
            config,
            ModelProviderResolver.ResolveSubAgent(config, effectiveMainProviderId, effectiveMainModel));

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
        ResolveRuntimeMetadata(
            config,
            ModelProviderResolver.ResolveConsolidation(config, providerIdOverride, mainModelOverride));

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
            ChatGptAccountId = string.IsNullOrWhiteSpace(runtime.ChatGptAccountId) ? null : runtime.ChatGptAccountId.Trim(),
            UseResponsesLite = runtime.IsChatGptOAuth && runtime.UseResponsesLite
        };
        var key = ModelRuntimeKey.From(normalizedRuntime);
        return _chatClients.GetOrAdd(key, static (runtimeKey, registry) =>
        {
            var runtime = runtimeKey.ToRuntime();
            var client = registry._providerRegistry.CreateChatClient(runtime);

            return new StreamRetryingChatClient(
                client,
                new StreamRetryOptions(
                    runtime.StreamMaxRetries,
                    TimeSpan.FromMilliseconds(runtime.StreamIdleTimeoutMs),
                    runtime.IsChatGptOAuth ? 1 : 0));
        }, this);
    }

    /// <summary>Resolves an optional capability for a provider runtime.</summary>
    public TService? GetProviderService<TService>(EffectiveModelRuntime runtime)
        where TService : class =>
        _providerRegistry.GetService<TService>(runtime.Protocol);

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

    private EffectiveModelRuntime ResolveRuntimeMetadata(AppConfig config, EffectiveModelRuntime runtime)
    {
        if (!runtime.IsChatGptOAuth)
            return runtime with
            {
                UseResponsesLite = false,
                SupportsParallelToolCalls = false
            };

        var accountId = _providerRegistry
            .GetService<IProviderRuntimeIdentityResolver>(runtime.Protocol)?
            .ResolveAccountId(runtime);
        var metadata = _providerRegistry
            .GetService<IProviderRuntimeMetadataResolver>(runtime.Protocol)?
            .Resolve(runtime) ?? new ProviderRuntimeMetadata();
        return runtime with
        {
            ChatGptAccountId = accountId,
            UseResponsesLite = EnableChatGptResponsesLite && metadata.UseLightweightResponses,
            SupportsParallelToolCalls = metadata.SupportsParallelToolCalls
        };
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
        string? ChatGptAccountId,
        bool UseResponsesLite,
        string? ProviderStateDirectory)
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
                string.IsNullOrWhiteSpace(runtime.ChatGptAccountId) ? null : runtime.ChatGptAccountId.Trim(),
                runtime.UseResponsesLite,
                runtime.ProviderStateDirectory);

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
            ChatGptAccountId,
            UseResponsesLite: UseResponsesLite,
            ProviderStateDirectory: ProviderStateDirectory);
    }

    private static int? NormalizeMaxOutputTokens(string protocol, int? value)
    {
        if (value is > 0)
            return value.Value;

        return string.Equals(ModelProviderProtocols.Normalize(protocol), ModelProviderProtocols.Anthropic, StringComparison.OrdinalIgnoreCase)
            ? ModelProviderDefaults.DefaultAnthropicMaxOutputTokens
            : null;
    }
}
