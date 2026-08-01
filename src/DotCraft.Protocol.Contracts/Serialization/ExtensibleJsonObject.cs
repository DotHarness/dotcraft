using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Protocol.Contracts;

/// <summary>Base for contract objects that retain unknown JSON properties during projection.</summary>
public abstract class ExtensibleJsonObject
{
    /// <summary>Unknown properties preserved for forward-compatible wire round trips.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; set; }
}
