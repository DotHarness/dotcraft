using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace DotCraft.Agents;

/// <summary>
/// Creates and caches OpenAI SDK clients for DotCraft runtime paths.
/// </summary>
public sealed partial class OpenAIClientProvider :
    IModelProvider,
    IProviderRuntimeIdentityResolver,
    IProviderImageGeneration,
    IProviderNativeCompactorFactory,
    IProviderHistorySessionFactory,
    IModelCatalogProvider,
    IProviderRuntimeMetadataResolver,
    IProviderAuthentication,
    IProviderUsageReader,
    IProviderLifecycle
{
    private static readonly IReadOnlyCollection<string> SupportedProtocols = Array.AsReadOnly([
        ModelProviderProtocols.OpenAIChatCompletions,
        ModelProviderProtocols.OpenAIResponses
    ]);
    private static readonly HttpClient SharedChatGptHttpClient = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly ConcurrentDictionary<OpenAIClientKey, OpenAIClient> _openAIClients = new();
    private readonly ConcurrentDictionary<OpenAIChatClientKey, ChatClient> _openAIChatClients = new();
    private readonly ConcurrentDictionary<OpenAIClientKey, ResponsesClient> _openAIResponsesClients = new();
    private readonly ConcurrentDictionary<OpenAIClientKey, ResponsesLiteClientContext> _openAIResponsesLiteClients = new();
    private readonly ConcurrentDictionary<OpenAIChatClientKey, IChatClient> _openAIResponsesChatClients = new();
    private readonly IOpenAIAuthService? _openAIAuthService;
    private readonly IOpenAIUsageService? _openAIUsageService;
    private readonly OpenAIInstallationIdProvider? _installationIdProvider;
    private readonly HttpClient _chatGptHttpClient;
    private readonly IProviderHttpTransport? _httpTransport;
    private readonly ILogger<OpenAIClientProvider>? _logger;
    private int _accountMismatchWarningLogged;

    /// <inheritdoc />
    public IReadOnlyCollection<string> Protocols => SupportedProtocols;

    /// <inheritdoc />
    public IChatClient CreateChatClient(EffectiveModelRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var client = ModelProviderProtocols.Normalize(runtime.Protocol) switch
        {
            ModelProviderProtocols.OpenAIChatCompletions => new OpenAIMaxReasoningChatClient(GetOpenAIChatClient(runtime).AsIChatClient()),
            ModelProviderProtocols.OpenAIResponses => GetOpenAIResponsesChatClient(runtime),
            _ => throw new ArgumentException(
                $"Unsupported OpenAI provider protocol '{runtime.Protocol}'.",
                nameof(runtime))
        };
        return new ProviderServiceChatClient(
            new OpenAIFastModeChatClient(new DeepThinkingChatClient(client, runtime)),
            new Dictionary<Type, object>
            {
                [typeof(IToolCallArgumentsDeltaExtractor)] = OpenAIToolCallArgumentsDeltaExtractor.Instance,
                [typeof(IProviderFailureClassifier)] =
                    new OpenAIProviderFailureClassifier(runtime.IsChatGptOAuth)
            });
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey != null)
            return null;
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    string? IProviderRuntimeIdentityResolver.ResolveAccountId(EffectiveModelRuntime runtime) =>
        ResolveChatGptAccountId(runtime);

    ProviderRuntimeMetadata IProviderRuntimeMetadataResolver.Resolve(EffectiveModelRuntime runtime)
        => new(runtime.RequestAdaptation?.UseResponsesLite ?? ChatGptCodexModelCatalog.ResolveUseResponsesLite(
            runtime,
            ResolveChatGptAccountId(runtime)));

    async Task<ModelCatalogResult> IModelCatalogProvider.FetchModelsAsync(
        EffectiveModelRuntime runtime,
        CancellationToken cancellationToken)
    {
        var result = await OpenAIModelCatalog.FetchAsync(runtime, cancellationToken, this)
            .ConfigureAwait(false);
        return new ModelCatalogResult
        {
            Success = result.Success,
            Models = result.Models.Select(static model => new ModelCatalogEntry
            {
                Id = model.Id,
                OwnedBy = model.OwnedBy,
                CreatedAt = model.CreatedAt
            }).ToList(),
            ErrorCode = result.ErrorCode switch
            {
                OpenAIModelCatalogErrorCode.None => ModelCatalogErrorCode.None,
                OpenAIModelCatalogErrorCode.MissingApiKey => ModelCatalogErrorCode.MissingApiKey,
                OpenAIModelCatalogErrorCode.InvalidEndpoint => ModelCatalogErrorCode.InvalidEndpoint,
                OpenAIModelCatalogErrorCode.Unauthorized => ModelCatalogErrorCode.Unauthorized,
                OpenAIModelCatalogErrorCode.Forbidden => ModelCatalogErrorCode.Forbidden,
                OpenAIModelCatalogErrorCode.EndpointNotSupported => ModelCatalogErrorCode.EndpointNotSupported,
                OpenAIModelCatalogErrorCode.Network => ModelCatalogErrorCode.Network,
                OpenAIModelCatalogErrorCode.Timeout => ModelCatalogErrorCode.Timeout,
                _ => ModelCatalogErrorCode.Unknown
            },
            ErrorMessage = result.ErrorMessage
        };
    }

    async Task<ProviderUsageSnapshot?> IProviderUsageReader.ReadAsync(CancellationToken cancellationToken)
    {
        if (_openAIUsageService == null)
            return null;
        var snapshot = _openAIUsageService.CurrentSnapshot
                       ?? await _openAIUsageService.RefreshAsync(cancellationToken).ConfigureAwait(false);
        return snapshot == null ? null : new ProviderUsageSnapshot(
            snapshot.FetchedAt,
            new Dictionary<string, double>(),
            PlanType: snapshot.PlanType,
            Primary: snapshot.Primary == null ? null : new ProviderRateLimitWindow(
                snapshot.Primary.UsedPercent,
                snapshot.Primary.WindowDuration,
                snapshot.Primary.ResetAt),
            Secondary: snapshot.Secondary == null ? null : new ProviderRateLimitWindow(
                snapshot.Secondary.UsedPercent,
                snapshot.Secondary.WindowDuration,
                snapshot.Secondary.ResetAt),
            Credits: snapshot.Credits == null ? null : new ProviderCreditStatus(
                snapshot.Credits.HasCredits,
                snapshot.Credits.Unlimited,
                snapshot.Credits.Balance),
            LimitReachedKind: snapshot.LimitReachedKind);
    }

    void IProviderLifecycle.Start()
    {
        if (_openAIUsageService is OpenAIUsagePoller poller)
            poller.Start();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        if (_openAIUsageService is IAsyncDisposable disposable)
            await disposable.DisposeAsync().ConfigureAwait(false);
    }

    IProviderNativeCompactor IProviderNativeCompactorFactory.CreateCompactor(
        EffectiveModelRuntime runtime,
        IChatClient? rawRepresentationClient) =>
        new OpenAIResponsesCompactor(
            runtime.Model,
            runtime.UseResponsesLite,
            GetChatGptResponsesCompactTransport(runtime),
            rawRepresentationClient,
            runtime.UseResponsesLite ? ResolveInstallationId() : null);

    IProviderConversationHistory IProviderHistorySessionFactory.CreateSession(
        ProviderConversationIdentity conversationIdentity,
        OpaqueProviderHistorySnapshot snapshot,
        IReadOnlyList<Microsoft.Extensions.AI.ChatMessage> coveredMessages,
        IProviderHistorySink? sink)
    {
        ArgumentNullException.ThrowIfNull(conversationIdentity);
        ArgumentNullException.ThrowIfNull(snapshot);
        var internalSnapshot = new DotCraft.Sessions.ProviderHistorySnapshot(
            snapshot.Identity.GenerationId,
            snapshot.Identity.ContextWindowId,
            snapshot.Items.Select(static item => new DotCraft.Sessions.ProviderHistoryEntry
            {
                EntryId = item.EntryId,
                Item = item.Payload.Clone()
            }).ToArray(),
            snapshot.CoveredThroughTurnId,
            snapshot.IsNativeCompacted);

        return new OpenAIResponsesProviderHistoryContext(
            conversationIdentity,
            snapshot.Identity.ProviderId,
            internalSnapshot,
            coveredMessages,
            sink is null ? null : async (payload, ct) =>
            {
                await sink.AppendAsync(
                    ToHistoryIdentity(snapshot.Identity, payload.GenerationId, payload.ContextWindowId, payload.TurnId),
                    string.Equals(payload.Source, DotCraft.Sessions.ProviderHistorySources.LocalInput, StringComparison.Ordinal)
                        ? ProviderHistoryEntrySource.LocalInput
                        : ProviderHistoryEntrySource.ProviderOutput,
                    payload.AttemptId,
                    payload.Entries.Select(static entry => new ProviderHistoryItem(entry.EntryId, entry.Item.Clone())).ToArray(),
                    ct).ConfigureAwait(false);
            },
            sink is null ? null : async (payload, ct) =>
            {
                await sink.ReplaceAsync(
                    ToHistoryIdentity(snapshot.Identity, payload.GenerationId, payload.ContextWindowId, conversationIdentity.TurnId),
                    payload.CoveredThroughTurnId,
                    payload.Reason,
                    payload.Entries.Select(static entry => new ProviderHistoryItem(entry.EntryId, entry.Item.Clone())).ToArray(),
                    ct).ConfigureAwait(false);
            },
            sink is null ? null : async (payload, ct) =>
            {
                await sink.AbortAttemptAsync(
                    ToHistoryIdentity(snapshot.Identity, payload.GenerationId, snapshot.Identity.ContextWindowId, payload.TurnId),
                    payload.AttemptId,
                    ct).ConfigureAwait(false);
            });
    }

    private static ProviderHistoryIdentity ToHistoryIdentity(
        ProviderHistoryIdentity seed,
        string generationId,
        string contextWindowId,
        string? turnId) =>
        seed with
        {
            TurnId = turnId,
            GenerationId = generationId,
            ContextWindowId = contextWindowId
        };

    /// <summary>Default constructor for DI; OAuth-mode providers require an auth service.</summary>
    public OpenAIClientProvider(
        IOpenAIAuthService? openAIAuthService = null,
        OpenAIInstallationIdProvider? installationIdProvider = null,
        ILogger<OpenAIClientProvider>? logger = null,
        IOpenAIUsageService? openAIUsageService = null,
        IProviderHttpTransport? httpTransport = null)
        : this(openAIAuthService, installationIdProvider, chatGptHttpMessageHandler: null, logger, openAIUsageService, httpTransport)
    {
    }

    internal OpenAIClientProvider(
        IOpenAIAuthService? openAIAuthService,
        HttpMessageHandler? chatGptHttpMessageHandler)
        : this(openAIAuthService, installationIdProvider: null, chatGptHttpMessageHandler)
    {
    }

    internal OpenAIClientProvider(
        IOpenAIAuthService? openAIAuthService,
        HttpMessageHandler? chatGptHttpMessageHandler,
        ILogger<OpenAIClientProvider>? logger)
        : this(openAIAuthService, installationIdProvider: null, chatGptHttpMessageHandler, logger)
    {
    }

    internal OpenAIClientProvider(
        IOpenAIAuthService? openAIAuthService,
        OpenAIInstallationIdProvider? installationIdProvider,
        HttpMessageHandler? chatGptHttpMessageHandler,
        ILogger<OpenAIClientProvider>? logger = null,
        IOpenAIUsageService? openAIUsageService = null,
        IProviderHttpTransport? httpTransport = null)
    {
        _openAIAuthService = openAIAuthService;
        _httpTransport = httpTransport;
        _openAIUsageService = openAIUsageService;
        _installationIdProvider = installationIdProvider;
        _logger = logger;
        _chatGptHttpClient = chatGptHttpMessageHandler is null
            ? SharedChatGptHttpClient
            : new HttpClient(chatGptHttpMessageHandler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>
    /// Gets a cached OpenAI client for a resolved OpenAI protocol runtime.
    /// </summary>
    public OpenAIClient GetOpenAIClient(EffectiveModelRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!runtime.IsOpenAICompatible)
            throw new ArgumentException($"Provider '{runtime.ProviderId}' does not use the OpenAI protocol.", nameof(runtime));

        return GetOpenAIClient(OpenAIClientKey.From(runtime));
    }

    /// <summary>
    /// Gets a cached OpenAI SDK chat client for OpenAI protocol integrations.
    /// </summary>
    public ChatClient GetOpenAIChatClient(EffectiveModelRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!runtime.IsOpenAICompatible)
            throw new ArgumentException($"Provider '{runtime.ProviderId}' does not use the OpenAI protocol.", nameof(runtime));

        var key = OpenAIChatClientKey.From(runtime);
        return _openAIChatClients.GetOrAdd(key, static (chatKey, provider) =>
            provider.GetOpenAIClient(chatKey.Client).GetChatClient(chatKey.Model), this);
    }

    /// <summary>
    /// Gets a cached OpenAI Responses chat client for OpenAI Responses protocol integrations.
    /// </summary>
    public IChatClient GetOpenAIResponsesChatClient(EffectiveModelRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!runtime.IsOpenAIResponses)
            throw new ArgumentException($"Provider '{runtime.ProviderId}' does not use the OpenAI Responses protocol.", nameof(runtime));

        var key = OpenAIChatClientKey.From(runtime);
        return _openAIResponsesChatClients.GetOrAdd(key, static (chatKey, provider) =>
        {
            return chatKey.Client.AuthMethod == ModelProviderAuthMethods.ChatGptOAuth
                   && chatKey.UseResponsesLite
                ? provider.CreateOpenAIResponsesLiteChatClient(chatKey)
                : new OpenAIResponsesToolSearchChatClient(
                    provider.GetOpenAIResponsesClient(chatKey.Client),
                    chatKey.Model);
        }, this);
    }

    internal IChatGptResponsesCompactTransport GetChatGptResponsesCompactTransport(
        EffectiveModelRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!runtime.IsOpenAIResponses || !runtime.IsChatGptOAuth)
        {
            throw new ArgumentException(
                "Provider must use ChatGPT OAuth with the OpenAI Responses protocol.",
                nameof(runtime));
        }

        var key = OpenAIClientKey.From(runtime);
        return new SdkChatGptResponsesCompactTransport(
            runtime.UseResponsesLite
                ? GetOpenAIResponsesLiteClient(key).Client
                : GetOpenAIResponsesClient(key));
    }

    internal static OpenAIClientOptions CreateClientOptions(
        Uri endpoint,
        int networkTimeoutSeconds)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = endpoint,
            NetworkTimeout = TimeSpan.FromSeconds(NormalizeNetworkTimeoutSeconds(networkTimeoutSeconds)),
            RetryPolicy = new ClientRetryPolicy(0)
        };
        options.AddPolicy(new OpenAIRetryAdvicePipelinePolicy(), PipelinePosition.BeforeTransport);
        options.AddPolicy(new DotCraftUserAgentPipelinePolicy(), PipelinePosition.PerCall);
        options.AddPolicy(new OpenAIResponsesRequestBodyCanonicalizationPipelinePolicy(), PipelinePosition.PerCall);
        options.AddPolicy(new LlmHttpCapturePipelinePolicy(), PipelinePosition.PerCall);
        options.AddPolicy(new OpenAIResponsesAttemptDiagnosticPipelinePolicy(), PipelinePosition.PerCall);
        return options;
    }

    internal static OpenAIClientOptions CreateResponsesLiteClientOptions(
        Uri endpoint,
        int networkTimeoutSeconds)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = endpoint,
            NetworkTimeout = TimeSpan.FromSeconds(NormalizeNetworkTimeoutSeconds(networkTimeoutSeconds)),
            RetryPolicy = new ClientRetryPolicy(0)
        };
        options.AddPolicy(new OpenAIRetryAdvicePipelinePolicy(), PipelinePosition.BeforeTransport);
        options.AddPolicy(new DotCraftUserAgentPipelinePolicy(), PipelinePosition.PerCall);
        options.AddPolicy(new OpenAIResponsesLiteHeadersPipelinePolicy(), PipelinePosition.PerCall);
        options.AddPolicy(new OpenAIResponsesRequestCompressionPipelinePolicy(), PipelinePosition.PerCall);
        options.AddPolicy(new OpenAIResponsesAttemptDiagnosticPipelinePolicy(), PipelinePosition.PerCall);
        return options;
    }

    internal string? ResolveChatGptAccountId(EffectiveModelRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var tokenAccountId = runtime.IsRemote ? null : NormalizeOptional(_openAIAuthService?.GetAccountId());
        var configuredAccountId = NormalizeOptional(runtime.ChatGptAccountId);
        if (!string.IsNullOrEmpty(tokenAccountId))
        {
            if (!string.IsNullOrEmpty(configuredAccountId) &&
                !string.Equals(configuredAccountId, tokenAccountId, StringComparison.Ordinal) &&
                Interlocked.Exchange(ref _accountMismatchWarningLogged, 1) == 0)
            {
                _logger?.LogWarning(
                    "ChatGPT OAuth account id from config ({ConfiguredAccountId}) differs from the signed-in token account ({TokenAccountId}); using the token account for request routing.",
                    configuredAccountId,
                    tokenAccountId);
            }

            return tokenAccountId;
        }

        return configuredAccountId;
    }

    internal async Task<ChatGptCodexModelsHttpResponse> FetchChatGptCodexModelsAsync(
        EffectiveModelRuntime runtime,
        string clientVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!runtime.IsChatGptOAuth)
            throw new ArgumentException("Provider must use ChatGPT OAuth.", nameof(runtime));
        if (!runtime.IsRemote && _openAIAuthService is null)
            throw new InvalidOperationException(
                "ChatGPT OAuth provider requested but no IOpenAIAuthService was registered.");
        if (string.IsNullOrWhiteSpace(clientVersion))
            throw new ArgumentException("Client version must be configured.", nameof(clientVersion));

        var requestUri = BuildChatGptCodexModelsUri(runtime, clientVersion);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(NormalizeNetworkTimeoutSeconds(runtime.NetworkTimeoutSeconds)));

        var response = await SendChatGptCodexModelsRequestAsync(
            requestUri,
            runtime,
            forceRefresh: false,
            timeoutCts.Token).ConfigureAwait(false);
        if (!runtime.IsRemote && (int)response.StatusCode == 401)
        {
            response.Dispose();
            response = await SendChatGptCodexModelsRequestAsync(
                requestUri,
                runtime,
                forceRefresh: true,
                timeoutCts.Token).ConfigureAwait(false);
        }

        return await ReadChatGptCodexModelsResponseAsync(response, timeoutCts.Token).ConfigureAwait(false);
    }

}
