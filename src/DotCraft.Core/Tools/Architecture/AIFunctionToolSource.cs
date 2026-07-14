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
        ToolPlanningContext context) => CoreToolPresentationCatalog.Resolve(function.Name);

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
        var name = new ToolName(
            ToolNamespaceMetadataResolver.TryGet(function, out var toolNamespace) ? toolNamespace : null,
            function.Name);
        var definition = new ToolDefinition(
            definitionId,
            name,
            string.IsNullOrWhiteSpace(function.Description) ? function.Name : function.Description,
            function.JsonSchema,
            function.ReturnJsonSchema,
            annotations: GetAnnotations(function, context),
            policyHints: GetPolicyHints(function, context),
            presentation: GetPresentation(function, context),
            provenance: new ToolProvenance(ToolSourceKind.CoreNative, SourceId, "native"));
        var binding = new ToolRuntimeBinding(
            new RuntimeBindingId($"native:{SourceId}:{function.Name}:{revision}"),
            definitionId,
            new AIFunctionToolRuntime(function),
            ToolBindingLeases.AlwaysAvailable,
            $"native:{SourceId}",
            revision);
        return new ToolRegistration(definition, binding);
    }
}

/// <summary>Stable presentation registrations emitted only by trusted Core native sources.</summary>
internal static class CoreToolPresentationCatalog
{
    public static ToolPresentationDescriptor? Resolve(string toolName) => toolName switch
    {
        "CreatePlan" => Descriptor("core.create-plan"),
        "Cron" => Descriptor("core.cron"),
        "SkillManage" => Descriptor("core.skill-manage"),
        "SkillView" => Descriptor("core.skill-view"),
        "SpawnAgent" => Descriptor("core.subagent", "spawn"),
        "WaitAgent" => Descriptor("core.subagent", "wait"),
        "SendMessage" => Descriptor("core.subagent", "sendMessage"),
        "FollowupTask" => Descriptor("core.subagent", "followupTask"),
        "ListAgents" => Descriptor("core.subagent", "list"),
        "CloseAgent" => Descriptor("core.subagent", "close"),
        "SendInput" => Descriptor("core.subagent", "sendInput"),
        "ResumeAgent" => Descriptor("core.subagent", "resume"),
        "Exec" or "WriteStdin" => Descriptor("core.shell"),
        "WriteFile" => Descriptor("core.file-write", "write"),
        "EditFile" => Descriptor("core.file-write", "edit"),
        "WebSearch" => Descriptor("core.web", "search"),
        "WebFetch" => Descriptor("core.web", "fetch"),
        "RequestUserInput" => Descriptor("core.request-user-input"),
        "ReadFile" => Descriptor("core.read-file"),
        "TodoWrite" or "UpdateTodos" => Descriptor("core.todo"),
        "SearchTools" => Descriptor("core.deferred-search"),
        _ => null
    };

    private static ToolPresentationDescriptor Descriptor(string id, string? operation = null) =>
        new(
            new PresentationId(id),
            operation is null
                ? null
                : new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["operation"] = JsonSerializer.SerializeToElement(operation)
                });
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

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in arguments)
            values[key] = value?.Deserialize<object>(_function.JsonSerializerOptions);

        try
        {
            var result = await _function.InvokeAsync(new AIFunctionArguments(values), cancellationToken)
                .ConfigureAwait(false);
            return ToolExecutionResult.Succeeded(ToModelText(result));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToolExecutionResult.Failed(
                new ToolError(ToolErrorCodes.ExecutionFailed, ex.Message));
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
}
