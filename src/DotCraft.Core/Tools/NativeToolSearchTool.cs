using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

internal sealed class NativeToolSearchTool(
    DeferredToolActivationIndex registry,
    int maxSearchResults = 5,
    DeferredToolLoadingTraceContext? traceContext = null) : AIFunction
{
    public const string ToolName = "tool_search";

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["query"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Search keywords for deferred tools."
            },
            ["max_results"] = new JsonObject
            {
                ["type"] = "integer",
                ["description"] = "Maximum number of matching tools to return."
            }
        },
        ["required"] = new JsonArray("query"),
        ["additionalProperties"] = false
    });

    public override string Name => ToolName;

    public override string Description =>
        "Search for deferred local tools and return matching tool definitions.";

    public override JsonElement JsonSchema => InputSchema;

    public override JsonElement? ReturnJsonSchema => null;

    public override MethodInfo? UnderlyingMethod => null;

    public override JsonSerializerOptions JsonSerializerOptions => JsonSerializerOptions.Default;

    protected override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var query = ReadString(arguments, "query") ?? ReadString(arguments, "q") ?? string.Empty;
        var maxResults = ReadInt(arguments, "max_results") ?? ReadInt(arguments, "maxResults") ?? maxSearchResults;
        var effectiveMaxResults = Math.Min(maxResults, maxSearchResults);
        var activatedBefore = traceContext == null
            ? null
            : registry.GetActivatedToolNames().ToHashSet(StringComparer.Ordinal);
        var results = registry.SearchAndActivate(query, effectiveMaxResults);
        var entries = results
            .Select(result => registry.Entries.TryGetValue(result.Name, out var entry) ? entry : null)
            .Where(static entry => entry != null)
            .Select(static entry => entry!)
            .ToArray();

        DeferredToolLoadingTraceRecorder.RecordNewActivations(
            traceContext,
            query,
            maxResults,
            entries,
            activatedBefore,
            DeferredToolLoadingTraceRecorder.OpenAIResponsesToolSearchOutputWireShape);

        return ValueTask.FromResult<object?>(new NativeToolSearchOutput(ToOutputTools(entries)));
    }

    private static string? ReadString(AIFunctionArguments arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var value) || value == null)
            return null;

        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => value.ToString()
        };
    }

    private static int? ReadInt(AIFunctionArguments arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var value) || value == null)
            return null;

        return value switch
        {
            int number => number,
            long number => checked((int)number),
            JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var number) => number,
            string text when int.TryParse(text, out var number) => number,
            _ => null
        };
    }

    internal static NativeToolSearchOutputTool ToOutputTool(AITool tool) =>
        new(
            "function",
            tool.Name,
            tool.Description ?? string.Empty,
            GetJsonSchema(tool).ValueKind == JsonValueKind.Undefined
                ? JsonSerializer.SerializeToElement(new JsonObject { ["type"] = "object" })
                : GetJsonSchema(tool),
            Strict: false,
            DeferLoading: true);

    private static NativeToolSearchOutputTool ToOutputTool(AITool tool, string localName) =>
        new(
            "function",
            localName,
            tool.Description ?? string.Empty,
            GetJsonSchema(tool).ValueKind == JsonValueKind.Undefined
                ? JsonSerializer.SerializeToElement(new JsonObject { ["type"] = "object" })
                : GetJsonSchema(tool),
            Strict: false,
            DeferLoading: true);

    internal static string FormatOutputForDisplay(NativeToolSearchOutput output)
    {
        var tools = FlattenOutputTools(output.Tools).ToArray();
        if (tools.Length == 0)
            return "No matching tools found.";

        var sb = new StringBuilder();
        sb.Append("Found ");
        sb.Append(tools.Length);
        sb.AppendLine(" matching tool(s):");

        foreach (var tool in tools)
        {
            sb.Append("- ");
            sb.Append(tool.Name);

            if (!string.IsNullOrWhiteSpace(tool.Description))
            {
                sb.Append(": ");
                sb.Append(tool.Description.Trim());
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    internal static NativeToolSearchOutputTool[] ToOutputTools(IReadOnlyList<DeferredToolEntry> entries)
    {
        var output = new List<NativeToolSearchOutputTool>(entries.Count);
        var namespaceOrder = new List<string>();
        var namespaceTools = new Dictionary<string, List<NativeToolSearchOutputTool>>(StringComparer.Ordinal);
        var namespaceDescriptions = new Dictionary<string, List<string?>>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Namespace))
            {
                output.Add(ToOutputTool(entry.Tool));
                continue;
            }

            var namespaceName = entry.Namespace.Trim();
            if (!namespaceTools.TryGetValue(namespaceName, out var tools))
            {
                tools = [];
                namespaceTools[namespaceName] = tools;
                namespaceDescriptions[namespaceName] = [];
                namespaceOrder.Add(namespaceName);
            }

            if (!CanonicalToolIdentityMetadataResolver.TryGet(entry.Tool, out var canonicalName, out _)
                || !string.Equals(canonicalName.Namespace, namespaceName, StringComparison.Ordinal))
            {
                continue;
            }

            tools.Add(ToOutputTool(
                entry.Tool,
                canonicalName.Name));
            namespaceDescriptions[namespaceName].Add(entry.NamespaceDescription);
        }

        foreach (var namespaceName in namespaceOrder)
        {
            var tools = namespaceTools[namespaceName];
            if (tools.Count == 0)
                continue;

            output.Add(CreateNamespaceTool(
                namespaceName,
                ToolNamespaceDescriptionResolver.Resolve(
                    namespaceName,
                    namespaceDescriptions[namespaceName],
                    out _),
                tools.ToArray()));
        }

        return output.ToArray();
    }

    private static NativeToolSearchOutputTool CreateNamespaceTool(
        string namespaceName,
        string description,
        NativeToolSearchOutputTool[] tools) =>
        new(
            "namespace",
            namespaceName,
            description,
            Tools: tools);

    private static IEnumerable<NativeToolSearchDisplayTool> FlattenOutputTools(
        IEnumerable<NativeToolSearchOutputTool> tools,
        string? namespaceName = null)
    {
        foreach (var tool in tools)
        {
            var type = tool.Type.Trim();
            var name = tool.Name.Trim();

            if (string.Equals(type, "namespace", StringComparison.OrdinalIgnoreCase) && tool.Tools is { Length: > 0 })
            {
                var childNamespace = string.IsNullOrWhiteSpace(name) ? namespaceName : name;
                foreach (var child in FlattenOutputTools(tool.Tools, childNamespace))
                    yield return child;
                continue;
            }

            if (string.IsNullOrWhiteSpace(name))
                continue;

            var displayName = string.IsNullOrWhiteSpace(namespaceName)
                ? name
                : $"{namespaceName}.{name}";
            yield return new NativeToolSearchDisplayTool(displayName, tool.Description);
        }
    }

    private static JsonElement GetJsonSchema(AITool tool) =>
        tool is AIFunction function
            ? function.JsonSchema
            : JsonSerializer.SerializeToElement(new JsonObject { ["type"] = "object" });
}

internal sealed record NativeToolSearchOutput(
    [property: JsonPropertyName("tools")] NativeToolSearchOutputTool[] Tools);

internal sealed record DeferredToolLoadingTraceContext(
    TraceCollector Collector,
    string Strategy,
    string EffectiveMode,
    string ProviderProtocol,
    string Trigger,
    int DeferredToolCount,
    int MaxSearchResults);

internal sealed record NativeToolSearchDisplayTool(string Name, string Description);

internal sealed record NativeToolSearchOutputTool(
    [property: JsonPropertyName("type")]
    string Type,
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("description")]
    string Description,
    [property: JsonPropertyName("parameters")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    JsonElement? Parameters = null,
    [property: JsonPropertyName("strict")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? Strict = null,
    [property: JsonPropertyName("defer_loading")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? DeferLoading = null,
    [property: JsonPropertyName("tools")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    NativeToolSearchOutputTool[]? Tools = null);
