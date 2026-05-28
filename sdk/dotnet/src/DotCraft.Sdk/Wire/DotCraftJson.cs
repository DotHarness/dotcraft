using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Sdk.Wire;

/// <summary>
/// Canonical JSON options for DotCraft wire payloads.
/// </summary>
public static class DotCraftJson
{
    /// <summary>
    /// Serializes camelCase JSON and omits null values to match AppServer and Hub wire contracts.
    /// </summary>
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
