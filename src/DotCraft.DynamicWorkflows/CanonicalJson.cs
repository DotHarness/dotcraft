using System.Text.Json.Nodes;

namespace DotCraft.DynamicWorkflows;

internal static class CanonicalJson
{
    public static JsonNode? Normalize(JsonNode? node)
    {
        if (node == null) return null;
        return node switch
        {
            JsonObject value => NormalizeObject(value),
            JsonArray value => new JsonArray(value.Select(Normalize).ToArray()),
            JsonValue value => NormalizeValue(value),
            _ => throw new DynamicWorkflowValidationException("result_not_serializable", "Workflow value is not JSON serializable.")
        };
    }

    private static JsonObject NormalizeObject(JsonObject value)
    {
        var result = new JsonObject();
        foreach (var pair in value.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            result[pair.Key] = Normalize(pair.Value);
        return result;
    }

    private static JsonNode NormalizeValue(JsonValue value)
    {
        if (value.TryGetValue<double>(out var number) && !double.IsFinite(number))
            throw new DynamicWorkflowValidationException("result_not_serializable", "Workflow numbers must be finite.");
        return value.DeepClone();
    }
}
