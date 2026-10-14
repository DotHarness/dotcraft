using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Base class for native sources whose implementation is generated as MEAI functions.
/// The MEAI function is kept behind the runtime binding and is never stored in the
/// durable definition.
/// </summary>
public abstract class AIFunctionToolSource : IToolSource
{
    /// <inheritdoc />
    public abstract string SourceId { get; }

    /// <inheritdoc />
    public virtual int Priority => 100;

    /// <summary>Creates the functions that are valid for one immutable planning context.</summary>
    protected abstract IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context);

    /// <summary>Returns immutable source annotations for one generated declaration.</summary>
    protected virtual IReadOnlyDictionary<string, JsonElement>? GetAnnotations(
        AIFunction function,
        ToolPlanningContext context) => null;

    /// <summary>Returns source policy hints consumed by the common dispatcher.</summary>
    protected virtual ToolPolicyHints GetPolicyHints(
        AIFunction function,
        ToolPlanningContext context) => new();

    /// <summary>Returns trusted Core presentation metadata for a generated declaration.</summary>
    protected virtual ToolPresentationDescriptor? GetPresentation(
        AIFunction function,
        ToolPlanningContext context) =>
        GeneratedToolMetadataResolver.TryGet(function, out var metadata) ? metadata.Presentation : null;

    /// <summary>Returns the model-visible description frozen into this planning snapshot.</summary>
    protected virtual string GetDescription(AIFunction function, ToolPlanningContext context) =>
        string.IsNullOrWhiteSpace(function.Description) ? function.Name : function.Description;

    /// <summary>Returns the canonical namespace for one generated declaration.</summary>
    protected virtual string? GetNamespace(AIFunction function, ToolPlanningContext context) =>
        ToolNamespaceMetadataResolver.TryGet(function, out var toolNamespace) ? toolNamespace : null;

    /// <summary>Returns the model-facing description for the canonical namespace.</summary>
    protected virtual string? GetNamespaceDescription(AIFunction function, ToolPlanningContext context) =>
        ToolNamespaceMetadataResolver.GetDescription(function);

    /// <summary>Returns the model exposure for one generated declaration.</summary>
    protected virtual ToolExposure GetExposure(AIFunction function, ToolPlanningContext context) =>
        ToolExposure.Direct;

    /// <summary>Returns whether profile restrictions may filter one generated declaration.</summary>
    protected virtual ToolPolicyScope GetPolicyScope(AIFunction function, ToolPlanningContext context) =>
        ToolPolicyScope.ProfileManaged;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var registrations = CreateFunctions(context)
            .OrderBy(function => function.Name, StringComparer.Ordinal)
            .Select(function => CreateRegistration(function, context))
            .ToArray();
        return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>(registrations);
    }

    private ToolRegistration CreateRegistration(AIFunction function, ToolPlanningContext context)
    {
        var revision = context.Revision;
        var sourceToolId = new SourceToolId(function.Name);
        var definitionId = new ToolDefinitionId(ToolSourceKind.CoreNative, SourceId, sourceToolId);
        var name = new ToolName(GetNamespace(function, context), function.Name);
        var definition = new ToolDefinition(
            definitionId,
            name,
            GetDescription(function, context),
            function.JsonSchema,
            function.ReturnJsonSchema,
            annotations: BuildAnnotations(function, context),
            policyHints: GetPolicyHints(function, context),
            presentation: GetPresentation(function, context),
            provenance: new ToolProvenance(ToolSourceKind.CoreNative, SourceId, "native"),
            namespaceDescription: GetNamespaceDescription(function, context),
            policyScope: GetPolicyScope(function, context));
        var binding = new ToolRuntimeBinding(
            new RuntimeBindingId($"native:{SourceId}:{function.Name}:{revision}"),
            definitionId,
            new AIFunctionToolRuntime(function),
            ToolBindingLeases.AlwaysAvailable,
            $"native:{SourceId}",
            revision);
        return new ToolRegistration(
            definition,
            binding,
            ToolProjectionShape.StandardPair,
            GetExposure(function, context));
    }

    private IReadOnlyDictionary<string, JsonElement>? BuildAnnotations(
        AIFunction function,
        ToolPlanningContext context)
    {
        var annotations = GetAnnotations(function, context) is { } sourceAnnotations
            ? new Dictionary<string, JsonElement>(sourceAnnotations, StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (GeneratedToolMetadataResolver.TryGet(function, out var metadata))
        {
            annotations["dotcraft/streamArguments"] =
                JsonSerializer.SerializeToElement(metadata.StreamArgumentsEnabled);
            if (metadata.MaxResultChars.HasValue)
            {
                annotations["dotcraft/maxResultChars"] =
                    JsonSerializer.SerializeToElement(metadata.MaxResultChars.Value);
            }
            if (metadata.RpcEligible)
            {
                annotations[RemoteToolMetadata.RpcEligibleAnnotation] =
                    JsonSerializer.SerializeToElement(true);
            }
            if (metadata.ReturnsContent)
            {
                annotations[ToolContentMetadata.ReturnsContentAnnotation] =
                    JsonSerializer.SerializeToElement(true);
            }
        }

        return annotations.Count == 0 ? null : annotations;
    }
}

/// <summary>Executes a generated MEAI function behind a source-neutral runtime binding.</summary>
public sealed class AIFunctionToolRuntime(AIFunction function) : IToolRuntime
{
    private readonly AIFunction _function = function ?? throw new ArgumentNullException(nameof(function));

    /// <inheritdoc />
    public async ValueTask<ToolExecutionResult> InvokeAsync(
        ToolInvocationContext context,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (key, value) in arguments)
                values[key] = value?.Deserialize<object>(_function.JsonSerializerOptions);

            var functionArguments = new AIFunctionArguments(values)
            {
                Context = new Dictionary<object, object?> { [typeof(ToolInvocationContext)] = context }
            };
            object? result;
            using (ToolInvocationScope.Enter(context))
            {
                result = await _function.InvokeAsync(functionArguments, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (result is ToolExecutionResult executionResult)
                return executionResult;
            if (result is IEnumerable<AIContent> richContent)
            {
                var contentItems = richContent.ToArray();
                return ToolExecutionResult.Succeeded(
                    EnsureModelText(ToModelText(contentItems)),
                    contentItems: contentItems);
            }
            return ToolExecutionResult.Succeeded(EnsureModelText(ToModelText(result)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToolExecutionResult.Failed(
                ToolFailure.FromException(ex, $"Tool '{_function.Name}'"));
        }
    }

    private string? ToModelText(object? value) => value switch
    {
        null => null,
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        JsonElement element => element.GetRawText(),
        JsonValue jsonValue when jsonValue.TryGetValue<string>(out var text) => text,
        JsonNode node => node.ToJsonString(_function.JsonSerializerOptions),
        TextContent text => text.Text,
        IEnumerable<AIContent> content => string.Join(
            Environment.NewLine,
            content.OfType<TextContent>().Select(item => item.Text)),
        _ => JsonSerializer.Serialize(value, _function.JsonSerializerOptions)
    };

    private string EnsureModelText(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? $"({_function.Name} completed with no output)"
            : text;
}
