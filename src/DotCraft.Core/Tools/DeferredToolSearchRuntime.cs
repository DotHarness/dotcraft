using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

internal interface IDeferredToolSearchDeclaration
{
    [ToolDeclaration(Name = NativeToolSearchTool.ToolName)]
    [ToolSchema(DisallowAdditionalProperties = true)]
    [Description("Search for deferred tools and activate matching definitions.")]
    void Search(
        [Description("Search keywords for deferred tools.")] string query,
        [ToolParameter(Name = "max_results")]
        [Range(0, int.MaxValue)]
        [Description("Maximum number of matching tools to return.")] int maxResultsSnakeCase = 0,
        [Range(0, int.MaxValue)]
        [Description("Maximum number of matching tools to return.")] int maxResults = 0);
}

internal sealed class DeferredToolSearchRuntime(
    DeferredToolActivationIndex activationIndex,
    DeferredToolSearchPlan plan,
    DeferredToolLoadingTraceContext? traceContext) : IToolRuntime
{
    internal const string CanonicalName = NativeToolSearchTool.ToolName;
    internal const string SourceId = "core-native";

    internal DeferredToolActivationIndex ActivationIndex => activationIndex;
    internal DeferredToolSearchPlan Plan => plan;

    public ValueTask<ToolExecutionResult> InvokeAsync(
        ToolInvocationContext context,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = ReadString(arguments, "query") ?? ReadString(arguments, "q") ?? string.Empty;
        var requestedMax = ReadInt(arguments, "max_results")
            ?? ReadInt(arguments, "maxResults")
            ?? plan.MaxSearchResults;
        var maxResults = Math.Clamp(requestedMax, 0, plan.MaxSearchResults);
        var activatedBefore = traceContext == null
            ? null
            : activationIndex.GetActivatedToolNames().ToHashSet(StringComparer.Ordinal);
        var isAnthropic = plan.Mode == DeferredToolLoadingMode.Native
                          && string.Equals(plan.ProviderProtocol, ModelProviderProtocols.Anthropic, StringComparison.Ordinal);
        IReadOnlyList<ToolSearchResult> selected = [];
        var hasSelection = isAnthropic
                           && activationIndex.TrySelectAndActivate(query, maxResults, out selected);
        var matches = hasSelection
            ? selected
            : activationIndex.SearchAndActivate(query, maxResults);
        var entries = matches
            .Select(match => activationIndex.Entries.TryGetValue(match.Name, out var entry) ? entry : null)
            .Where(static entry => entry != null)
            .Select(static entry => entry!)
            .ToArray();

        DeferredToolLoadingTraceRecorder.RecordNewActivations(
            traceContext,
            query,
            requestedMax,
            entries,
            activatedBefore,
            isAnthropic
                ? DeferredToolLoadingTraceRecorder.AnthropicToolReferenceWireShape
                : plan.Mode == DeferredToolLoadingMode.Native
                    ? DeferredToolLoadingTraceRecorder.OpenAIResponsesToolSearchOutputWireShape
                    : "simulated");

        if (isAnthropic)
        {
            object providerResult = entries.Length == 0
                ? "No matching tools found. Try different keywords."
                : entries.Select(static entry => (AIContent)new DeferredToolReferenceContent(
                    DeferredToolActivationIndex.GetIdentityKey(entry))).ToArray();
            return ValueTask.FromResult(ToolExecutionResult.Succeeded(
                FormatDisplay(entries),
                providerResult: providerResult));
        }

        if (plan.Mode == DeferredToolLoadingMode.Native)
        {
            var output = new NativeToolSearchOutput(NativeToolSearchTool.ToOutputTools(entries));
            return ValueTask.FromResult(ToolExecutionResult.Succeeded(
                NativeToolSearchTool.FormatOutputForDisplay(output),
                providerResult: output));
        }

        return ValueTask.FromResult(ToolExecutionResult.Succeeded(FormatSimulated(entries)));
    }

    internal static ToolRegistration CreateRegistration(
        IReadOnlyDictionary<string, IReadOnlyList<ToolDefinition>> deferredDefinitions,
        IReadOnlyDictionary<ToolName, ToolRegistration> registrations,
        IReadOnlyDictionary<string, string> namespaceDescriptions,
        long revision,
        DeferredToolSearchPlan plan)
    {
        var declaration = DotCraft.GeneratedTools.Core.GeneratedToolDeclarations
            .IDeferredToolSearchDeclaration_Search_Declaration;
        var providerNames = ProviderToolProjector.Project(registrations.Keys.ToArray()).ToDictionary();
        foreach (var (name, registration) in registrations)
        {
            if (registration.ProviderFlatNameOverride is { } callNameOverride)
                providerNames[name] = callNameOverride;
        }
        var entries = deferredDefinitions
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .SelectMany(pair => pair.Value.Select(definition => new DeferredToolEntry(
                new DeferredSnapshotToolDeclaration(providerNames[definition.Name], definition),
                definition.Provenance.SourceId,
                pair.Key,
                namespaceDescriptions.GetValueOrDefault(pair.Key))))
            .ToArray();
        var activationIndex = new DeferredToolActivationIndex(entries, plan.Mode);
        var providerFlatName = CanonicalName;
        var traceContext = plan.TraceCollector == null
            ? null
            : new DeferredToolLoadingTraceContext(
                plan.TraceCollector,
                plan.Strategy,
                plan.Mode.ToString(),
                plan.ProviderProtocol,
                providerFlatName,
                entries.Length,
                plan.MaxSearchResults);
        var runtime = new DeferredToolSearchRuntime(activationIndex, plan, traceContext);
        var sourceToolId = new SourceToolId(CanonicalName);
        var definitionId = new ToolDefinitionId(ToolSourceKind.CoreNative, SourceId, sourceToolId);
        var definition = new ToolDefinition(
            definitionId,
            new ToolName(null, declaration.Name),
            declaration.Description,
            declaration.InputSchema,
            declaration.OutputSchema,
            presentation: new ToolPresentationDescriptor(new PresentationId("core.deferred-search")),
            provenance: new ToolProvenance(ToolSourceKind.CoreNative, SourceId));
        var binding = new ToolRuntimeBinding(
            new RuntimeBindingId($"core-native:{CanonicalName}:{revision}"),
            definitionId,
            runtime,
            ToolBindingLeases.AlwaysAvailable,
            $"core-native:{CanonicalName}",
            revision);
        return new ToolRegistration(
            definition,
            binding,
            ToolProjectionShape.StandardPair,
            ToolExposure.Direct,
            ToolInvocationAudience.Model,
            providerFlatNameOverride: null);
    }

    internal static bool IsRegistration(ToolRegistration registration) =>
        registration.Definition.Id.Kind == ToolSourceKind.CoreNative
        && string.Equals(registration.Definition.Id.SourceId, SourceId, StringComparison.Ordinal)
        && string.Equals(registration.Definition.Id.SourceToolId.Value, CanonicalName, StringComparison.Ordinal);

    private static string? ReadString(JsonObject arguments, string name) =>
        arguments.TryGetPropertyValue(name, out var node) && node is JsonValue value
            ? value.TryGetValue<string>(out var text) ? text : null
            : null;

    private static int? ReadInt(JsonObject arguments, string name)
    {
        if (!arguments.TryGetPropertyValue(name, out var node) || node is not JsonValue value)
            return null;
        if (value.TryGetValue<int>(out var number))
            return number;
        return value.TryGetValue<string>(out var text) && int.TryParse(text, out number) ? number : null;
    }

    private static string FormatDisplay(IReadOnlyList<DeferredToolEntry> entries) =>
        entries.Count == 0
            ? "No matching tools found."
            : $"Found {entries.Count} matching tool(s):\n" + string.Join(
                "\n",
                entries.Select(static entry => $"- {entry.Tool.Name}: {entry.Tool.Description}"));

    private static string FormatSimulated(IReadOnlyList<DeferredToolEntry> entries)
    {
        if (entries.Count == 0)
            return "No matching tools found. Try different keywords.";
        var builder = new StringBuilder($"Found {entries.Count} matching tool(s) — they are now available:\n");
        foreach (var entry in entries)
            builder.Append("- **").Append(entry.Tool.Name).Append("**: ").AppendLine(entry.Tool.Description);
        return builder.Append("\nYou can call these tools directly in your next action.").ToString();
    }

    private sealed class DeferredSnapshotToolDeclaration(string providerFlatName, ToolDefinition definition)
        : AIFunction, ICanonicalToolIdentityMetadata
    {
        public override string Name => providerFlatName;
        public ToolName CanonicalToolName => definition.Name;
        public string ProviderFlatName => providerFlatName;
        public string? ToolNamespaceDescription => definition.NamespaceDescription;
        public override string Description => definition.Description;
        public override JsonElement JsonSchema => definition.InputSchema;
        public override JsonElement? ReturnJsonSchema => definition.OutputSchema;
        public override System.Reflection.MethodInfo? UnderlyingMethod => null;
        public override JsonSerializerOptions JsonSerializerOptions => JsonSerializerOptions.Default;
        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<object?>(new InvalidOperationException(
                "Deferred snapshot declarations execute through the common dispatcher."));
    }
}
