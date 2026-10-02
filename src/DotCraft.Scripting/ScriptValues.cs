using System.Text.Json;
using System.Text.Json.Nodes;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;

namespace DotCraft.Scripting;

public static class ScriptValues
{
    public static JsonNode? ToJson(Engine engine, JsValue value) =>
        ToJson(engine, value, new HashSet<ObjectInstance>(ReferenceEqualityComparer.Instance));

    public static object? ToClr(JsonNode? node)
    {
        if (node == null) return null;
        var element = JsonSerializer.Deserialize<JsonElement>(node.ToJsonString());
        return ConvertElement(element);

        static object? ConvertElement(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.Array => value.EnumerateArray().Select(ConvertElement).ToArray(),
            JsonValueKind.Object => value.EnumerateObject().ToDictionary(
                property => property.Name,
                property => ConvertElement(property.Value),
                StringComparer.Ordinal),
            _ => throw new ScriptProtocolException("protocol_result_invalid", "Host result is not valid JSON.")
        };
    }

    private static JsonNode? ToJson(Engine engine, JsValue value, HashSet<ObjectInstance> ancestors)
    {
        switch (value.Type)
        {
            case Types.Null:
                return null;
            case Types.Boolean:
                return JsonValue.Create(value.AsBoolean());
            case Types.String:
                return JsonValue.Create(value.AsString());
            case Types.Number:
                var number = value.AsNumber();
                if (!double.IsFinite(number)) throw NotJson();
                return JsonValue.Create(number);
            case Types.Object:
                return ObjectToJson(engine, value.AsObject(), ancestors);
            default:
                throw NotJson();
        }
    }

    private static JsonNode ObjectToJson(Engine engine, ObjectInstance value, HashSet<ObjectInstance> ancestors)
    {
        if (!ancestors.Add(value)) throw NotJson();
        try
        {
            if (value.IsArray())
            {
                var source = value.AsArray();
                var arrayResult = new JsonArray();
                for (uint index = 0; index < source.Length; index++)
                    arrayResult.Add(ToJson(engine, source[index], ancestors));
                return arrayResult;
            }
            if (value.Prototype != null
                && !ReferenceEquals(value.Prototype, engine.Intrinsics.Object.PrototypeObject))
                throw NotJson();

            var properties = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal);
            foreach (var pair in value.GetOwnProperties())
            {
                if (!pair.Value.Enumerable) continue;
                if (!pair.Key.IsString() || !pair.Value.IsDataDescriptor()) throw NotJson();
                properties.Add(pair.Key.AsString(), ToJson(engine, pair.Value.Value, ancestors));
            }
            var objectResult = new JsonObject();
            foreach (var pair in properties) objectResult[pair.Key] = pair.Value;
            return objectResult;
        }
        finally { ancestors.Remove(value); }
    }

    private static ScriptValueException NotJson() =>
        new("result_not_serializable", "Script values must contain only JSON primitives, arrays, and plain objects.");
}

public sealed class ScriptValueException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
