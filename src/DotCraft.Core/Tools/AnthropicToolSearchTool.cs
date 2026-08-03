using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;

namespace DotCraft.Tools;

internal sealed class AnthropicToolSearchTool(
    DeferredToolActivationIndex registry,
    int maxSearchResults = 5,
    DeferredToolLoadingTraceContext? traceContext = null) : AIFunction
{
    public const string ToolName = NativeToolSearchTool.ToolName;

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["query"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Search keywords for deferred tools, or select exact tools with select:ToolName,OtherTool."
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
        "Search for deferred local tools and return Anthropic tool references for matching tool definitions.";

    public override JsonElement JsonSchema => InputSchema;

    public override JsonElement? ReturnJsonSchema => null;

    public override MethodInfo? UnderlyingMethod => null;

    public override JsonSerializerOptions JsonSerializerOptions => JsonSerializerOptions.Default;

    internal DeferredToolActivationIndex Registry => registry;

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
        var results = TryParseSelect(query, out var selectedNames)
            ? registry.ActivateByName(selectedNames).Take(effectiveMaxResults).ToArray()
            : registry.SearchAndActivate(query, effectiveMaxResults);
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
            DeferredToolLoadingTraceRecorder.AnthropicToolReferenceWireShape);

        if (entries.Length == 0)
            return ValueTask.FromResult<object?>("No matching tools found. Try different keywords.");

        var references = entries
            .Select(static entry => (AIContent)new TextContent(string.Empty)
            {
                RawRepresentation = new Block(new BetaToolReferenceBlockParam(entry.Tool.Name))
            })
            .ToArray();
        return ValueTask.FromResult<object?>(references);
    }

    private static bool TryParseSelect(string query, out string[] names)
    {
        const string Prefix = "select:";
        names = [];
        if (!query.TrimStart().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var selection = query.TrimStart()[Prefix.Length..];
        names = selection
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return names.Length > 0;
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
}
