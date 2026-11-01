using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

public sealed class ProviderRoutingListParams
{
    [JsonPropertyName("providerId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ProviderId { get; init; }
    [JsonPropertyName("backendId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BackendId { get; init; }
    [JsonPropertyName("endPoint"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> EndPoint { get; init; }
}

public sealed class ProviderRoutingListResult
{
    [JsonPropertyName("providers")] public IReadOnlyList<ProviderRoutingInfo> Providers { get; init; } = [];
}

public sealed class ProviderRoutingInfo
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("displayName")] public string DisplayName { get; init; } = string.Empty;
}
