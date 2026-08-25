using System.Text.Json;
using System.Text.Json.Nodes;
using Acme.ReviewCore.Api;
using DotCraft.Contributions;
using DotCraft.Plugins;
using DotCraft.Tools;

namespace Acme.ReviewConsumer;

/// <summary>The sample consumer plugin: it binds the provider's typed API and contributes on top of it.</summary>
public sealed class Plugin : IDotCraftPlugin
{
    /// <inheritdoc />
    public ValueTask ActivateAsync(
        IPluginActivationContext context,
        CancellationToken cancellationToken)
    {
        // Dependencies resolve during activation only; the manifest pin is what orders providers
        // ahead of consumers, so the instance stays valid for the whole generation.
        var service = context.Dependencies.GetRequired<IReviewService>("acme.review-core");
        context.Contributions.Add<IToolSource>(new NormalizeTool(service));
        context.Contributions.Add<ISystemPromptSection>(
            new ConsumerSection(service),
            new ContributionOptions(Order: 1260));
        return ValueTask.CompletedTask;
    }
}

/// <summary>Exposes the provider's normalization as a Tool of the consumer's own.</summary>
internal sealed class NormalizeTool(IReviewService service) : IToolSource, IToolRuntime
{
    private const string ToolId = "review-normalize";

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { text = new { type = "string" } },
        required = new[] { "text" },
        additionalProperties = false
    });

    /// <inheritdoc />
    public string SourceId => "acme.review-consumer.normalize";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context,
        CancellationToken cancellationToken = default)
    {
        var definitionId = new ToolDefinitionId(
            ToolSourceKind.PluginNative,
            SourceId,
            new SourceToolId(ToolId));
        var registration = new ToolRegistration(
            new ToolDefinition(
                definitionId,
                new ToolName("review", "normalize"),
                "Normalizes review text through the review-core provider.",
                InputSchema,
                policyHints: new ToolPolicyHints(RequiresApproval: false, ReadOnly: true)),
            new ToolRuntimeBinding(
                new RuntimeBindingId($"{SourceId}:{ToolId}:{context.Revision}"),
                definitionId,
                this,
                ToolBindingLeases.AlwaysAvailable,
                SourceId,
                context.Revision),
            ToolProjectionShape.StandardPair);
        return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>([registration]);
    }

    /// <inheritdoc />
    public ValueTask<ToolExecutionResult> InvokeAsync(
        ToolInvocationContext context,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        var text = arguments.TryGetPropertyValue("text", out var node)
                   && node?.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : string.Empty;
        return ValueTask.FromResult(ToolExecutionResult.Succeeded(service.Normalize(text)));
    }
}

/// <summary>Adds one prompt section that reads the provider's checklist.</summary>
internal sealed class ConsumerSection(IReviewService service) : ISystemPromptSection
{
    /// <inheritdoc />
    public string Name => "review-consumer";

    /// <inheritdoc />
    public string? GetContent(SystemPromptSectionContext context) =>
        service.Checklist.Count == 0
            ? null
            : "The review.normalize Tool shares the review-core checklist.";
}
