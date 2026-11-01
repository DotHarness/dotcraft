using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

public sealed partial class ModelCatalogItem
{
    [JsonPropertyName("supportedProtocols"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> SupportedProtocols { get; init; }
    [JsonPropertyName("defaultProtocol"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DefaultProtocol { get; init; }
    [JsonPropertyName("maxOutputTokens"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> MaxOutputTokens { get; init; }
    [JsonPropertyName("inputModalities"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> InputModalities { get; init; }
    [JsonPropertyName("pricing"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ModelPricingInfo> Pricing { get; init; }
}

public sealed class ModelPricingInfo
{
    [JsonPropertyName("inputPerMillion"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public decimal? InputPerMillion { get; init; }
    [JsonPropertyName("outputPerMillion"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public decimal? OutputPerMillion { get; init; }
    [JsonPropertyName("cacheReadPerMillion"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public decimal? CacheReadPerMillion { get; init; }
    [JsonPropertyName("cacheWritePerMillion"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public decimal? CacheWritePerMillion { get; init; }
}

public sealed partial class ModelListResult
{
    [JsonPropertyName("source"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Source { get; init; }
    [JsonPropertyName("refreshedAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> RefreshedAt { get; init; }
    [JsonPropertyName("refreshNotice"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> RefreshNotice { get; init; }
}

public sealed partial class ModelListParams
{
    [JsonPropertyName("refresh"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Refresh { get; init; }
}

public sealed partial class ModelReasoningCapability
{
    [JsonPropertyName("defaultEnabled"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> DefaultEnabled { get; init; }
}

public sealed partial class ProviderInfo
{
    [JsonPropertyName("backendSettings"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement> BackendSettings { get; init; }
}

public sealed partial class ProviderTestParams
{
    [JsonPropertyName("backendId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BackendId { get; init; }
    [JsonPropertyName("backendSettings"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement> BackendSettings { get; init; }
}
