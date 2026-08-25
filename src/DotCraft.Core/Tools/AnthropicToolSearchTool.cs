using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using DotCraft.Agents;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

internal interface IAnthropicToolSearchDeclaration
{
    [ToolDeclaration(Name = AnthropicToolSearchTool.ToolName)]
    [ToolSchema(DisallowAdditionalProperties = true)]
    [Description("Fetches full schema definitions for deferred tools so they can be called.")]
    void Search(
        [Description("Query to find deferred tools. Use \"select:<tool_name>\" for direct selection, or keywords to search.")] string query,
        [ToolParameter(Name = "max_results")]
        [Range(1, int.MaxValue)]
        [Description("Maximum number of matching tools to return (default: 5).")] int maxResults = 5);
}

internal sealed class AnthropicToolSearchTool(
    DeferredToolActivationIndex registry,
    int maxSearchResults = 5,
    DeferredToolLoadingTraceContext? traceContext = null) : AIFunction, IDeferredToolSearchMarker
{
    IDeferredToolActivationView IDeferredToolSearchMarker.Registry => Registry;
    public const string ToolName = NativeToolSearchTool.ToolName;

    private static GeneratedToolDeclaration Declaration =>
        DotCraft.GeneratedTools.Core.GeneratedToolDeclarations.IAnthropicToolSearchDeclaration_Search_Declaration;

    public override string Name => Declaration.Name;

    public override string Description => Declaration.Description;

    public override JsonElement JsonSchema => Declaration.InputSchema;

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
        var hasSelection = registry.TrySelectAndActivate(query, effectiveMaxResults, out var selected);
        var results = hasSelection
            ? selected
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
            .Select(static entry => (AIContent)new DeferredToolReferenceContent(
                DeferredToolActivationIndex.GetIdentityKey(entry)))
            .ToArray();
        return ValueTask.FromResult<object?>(references);
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
