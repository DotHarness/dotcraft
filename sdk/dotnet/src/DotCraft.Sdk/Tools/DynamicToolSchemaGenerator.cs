using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace DotCraft.Sdk.DynamicTools;

/// <summary>
/// Generates a dynamic tool's <c>inputSchema</c> with net's <see cref="JsonSchemaExporter"/>.
/// A <c>TransformSchemaNode</c> maps <see cref="DescriptionAttribute"/> / <c>Schema*</c>
/// constraint attributes to JSON Schema keywords, collapses nullable union types and adds
/// <c>additionalProperties:false</c>, so the output matches a hand-written schema.
/// </summary>
/// <remarks>
/// Two entry points cover both authoring conventions:
/// <see cref="Generate"/> for a single typed-arguments record, and
/// <see cref="GenerateFromParameters"/> for flat method parameters.
/// </remarks>
public static class DynamicToolSchemaGenerator
{
    /// <summary>Generates an object schema from a typed-arguments record/POCO type.</summary>
    public static JsonElement Generate(Type argsType, JsonSerializerOptions options)
    {
        JsonNode node = GenerateNode(argsType, options);
        return JsonSerializer.SerializeToElement(node, options);
    }

    /// <summary>
    /// Generates an object schema from a set of flat method parameters.
    /// Each parameter becomes a property (camelCase); parameters without a default value and
    /// not nullable become <c>required</c>; the object is closed (<c>additionalProperties:false</c>).
    /// </summary>
    public static JsonElement GenerateFromParameters(IReadOnlyList<ParameterInfo> parameters, JsonSerializerOptions options)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        var nullability = new NullabilityInfoContext();

        foreach (ParameterInfo parameter in parameters)
        {
            string name = ToCamelCase(parameter.Name ?? "arg");

            JsonNode propertySchema = GenerateNode(parameter.ParameterType, options);
            if (propertySchema is JsonObject propertyObj)
            {
                ApplyParameterRefinements(propertyObj, parameter);
                propertySchema = propertyObj;
            }

            properties[name] = propertySchema;

            if (!IsOptionalParameter(parameter, nullability))
            {
                required.Add(name);
            }
        }

        var root = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
        };
        if (required.Count > 0)
        {
            root["required"] = required;
        }

        root["additionalProperties"] = false;
        return JsonSerializer.SerializeToElement(root, options);
    }

    private static JsonNode GenerateNode(Type type, JsonSerializerOptions options)
    {
        var exporterOptions = new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = Transform,
        };

        JsonNode node = JsonSchemaExporter.GetJsonSchemaAsNode(options, type, exporterOptions);
        return InlineReferences(node);
    }

    /// <summary>
    /// Inlines every <c>$ref</c> (JSON Pointer to the first occurrence) emitted by
    /// JsonSchemaExporter, yielding a fully inlined structure. Assumes the type graph is acyclic
    /// (true for argument records in this tool layer).
    /// </summary>
    private static JsonNode InlineReferences(JsonNode root)
    {
        if (root is not JsonObject rootObj)
        {
            return root;
        }

        rootObj.Remove("$defs");
        return Inline(rootObj, rootObj);
    }

    private static JsonNode Inline(JsonNode node, JsonObject root)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                if (obj["$ref"] is JsonValue refValue && refValue.TryGetValue(out string? refPath) && refPath is not null)
                {
                    JsonNode target = ResolvePointer(root, refPath);
                    var inlined = (JsonObject)Inline(target.DeepClone(), root);
                    foreach ((string key, JsonNode? value) in obj)
                    {
                        if (key == "$ref")
                        {
                            continue;
                        }

                        inlined[key] = value?.DeepClone();
                    }

                    return inlined;
                }

                var result = new JsonObject();
                foreach ((string key, JsonNode? value) in obj)
                {
                    result[key] = value is null ? null : Inline(value.DeepClone(), root);
                }

                return result;
            }
            case JsonArray array:
            {
                var result = new JsonArray();
                foreach (JsonNode? item in array)
                {
                    result.Add(item is null ? null : Inline(item.DeepClone(), root));
                }

                return result;
            }
            default:
                return node;
        }
    }

    private static JsonNode ResolvePointer(JsonObject root, string pointer)
    {
        string path = pointer.StartsWith('#') ? pointer[1..] : pointer;
        JsonNode current = root;
        foreach (string rawSegment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            string segment = rawSegment.Replace("~1", "/").Replace("~0", "~");
            current = current switch
            {
                JsonObject obj => obj[segment]
                    ?? throw new InvalidOperationException($"Unresolved $ref segment '{segment}' in '{pointer}'."),
                JsonArray array when int.TryParse(segment, out int index) => array[index]
                    ?? throw new InvalidOperationException($"Unresolved $ref index '{segment}' in '{pointer}'."),
                _ => throw new InvalidOperationException($"Cannot navigate $ref segment '{segment}' in '{pointer}'.")
            };
        }

        return current;
    }

    private static JsonNode Transform(JsonSchemaExporterContext context, JsonNode schema)
    {
        if (schema is not JsonObject obj)
        {
            return schema;
        }

        ICustomAttributeProvider? attributes = context.PropertyInfo?.AttributeProvider;

        // 1) const-true confirmation flag: overwrite with { type:boolean, enum:[true] }.
        if (attributes?.IsDefined(typeof(SchemaConstTrueAttribute), inherit: false) == true)
        {
            obj.Clear();
            obj["type"] = "boolean";
            obj["enum"] = new JsonArray(true);
            ApplyDescription(obj, attributes);
            return obj;
        }

        // 2) Collapse nullable union ["T","null"] -> "T", normalize numeric unions, strip null from enum.
        CollapseNullableType(obj);
        NormalizeNumericType(obj);
        RemoveNullFromEnum(obj);

        // 3) Description.
        ApplyDescription(obj, attributes);

        // 4) Numeric / string / array constraints.
        ApplyConstraintAttributes(obj, attributes);

        // 5) enum nodes get an explicit type (JsonSchemaExporter omits type for string enums).
        AddEnumType(obj);

        // 6) object nodes get additionalProperties (dictionaries keep their own).
        if (IsObjectSchema(obj) && !obj.ContainsKey("additionalProperties"))
        {
            bool allowAdditional = context.TypeInfo?.Type is { } type &&
                type.IsDefined(typeof(SchemaAllowAdditionalPropertiesAttribute), inherit: false);
            obj["additionalProperties"] = allowAdditional;

            // Closed objects (additionalProperties:false) with no properties get an empty
            // properties object, matching hand-written empty schemas.
            if (!allowAdditional && !obj.ContainsKey("properties"))
            {
                obj["properties"] = new JsonObject();
            }
        }

        return obj;
    }

    /// <summary>
    /// Applies parameter-level refinements (const-true, nullable/numeric collapse, description,
    /// constraints, enum type) to the root schema of a flat parameter, mirroring <see cref="Transform"/>.
    /// </summary>
    private static void ApplyParameterRefinements(JsonObject obj, ParameterInfo parameter)
    {
        if (parameter.IsDefined(typeof(SchemaConstTrueAttribute), inherit: false))
        {
            obj.Clear();
            obj["type"] = "boolean";
            obj["enum"] = new JsonArray(true);
            ApplyDescription(obj, parameter);
            return;
        }

        CollapseNullableType(obj);
        NormalizeNumericType(obj);
        RemoveNullFromEnum(obj);
        ApplyDescription(obj, parameter);
        ApplyConstraintAttributes(obj, parameter);
        AddEnumType(obj);
    }

    private static void ApplyConstraintAttributes(JsonObject obj, ICustomAttributeProvider? attributes)
    {
        if (attributes is null)
        {
            return;
        }

        if (GetAttribute<SchemaMinimumAttribute>(attributes) is { } min)
        {
            obj["minimum"] = JsonValue.Create(min.Value);
        }

        if (GetAttribute<SchemaMaximumAttribute>(attributes) is { } max)
        {
            obj["maximum"] = JsonValue.Create(max.Value);
        }

        if (GetAttribute<SchemaPatternAttribute>(attributes) is { } pattern)
        {
            obj["pattern"] = pattern.Pattern;
        }

        if (GetAttribute<SchemaMaxItemsAttribute>(attributes) is { } maxItems)
        {
            obj["maxItems"] = maxItems.Value;
        }

        if (GetAttribute<SchemaMinItemsAttribute>(attributes) is { } minItems)
        {
            obj["minItems"] = minItems.Value;
        }
    }

    private static void AddEnumType(JsonObject obj)
    {
        if (!obj.ContainsKey("type") && obj["enum"] is JsonArray enumValues && enumValues.Count > 0)
        {
            if (enumValues[0] is JsonValue first && first.TryGetValue(out string? _))
            {
                obj["type"] = "string";
            }
        }
    }

    private static void ApplyDescription(JsonObject obj, ICustomAttributeProvider? attributes)
    {
        if (attributes is null)
        {
            return;
        }

        if (GetAttribute<DescriptionAttribute>(attributes) is { } description &&
            !string.IsNullOrEmpty(description.Description) &&
            !obj.ContainsKey("description"))
        {
            obj["description"] = description.Description;
        }
    }

    private static void CollapseNullableType(JsonObject obj)
    {
        if (obj["type"] is not JsonArray typeArray)
        {
            return;
        }

        var nonNull = new List<JsonNode?>();
        foreach (JsonNode? entry in typeArray)
        {
            if (entry is JsonValue value && value.TryGetValue(out string? text) && text == "null")
            {
                continue;
            }

            nonNull.Add(entry?.DeepClone());
        }

        if (nonNull.Count == 1 && nonNull[0] is JsonValue single && single.TryGetValue(out string? singleType))
        {
            obj["type"] = singleType;
        }
        else
        {
            obj["type"] = new JsonArray(nonNull.ToArray());
        }
    }

    private static void RemoveNullFromEnum(JsonObject obj)
    {
        if (obj["enum"] is not JsonArray enumArray)
        {
            return;
        }

        List<JsonNode?> kept = enumArray.Where(n => n is not null).Select(n => (JsonNode?)n!.DeepClone()).ToList();
        if (kept.Count == enumArray.Count)
        {
            return;
        }

        obj["enum"] = new JsonArray(kept.ToArray());
    }

    private static void NormalizeNumericType(JsonObject obj)
    {
        if (obj["type"] is not JsonArray typeArray)
        {
            return;
        }

        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonNode? entry in typeArray)
        {
            if (entry is JsonValue value && value.TryGetValue(out string? text) && text is not null)
            {
                types.Add(text);
            }
        }

        // STJ renders double/int as ["string","number"]/["string","integer"] + numeric pattern;
        // normalize to a pure numeric type.
        if (types.SetEquals(new[] { "string", "number" }))
        {
            obj["type"] = "number";
            obj.Remove("pattern");
        }
        else if (types.SetEquals(new[] { "string", "integer" }))
        {
            obj["type"] = "integer";
            obj.Remove("pattern");
        }
    }

    private static bool IsObjectSchema(JsonObject obj)
    {
        if (obj["type"] is JsonValue typeValue && typeValue.TryGetValue(out string? type))
        {
            return type == "object";
        }

        return obj.ContainsKey("properties");
    }

    private static bool IsOptionalParameter(ParameterInfo parameter, NullabilityInfoContext nullability)
    {
        if (parameter.HasDefaultValue)
        {
            return true;
        }

        if (Nullable.GetUnderlyingType(parameter.ParameterType) is not null)
        {
            return true;
        }

        if (!parameter.ParameterType.IsValueType)
        {
            NullabilityInfo info = nullability.Create(parameter);
            return info.WriteState == NullabilityState.Nullable;
        }

        return false;
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0]))
        {
            return name;
        }

        Span<char> buffer = stackalloc char[name.Length];
        name.CopyTo(buffer);
        buffer[0] = char.ToLowerInvariant(buffer[0]);
        return new string(buffer);
    }

    private static TAttribute? GetAttribute<TAttribute>(ICustomAttributeProvider attributes)
        where TAttribute : Attribute
        => attributes.GetCustomAttributes(typeof(TAttribute), inherit: false) is { Length: > 0 } found
            ? (TAttribute)found[0]
            : null;
}
