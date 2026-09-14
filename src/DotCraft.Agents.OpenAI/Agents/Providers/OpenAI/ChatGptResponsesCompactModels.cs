using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Agents;

internal sealed record ChatGptResponsesCompactRequest
{
    [JsonPropertyName("model")]
    [JsonRequired]
    public required string Model { get; init; }

    [JsonPropertyName("input")]
    [JsonRequired]
    public required List<JsonElement> Input { get; init; }

    [JsonPropertyName("instructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Instructions { get; init; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<JsonElement>? Tools { get; init; }

    [JsonPropertyName("parallel_tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ParallelToolCalls { get; init; }

    [JsonPropertyName("reasoning")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Reasoning { get; init; }

    [JsonPropertyName("service_tier")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ServiceTier { get; init; }

    [JsonPropertyName("prompt_cache_key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PromptCacheKey { get; init; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Text { get; init; }

    [JsonPropertyName("stream")]
    public bool Stream { get; init; } = true;

    [JsonPropertyName("store")]
    public bool Store { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

internal sealed record ChatGptResponsesCompactResponse
{
    public string? ResponseId { get; init; }
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    [JsonPropertyName("output")]
    [JsonRequired]
    public required List<JsonElement>? Output { get; init; }
}

internal static class ChatGptResponsesCompactJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
