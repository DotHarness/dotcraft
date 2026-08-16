using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using System.Net;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Images;
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
    IProviderHostedToolAdapter,
    IProviderLifecycle
{
    private static readonly IReadOnlyCollection<string> SupportedProtocols = Array.AsReadOnly([
        ModelProviderProtocols.OpenAIChatCompletions,
        ModelProviderProtocols.OpenAIResponses
    ]);
    private static readonly HttpClient SharedChatGptHttpClient = new();

    private readonly ConcurrentDictionary<OpenAIClientKey, OpenAIClient> _openAIClients = new();
    private readonly ConcurrentDictionary<OpenAIChatClientKey, ChatClient> _openAIChatClients = new();
    private readonly ConcurrentDictionary<OpenAIImageClientKey, ImageClient> _openAIImageClients = new();
    private readonly ConcurrentDictionary<OpenAIClientKey, ResponsesClient> _openAIResponsesClients = new();
    private readonly ConcurrentDictionary<OpenAIClientKey, ResponsesLiteClientContext> _openAIResponsesLiteClients = new();
    private readonly ConcurrentDictionary<OpenAIChatClientKey, IChatClient> _openAIResponsesChatClients = new();
    private readonly IOpenAIAuthService? _openAIAuthService;
    private readonly IOpenAIUsageService? _openAIUsageService;
    private readonly OpenAIInstallationIdProvider? _installationIdProvider;
    private readonly HttpClient _chatGptHttpClient;
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
            ModelProviderProtocols.OpenAIChatCompletions => GetOpenAIChatClient(runtime).AsIChatClient(),
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
                [typeof(IPromptCacheDialect)] = OpenAIPromptCacheDialect.Instance
            });
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey != null)
            return null;
        if (serviceType == typeof(IPromptCacheDialect))
            return OpenAIPromptCacheDialect.Instance;
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    string? IProviderRuntimeIdentityResolver.ResolveAccountId(EffectiveModelRuntime runtime) =>
        ResolveChatGptAccountId(runtime);

    ProviderRuntimeMetadata IProviderRuntimeMetadataResolver.Resolve(EffectiveModelRuntime runtime)
    {
        var metadata = ChatGptCodexModelCatalog.ResolveRuntimeMetadata(
            runtime,
            ResolveChatGptAccountId(runtime));
        return new ProviderRuntimeMetadata(
            metadata.UseResponsesLite,
            metadata.SupportsParallelToolCalls);
    }

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

    void IProviderHostedToolAdapter.Configure(
        ChatOptions options,
        IReadOnlySet<string> enabledCapabilities)
    {
        if (enabledCapabilities.Contains("image_generation"))
            ResponsesToolSearchMapper.EnableHostedImageGeneration(options);
    }

    bool IProviderHostedToolAdapter.TryGetFunctionNamespace(
        FunctionCallContent call,
        out string? toolNamespace)
    {
        if (ResponsesToolSearchMapper.TryGetFunctionCallNamespace(call, out var value))
        {
            toolNamespace = value;
            return true;
        }
        toolNamespace = null;
        return false;
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
            rawRepresentationClient);

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

    async Task<byte[]> IProviderImageGeneration.GenerateAsync(
        EffectiveModelRuntime runtime,
        string model,
        string prompt,
        CancellationToken cancellationToken)
    {
        var result = await GetOpenAIImageClient(runtime, model).GenerateImageAsync(
            prompt,
            new OpenAI.Images.ImageGenerationOptions
            {
                ResponseFormat = GeneratedImageFormat.Bytes,
                OutputFileFormat = GeneratedImageFileFormat.Png,
                Size = GeneratedImageSize.Auto,
                Quality = GeneratedImageQuality.Auto
            },
            cancellationToken).ConfigureAwait(false);
        return ExtractImageBytes(result.Value);
    }

    async Task<byte[]> IProviderImageGeneration.EditAsync(
        EffectiveModelRuntime runtime,
        string model,
        string prompt,
        IReadOnlyList<ProviderImageReference> images,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count == 0)
            throw new ArgumentException("At least one reference image is required.", nameof(images));

        if (images.Count == 1)
        {
            using var imageStream = new MemoryStream(images[0].Data, writable: false);
            var result = await GetOpenAIImageClient(runtime, model).GenerateImageEditAsync(
                imageStream,
                images[0].FileName,
                prompt,
                new ImageEditOptions
                {
                    ResponseFormat = GeneratedImageFormat.Bytes,
                    OutputFileFormat = GeneratedImageFileFormat.Png,
                    Size = GeneratedImageSize.Auto,
                    Quality = GeneratedImageQuality.Auto
                },
                cancellationToken).ConfigureAwait(false);
            return ExtractImageBytes(result.Value);
        }

        return await GenerateOpenAIImageEditAsync(
            runtime,
            model,
            prompt,
            images.Select(static image => new OpenAIImageEditInput(
                image.Data,
                image.FileName,
                image.MediaType)).ToArray(),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Default constructor for DI; OAuth-mode providers require an auth service.</summary>
    public OpenAIClientProvider(
        IOpenAIAuthService? openAIAuthService = null,
        OpenAIInstallationIdProvider? installationIdProvider = null,
        ILogger<OpenAIClientProvider>? logger = null,
        IOpenAIUsageService? openAIUsageService = null)
        : this(openAIAuthService, installationIdProvider, chatGptHttpMessageHandler: null, logger, openAIUsageService)
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
        IOpenAIUsageService? openAIUsageService = null)
    {
        _openAIAuthService = openAIAuthService;
        _openAIUsageService = openAIUsageService;
        _installationIdProvider = installationIdProvider;
        _logger = logger;
        _chatGptHttpClient = chatGptHttpMessageHandler is null
            ? SharedChatGptHttpClient
            : new HttpClient(chatGptHttpMessageHandler, disposeHandler: false);
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
    /// Gets a cached OpenAI SDK image client for OpenAI protocol integrations.
    /// </summary>
    public ImageClient GetOpenAIImageClient(EffectiveModelRuntime runtime, string imageModel)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!runtime.IsOpenAICompatible)
            throw new ArgumentException($"Provider '{runtime.ProviderId}' does not use the OpenAI protocol.", nameof(runtime));

        var key = OpenAIImageClientKey.From(runtime, imageModel);
        return _openAIImageClients.GetOrAdd(key, static (imageKey, provider) =>
            provider.GetOpenAIClient(imageKey.Client).GetImageClient(imageKey.Model), this);
    }

    /// <summary>
    /// Sends a multipart multi-image edit request for SDK gaps while preserving provider auth.
    /// </summary>
    internal async Task<byte[]> GenerateOpenAIImageEditAsync(
        EffectiveModelRuntime runtime,
        string imageModel,
        string prompt,
        IReadOnlyList<OpenAIImageEditInput> images,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(images);
        if (!runtime.IsOpenAICompatible)
            throw new ArgumentException($"Provider '{runtime.ProviderId}' does not use the OpenAI protocol.", nameof(runtime));
        if (images.Count == 0)
            throw new ArgumentException("At least one image is required.", nameof(images));

        var model = NormalizeRequiredModel(imageModel);
        if (!Uri.TryCreate(runtime.EndPoint, UriKind.Absolute, out var endpoint))
            throw new ArgumentException("Endpoint must be an absolute URI.", nameof(runtime));

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(NormalizeNetworkTimeoutSeconds(runtime.NetworkTimeoutSeconds)));

        var response = await SendImageEditRequestAsync(
            endpoint,
            runtime,
            model,
            prompt,
            images,
            forceRefresh: false,
            timeoutCts.Token).ConfigureAwait(false);
        if (runtime.IsChatGptOAuth && response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            response = await SendImageEditRequestAsync(
                endpoint,
                runtime,
                model,
                prompt,
                images,
                forceRefresh: true,
                timeoutCts.Token).ConfigureAwait(false);
        }

        return await ReadImageEditResponseAsync(response, timeoutCts.Token).ConfigureAwait(false);
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
        options.AddPolicy(new PromptCacheControlPipelinePolicy(), PipelinePosition.PerCall);
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
        options.AddPolicy(new PromptCacheControlPipelinePolicy(), PipelinePosition.PerCall);
        options.AddPolicy(new DotCraftUserAgentPipelinePolicy(), PipelinePosition.PerCall);
        options.AddPolicy(new OpenAIResponsesLiteHeadersPipelinePolicy(), PipelinePosition.PerCall);
        options.AddPolicy(new OpenAIResponsesRequestCompressionPipelinePolicy(), PipelinePosition.PerCall);
        options.AddPolicy(new OpenAIResponsesAttemptDiagnosticPipelinePolicy(), PipelinePosition.PerCall);
        return options;
    }

    internal string? ResolveChatGptAccountId(EffectiveModelRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var tokenAccountId = NormalizeOptional(_openAIAuthService?.GetAccountId());
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
        if (_openAIAuthService is null)
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
        if ((int)response.StatusCode == 401)
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

    private OpenAIClient GetOpenAIClient(OpenAIClientKey key)
    {
        return _openAIClients.GetOrAdd(key, static (clientKey, provider) =>
        {
            var options = CreateClientOptions(
                clientKey.Endpoint,
                clientKey.NetworkTimeoutSeconds);
            if (clientKey.AuthMethod == ModelProviderAuthMethods.ChatGptOAuth)
            {
                if (provider._openAIAuthService is null)
                    throw new InvalidOperationException(
                        "ChatGPT OAuth provider requested but no IOpenAIAuthService was registered.");

                var installationId = provider._installationIdProvider?.GetInstallationId();
                options.AddPolicy(
                    new OpenAIOAuthPipelinePolicy(
                        provider._openAIAuthService,
                        clientKey.AccountId,
                        installationId,
                        provider._logger),
                    PipelinePosition.BeforeTransport);
                if (!string.IsNullOrWhiteSpace(installationId))
                {
                    options.AddPolicy(
                        new OpenAIResponsesClientMetadataPipelinePolicy(installationId, provider._logger),
                        PipelinePosition.PerCall);
                }
                options.AddPolicy(
                    new OpenAIResponsesRequestCompressionPipelinePolicy(),
                    PipelinePosition.PerCall);

                // SDK requires a non-empty credential to construct the client; the OAuth policy
                // overrides Authorization right before transport so the value is ignored.
                return new OpenAIClient(new ApiKeyCredential("chatgpt-oauth"), options);
            }

            return new OpenAIClient(new ApiKeyCredential(clientKey.ApiKey), options);
        }, this);
    }

    private ResponsesClient GetOpenAIResponsesClient(OpenAIClientKey key) =>
        _openAIResponsesClients.GetOrAdd(key, static (clientKey, provider) =>
            provider.GetOpenAIClient(clientKey).GetResponsesClient(), this);

    private OpenAIResponsesLiteChatClient CreateOpenAIResponsesLiteChatClient(OpenAIChatClientKey key)
    {
        var context = GetOpenAIResponsesLiteClient(key.Client);
        return new OpenAIResponsesLiteChatClient(context.Client, key.Model, context.InstallationId);
    }

    private ResponsesLiteClientContext GetOpenAIResponsesLiteClient(OpenAIClientKey key) =>
        _openAIResponsesLiteClients.GetOrAdd(key, static (clientKey, provider) =>
        {
            if (clientKey.AuthMethod != ModelProviderAuthMethods.ChatGptOAuth)
                throw new InvalidOperationException("Responses Lite requires ChatGPT OAuth authentication.");
            if (provider._openAIAuthService is null)
                throw new InvalidOperationException(
                    "ChatGPT OAuth provider requested but no IOpenAIAuthService was registered.");

            var installationId = provider.ResolveInstallationId();
            var options = CreateResponsesLiteClientOptions(
                clientKey.Endpoint,
                clientKey.NetworkTimeoutSeconds);
            options.AddPolicy(
                new OpenAIOAuthPipelinePolicy(
                    provider._openAIAuthService,
                    clientKey.AccountId,
                    installationId,
                    provider._logger),
                PipelinePosition.BeforeTransport);
            // Capture below OAuth so every physical attempt records final headers and wire bytes.
            options.AddPolicy(new LlmHttpCapturePipelinePolicy(), PipelinePosition.BeforeTransport);
            var client = new OpenAIClient(new ApiKeyCredential("chatgpt-oauth"), options).GetResponsesClient();
            return new ResponsesLiteClientContext(client, installationId);
        }, this);

    private static string NormalizeRequiredModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model must be configured.", nameof(model));

        return model.Trim();
    }

    private string ResolveInstallationId()
    {
        if (_installationIdProvider is null)
        {
            throw new InvalidOperationException(
                "ChatGPT OAuth provider requested but no OpenAIInstallationIdProvider was registered.");
        }

        var installationId = _installationIdProvider.GetInstallationId();
        return string.IsNullOrWhiteSpace(installationId)
            ? throw new InvalidOperationException("OpenAI installation id provider returned an empty value.")
            : installationId;
    }

    private readonly record struct OpenAIClientKey(
        Uri Endpoint,
        string ApiKey,
        int NetworkTimeoutSeconds,
        string AuthMethod,
        string? AccountId)
    {
        public static OpenAIClientKey From(EffectiveModelRuntime runtime)
        {
            var authMethod = ModelProviderAuthMethods.Normalize(runtime.AuthMethod);
            if (authMethod == ModelProviderAuthMethods.ApiKey &&
                string.IsNullOrWhiteSpace(runtime.ApiKey))
            {
                throw new ArgumentException("API key must be configured.", nameof(runtime));
            }

            if (!Uri.TryCreate(runtime.EndPoint, UriKind.Absolute, out var endpoint))
                throw new ArgumentException("Endpoint must be an absolute URI.", nameof(runtime));

            return new OpenAIClientKey(
                endpoint,
                runtime.ApiKey,
                NormalizeNetworkTimeoutSeconds(runtime.NetworkTimeoutSeconds),
                authMethod,
                runtime.ChatGptAccountId);
        }
    }

    private readonly record struct OpenAIChatClientKey(
        OpenAIClientKey Client,
        string Model,
        bool UseResponsesLite)
    {
        public static OpenAIChatClientKey From(EffectiveModelRuntime runtime) =>
            new(
                OpenAIClientKey.From(runtime),
                NormalizeRequiredModel(runtime.Model),
                runtime.IsChatGptOAuth && runtime.UseResponsesLite);
    }

    private sealed record ResponsesLiteClientContext(
        ResponsesClient Client,
        string InstallationId);

    private readonly record struct OpenAIImageClientKey(OpenAIClientKey Client, string Model)
    {
        public static OpenAIImageClientKey From(EffectiveModelRuntime runtime, string imageModel) =>
            new(OpenAIClientKey.From(runtime), NormalizeRequiredModel(imageModel));
    }

    private static int NormalizeNetworkTimeoutSeconds(int seconds) => Math.Max(1, seconds);

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static byte[] ExtractImageBytes(GeneratedImage image)
    {
        if (image.ImageBytes == null)
            throw new InvalidOperationException("OpenAI image response did not include image bytes.");

        return image.ImageBytes.ToArray();
    }
}

internal sealed record ChatGptCodexModelsHttpResponse(
    int StatusCode,
    string Content,
    string? ETag);

internal sealed record OpenAIImageEditInput(
    byte[] Bytes,
    string FileName,
    string MediaType);
