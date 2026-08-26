using System.Text.Json;
using System.Text.Json.Nodes;
using Acme.ReviewCore.Api;
using DotCraft.Tools;

namespace Acme.ReviewCore;

/// <summary>The canonical names of the Tools this plugin publishes.</summary>
internal static class ReviewToolNames
{
    internal const string Namespace = "review";
    internal const string Summary = "summary";
    internal const string Publish = "publish";
}

/// <summary>Normalizes review text through the plugin's own service: one source contributing one Tool.</summary>
internal sealed class SummaryTool(IReviewService service, ReviewJournal journal) : IToolSource, IToolRuntime
{
    private const string ToolId = "review-summary";

    /// <inheritdoc />
    public string SourceId => "acme.review-core.summary";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ToolRegistration>>(
        [
            ToolRegistrations.Create(
                SourceId,
                ToolId,
                new ToolName(ReviewToolNames.Namespace, ReviewToolNames.Summary),
                "Normalizes review text and appends the review checklist.",
                ToolSchemas.Text,
                this,
                context.Revision)
        ]);

    /// <inheritdoc />
    public ValueTask<ToolExecutionResult> InvokeAsync(
        ToolInvocationContext context,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        if (!ToolRegistrations.TryReadText(arguments, "text", out var text))
        {
            // An expected failure is a result, not an exception: a thrown exception becomes an
            // unspecified Tool failure, while a returned error keeps its stable code.
            return ValueTask.FromResult(ToolExecutionResult.Failed(
                new ToolError("ReviewTextMissing", "The 'text' argument is required."),
                "The 'text' argument is required."));
        }

        var normalized = service.Normalize(text);
        journal.Write($"summary Tool normalized {normalized.Length} characters");
        return ValueTask.FromResult(ToolExecutionResult.Succeeded(
            string.Join(
                Environment.NewLine,
                [normalized, string.Empty, .. service.Checklist.Select(static item => $"- {item}")]),
            JsonSerializer.SerializeToElement(new
            {
                normalized,
                checklist = service.Checklist
            })));
    }
}

/// <summary>The plugin's one write Tool, kept off the model by the restriction and off Host dispatch by the
/// approval stage until the caller acknowledges it.</summary>
internal sealed class PublishTool(ReviewJournal journal) : IToolSource, IToolRuntime
{
    private const string ToolId = "review-publish";

    /// <inheritdoc />
    public string SourceId => "acme.review-core.publish";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ToolRegistration>>(
        [
            ToolRegistrations.Create(
                SourceId,
                ToolId,
                new ToolName(ReviewToolNames.Namespace, ReviewToolNames.Publish),
                "Publishes the finished review.",
                ToolSchemas.Approval,
                this,
                context.Revision,
                readOnly: false)
        ]);

    /// <inheritdoc />
    public ValueTask<ToolExecutionResult> InvokeAsync(
        ToolInvocationContext context,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        journal.Write("publish Tool invoked");
        return ValueTask.FromResult(ToolExecutionResult.Succeeded("Review published."));
    }
}

/// <summary>Joins a definition to the binding that executes it. The Host re-keys both to the contributing generation.</summary>
internal static class ToolRegistrations
{
    public static ToolRegistration Create(
        string sourceId,
        string toolId,
        ToolName name,
        string description,
        JsonElement inputSchema,
        IToolRuntime runtime,
        long revision,
        bool readOnly = true)
    {
        var definitionId = new ToolDefinitionId(
            ToolSourceKind.PluginNative,
            sourceId,
            new SourceToolId(toolId));
        return new ToolRegistration(
            new ToolDefinition(
                definitionId,
                name,
                description,
                inputSchema,
                policyHints: new ToolPolicyHints(RequiresApproval: false, ReadOnly: readOnly)),
            new ToolRuntimeBinding(
                new RuntimeBindingId($"{sourceId}:{toolId}:{revision}"),
                definitionId,
                runtime,
                ToolBindingLeases.AlwaysAvailable,
                sourceId,
                revision),
            ToolProjectionShape.StandardPair);
    }

    public static bool TryReadText(JsonObject arguments, string name, out string value)
    {
        value = string.Empty;
        if (!arguments.TryGetPropertyValue(name, out var node)
            || node?.GetValueKind() != JsonValueKind.String)
        {
            return false;
        }

        value = node.GetValue<string>();
        return true;
    }
}

/// <summary>The JSON schemas the sample's Tools declare.</summary>
internal static class ToolSchemas
{
    public static JsonElement Text { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { text = new { type = "string" } },
        required = new[] { "text" },
        additionalProperties = false
    });

    public static JsonElement Approval { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { approved = new { type = "boolean" } },
        additionalProperties = false
    });

    public static JsonElement Empty { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { },
        additionalProperties = false
    });
}
