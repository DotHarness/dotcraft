using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

/// <summary>Sets the reading state of a bounded batch of automation runs.</summary>
[ContractModule("automations")]
public sealed class AutomationRunReadParams
{
    [JsonPropertyName("automationId")]
    public string AutomationId { get; init; } = "";
    [JsonPropertyName("runIds")]
    public List<string> RunIds { get; init; } = [];
    [JsonPropertyName("read")]
    public bool Read { get; init; }
}
