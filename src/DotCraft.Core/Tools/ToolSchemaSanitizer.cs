using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Normalizes tool JSON schemas for providers with narrower schema support.
/// </summary>
internal static class ToolSchemaSanitizer
{
    public static List<AITool> SanitizeTools(IEnumerable<AITool> tools) =>
        [.. tools.Select(SanitizeTool)];

    public static AITool SanitizeTool(AITool tool) =>
        tool switch
        {
            ToolSchemaSanitizingFunction => tool,
            AIFunction function when function is IDeferredToolSearchMarker marker =>
                new DeferredToolSearchSchemaSanitizingFunction(function, marker.Registry),
            AIFunction function => new ToolSchemaSanitizingFunction(function),
            _ => tool
        };

    public static JsonElement SanitizeJsonSchema(JsonElement schema)
    {
        var node = JsonNode.Parse(schema.GetRawText()) ?? new JsonObject();
        SanitizeNode(node);
        return JsonSerializer.SerializeToElement(node);
    }

    private static void SanitizeNode(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            // Some LLM providers do not support nullable primitive parameters, so the application filters parameter schemas before sending them.
            if (TryNormalizeNullablePrimitiveType(obj) &&
                obj.TryGetPropertyValue("default", out var defaultNode) &&
                defaultNode is null)
            {
                obj.Remove("default");
            }

            foreach (var property in obj.ToArray())
                SanitizeNode(property.Value);
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array.ToArray())
                SanitizeNode(item);
        }
    }

    private static bool TryNormalizeNullablePrimitiveType(JsonObject obj)
    {
        if (!obj.TryGetPropertyValue("type", out var typeNode) || typeNode is not JsonArray typeArray)
            return false;

        var values = typeArray
            .Select(item => item?.GetValue<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        if (values.Length != 2 ||
            !values.Contains("null", StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var nonNullType = values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) &&
            !string.Equals(value, "null", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(nonNullType))
            return false;

        if (!IsPrimitiveSchemaType(nonNullType))
            return false;

        obj["type"] = nonNullType;
        return true;
    }

    private static bool IsPrimitiveSchemaType(string type) =>
        type.Equals("string", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("integer", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("number", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("boolean", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Wraps an <see cref="AIFunction"/> while exposing a provider-compatible input schema.
/// </summary>
internal class ToolSchemaSanitizingFunction(AIFunction innerFunction)
    : DelegatingAIFunction(innerFunction),
        IDeferredToolMetadata,
        IGeneratedToolMetadata,
        IToolNamespaceMetadata,
        ICanonicalToolIdentityMetadata,
        IOpenAIResponsesFunctionToolMetadata
{
    private readonly JsonElement _jsonSchema = ToolSchemaSanitizer.SanitizeJsonSchema(innerFunction.JsonSchema);

    public override JsonElement JsonSchema => _jsonSchema;

    public bool DeferLoading =>
        DeferredToolMetadataResolver.TryGet(InnerFunction, out var metadata) && metadata.DeferLoading;

    public string? DeferredToolSource =>
        DeferredToolMetadataResolver.TryGet(InnerFunction, out var metadata) ? metadata.Source : null;

    public string? DeferredToolNamespace =>
        DeferredToolMetadataResolver.TryGet(InnerFunction, out var metadata) ? metadata.Namespace : null;

    public string? ToolNamespace =>
        ToolNamespaceMetadataResolver.TryGet(InnerFunction, out var toolNamespace) ? toolNamespace : null;

    public string? ToolNamespaceDescription => ToolNamespaceMetadataResolver.GetDescription(InnerFunction);

    public ToolName CanonicalToolName =>
        CanonicalToolIdentityMetadataResolver.TryGet(InnerFunction, out var toolName, out _)
            ? toolName
            : new ToolName(
                ToolNamespaceMetadataResolver.TryGet(InnerFunction, out var toolNamespace)
                    ? toolNamespace
                    : null,
                InnerFunction.Name);

    public string ProviderFlatName =>
        CanonicalToolIdentityMetadataResolver.TryGet(InnerFunction, out _, out var providerFlatName)
            ? providerFlatName
            : ProviderToolProjector.Project([CanonicalToolName])[CanonicalToolName];

    public bool? Strict =>
        InnerFunction is IOpenAIResponsesFunctionToolMetadata metadata ? metadata.Strict : null;

    public bool StreamArgumentsEnabled =>
        !GeneratedToolMetadataResolver.TryGet(InnerFunction, out var metadata) || metadata.StreamArgumentsEnabled;

    public int? MaxResultChars =>
        GeneratedToolMetadataResolver.TryGet(InnerFunction, out var metadata) ? metadata.MaxResultChars : null;

    public string? Icon =>
        GeneratedToolMetadataResolver.TryGet(InnerFunction, out var metadata) ? metadata.Icon : null;

    public Func<IDictionary<string, object?>?, string>? DisplayFormatter =>
        GeneratedToolMetadataResolver.TryGet(InnerFunction, out var metadata) ? metadata.DisplayFormatter : null;

    public bool RpcEligible =>
        GeneratedToolMetadataResolver.TryGet(InnerFunction, out var metadata) && metadata.RpcEligible;
}

internal sealed class DeferredToolSearchSchemaSanitizingFunction(
    AIFunction innerFunction,
    IDeferredToolActivationView registry)
    : ToolSchemaSanitizingFunction(innerFunction), IDeferredToolSearchMarker
{
    public IDeferredToolActivationView Registry { get; } = registry;
}
