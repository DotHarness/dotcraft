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
    private readonly IModelRuntimeResolver _runtimeResolver;
    private readonly ConcurrentDictionary<EffectiveModelRuntime, IChatClient> _chatClients = new();

    [ActivatorUtilitiesConstructor]
    public ChatClientRegistry(ModelProviderRegistry providerRegistry, IModelRuntimeResolver? runtimeResolver = null)
    {
        _providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
        _runtimeResolver = runtimeResolver ?? providerRegistry;
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

    internal ModelProviderRegistry ProviderRegistry => _providerRegistry;
    public IModelRuntimeResolver RuntimeResolver => _runtimeResolver;

    public void InvalidateBackends()
    {
        // Generation teardown owns plugin objects; cache eviction must not dispose admitted calls.
        foreach (var key in _chatClients.Keys.Where(key => key.BackendId is not null || key.IsChatGptOAuth))
            _chatClients.TryRemove(key, out _);
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
    /// Resolves the model Dreams runs use.
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
    /// Gets a cached provider-neutral chat client for a specific provider runtime.
    /// </summary>
    public IChatClient GetChatClient(EffectiveModelRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        runtime = _runtimeResolver.Normalize(runtime);

        var normalizedRuntime = runtime with
        {
            Model = NormalizeRequiredModel(runtime.Model),
            Protocol = runtime.BackendId is null ? ModelProviderProtocols.Normalize(runtime.Protocol) : runtime.Protocol,
            EndPoint = runtime.EndPoint.Trim(),
            ApiKey = runtime.ApiKey.Trim(),
            NetworkTimeoutSeconds = Math.Max(1, runtime.NetworkTimeoutSeconds),
            MaxOutputTokens = runtime.BackendId is null ? NormalizeMaxOutputTokens(runtime.Protocol, runtime.MaxOutputTokens) : runtime.MaxOutputTokens,
            StreamMaxRetries = Math.Clamp(runtime.StreamMaxRetries, 0, ModelProviderDefaults.MaxStreamMaxRetries),
            StreamIdleTimeoutMs = Math.Max(1, runtime.StreamIdleTimeoutMs),
            AuthMethod = runtime.BackendId is null ? ModelProviderAuthMethods.Normalize(runtime.AuthMethod) : runtime.AuthMethod,
            AccountId = string.IsNullOrWhiteSpace(runtime.AccountId) ? null : runtime.AccountId.Trim(),
            UseResponsesLite = runtime.IsChatGptOAuth && runtime.UseResponsesLite
        };
        return _chatClients.GetOrAdd(normalizedRuntime, static (runtime, registry) =>
        {
            var client = registry._runtimeResolver.Resolve(runtime).CreateChatClient(runtime);

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
        _runtimeResolver.Resolve(runtime).GetService(typeof(TService)) as TService;

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
        runtime = _runtimeResolver.Normalize(runtime);
        if (!runtime.IsChatGptOAuth)
            return runtime with
            {
                UseResponsesLite = false
            };

        var accountId = _providerRegistry
            .GetService<IProviderRuntimeIdentityResolver>(runtime.Protocol)?
            .ResolveAccountId(runtime);
        var metadata = _providerRegistry
            .GetService<IProviderRuntimeMetadataResolver>(runtime.Protocol)?
            .Resolve(runtime) ?? new ProviderRuntimeMetadata();
        return runtime with
        {
            AccountId = accountId,
            UseResponsesLite = EnableChatGptResponsesLite && metadata.UseLightweightResponses
        };
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
