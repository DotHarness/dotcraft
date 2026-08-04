using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Plugins;

/// <summary>
/// Minimal JSON Schema validator for plugin function descriptors and runtime arguments.
/// </summary>
public static class PluginFunctionSchemaValidator
{
    public static bool TryValidateSchema(JsonObject schema, out string message) =>
        TryValidateSchemaNode(schema, "$", out message);

    public static bool TryValidateArguments(JsonObject schema, JsonObject arguments, out string message) =>
        TryValidateValue(schema, arguments, "$", out message);

    private static bool TryValidateSchemaNode(JsonNode? schemaNode, string path, out string message)
    {
        if (schemaNode is not JsonObject schema)
        {
            message = $"{path} must be an object.";
            return false;
        }

        if (!TryGetSchemaTypes(schema, path, requireType: true, out var types, out message))
            return false;

        foreach (var type in types)
        {
            switch (type)
            {
                case "object":
                    if (schema["properties"] is JsonNode propertiesNode
                        && propertiesNode is not JsonObject)
                    {
                        message = $"{path}.properties must be an object.";
                        return false;
                    }

                    if (schema["required"] is JsonNode requiredNode
                        && requiredNode is not JsonArray)
                    {
                        message = $"{path}.required must be an array.";
                        return false;
                    }

                    if (schema["required"] is JsonArray required)
                    {
                        var props = schema["properties"] as JsonObject;
                        foreach (var item in required)
                        {
                            var name = item?.GetValue<string>();
                            if (string.IsNullOrWhiteSpace(name))
                            {
                                message = $"{path}.required entries must be strings.";
                                return false;
                            }

                            if (props != null && !props.ContainsKey(name))
                            {
                                message = $"{path}.required references unknown property '{name}'.";
                                return false;
                            }
                        }
                    }

                    if (schema["properties"] is JsonObject nestedProperties)
                    {
                        foreach (var (propertyName, propertySchema) in nestedProperties)
                        {
                            if (!TryValidateSchemaNode(propertySchema, $"{path}.properties.{propertyName}", out message))
                                return false;
                        }
                    }

                    break;

                case "array":
                    if (schema["items"] is not JsonNode itemsNode)
                    {
                        message = $"{path}.items is required for array schemas.";
                        return false;
                    }

                    if (!TryValidateSchemaNode(itemsNode, $"{path}.items", out message))
                        return false;
                    break;

                case "string":
                case "number":
                case "integer":
                case "boolean":
                case "null":
                    break;

                default:
                    message = $"{path}.type '{type}' is not supported.";
                    return false;
            }
        }

        message = string.Empty;
        return true;
    }

    private static bool TryValidateValue(JsonObject schema, JsonNode? value, string path, out string message)
    {
        if (!TryGetSchemaTypes(schema, path, requireType: false, out var types, out message))
            return false;

        if (value is null && types.Contains("null", StringComparer.Ordinal))
        {
            message = string.Empty;
            return true;
        }

        string? firstFailure = null;
        foreach (var type in types)
        {
            if (type == "null")
                continue;

            if (TryValidateValueAsType(schema, value, path, type, out var typeMessage))
            {
                message = string.Empty;
                return true;
            }

            firstFailure ??= typeMessage;
        }

        message = types.Count == 1
            ? firstFailure ?? $"{path} does not match schema type '{types[0]}'."
            : $"{path} must match one of the schema types: {string.Join(", ", types)}.";
        return false;
    }

    private static bool TryValidateValueAsType(
        JsonObject schema,
        JsonNode? value,
        string path,
        string type,
        out string message)
    {
        switch (type)
        {
            case "object":
                if (value is not JsonObject objValue)
                {
                    message = $"{path} must be an object.";
                    return false;
                }

                var properties = schema["properties"] as JsonObject;
                if (schema["required"] is JsonArray required)
                {
                    foreach (var item in required)
                    {
                        var name = item?.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(name) && !objValue.ContainsKey(name))
                        {
                            message = $"{path}.{name} is required.";
                            return false;
                        }
                    }
                }

                if (properties != null)
                {
                    foreach (var (propertyName, propertyValue) in objValue)
                    {
                        if (!properties.TryGetPropertyValue(propertyName, out var propertySchema))
                        {
                            message = $"{path}.{propertyName} is not declared by the function schema.";
                            return false;
                        }

                        if (propertySchema is not JsonObject propertySchemaObject)
                        {
                            message = $"{path}.{propertyName} schema is invalid.";
                            return false;
                        }

                        if (!TryValidateValue(propertySchemaObject, propertyValue, $"{path}.{propertyName}", out message))
                            return false;
                    }
                }

                message = string.Empty;
                return true;

            case "array":
                if (value is not JsonArray arrayValue)
                {
                    message = $"{path} must be an array.";
                    return false;
                }

                if (schema["items"] is not JsonObject itemSchema)
                {
                    message = $"{path} array schema is missing items.";
                    return false;
                }

                for (int i = 0; i < arrayValue.Count; i++)
                {
                    if (!TryValidateValue(itemSchema, arrayValue[i], $"{path}[{i}]", out message))
                        return false;
                }

                message = string.Empty;
                return true;

            case "string":
                if (value == null || value.GetValueKind() != JsonValueKind.String)
                {
                    message = $"{path} must be a string.";
                    return false;
                }

                message = string.Empty;
                return true;

            case "number":
                if (value == null || value.GetValueKind() is not JsonValueKind.Number)
                {
                    message = $"{path} must be a number.";
                    return false;
                }

                message = string.Empty;
                return true;

            case "integer":
                if (!IsJsonInteger(value))
                {
                    message = $"{path} must be an integer.";
                    return false;
                }

                message = string.Empty;
                return true;

            case "boolean":
                if (value == null || value.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
                {
                    message = $"{path} must be a boolean.";
                    return false;
                }

                message = string.Empty;
                return true;

            default:
                message = $"{path} uses unsupported schema type '{type}'.";
                return false;
        }
    }

    private static bool TryGetSchemaTypes(
        JsonObject schema,
        string path,
        bool requireType,
        out IReadOnlyList<string> types,
        out string message)
    {
        var typeNode = schema["type"];
        if (typeNode is null)
        {
            if (requireType)
            {
                types = [];
                message = $"{path}.type is required.";
                return false;
            }

            types = ["object"];
            message = string.Empty;
            return true;
        }

        if (typeNode is JsonValue value
            && value.TryGetValue<string>(out var singleType)
            && !string.IsNullOrWhiteSpace(singleType))
        {
            types = [singleType];
            message = string.Empty;
            return true;
        }

        if (typeNode is not JsonArray array || array.Count == 0)
        {
            types = [];
            message = $"{path}.type must be a string or a non-empty array of strings.";
            return false;
        }

        var parsedTypes = new List<string>(array.Count);
        foreach (var item in array)
        {
            if (item is not JsonValue itemValue
                || !itemValue.TryGetValue<string>(out var itemType)
                || string.IsNullOrWhiteSpace(itemType))
            {
                types = [];
                message = $"{path}.type entries must be non-empty strings.";
                return false;
            }

            if (!parsedTypes.Contains(itemType, StringComparer.Ordinal))
                parsedTypes.Add(itemType);
        }

        types = parsedTypes;
        message = string.Empty;
        return true;
    }

    private static bool IsJsonInteger(JsonNode? value)
    {
        if (value is null || value.GetValueKind() != JsonValueKind.Number)
            return false;

        // JsonValue may retain the CLR numeric type supplied by an in-process host
        // (for example Int32) or a JsonElement supplied by a wire parser. Reparse its
        // JSON token so validation follows JSON Schema numeric semantics instead of
        // depending on that incidental backing type.
        using var document = JsonDocument.Parse(value.ToJsonString());
        var number = document.RootElement;
        return number.TryGetInt64(out _)
               || number.TryGetUInt64(out _)
               || (number.TryGetDecimal(out var decimalValue)
                   && decimalValue == decimal.Truncate(decimalValue));
    }
}
