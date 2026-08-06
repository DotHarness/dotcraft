using System.Collections.Concurrent;
using Anthropic;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>
/// Creates and caches Anthropic SDK clients for DotCraft runtime paths.
/// </summary>
public sealed class AnthropicClientProvider : IModelProvider, IModelCatalogProvider
{
    private static readonly IReadOnlyCollection<string> SupportedProtocols =
        Array.AsReadOnly([ModelProviderProtocols.Anthropic]);

    /// <summary>
    /// DotCraft's default Anthropic maximum output token budget when provider config does not override it.
    /// </summary>
    public const int DefaultMaxOutputTokens = 64_000;

    private readonly ConcurrentDictionary<AnthropicClientKey, AnthropicClient> _anthropicClients = new();
    private static readonly HttpClient SharedHttpClient = new();

    /// <inheritdoc />
    public IReadOnlyCollection<string> Protocols => SupportedProtocols;

    /// <inheritdoc />
    public IChatClient CreateChatClient(EffectiveModelRuntime runtime)
    {
        var client = GetChatClient(runtime);
        var catalogPath = string.IsNullOrWhiteSpace(runtime.ProviderStateDirectory)
            ? null
            : Path.Combine(runtime.ProviderStateDirectory, ModelThinkingAdapterCatalog.FileName);
        var contentAdapter = ModelThinkingAdapterCatalog.ResolveAnthropicMessageContentAdapter(
            runtime.EndPoint,
            runtime.Model,
            catalogPath);
        if (contentAdapter != null)
            client = new DeepSeekAnthropicReasoningHistoryChatClient(client, contentAdapter);
        client = new AnthropicThinkingChatClient(
            client,
            ModelThinkingAdapterCatalog.ResolveAnthropicThinkingAdapter(
                runtime.EndPoint,
                runtime.Model,
                catalogPath),
            runtime.Model,
            runtime.MaxOutputTokens);
        client = new AnthropicEagerToolInputStreamingChatClient(client);
        client = new AnthropicDeferredToolLoadingChatClient(
            client,
            runtime.Model,
            runtime.MaxOutputTokens);
        return new ProviderServiceChatClient(
            new AnthropicFastModeChatClient(new AnthropicProviderContentChatClient(client), runtime),
            new Dictionary<Type, object>
            {
                [typeof(IToolCallArgumentsDeltaExtractor)] = AnthropicToolCallArgumentsDeltaExtractor.Instance,
                [typeof(IPromptCacheDialect)] = AnthropicPromptCacheDialect.Instance
            });
    }

    async Task<ModelCatalogResult> IModelCatalogProvider.FetchModelsAsync(
        EffectiveModelRuntime runtime,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runtime.ApiKey))
            return Failure(ModelCatalogErrorCode.MissingApiKey, "API key is not configured.");
        if (!Uri.TryCreate(runtime.EndPoint, UriKind.Absolute, out var endpoint))
            return Failure(ModelCatalogErrorCode.InvalidEndpoint, "Endpoint is invalid.");

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(endpoint.ToString().TrimEnd('/') + "/v1/models?limit=1000"));
            request.Headers.Add("x-api-key", runtime.ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(runtime.NetworkTimeoutSeconds));
            using var response = await SharedHttpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return Failure(ModelCatalogErrorCode.Unauthorized, "Request was unauthorized.");
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return Failure(ModelCatalogErrorCode.Forbidden, "Request was forbidden.");
            if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.MethodNotAllowed)
                return Failure(ModelCatalogErrorCode.EndpointNotSupported, "Endpoint does not support model listing.");
            if (!response.IsSuccessStatusCode)
                return Failure(ModelCatalogErrorCode.Unknown, $"Anthropic model list request failed with HTTP {(int)response.StatusCode}.");

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            using var document = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: cts.Token).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != System.Text.Json.JsonValueKind.Array)
                return Failure(ModelCatalogErrorCode.Unknown, "Model list response did not contain a data array.");

            var models = new List<ModelCatalogEntry>();
            foreach (var item in data.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idValue)
                    || idValue.ValueKind != System.Text.Json.JsonValueKind.String
                    || string.IsNullOrWhiteSpace(idValue.GetString()))
                    continue;
                DateTimeOffset.TryParse(
                    item.TryGetProperty("created_at", out var createdValue)
                    && createdValue.ValueKind == System.Text.Json.JsonValueKind.String
                        ? createdValue.GetString()
                        : null,
                    out var createdAt);
                models.Add(new ModelCatalogEntry
                {
                    Id = idValue.GetString()!.Trim(),
                    OwnedBy = item.TryGetProperty("display_name", out var nameValue)
                              && nameValue.ValueKind == System.Text.Json.JsonValueKind.String
                        ? nameValue.GetString() ?? "anthropic"
                        : "anthropic",
                    CreatedAt = createdAt
                });
            }
            return new ModelCatalogResult
            {
                Success = true,
                Models = models.OrderBy(static model => model.Id, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(ModelCatalogErrorCode.Timeout, "Request timed out.");
        }
        catch (HttpRequestException ex)
        {
            return Failure(ModelCatalogErrorCode.Network, ex.Message);
        }
    }

    private static ModelCatalogResult Failure(ModelCatalogErrorCode code, string message) => new()
    {
        Success = false,
        ErrorCode = code,
        ErrorMessage = message
    };

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey != null)
            return null;
        if (serviceType == typeof(IPromptCacheDialect))
            return AnthropicPromptCacheDialect.Instance;
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <summary>
    /// Gets a cached Anthropic client for a resolved Anthropic runtime.
    /// </summary>
    public AnthropicClient GetAnthropicClient(EffectiveModelRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!string.Equals(runtime.Protocol, ModelProviderProtocols.Anthropic, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Provider '{runtime.ProviderId}' does not use the Anthropic protocol.", nameof(runtime));

        return GetAnthropicClient(AnthropicClientKey.From(runtime));
    }

    /// <summary>
    /// Gets a provider-neutral chat client for a resolved Anthropic runtime.
    /// </summary>
    public IChatClient GetChatClient(EffectiveModelRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return GetAnthropicClient(runtime).Beta.AsIChatClient(
            NormalizeRequiredModel(runtime.Model),
            defaultMaxOutputTokens: NormalizeMaxOutputTokens(runtime.MaxOutputTokens));
    }

    private AnthropicClient GetAnthropicClient(AnthropicClientKey key) =>
        _anthropicClients.GetOrAdd(key, static clientKey =>
            new AnthropicClient
            {
                ApiKey = clientKey.ApiKey,
                BaseUrl = clientKey.Endpoint.ToString().TrimEnd('/'),
                MaxRetries = 0,
                Timeout = TimeSpan.FromSeconds(clientKey.NetworkTimeoutSeconds)
            });

    private static string NormalizeRequiredModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model must be configured.", nameof(model));

        return model.Trim();
    }

    private readonly record struct AnthropicClientKey(Uri Endpoint, string ApiKey, int NetworkTimeoutSeconds)
    {
        public static AnthropicClientKey From(EffectiveModelRuntime runtime)
        {
            if (string.IsNullOrWhiteSpace(runtime.ApiKey))
                throw new ArgumentException("API key must be configured.", nameof(runtime));

            if (!Uri.TryCreate(runtime.EndPoint, UriKind.Absolute, out var endpoint))
                throw new ArgumentException("Endpoint must be an absolute URI.", nameof(runtime));

            return new AnthropicClientKey(endpoint, runtime.ApiKey, NormalizeNetworkTimeoutSeconds(runtime.NetworkTimeoutSeconds));
        }
    }

    private static int NormalizeNetworkTimeoutSeconds(int seconds) => Math.Max(1, seconds);

    private static int NormalizeMaxOutputTokens(int? value) =>
        value is > 0 ? value.Value : DefaultMaxOutputTokens;
}
