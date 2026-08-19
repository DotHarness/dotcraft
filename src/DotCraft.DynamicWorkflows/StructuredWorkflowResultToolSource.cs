using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using DotCraft.Tools;
using Json.Schema;

namespace DotCraft.DynamicWorkflows;

internal interface IStructuredWorkflowResultToolDeclaration
{
    [ToolDeclaration(Name = "SubmitWorkflowResult")]
    [ToolSchema(DisallowAdditionalProperties = true)]
    [Description("Submit the final structured result for the current task.")]
    void SubmitWorkflowResult(
        [Description("Final structured result for the current task.")] JsonNode? result);
}

public sealed class StructuredWorkflowResultRegistry
{
    private sealed record Entry(JsonSchema Schema, int MaxResultBytes, TaskCompletionSource<JsonNode?> Completion);
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public void Bind(string threadId, JsonNode schema, int maxResultBytes = 2 * 1024 * 1024) =>
        _entries[threadId] = new Entry(
            JsonSchema.FromText(schema.ToJsonString()),
            maxResultBytes,
            new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously));

    public bool Contains(string threadId) => _entries.ContainsKey(threadId);

    public bool TrySubmit(string threadId, JsonNode? value, out string? error)
    {
        error = null;
        if (!_entries.TryGetValue(threadId, out var entry)) { error = "No structured workflow result is expected for this thread."; return false; }
        var normalized = CanonicalJson.Normalize(value);
        if (Encoding.UTF8.GetByteCount(normalized?.ToJsonString() ?? "null") > entry.MaxResultBytes)
        {
            error = "The structured result exceeds the configured size limit.";
            return false;
        }
        var evaluation = entry.Schema.Evaluate(normalized, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (!evaluation.IsValid)
        {
            var paths = evaluation.Details
                .Where(detail => !detail.IsValid)
                .Select(detail => string.IsNullOrWhiteSpace(detail.InstanceLocation.ToString())
                    ? "/"
                    : detail.InstanceLocation.ToString())
                .Distinct(StringComparer.Ordinal)
                .Take(5)
                .ToArray();
            error = paths.Length == 0
                ? "The result does not satisfy the requested JSON Schema."
                : $"The result does not satisfy the requested JSON Schema at: {string.Join(", ", paths)}.";
            return false;
        }
        entry.Completion.TrySetResult(normalized);
        return true;
    }

    public bool TryGetResult(string threadId, out JsonNode? result)
    {
        result = null;
        if (!_entries.TryGetValue(threadId, out var entry) || !entry.Completion.Task.IsCompletedSuccessfully) return false;
        result = entry.Completion.Task.Result?.DeepClone();
        return true;
    }

    public void Remove(string threadId) => _entries.TryRemove(threadId, out _);
}

public sealed class StructuredWorkflowResultToolSource(StructuredWorkflowResultRegistry registry) : IToolSource, IThreadScopedToolSource
{
    public string SourceId => "structured-result";
    public int Priority => 58;

    public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context,
        CancellationToken cancellationToken = default)
    {
        if (!registry.Contains(context.ThreadId)) return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>([]);
        var declaration = DotCraft.GeneratedTools.DynamicWorkflows.GeneratedToolDeclarations
            .IStructuredWorkflowResultToolDeclaration_SubmitWorkflowResult_Declaration;
        var sourceToolId = new SourceToolId(declaration.Name);
        var definitionId = new ToolDefinitionId(ToolSourceKind.PluginNative, SourceId, sourceToolId);
        var definition = new ToolDefinition(
            definitionId,
            new ToolName(null, declaration.Name),
            declaration.Description,
            declaration.InputSchema,
            declaration.OutputSchema,
            policyHints: new ToolPolicyHints(ReadOnly: true),
            provenance: new ToolProvenance(ToolSourceKind.PluginNative, SourceId),
            policyScope: ToolPolicyScope.RuntimeManaged);
        var bindingId = $"structured-result:{context.ThreadId}";
        var binding = new ToolRuntimeBinding(
            new RuntimeBindingId(bindingId),
            definitionId,
            new Runtime(registry),
            ToolBindingLeases.AlwaysAvailable,
            bindingId,
            context.Revision);
        return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>([
            new ToolRegistration(definition, binding, ToolProjectionShape.StandardPair, invocationAudiences: ToolInvocationAudience.Model)
        ]);
    }

    public ValueTask ReleaseThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        registry.Remove(threadId);
        return ValueTask.CompletedTask;
    }

    private sealed class Runtime(StructuredWorkflowResultRegistry registry) : IToolRuntime
    {
        public ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default)
        {
            var value = arguments["result"]?.DeepClone();
            return registry.TrySubmit(context.ThreadId, value, out var error)
                ? ValueTask.FromResult(ToolExecutionResult.Succeeded(
                    "Structured result accepted.",
                    directive: ToolExecutionDirective.TerminateTurn))
                : ValueTask.FromResult(ToolExecutionResult.Failed(
                    new ToolError(ToolErrorCodes.InputInvalid, error ?? "Structured result is invalid.")));
        }
    }
}
