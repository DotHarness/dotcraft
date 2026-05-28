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

        var type = schema["type"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(type))
        {
            message = $"{path}.type is required.";
            return false;
        }

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

                message = string.Empty;
                return true;

            case "array":
                if (schema["items"] is not JsonNode itemsNode)
                {
                    message = $"{path}.items is required for array schemas.";
                    return false;
                }

                return TryValidateSchemaNode(itemsNode, $"{path}.items", out message);

            case "string":
            case "number":
            case "integer":
            case "boolean":
                message = string.Empty;
                return true;

            default:
                message = $"{path}.type '{type}' is not supported.";
                return false;
        }
    }

    private static bool TryValidateValue(JsonObject schema, JsonNode? value, string path, out string message)
    {
        var type = schema["type"]?.GetValue<string>() ?? "object";
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
                if (value is not JsonValue jsonValue
                    || value.GetValueKind() != JsonValueKind.Number
                    || !jsonValue.TryGetValue<long>(out _))
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
}
