namespace DotCraft.Configuration;

/// <summary>Stable protocol identifiers understood by the DotCraft model runtime.</summary>
public static class ModelProviderProtocols
{
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

/// <summary>Default values shared by runtime resolution and built-in providers.</summary>
public static class ModelProviderDefaults
{
    public const int DefaultAnthropicMaxOutputTokens = 64_000;
    public const string DefaultOpenAIEndpoint = "https://api.openai.com/v1";
    public const string DefaultAnthropicEndpoint = "https://api.anthropic.com";
    public const string ChatGptBackendEndpoint = "https://chatgpt.com/backend-api/codex";
    public const string DefaultChatGptCodexModel = "gpt-5.6-sol";
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

/// <summary>Stable authentication method identifiers used by model runtimes.</summary>
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

/// <summary>Provider-neutral capabilities available for a resolved runtime.</summary>
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
                PromptCacheRequestShaping = true,
                NativeDeferredToolLoading = true
            },
            _ => new ModelProviderCapabilities()
        };
    }
}

/// <summary>Immutable effective provider configuration supplied to an integration.</summary>
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
    bool SupportsHostedImageGeneration = false,
    bool UseResponsesLite = false,
    string? ProviderStateDirectory = null)
{
    public bool IsOpenAICompatible => ModelProviderProtocols.IsOpenAIProtocol(Protocol);
    public bool IsOpenAIResponses => ModelProviderProtocols.IsOpenAIResponses(Protocol);
    public bool IsChatGptOAuth => ModelProviderAuthMethods.IsChatGptOAuth(AuthMethod);
}

/// <summary>Stable model-catalog failure classifications.</summary>
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

/// <summary>A provider-neutral model catalog entry.</summary>
public sealed class ModelCatalogEntry
{
    public string Id { get; set; } = string.Empty;
    public string OwnedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A provider-neutral model catalog operation result.</summary>
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
