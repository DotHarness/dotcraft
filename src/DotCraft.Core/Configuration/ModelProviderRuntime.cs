using System.Net.Http.Headers;
using System.Text.Json;

namespace DotCraft.Configuration;

public static class ModelProviderProtocols
{
    public const string LegacyOpenAI = "openai";
    public const string OpenAIChatCompletions = "openai-chat-completions";
    public const string OpenAIResponses = "openai-responses";
    public const string OpenAI = OpenAIChatCompletions;
    public const string Anthropic = "anthropic";

    public static string Normalize(string? protocol)
    {
        var normalized = protocol?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "" or null => OpenAIChatCompletions,
            LegacyOpenAI => OpenAIChatCompletions,
            OpenAIChatCompletions => OpenAIChatCompletions,
            OpenAIResponses => OpenAIResponses,
            Anthropic => Anthropic,
            _ => throw new ArgumentException($"Unsupported model provider protocol '{protocol}'.", nameof(protocol))
        };
    }

    public static bool IsOpenAIProtocol(string? protocol)
    {
        try
        {
            var normalized = Normalize(protocol);
            return normalized is OpenAIChatCompletions or OpenAIResponses;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static bool IsOpenAIResponses(string? protocol) =>
        string.Equals(Normalize(protocol), OpenAIResponses, StringComparison.OrdinalIgnoreCase);
}

public static class ModelProviderDefaults
{
    public const string DefaultOpenAIEndpoint = "https://api.openai.com/v1";
    public const string DefaultAnthropicEndpoint = "https://api.anthropic.com";
    public const string ChatGptBackendEndpoint = "https://chatgpt.com/backend-api/codex";
    public const string DefaultChatGptCodexModel = "gpt-5.5";
    public const int DefaultStreamMaxRetries = 5;
    public const int MaxStreamMaxRetries = 100;
    public const int DefaultStreamIdleTimeoutMs = 300_000;

    public static bool IsOfficialOpenAIEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var candidate) ||
            !Uri.TryCreate(DefaultOpenAIEndpoint, UriKind.Absolute, out var official))
        {
            return false;
        }

        return string.Equals(candidate.Scheme, official.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(candidate.Host, official.Host, StringComparison.OrdinalIgnoreCase) &&
               candidate.Port == official.Port &&
               string.Equals(
                   candidate.AbsolutePath.TrimEnd('/'),
                   official.AbsolutePath.TrimEnd('/'),
                   StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Identifier values for <see cref="AppConfig.ModelProviderConfig.AuthMethod"/>.
/// </summary>
public static class ModelProviderAuthMethods
{
    public const string ApiKey = "apiKey";
    public const string ChatGptOAuth = "chatgptOAuth";

    public static string Normalize(string? method)
    {
        var trimmed = method?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return ApiKey;
        return trimmed.Equals(ChatGptOAuth, StringComparison.OrdinalIgnoreCase)
            ? ChatGptOAuth
            : ApiKey;
    }

    public static bool IsChatGptOAuth(string? method) =>
        Normalize(method) == ChatGptOAuth;
}

public sealed record ModelProviderCapabilities
{
    public bool StreamingChat { get; init; } = true;

    public bool ToolCalling { get; init; } = true;

    public bool ModelListing { get; init; } = true;

    public bool TokenUsageReporting { get; init; } = true;

    public bool CachedInputUsageReporting { get; init; }

    public bool PromptCacheRequestShaping { get; init; }

    public bool ExtendedThinking { get; init; }

    public bool ToolChoiceControls { get; init; }

    public bool RawMetadataPassthrough { get; init; }

    public bool ResponsesApi { get; init; }

    public bool NativeDeferredToolLoading { get; init; }

    public static ModelProviderCapabilities ForProtocol(string protocol)
    {
        var normalized = ModelProviderProtocols.Normalize(protocol);
        return normalized switch
        {
            ModelProviderProtocols.OpenAIChatCompletions => new ModelProviderCapabilities
            {
                CachedInputUsageReporting = true,
                PromptCacheRequestShaping = true,
                ExtendedThinking = true,
                ToolChoiceControls = true,
                RawMetadataPassthrough = true
            },
            ModelProviderProtocols.OpenAIResponses => new ModelProviderCapabilities
            {
                CachedInputUsageReporting = true,
                PromptCacheRequestShaping = true,
                ExtendedThinking = true,
                ToolChoiceControls = true,
                RawMetadataPassthrough = true,
                ResponsesApi = true,
                NativeDeferredToolLoading = true
            },
            ModelProviderProtocols.Anthropic => new ModelProviderCapabilities
            {
                CachedInputUsageReporting = true,
                ExtendedThinking = true,
                ToolChoiceControls = false,
                PromptCacheRequestShaping = true,
                RawMetadataPassthrough = false,
                NativeDeferredToolLoading = true
            },
            _ => new ModelProviderCapabilities()
        };
    }
}

public sealed record EffectiveModelRuntime(
    string ProviderId,
    string Model,
    string Protocol,
    string DisplayName,
    string ApiKey,
    string EndPoint,
    int NetworkTimeoutSeconds,
    int? MaxOutputTokens,
    bool IsImplicit,
    ModelProviderCapabilities Capabilities,
    int StreamMaxRetries = ModelProviderDefaults.DefaultStreamMaxRetries,
    int StreamIdleTimeoutMs = ModelProviderDefaults.DefaultStreamIdleTimeoutMs,
    string AuthMethod = ModelProviderAuthMethods.ApiKey,
    string? ChatGptAccountId = null,
    bool SupportsHostedImageGeneration = false)
{
    public bool IsOpenAICompatible =>
        ModelProviderProtocols.IsOpenAIProtocol(Protocol);

    public bool IsOpenAIResponses =>
        ModelProviderProtocols.IsOpenAIResponses(Protocol);

    /// <summary>True when this runtime authenticates with a ChatGPT subscription via OAuth.</summary>
    public bool IsChatGptOAuth =>
        ModelProviderAuthMethods.IsChatGptOAuth(AuthMethod);
}

public static class ModelProviderResolver
{
    public const string MissingProviderMessage = "Create or select a model provider.";

    public static EffectiveModelRuntime ResolveMain(
        AppConfig config,
        string? providerIdOverride = null,
        string? modelOverride = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var provider = ResolveProvider(config, providerIdOverride);
        var model = string.IsNullOrWhiteSpace(modelOverride)
            ? config.Model
            : modelOverride;

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model must be configured.", nameof(config));

        return provider with { Model = model.Trim() };
    }

    public static EffectiveModelRuntime ResolveConsolidation(
        AppConfig config,
        string? providerIdOverride = null,
        string? mainModelOverride = null)
    {
        var model = string.IsNullOrWhiteSpace(config.ConsolidationModel)
            ? mainModelOverride
            : config.ConsolidationModel;
        return ResolveMain(config, providerIdOverride, model);
    }

    public static EffectiveModelRuntime ResolveSubAgent(
        AppConfig config,
        string effectiveMainProviderId,
        string effectiveMainModel)
    {
        var model = string.IsNullOrWhiteSpace(config.SubAgent.Model)
            ? effectiveMainModel
            : config.SubAgent.Model;
        return ResolveMain(config, effectiveMainProviderId, model);
    }

    public static EffectiveModelRuntime ResolveProvider(AppConfig config, string? providerIdOverride = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var providerId = NormalizeProviderId(providerIdOverride);
        if (string.IsNullOrWhiteSpace(providerId))
            providerId = NormalizeProviderId(config.ProviderId);

        if (string.IsNullOrWhiteSpace(providerId))
            throw new ModelProviderConfigurationException(
                ModelCatalogErrorCode.ProviderNotConfigured,
                MissingProviderMessage);

        if (TryResolveExplicitProvider(config, providerId, out var explicitRuntime))
            return explicitRuntime;

        throw new ModelProviderConfigurationException(
            ModelCatalogErrorCode.ProviderNotConfigured,
            $"Model provider '{providerId}' is not configured. {MissingProviderMessage}");
    }

    public static bool IsImplicitProviderId(string? providerId) => false;

    public static bool ResolveHostedImageGenerationSupport(AppConfig.ModelProviderConfig provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var configuredAuthMethod = string.IsNullOrWhiteSpace(provider.AuthMethod)
            ? ModelProviderAuthMethods.ApiKey
            : provider.AuthMethod.Trim();
        var authMethod = ModelProviderAuthMethods.Normalize(configuredAuthMethod);
        var protocol = ModelProviderProtocols.Normalize(provider.Protocol);

        if (authMethod == ModelProviderAuthMethods.ChatGptOAuth && ModelProviderProtocols.IsOpenAIProtocol(protocol))
            protocol = ModelProviderProtocols.OpenAIResponses;

        var endpoint = NormalizeEndpoint(protocol, provider.EndPoint, authMethod);
        return provider.SupportsHostedImageGeneration
            ?? DefaultSupportsHostedImageGeneration(protocol, endpoint, configuredAuthMethod);
    }

    public static bool DefaultSupportsHostedImageGeneration(string protocol, string endpoint, string authMethod)
    {
        var configuredAuthMethod = string.IsNullOrWhiteSpace(authMethod)
            ? ModelProviderAuthMethods.ApiKey
            : authMethod.Trim();

        if (ModelProviderAuthMethods.IsChatGptOAuth(configuredAuthMethod))
            return true;

        if (!string.Equals(configuredAuthMethod, ModelProviderAuthMethods.ApiKey, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!ModelProviderProtocols.IsOpenAIResponses(protocol))
            return false;

        return ModelProviderDefaults.IsOfficialOpenAIEndpoint(endpoint);
    }

    private static string? NormalizeProviderId(string? providerId)
    {
        var trimmed = providerId?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static bool TryResolveExplicitProvider(
        AppConfig config,
        string providerId,
        out EffectiveModelRuntime runtime)
    {
        if (config.Providers.TryGetValue(providerId, out var provider))
        {
            var configuredAuthMethod = string.IsNullOrWhiteSpace(provider.AuthMethod)
                ? ModelProviderAuthMethods.ApiKey
                : provider.AuthMethod.Trim();
            var authMethod = ModelProviderAuthMethods.Normalize(configuredAuthMethod);
            var protocol = ModelProviderProtocols.Normalize(provider.Protocol);

            if (authMethod == ModelProviderAuthMethods.ChatGptOAuth)
            {
                if (!ModelProviderProtocols.IsOpenAIProtocol(protocol))
                {
                    throw new ModelProviderConfigurationException(
                        ModelCatalogErrorCode.UnsupportedProtocol,
                        $"Provider '{providerId}' uses chatgptOAuth but its protocol '{protocol}' is not OpenAI-compatible.");
                }
                protocol = ModelProviderProtocols.OpenAIResponses;
            }

            var endPoint = NormalizeEndpoint(protocol, provider.EndPoint, authMethod);
            var timeout = Math.Max(1, provider.NetworkTimeoutSeconds ?? config.NetworkTimeoutSeconds);
            var maxOutputTokens = NormalizePositiveNullable(provider.MaxOutputTokens);
            var streamMaxRetries = NormalizeStreamMaxRetries(provider.StreamMaxRetries);
            var streamIdleTimeoutMs = NormalizeStreamIdleTimeoutMs(provider.StreamIdleTimeoutMs);
            var supportsHostedImageGeneration = provider.SupportsHostedImageGeneration
                ?? DefaultSupportsHostedImageGeneration(protocol, endPoint, configuredAuthMethod);
            var accountId = string.IsNullOrWhiteSpace(provider.ChatGptAccountId)
                ? null
                : provider.ChatGptAccountId.Trim();

            // API key is irrelevant in OAuth mode; the OpenAIClientProvider injects a placeholder so
            // the SDK still constructs a client.
            var apiKey = authMethod == ModelProviderAuthMethods.ChatGptOAuth
                ? string.Empty
                : provider.ApiKey?.Trim() ?? string.Empty;

            runtime = new EffectiveModelRuntime(
                providerId,
                NormalizeRequiredModel(config.Model),
                protocol,
                string.IsNullOrWhiteSpace(provider.DisplayName) ? providerId : provider.DisplayName.Trim(),
                apiKey,
                endPoint,
                timeout,
                maxOutputTokens,
                IsImplicit: false,
                ModelProviderCapabilities.ForProtocol(protocol),
                streamMaxRetries,
                streamIdleTimeoutMs,
                configuredAuthMethod,
                accountId,
                supportsHostedImageGeneration);
            return true;
        }

        runtime = null!;
        return false;
    }

    private static string NormalizeEndpoint(string protocol, string? endpoint, string authMethod)
    {
        if (authMethod == ModelProviderAuthMethods.ChatGptOAuth)
            return ModelProviderDefaults.ChatGptBackendEndpoint;

        if (!string.IsNullOrWhiteSpace(endpoint))
            return endpoint.Trim();

        return protocol switch
        {
            ModelProviderProtocols.OpenAIChatCompletions => ModelProviderDefaults.DefaultOpenAIEndpoint,
            ModelProviderProtocols.OpenAIResponses => ModelProviderDefaults.DefaultOpenAIEndpoint,
            ModelProviderProtocols.Anthropic => ModelProviderDefaults.DefaultAnthropicEndpoint,
            _ => string.Empty
        };
    }

    private static string NormalizeRequiredModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model must be configured.", nameof(model));

        return model.Trim();
    }

    private static int? NormalizePositiveNullable(int? value) =>
        value is > 0 ? value : null;

    private static int NormalizeStreamMaxRetries(int? value) =>
        value.HasValue
            ? Math.Clamp(value.Value, 0, ModelProviderDefaults.MaxStreamMaxRetries)
            : ModelProviderDefaults.DefaultStreamMaxRetries;

    private static int NormalizeStreamIdleTimeoutMs(int? value) =>
        value is > 0 ? value.Value : ModelProviderDefaults.DefaultStreamIdleTimeoutMs;
}

public sealed class ModelProviderConfigurationException(
    ModelCatalogErrorCode errorCode,
    string message)
    : InvalidOperationException(message)
{
    public ModelCatalogErrorCode ErrorCode { get; } = errorCode;
}

public enum ModelCatalogErrorCode
{
    None = 0,
    ProviderNotConfigured,
    MissingApiKey,
    InvalidEndpoint,
    UnsupportedProtocol,
    Unauthorized,
    Forbidden,
    EndpointNotSupported,
    Network,
    Timeout,
    Unknown
}

public sealed class ModelCatalogEntry
{
    public string Id { get; set; } = string.Empty;

    public string OwnedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ModelCatalogResult
{
    public bool Success { get; set; }

    public List<ModelCatalogEntry> Models { get; set; } = [];

    public ModelCatalogErrorCode ErrorCode { get; set; } = ModelCatalogErrorCode.None;

    public string? ErrorMessage { get; set; }

    public string? ProviderId { get; set; }

    public string? Protocol { get; set; }

    public string? EndPoint { get; set; }
}

public static class ModelProviderCatalog
{
    private static readonly HttpClient HttpClient = new();

    public static async Task<ModelCatalogResult> FetchAsync(
        AppConfig config,
        string? providerId = null,
        CancellationToken cancellationToken = default,
        DotCraft.Agents.OpenAIClientProvider? clientProvider = null)
    {
        EffectiveModelRuntime runtime;
        try
        {
            runtime = ModelProviderResolver.ResolveProvider(config, providerId);
        }
        catch (ModelProviderConfigurationException ex)
        {
            return Failure(ex.ErrorCode, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Failure(ModelCatalogErrorCode.Unknown, ex.Message);
        }

        var result = runtime.Protocol switch
        {
            ModelProviderProtocols.OpenAIChatCompletions or ModelProviderProtocols.OpenAIResponses
                => await FetchOpenAIAsync(config, runtime, cancellationToken, clientProvider),
            ModelProviderProtocols.Anthropic => await FetchAnthropicAsync(runtime, cancellationToken),
            _ => Failure(ModelCatalogErrorCode.UnsupportedProtocol, $"Protocol '{runtime.Protocol}' does not support model listing.")
        };
        result.ProviderId = runtime.ProviderId;
        result.Protocol = runtime.Protocol;
        result.EndPoint = runtime.EndPoint;
        return result;
    }

    private static async Task<ModelCatalogResult> FetchOpenAIAsync(
        AppConfig config,
        EffectiveModelRuntime runtime,
        CancellationToken cancellationToken,
        DotCraft.Agents.OpenAIClientProvider? clientProvider)
    {
        var result = await OpenAIModelCatalog.FetchAsync(config, runtime, cancellationToken, clientProvider);
        return new ModelCatalogResult
        {
            Success = result.Success,
            Models = [.. result.Models.Select(m => new ModelCatalogEntry
            {
                Id = m.Id,
                OwnedBy = m.OwnedBy,
                CreatedAt = m.CreatedAt
            })],
            ErrorCode = MapOpenAIError(result.ErrorCode),
            ErrorMessage = result.ErrorMessage
        };
    }

    private static async Task<ModelCatalogResult> FetchAnthropicAsync(
        EffectiveModelRuntime runtime,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runtime.ApiKey))
            return Failure(ModelCatalogErrorCode.MissingApiKey, "API key is not configured.");

        if (!Uri.TryCreate(runtime.EndPoint, UriKind.Absolute, out var endpoint))
            return Failure(ModelCatalogErrorCode.InvalidEndpoint, "Endpoint is invalid.");

        try
        {
            var modelsUri = new Uri(endpoint.ToString().TrimEnd('/') + "/v1/models?limit=1000");
            using var request = new HttpRequestMessage(HttpMethod.Get, modelsUri);
            request.Headers.Add("x-api-key", runtime.ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(runtime.NetworkTimeoutSeconds));
            using var response = await HttpClient.SendAsync(request, cts.Token);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return Failure(ModelCatalogErrorCode.Unauthorized, "Request was unauthorized.");
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return Failure(ModelCatalogErrorCode.Forbidden, "Request was forbidden.");
            if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.MethodNotAllowed)
                return Failure(ModelCatalogErrorCode.EndpointNotSupported, "Endpoint does not support model listing.");
            if (!response.IsSuccessStatusCode)
                return Failure(ModelCatalogErrorCode.Unknown, $"Anthropic model list request failed with HTTP {(int)response.StatusCode}.");

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
            var root = document.RootElement;
            if (!TryGetProperty(root, "data", out var data) || data.ValueKind != JsonValueKind.Array)
                return Failure(ModelCatalogErrorCode.Unknown, "Model list response did not contain a data array.");

            var entries = new List<ModelCatalogEntry>();
            foreach (var item in data.EnumerateArray())
            {
                if (!TryGetProperty(item, "id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
                    continue;

                var id = idEl.GetString();
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                DateTimeOffset.TryParse(
                    TryGetProperty(item, "created_at", out var createdAtEl) && createdAtEl.ValueKind == JsonValueKind.String
                        ? createdAtEl.GetString()
                        : null,
                    out var createdAt);
                var ownedBy = TryGetProperty(item, "display_name", out var displayNameEl)
                    && displayNameEl.ValueKind == JsonValueKind.String
                        ? displayNameEl.GetString() ?? string.Empty
                        : "anthropic";
                entries.Add(new ModelCatalogEntry
                {
                    Id = id.Trim(),
                    OwnedBy = ownedBy,
                    CreatedAt = createdAt
                });
            }

            return new ModelCatalogResult
            {
                Success = true,
                Models = [.. entries.OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase)]
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(ModelCatalogErrorCode.Timeout, "Request timed out.");
        }
        catch (HttpRequestException ex)
        {
            return Failure(ModelCatalogErrorCode.Network, string.IsNullOrWhiteSpace(ex.Message) ? "Network request failed." : ex.Message);
        }
        catch (TaskCanceledException)
        {
            return Failure(ModelCatalogErrorCode.Timeout, "Request timed out.");
        }
        catch (Exception ex)
        {
            return Failure(ModelCatalogErrorCode.Unknown, string.IsNullOrWhiteSpace(ex.Message) ? "Unknown error." : ex.Message);
        }
    }

    private static ModelCatalogResult Failure(ModelCatalogErrorCode code, string message) => new()
    {
        Success = false,
        ErrorCode = code,
        ErrorMessage = message,
        Models = []
    };

    private static ModelCatalogErrorCode MapOpenAIError(OpenAIModelCatalogErrorCode code) => code switch
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
    };

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
