using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Validates the restricted, flat JSON Schema subset allowed by MCP form elicitation.
/// </summary>
internal static partial class McpElicitationSchemaValidator
{
    private static readonly HashSet<string> RootKeys = ["type", "properties", "required"];
    private static readonly HashSet<string> CommonKeys = ["type", "title", "description", "default"];

    public static bool TryValidateSchema(object? value, out JsonObject schema)
    {
        schema = null!;
        try
        {
            schema = (value switch
            {
                JsonObject jsonObject => jsonObject,
                JsonElement element => JsonNode.Parse(element.GetRawText()) as JsonObject,
                _ when value != null => JsonSerializer.SerializeToNode(value, SessionWireJsonOptions.Default) as JsonObject,
                _ => null
            })!;
        }
        catch (JsonException)
        {
            return false;
        }

        if (schema == null || HasUnknownKeys(schema, RootKeys)
            || schema["type"]?.GetValue<string>() != "object"
            || schema["properties"] is not JsonObject properties)
        {
            return false;
        }

        var requiredNames = new HashSet<string>(StringComparer.Ordinal);
        if (schema["required"] is JsonNode requiredNode)
        {
            if (requiredNode is not JsonArray required)
                return false;
            foreach (var entry in required)
            {
                if (entry is not JsonValue item || !item.TryGetValue<string>(out var name)
                    || string.IsNullOrWhiteSpace(name) || !requiredNames.Add(name))
                {
                    return false;
                }
            }
        }

        if (requiredNames.Any(name => !properties.ContainsKey(name)))
            return false;

        return properties.All(static property =>
            !string.IsNullOrWhiteSpace(property.Key)
            && property.Value is JsonObject propertySchema
            && IsSupportedPropertySchema(propertySchema));
    }

    public static bool TryValidateContent(JsonObject schema, Dictionary<string, JsonElement>? content)
    {
        if (content == null || schema["properties"] is not JsonObject properties)
            return false;

        var required = schema["required"] is JsonArray requiredArray
            ? requiredArray.Select(static item => item!.GetValue<string>()).ToHashSet(StringComparer.Ordinal)
            : [];
        if (required.Any(name => !content.ContainsKey(name))
            || content.Keys.Any(name => !properties.ContainsKey(name)))
        {
            return false;
        }

        foreach (var (name, value) in content)
        {
            if (properties[name] is not JsonObject propertySchema || !IsValidValue(propertySchema, value))
                return false;
        }

        return true;
    }

    private static bool IsSupportedPropertySchema(JsonObject schema)
    {
        var type = schema["type"]?.GetValue<string>();
        return type switch
        {
            "string" => IsSupportedStringSchema(schema),
            "number" => IsSupportedNumberSchema(schema, integer: false),
            "integer" => IsSupportedNumberSchema(schema, integer: true),
            "boolean" => HasOnlyKeys(schema, CommonKeys) && IsDefaultKind(schema, JsonValueKind.True, JsonValueKind.False),
            "array" => IsSupportedMultiSelectSchema(schema),
            _ => false
        };
    }

    private static bool IsSupportedStringSchema(JsonObject schema)
    {
        var keys = new HashSet<string>(CommonKeys, StringComparer.Ordinal)
            { "minLength", "maxLength", "pattern", "format", "enum", "enumNames", "oneOf" };
        if (!HasOnlyKeys(schema, keys) || !IsDefaultKind(schema, JsonValueKind.String))
            return false;

        if (!TryReadNonNegativeInteger(schema, "minLength", out var minLength)
            || !TryReadNonNegativeInteger(schema, "maxLength", out var maxLength)
            || (minLength != null && maxLength != null && minLength > maxLength))
        {
            return false;
        }

        if (schema["format"] is JsonNode formatNode
            && (formatNode is not JsonValue formatValue || !formatValue.TryGetValue<string>(out var format)
                || format is not ("email" or "uri" or "date" or "date-time")))
        {
            return false;
        }

        if (schema["pattern"] is JsonNode patternNode)
        {
            if (patternNode is not JsonValue patternValue || !patternValue.TryGetValue<string>(out var pattern))
                return false;
            try { _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); }
            catch (ArgumentException) { return false; }
        }

        var hasEnum = schema.ContainsKey("enum");
        var hasOneOf = schema.ContainsKey("oneOf");
        List<string>? enumValues = null;
        if (hasEnum && hasOneOf)
            return false;
        if (hasEnum && !TryReadStringArray(schema["enum"], out enumValues))
            return false;
        if (schema["enumNames"] is JsonNode enumNamesNode
            && (!hasEnum || !TryReadStringArray(enumNamesNode, out var names) || names.Count != enumValues!.Count))
        {
            return false;
        }
        if (hasOneOf && !TryReadTitledOptions(schema["oneOf"], out _))
            return false;

        return !TryGetStringDefault(schema, out var defaultValue)
               || (!hasEnum || enumValues!.Contains(defaultValue, StringComparer.Ordinal))
               && (!hasOneOf || TryReadTitledOptions(schema["oneOf"], out var options)
                   && options.Contains(defaultValue, StringComparer.Ordinal));
    }

    private static bool IsSupportedNumberSchema(JsonObject schema, bool integer)
    {
        var keys = new HashSet<string>(CommonKeys, StringComparer.Ordinal) { "minimum", "maximum" };
        if (!HasOnlyKeys(schema, keys)
            || !TryReadNumber(schema, "minimum", out var minimum)
            || !TryReadNumber(schema, "maximum", out var maximum)
            || (minimum != null && maximum != null && minimum > maximum))
        {
            return false;
        }

        if (schema["default"] is not JsonNode defaultNode)
            return true;
        return defaultNode.GetValueKind() == JsonValueKind.Number
               && (!integer || defaultNode.AsValue().TryGetValue<long>(out _));
    }

    private static bool IsSupportedMultiSelectSchema(JsonObject schema)
    {
        var keys = new HashSet<string>(CommonKeys, StringComparer.Ordinal) { "minItems", "maxItems", "items" };
        if (!HasOnlyKeys(schema, keys)
            || !TryReadNonNegativeInteger(schema, "minItems", out var minItems)
            || !TryReadNonNegativeInteger(schema, "maxItems", out var maxItems)
            || (minItems != null && maxItems != null && minItems > maxItems)
            || schema["items"] is not JsonObject items)
        {
            return false;
        }

        List<string> options;
        if (items.Count == 2 && items["type"]?.GetValue<string>() == "string"
            && TryReadStringArray(items["enum"], out var untitled))
        {
            options = untitled;
        }
        else if (items.Count == 1 && TryReadTitledOptions(items["anyOf"], out var titled))
        {
            options = titled;
        }
        else
        {
            return false;
        }

        if (options.Count == 0 || options.Count != options.Distinct(StringComparer.Ordinal).Count())
            return false;
        if (schema["default"] is not JsonNode defaultNode)
            return true;
        return defaultNode is JsonArray defaults
               && defaults.All(item => item is JsonValue jsonValue
                   && jsonValue.TryGetValue<string>(out var itemValue)
                   && options.Contains(itemValue, StringComparer.Ordinal));
    }

    private static bool IsValidValue(JsonObject schema, JsonElement value)
    {
        switch (schema["type"]?.GetValue<string>())
        {
            case "string":
                if (value.ValueKind != JsonValueKind.String)
                    return false;
                var stringValue = value.GetString()!;
                if (!TryReadNonNegativeInteger(schema, "minLength", out var minLength)
                    || !TryReadNonNegativeInteger(schema, "maxLength", out var maxLength)
                    || minLength != null && stringValue.Length < minLength
                    || maxLength != null && stringValue.Length > maxLength)
                    return false;
                if (schema["pattern"] is JsonValue patternValue
                    && patternValue.TryGetValue<string>(out var pattern)
                    && !Regex.IsMatch(stringValue, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                    return false;
                if (!MatchesFormat(schema["format"]?.GetValue<string>(), stringValue))
                    return false;
                if (schema["enum"] is JsonNode enumNode
                    && (!TryReadStringArray(enumNode, out var values) || !values.Contains(stringValue, StringComparer.Ordinal)))
                    return false;
                return schema["oneOf"] is not JsonNode oneOf
                       || TryReadTitledOptions(oneOf, out var options) && options.Contains(stringValue, StringComparer.Ordinal);

            case "number":
            case "integer":
                if (value.ValueKind != JsonValueKind.Number
                    || schema["type"]!.GetValue<string>() == "integer" && !value.TryGetInt64(out _)
                    || !value.TryGetDouble(out var number))
                    return false;
                _ = TryReadNumber(schema, "minimum", out var minimum);
                _ = TryReadNumber(schema, "maximum", out var maximum);
                return (minimum == null || number >= minimum) && (maximum == null || number <= maximum);

            case "boolean":
                return value.ValueKind is JsonValueKind.True or JsonValueKind.False;

            case "array":
                if (value.ValueKind != JsonValueKind.Array || schema["items"] is not JsonObject items)
                    return false;
                var selected = value.EnumerateArray().ToArray();
                _ = TryReadNonNegativeInteger(schema, "minItems", out var minItems);
                _ = TryReadNonNegativeInteger(schema, "maxItems", out var maxItems);
                if (minItems != null && selected.Length < minItems || maxItems != null && selected.Length > maxItems
                    || selected.Any(item => item.ValueKind != JsonValueKind.String)
                    || selected.Select(item => item.GetString()).Distinct(StringComparer.Ordinal).Count() != selected.Length)
                    return false;
                var optionNode = items["enum"] ?? items["anyOf"];
                List<string> multiOptions;
                var parsed = items.ContainsKey("enum")
                    ? TryReadStringArray(optionNode, out multiOptions)
                    : TryReadTitledOptions(optionNode, out multiOptions);
                return parsed && selected.All(item => multiOptions.Contains(item.GetString()!, StringComparer.Ordinal));
            default:
                return false;
        }
    }

    private static bool MatchesFormat(string? format, string value) => format switch
    {
        null => true,
        "email" => EmailPattern().IsMatch(value),
        "uri" => Uri.TryCreate(value, UriKind.Absolute, out _),
        "date" => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        "date-time" => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
        _ => false
    };

    private static bool HasOnlyKeys(JsonObject value, HashSet<string> allowed) => !HasUnknownKeys(value, allowed);
    private static bool HasUnknownKeys(JsonObject value, HashSet<string> allowed) => value.Any(property => !allowed.Contains(property.Key));

    private static bool TryReadStringArray(JsonNode? node, out List<string> values)
    {
        values = [];
        if (node is not JsonArray array || array.Count == 0)
            return false;
        foreach (var item in array)
        {
            if (item is not JsonValue value || !value.TryGetValue<string>(out var text))
                return false;
            values.Add(text);
        }
        return values.Count == values.Distinct(StringComparer.Ordinal).Count();
    }

    private static bool TryReadTitledOptions(JsonNode? node, out List<string> values)
    {
        values = [];
        if (node is not JsonArray array || array.Count == 0)
            return false;
        foreach (var item in array)
        {
            if (item is not JsonObject option || option.Count != 2
                || option["const"] is not JsonValue constValue || !constValue.TryGetValue<string>(out var text)
                || option["title"] is not JsonValue titleValue || !titleValue.TryGetValue<string>(out var title)
                || string.IsNullOrWhiteSpace(title))
                return false;
            values.Add(text);
        }
        return values.Count == values.Distinct(StringComparer.Ordinal).Count();
    }

    private static bool TryReadNonNegativeInteger(JsonObject schema, string name, out int? value)
    {
        value = null;
        if (schema[name] is not JsonNode node)
            return true;
        if (node is not JsonValue jsonValue || !jsonValue.TryGetValue<int>(out var parsed) || parsed < 0)
            return false;
        value = parsed;
        return true;
    }

    private static bool TryReadNumber(JsonObject schema, string name, out double? value)
    {
        value = null;
        if (schema[name] is not JsonNode node)
            return true;
        if (node.GetValueKind() != JsonValueKind.Number || !node.AsValue().TryGetValue<double>(out var parsed) || !double.IsFinite(parsed))
            return false;
        value = parsed;
        return true;
    }

    private static bool IsDefaultKind(JsonObject schema, params JsonValueKind[] kinds) =>
        schema["default"] is not JsonNode node || kinds.Contains(node.GetValueKind());

    private static bool TryGetStringDefault(JsonObject schema, out string value)
    {
        value = string.Empty;
        return schema["default"] is JsonValue defaultValue && defaultValue.TryGetValue<string>(out value!);
    }

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
