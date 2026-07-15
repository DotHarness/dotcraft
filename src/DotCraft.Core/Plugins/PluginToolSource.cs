using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Protocol;
using DotCraft.Tools;

namespace DotCraft.Plugins;

/// <summary>
/// Projects plugin descriptors into immutable definitions and separate runtime bindings.
/// Common schema validation, approval, hooks, lifecycle recording, and audience normalization
/// are intentionally owned by the common dispatcher.
/// </summary>
public sealed class PluginToolSource : IToolSource
{
    private readonly IReadOnlyList<PluginToolRegistration> _registrations;
    private readonly PluginToolInvocationMetadata _invocationMetadata;
    private readonly IToolBindingLease _bindingLease;

    /// <summary>Creates a plugin source for a fixed set of authorized registrations.</summary>
    public PluginToolSource(
        string sourceId,
        IEnumerable<PluginToolRegistration> registrations,
        PluginToolInvocationMetadata? invocationMetadata = null,
        IToolBindingLease? bindingLease = null,
        int priority = 100)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(registrations);
        SourceId = sourceId;
        Priority = priority;
        _registrations = registrations.ToArray();
        _invocationMetadata = invocationMetadata ?? new PluginToolInvocationMetadata();
        _bindingLease = bindingLease ?? ToolBindingLeases.AlwaysAvailable;
    }

    /// <inheritdoc />
    public string SourceId { get; }

    /// <inheritdoc />
    public int Priority { get; }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var result = _registrations
            .OrderBy(registration => registration.Descriptor.Namespace, StringComparer.Ordinal)
            .ThenBy(registration => registration.Descriptor.Name, StringComparer.Ordinal)
            .Select(registration => CreateRegistration(registration, context.Revision))
            .ToArray();
        return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>(result);
    }

    private ToolRegistration CreateRegistration(PluginToolRegistration registration, long revision)
    {
        var descriptor = registration.Descriptor;
        var sourceToolId = new SourceToolId(
            descriptor.FunctionId
            ?? (descriptor.Namespace is null
                ? descriptor.Name
                : $"{descriptor.Namespace}/{descriptor.Name}"));
        var definitionId = new ToolDefinitionId(ToolSourceKind.PluginNative, SourceId, sourceToolId);
        var inputSchema = ToJsonElement(descriptor.InputSchema ?? new JsonObject { ["type"] = "object" });
        JsonElement? outputSchema = descriptor.OutputSchema is null
            ? null
            : ToJsonElement(descriptor.OutputSchema);
        var definition = new ToolDefinition(
            definitionId,
            new ToolName(descriptor.Namespace, descriptor.Name),
            descriptor.Description,
            inputSchema,
            outputSchema,
            annotations: CreateAnnotations(descriptor),
            policyHints: new ToolPolicyHints(RequiresApproval: descriptor.Approval is not null),
            provenance: new ToolProvenance(ToolSourceKind.PluginNative, SourceId, "plugin"));
        var binding = new ToolRuntimeBinding(
            new RuntimeBindingId($"plugin:{SourceId}:{descriptor.Namespace}:{descriptor.Name}:{revision}"),
            definitionId,
            new PluginToolRuntime(registration, _invocationMetadata),
            _bindingLease,
            $"plugin:{SourceId}",
            revision);

        var isDeferred = descriptor.DeferLoading == true && descriptor.Namespace is not null;
        return new ToolRegistration(
            definition,
            binding,
            ToolProjectionShape.StandardPair,
            isDeferred ? ToolExposure.Deferred : ToolExposure.Direct,
            ToolInvocationAudience.Model | ToolInvocationAudience.Host,
            isDeferred ? new DeferredToolDescriptor(descriptor.Namespace!, descriptor.Description) : null);
    }

    private static JsonElement ToJsonElement(JsonNode node) =>
        JsonSerializer.Deserialize<JsonElement>(node.ToJsonString(), SessionWireJsonOptions.Default);

    private static IReadOnlyDictionary<string, JsonElement>? CreateAnnotations(
        PluginFunctionDescriptor descriptor)
    {
        var annotations = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (descriptor.Approval is not null)
        {
            annotations["dotcraft/pluginApproval"] =
                JsonSerializer.SerializeToElement(descriptor.Approval, SessionWireJsonOptions.Default);
        }
        if (descriptor.RequiresChatContext)
            annotations["dotcraft/requiresChatContext"] = JsonSerializer.SerializeToElement(true);
        return annotations.Count == 0 ? null : annotations;
    }
}

/// <summary>Immutable channel metadata captured when a thread-scoped plugin source is created.</summary>
public sealed record PluginToolInvocationMetadata(
    string? OriginChannel = null,
    string? ChannelContext = null,
    string? SenderId = null,
    string? GroupId = null);

/// <summary>Source-specific plugin executor used behind a common runtime binding.</summary>
public sealed class PluginToolRuntime(
    PluginToolRegistration registration,
    PluginToolInvocationMetadata invocationMetadata) : IToolRuntime
{
    private readonly PluginToolRegistration _registration =
        registration ?? throw new ArgumentNullException(nameof(registration));
    private readonly PluginToolInvocationMetadata _invocationMetadata =
        invocationMetadata ?? throw new ArgumentNullException(nameof(invocationMetadata));

    /// <inheritdoc />
    public async ValueTask<ToolExecutionResult> InvokeAsync(
        ToolInvocationContext context,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(arguments);

        PluginFunctionInvocationResult result;
        try
        {
            result = await _registration.Invoker.InvokeAsync(
                new PluginToolInvocationContext
                {
                    Descriptor = _registration.Descriptor,
                    Invocation = context,
                    Arguments = arguments.DeepClone().AsObject(),
                    OriginChannel = _invocationMetadata.OriginChannel,
                    ChannelContext = _invocationMetadata.ChannelContext,
                    SenderId = _invocationMetadata.SenderId,
                    GroupId = _invocationMetadata.GroupId
                },
                cancellationToken).ConfigureAwait(false);
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

        var content = NormalizeModelContent(result);
        JsonElement? structuredContent = result.StructuredResult is null
            ? null
            : ToJsonElement(result.StructuredResult);
        var rawResult = ToJsonElement(JsonSerializer.SerializeToNode(result, SessionWireJsonOptions.Default)!);
        if (result.Success)
            return ToolExecutionResult.Succeeded(content, structuredContent, rawSourceResult: rawResult);

        return new ToolExecutionResult(
            false,
            content,
            structuredContent,
            rawSourceResult: rawResult,
            error: new ToolError(
                ToolErrorCodes.ExecutionFailed,
                result.ErrorMessage ?? "Plugin operation failed.",
                CreateErrorParameters(result.ErrorCode)));
    }

    private static string NormalizeModelContent(PluginFunctionInvocationResult result)
    {
        var lines = new List<string>();
        foreach (var item in result.ContentItems ?? [])
        {
            if (string.Equals(item.Type, "text", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(item.Text))
            {
                lines.Add(item.Text);
            }
            else if (string.Equals(item.Type, "image", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add("[image]");
            }
        }

        if (lines.Count > 0)
            return string.Join(Environment.NewLine, lines);
        if (!result.Success)
            return result.ErrorMessage ?? "Plugin operation failed.";
        return "Plugin operation completed without model-visible content.";
    }

    private static IReadOnlyDictionary<string, JsonElement>? CreateErrorParameters(string? sourceErrorCode)
    {
        if (string.IsNullOrWhiteSpace(sourceErrorCode))
            return null;
        return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["sourceErrorCode"] = JsonSerializer.SerializeToElement(sourceErrorCode)
        };
    }

    private static JsonElement ToJsonElement(JsonNode node) =>
        JsonSerializer.Deserialize<JsonElement>(node.ToJsonString(), SessionWireJsonOptions.Default);
}
