using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Tools;

internal static class RpcToolSchemaPostProcessor
{
    internal const string TargetParameterName = "target";

    internal static ToolDefinition Process(ToolDefinition definition, string? targetDescription = null)
    {
        var schema = JsonNode.Parse(definition.InputSchema.GetRawText())!.AsObject();
        if (schema["type"] is { } schemaType
            && (schemaType is not JsonValue type || !type.TryGetValue<string>(out var typeName) || typeName != "object"))
        {
            throw new InvalidOperationException($"RPC tool '{definition.Id}' requires an object input schema.");
        }

        var properties = schema["properties"]?.AsObject() ?? new JsonObject();
        if (properties.ContainsKey(TargetParameterName))
        {
            throw new InvalidOperationException(
                $"RPC tool '{definition.Id}' declares reserved parameter '{TargetParameterName}'.");
        }

        schema["properties"] ??= properties;
        properties[TargetParameterName] = new JsonObject
        {
            ["type"] = "string",
            ["enum"] = new JsonArray("local", "remote"),
            ["description"] = targetDescription
                ?? "Choose the execution location; omission uses the connected remote workspace, or local when disconnected."
        };
        return new ToolDefinition(definition.Id, definition.Name, definition.Description,
            JsonSerializer.SerializeToElement(schema), definition.OutputSchema, definition.Annotations,
            definition.PolicyHints, definition.Presentation, definition.Provenance,
            definition.NamespaceDescription, definition.PolicyScope);
    }
}
