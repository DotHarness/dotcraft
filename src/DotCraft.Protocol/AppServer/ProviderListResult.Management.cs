using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

public sealed partial class ProviderListResult
{
    [JsonPropertyName("managedBy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ManagedBy { get; init; }
}
