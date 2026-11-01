using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Agents;

internal static class OpenRouterJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter<OpenRouterSort>(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    internal static T Read<T>(string json) where T : class => JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new JsonException("Expected an OpenRouter response object.");
}

internal sealed record OpenRouterData<T>([property: JsonRequired] T Data);
internal sealed record OpenRouterSupplier(string Slug, string Name);
internal sealed record OpenRouterCredential(string Key);
internal sealed record OpenRouterKeyExchange(string Code, string CodeVerifier, string CodeChallengeMethod = "S256");

internal sealed class OpenRouterKeyUsage
{
    public double? Usage { get; init; }
    public double? UsageDaily { get; init; }
    public double? UsageWeekly { get; init; }
    public double? UsageMonthly { get; init; }
    public double? Limit { get; init; }
    public double? LimitRemaining { get; init; }
}

internal sealed class OpenRouterModel
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public long? Created { get; init; }
    public int? ContextLength { get; init; }
    public OpenRouterArchitecture? Architecture { get; init; }
    public OpenRouterModelLimits? TopProvider { get; init; }
    public string[] SupportedParameters { get; init; } = [];
    public string[] Endpoints { get; init; } = [];
    public OpenRouterReasoning? Reasoning { get; init; }
    public OpenRouterPricing? Pricing { get; init; }
}

internal sealed class OpenRouterArchitecture
{
    public string[] InputModalities { get; init; } = [];
    public string[] OutputModalities { get; init; } = [];
}

internal sealed record OpenRouterModelLimits(int? ContextLength, int? MaxCompletionTokens);

internal sealed class OpenRouterReasoning
{
    private string[]? _supportedEfforts;
    public string[]? SupportedEfforts
    {
        get => _supportedEfforts;
        init { _supportedEfforts = value; HasSupportedEfforts = true; }
    }
    [JsonIgnore] public bool HasSupportedEfforts { get; private set; }
    public string? DefaultEffort { get; init; }
    public bool? Mandatory { get; init; }
    public bool DefaultEnabled { get; init; }
}

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
internal sealed record OpenRouterPricing(decimal? Prompt, decimal? Completion, decimal? InputCacheRead, decimal? InputCacheWrite);

internal sealed record OpenRouterResponseUsage(decimal? Cost);
internal sealed record OpenRouterMetadata(OpenRouterResponseUsage? Usage);
internal sealed record OpenRouterReasoningRequest(bool? Enabled = null, string? Effort = null);
internal sealed record OpenRouterCacheControl(string Type, string? Ttl);
