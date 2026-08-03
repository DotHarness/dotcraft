using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace DotCraft.Sdk.DynamicTools;

/// <summary>
/// Shared JSON serialization configuration for the dynamic-tool authoring layer.
/// Argument deserialization, schema export and result packing all go through these
/// options so the wire shape (camelCase) stays consistent.
/// </summary>
/// <remarks>
/// <see cref="JsonSerializerOptions.TypeInfoResolver"/> must be set to a concrete
/// <see cref="DefaultJsonTypeInfoResolver"/>; otherwise <c>JsonSchemaExporter</c> throws
/// because the default (lazily-initialized) resolver makes the options read-only.
/// </remarks>
public static class DynamicToolJson
{
    /// <summary>The default options instance used by the schema generator and registry.</summary>
    public static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Creates a fresh options instance with the dynamic-tool defaults.</summary>
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    /// <summary>Serializes any value to a <see cref="JsonElement"/> using <see cref="Options"/>.</summary>
    public static JsonElement Wrap(object? value)
        => JsonSerializer.SerializeToElement(value, Options);
}
