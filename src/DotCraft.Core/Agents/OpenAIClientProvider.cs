using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using DotCraft.Tracing;
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
public sealed class OpenAIClientProvider
{
    private static readonly HttpClient SharedChatGptHttpClient = new();

    private readonly ConcurrentDictionary<OpenAIClientKey, OpenAIClient> _openAIClients = new();
    private readonly ConcurrentDictionary<OpenAIChatClientKey, ChatClient> _openAIChatClients = new();
    private readonly ConcurrentDictionary<OpenAIImageClientKey, ImageClient> _openAIImageClients = new();
    private readonly ConcurrentDictionary<OpenAIClientKey, ResponsesClient> _openAIResponsesClients = new();
    private readonly ConcurrentDictionary<OpenAIChatClientKey, IChatClient> _openAIResponsesChatClients = new();
    private readonly IOpenAIAuthService? _openAIAuthService;
    private readonly OpenAIInstallationIdProvider? _installationIdProvider;
    private readonly HttpClient _chatGptHttpClient;
    private readonly ILogger<OpenAIClientProvider>? _logger;
    private int _accountMismatchWarningLogged;

    /// <summary>Default constructor for DI; OAuth-mode providers require an auth service.</summary>
    public OpenAIClientProvider(
        IOpenAIAuthService? openAIAuthService = null,
        OpenAIInstallationIdProvider? installationIdProvider = null,
        ILogger<OpenAIClientProvider>? logger = null)
        : this(openAIAuthService, installationIdProvider, chatGptHttpMessageHandler: null, logger)
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
        ILogger<OpenAIClientProvider>? logger = null)
    {
        _openAIAuthService = openAIAuthService;
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
            new OpenAIResponsesToolSearchChatClient(
                provider.GetOpenAIResponsesClient(chatKey.Client),
                chatKey.Model), this);
    }

    internal static OpenAIClientOptions CreateClientOptions(Uri endpoint, int networkTimeoutSeconds)
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
            var options = CreateClientOptions(clientKey.Endpoint, clientKey.NetworkTimeoutSeconds);
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

    private async Task<HttpResponseMessage> SendChatGptCodexModelsRequestAsync(
        Uri requestUri,
        EffectiveModelRuntime runtime,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var token = await _openAIAuthService!.GetAccessTokenAsync(forceRefresh, cancellationToken).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var accountId = ResolveChatGptAccountId(runtime);
        if (!string.IsNullOrWhiteSpace(accountId))
            request.Headers.TryAddWithoutValidation(OpenAIAuthConstants.AccountIdHeader, accountId);
        request.Headers.TryAddWithoutValidation(OpenAIAuthConstants.OriginatorHeader, OpenAIAuthConstants.Originator);
        return await _chatGptHttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendImageEditRequestAsync(
        Uri endpoint,
        EffectiveModelRuntime runtime,
        string imageModel,
        string prompt,
        IReadOnlyList<OpenAIImageEditInput> images,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var baseUri = new Uri(endpoint.ToString().TrimEnd('/') + "/");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "images/edits"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("User-Agent", DotCraftUserAgentPipelinePolicy.UserAgentValue);
        await ApplyImageEditAuthHeadersAsync(request, runtime, forceRefresh, cancellationToken).ConfigureAwait(false);

        var content = new MultipartFormDataContent
        {
            { new StringContent(imageModel), "model" },
            { new StringContent(prompt), "prompt" },
            { new StringContent("b64_json"), "response_format" },
            { new StringContent("png"), "output_format" }
        };

        foreach (var image in images)
        {
            var imageContent = new ByteArrayContent(image.Bytes);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue(image.MediaType);
            content.Add(imageContent, "image[]", image.FileName);
        }

        request.Content = content;
        return await _chatGptHttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyImageEditAuthHeadersAsync(
        HttpRequestMessage request,
        EffectiveModelRuntime runtime,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (runtime.IsChatGptOAuth)
        {
            if (_openAIAuthService is null)
                throw new InvalidOperationException(
                    "ChatGPT OAuth provider requested but no IOpenAIAuthService was registered.");

            var token = await _openAIAuthService.GetAccessTokenAsync(forceRefresh, cancellationToken).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var accountId = ResolveChatGptAccountId(runtime);
            if (!string.IsNullOrWhiteSpace(accountId))
                request.Headers.TryAddWithoutValidation(OpenAIAuthConstants.AccountIdHeader, accountId);
            request.Headers.TryAddWithoutValidation(OpenAIAuthConstants.OriginatorHeader, OpenAIAuthConstants.Originator);

            var installationId = _installationIdProvider?.GetInstallationId();
            if (!string.IsNullOrWhiteSpace(installationId))
                request.Headers.TryAddWithoutValidation(OpenAIAuthConstants.InstallationIdHeader, installationId);

            var sessionKey = TracingChatClient.CurrentSessionKey ?? TracingChatClient.GetActiveSessionKey();
            if (!string.IsNullOrWhiteSpace(sessionKey))
            {
                var trimmed = sessionKey.Trim();
                request.Headers.TryAddWithoutValidation(OpenAIAuthConstants.SessionIdHeader, trimmed);
                request.Headers.TryAddWithoutValidation(OpenAIAuthConstants.ThreadIdHeader, trimmed);
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(runtime.ApiKey))
            throw new ArgumentException("API key must be configured.", nameof(runtime));

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", runtime.ApiKey);
    }

    private static async Task<byte[]> ReadImageEditResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            var body = response.Content is null
                ? string.Empty
                : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var snippet = body.Length > 1000 ? body[..1000] : body;
                throw new HttpRequestException(
                    $"Image edit request failed with HTTP {(int)response.StatusCode}: {snippet}");
            }

            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array ||
                data.GetArrayLength() == 0 ||
                !data[0].TryGetProperty("b64_json", out var b64Json) ||
                b64Json.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException("Image edit response did not include image bytes.");
            }

            var base64 = b64Json.GetString();
            if (string.IsNullOrWhiteSpace(base64))
                throw new InvalidOperationException("Image edit response included empty image bytes.");

            return Convert.FromBase64String(base64);
        }
    }

    private static async Task<ChatGptCodexModelsHttpResponse> ReadChatGptCodexModelsResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            var body = response.Content is null
                ? string.Empty
                : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new ChatGptCodexModelsHttpResponse(
                StatusCode: (int)response.StatusCode,
                Content: body,
                ETag: response.Headers.ETag?.Tag);
        }
    }

    private static Uri BuildChatGptCodexModelsUri(EffectiveModelRuntime runtime, string clientVersion)
    {
        if (!Uri.TryCreate(runtime.EndPoint, UriKind.Absolute, out var endpoint))
            throw new ArgumentException("Endpoint must be an absolute URI.", nameof(runtime));

        var baseUri = new Uri(endpoint.ToString().TrimEnd('/') + "/");
        var uriBuilder = new UriBuilder(new Uri(baseUri, "models"))
        {
            Query = $"client_version={Uri.EscapeDataString(clientVersion.Trim())}"
        };
        return uriBuilder.Uri;
    }

    private static string NormalizeRequiredModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model must be configured.", nameof(model));

        return model.Trim();
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

    private readonly record struct OpenAIChatClientKey(OpenAIClientKey Client, string Model)
    {
        public static OpenAIChatClientKey From(EffectiveModelRuntime runtime) =>
            new(OpenAIClientKey.From(runtime), NormalizeRequiredModel(runtime.Model));
    }

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
}

internal sealed record ChatGptCodexModelsHttpResponse(
    int StatusCode,
    string Content,
    string? ETag);

internal sealed record OpenAIImageEditInput(
    byte[] Bytes,
    string FileName,
    string MediaType);
