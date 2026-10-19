using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

public sealed partial class ProviderInfo
{
    [JsonPropertyName("webSearchBackend")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> WebSearchBackend { get; init; }

    [JsonPropertyName("managedBy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ManagedBy { get; init; }

    [JsonPropertyName("isAuthenticated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IsAuthenticated { get; init; }
}
