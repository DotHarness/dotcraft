using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

public sealed partial class ProviderInfo
{
    [JsonPropertyName("managedBy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ManagedBy { get; init; }

    [JsonPropertyName("isAuthenticated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IsAuthenticated { get; init; }
}
