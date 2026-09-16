using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>
/// Narrows a tool's schema to what Anthropic's input_schema accepts, recording what it dropped in
/// the description. The SDK's own <c>AnthropicClientExtensions.JsonSchemaTransformCache</c> is
/// internal and runs only where the SDK maps a declaration itself, so this reproduces it for the
/// tools DotCraft wraps as provider-native.
/// </summary>
internal static class AnthropicToolSchema
{
    private static readonly JsonSerializerOptions RelaxedJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly HashSet<string> SupportedStringFormats = new(StringComparer.Ordinal)
    {
        "date-time",
        "time",
        "date",
        "duration",
        "email",
        "hostname",
        "uri",
        "ipv4",
        "ipv6",
        "uuid"
    };

    private static readonly HashSet<string> SupportedBaseSchemaProperties = new(StringComparer.Ordinal)
    {
        "type",
        "description",
        "title",
        "$ref",
        "$defs",
        "anyOf",
        "allOf",
        "enum",
        "const"
    };

    private static readonly HashSet<string> SupportedObjectSchemaProperties = new(
        SupportedBaseSchemaProperties,
        StringComparer.Ordinal)
    {
        "properties",
        "required",
        "additionalProperties"
    };

    private static readonly HashSet<string> SupportedStringSchemaProperties = new(
        SupportedBaseSchemaProperties,
        StringComparer.Ordinal)
    {
        "format"
    };

    private static readonly HashSet<string> SupportedArraySchemaProperties = new(
        SupportedBaseSchemaProperties,
        StringComparer.Ordinal)
    {
        "items",
        "minItems"
    };

    private static readonly AIJsonSchemaTransformCache JsonSchemaTransformCache = new(
        new AIJsonSchemaTransformOptions
        {
            DisallowAdditionalProperties = true,
            TransformSchemaNode = static (_, schemaNode) => TransformSchemaNode(schemaNode)
        });

    internal static JsonElement Narrow(AIFunctionDeclaration function) =>
        JsonSchemaTransformCache.GetOrCreateTransformedSchema(function);

    private static JsonNode TransformSchemaNode(JsonNode schemaNode)
    {
        if (schemaNode is not JsonObject schemaObject)
            return schemaNode;

        if (schemaObject.TryGetPropertyValue("oneOf", out var oneOfNode) && oneOfNode is not null)
        {
            schemaObject.Remove("oneOf");
            schemaObject["anyOf"] = oneOfNode;
        }

        var type = schemaObject.TryGetPropertyValue("type", out var typeNode) && typeNode is JsonValue
            ? typeNode.GetValue<string>()
            : null;
        List<KeyValuePair<string, string>>? removed = null;

        if (type == "string"
            && schemaObject.TryGetPropertyValue("format", out var formatNode)
            && formatNode?.GetValue<string>() is { } format
            && !SupportedStringFormats.Contains(format))
        {
            var serialized = formatNode.ToJsonString(RelaxedJsonOptions);
            schemaObject.Remove("format");
            (removed ??= []).Add(new("format", serialized));
        }

        if (type == "array"
            && schemaObject.TryGetPropertyValue("minItems", out var minItemsNode)
            && minItemsNode is JsonValue minItemsJsonValue
            && minItemsJsonValue.TryGetValue(out int minItems)
            && minItems is not (0 or 1))
        {
            var serialized = minItemsNode.ToJsonString(RelaxedJsonOptions);
            schemaObject.Remove("minItems");
            (removed ??= []).Add(new("minItems", serialized));
        }

        var supported = type switch
        {
            "object" => SupportedObjectSchemaProperties,
            "string" => SupportedStringSchemaProperties,
            "array" => SupportedArraySchemaProperties,
            _ => SupportedBaseSchemaProperties
        };

        foreach (var property in schemaObject.ToArray())
        {
            if (supported.Contains(property.Key))
                continue;

            var serialized = property.Value?.ToJsonString(RelaxedJsonOptions) ?? "null";
            schemaObject.Remove(property.Key);
            (removed ??= []).Add(new(property.Key, serialized));
        }

        if (removed is { Count: > 0 })
        {
            var existing = schemaObject.TryGetPropertyValue("description", out var descriptionNode)
                ? descriptionNode?.GetValue<string>()
                : null;
            var constraintInfo = "{" + string.Join(", ", removed.Select(item => $"{item.Key}: {item.Value}")) + "}";
            schemaObject["description"] = existing is not null
                ? $"{existing}\n\n{constraintInfo}"
                : constraintInfo;
        }

        return schemaNode;
    }
}
