using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Plugins;
using DotCraft.Tools;

namespace DotCraft.Protocol;

public sealed partial class SessionService
{
    /// <inheritdoc />
    public async ValueTask RecordStartedAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveInvocationTurn(context, out var runtime, out var turnRuntime, out var turn))
            return;

        var existing = turn.Items.LastOrDefault(item => HasCallId(item, context.CallId));
        var item = existing ?? new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(turn.Items.Count + 1),
            TurnId = turn.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var providerCallName = turnRuntime.ToolSnapshot?.ProviderCallNames
            .GetValueOrDefault(registration.Definition.Name);

        item.Status = ItemStatus.Started;
        item.CompletedAt = null;
        switch (registration.Definition.Id.Kind)
        {
            case ToolSourceKind.Mcp:
                item.Type = ItemType.McpToolCall;
                item.Payload = new McpToolCallPayload
                {
                    Namespace = registration.Definition.Name.Namespace,
                    ToolName = registration.Definition.Name.Name,
                    ProviderCallName = providerCallName,
                    ToolDefinitionId = registration.Definition.Id.ToString(),
                    RuntimeBindingId = registration.Binding.Id.Value,
                    BindingRevision = registration.Binding.Revision,
                    SnapshotRevision = context.SnapshotRevision,
                    McpGeneration = registration.Binding.Revision,
                    Source = ToSessionProvenance(registration.Definition),
                    Presentation = ToSessionPresentation(registration.Definition.Presentation),
                    Server = registration.Definition.Id.SourceId,
                    Origin = registration.Definition.Provenance.Origin ?? "workspace",
                    SourceToolId = registration.Definition.Id.SourceToolId.Value,
                    CallId = context.CallId,
                    Arguments = arguments.DeepClone().AsObject(),
                    Status = "inProgress"
                };
                break;
            case ToolSourceKind.RuntimeDynamic:
                item.Type = ItemType.DynamicToolCall;
                item.Payload = new DynamicToolCallPayload
                {
                    Namespace = registration.Definition.Name.Namespace,
                    ToolName = registration.Definition.Name.Name,
                    ProviderCallName = providerCallName,
                    ToolDefinitionId = registration.Definition.Id.ToString(),
                    RuntimeBindingId = registration.Binding.Id.Value,
                    BindingRevision = registration.Binding.Revision,
                    SnapshotRevision = context.SnapshotRevision,
                    Source = ToSessionProvenance(registration.Definition),
                    Presentation = ToSessionPresentation(registration.Definition.Presentation),
                    CallId = context.CallId,
                    Arguments = arguments.DeepClone().AsObject(),
                    Status = "inProgress"
                };
                break;
            default:
                item.Type = ItemType.ToolCall;
                item.Payload = new ToolCallPayload
                {
                    Namespace = registration.Definition.Name.Namespace,
                    ToolName = registration.Definition.Name.Name,
                    ProviderCallName = providerCallName,
                    ToolDefinitionId = registration.Definition.Id.ToString(),
                    RuntimeBindingId = registration.Binding.Id.Value,
                    BindingRevision = registration.Binding.Revision,
                    SnapshotRevision = context.SnapshotRevision,
                    Source = ToSessionProvenance(registration.Definition),
                    Presentation = ToSessionPresentation(registration.Definition.Presentation),
                    Arguments = arguments.DeepClone().AsObject(),
                    CallId = context.CallId
                };
                break;
        }

        if (existing == null)
            turn.Items.Add(item);
        turnRuntime.ToolInvocationItems[context.CallId] = item;
        var channel = runtime.Broker.CreateTurnChannel(turn.Id);
        channel.EmitItemStarted(item);
        await PersistThreadIfMaterializedAsync(runtime.Thread, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask RecordTerminalAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        ToolExecutionResult result,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveInvocationTurn(context, out var runtime, out var turnRuntime, out var turn)
            || !turnRuntime.ToolInvocationItems.TryGetValue(context.CallId, out var startedItem))
            return;

        var completedAt = DateTimeOffset.UtcNow;
        var durationMs = Math.Max(0L, (long)duration.TotalMilliseconds);
        var channel = runtime.Broker.CreateTurnChannel(turn.Id);
        switch (registration.Definition.Id.Kind)
        {
            case ToolSourceKind.Mcp:
                if (startedItem.Payload is not McpToolCallPayload mcpStarted)
                    return;
                startedItem.Status = ItemStatus.Completed;
                startedItem.CompletedAt = completedAt;
                startedItem.Payload = CompleteMcpPayload(
                    mcpStarted,
                    result,
                    durationMs);
                channel.EmitItemCompleted(startedItem);
                break;
            case ToolSourceKind.RuntimeDynamic:
                if (startedItem.Payload is not DynamicToolCallPayload dynamicStarted)
                    return;
                startedItem.Status = ItemStatus.Completed;
                startedItem.CompletedAt = completedAt;
                startedItem.Payload = CompleteDynamicPayload(
                    dynamicStarted,
                    result,
                    durationMs);
                channel.EmitItemCompleted(startedItem);
                break;
            default:
                startedItem.Status = ItemStatus.Completed;
                startedItem.CompletedAt ??= completedAt;
                channel.EmitItemCompleted(startedItem);
                var resultItem = new SessionItem
                {
                    Id = SessionIdGenerator.NewItemId(turn.Items.Count + 1),
                    TurnId = turn.Id,
                    Type = ItemType.ToolResult,
                    Status = ItemStatus.Completed,
                    CreatedAt = completedAt,
                    CompletedAt = completedAt,
                    Payload = new ToolResultPayload
                    {
                        CallId = context.CallId,
                        Result = result.Content ?? result.Error?.Message ?? string.Empty,
                        ContentItems = registration.Definition.Id.Kind == ToolSourceKind.LegacyAppBinding
                            ? ExtractDynamicContentItems(result.RawSourceResult) ?? ToModelContentItems(result.Content)
                            : ToModelContentItems(result.Content),
                        StructuredContent = ToJsonNode(result.StructuredContent),
                        Meta = ToJsonNode(result.Meta),
                        ErrorCode = result.Error?.Code,
                        ErrorMessage = result.Error?.Message,
                        Success = result.Success
                    }
                };
                turn.Items.Add(resultItem);
                channel.EmitItemStarted(resultItem);
                channel.EmitItemCompleted(resultItem);
                break;
        }

        await PersistThreadIfMaterializedAsync(runtime.Thread, cancellationToken).ConfigureAwait(false);
    }

    private bool TryResolveInvocationTurn(
        ToolInvocationContext context,
        out ThreadRuntime runtime,
        out TurnRuntime turnRuntime,
        out SessionTurn turn)
    {
        turn = null!;
        turnRuntime = null!;
        if (!_runtimeRegistry.TryGetRuntime(context.ThreadId, out runtime!))
            return false;

        var turnId = context.TurnId;
        if (string.IsNullOrWhiteSpace(turnId))
            return false;
        turn = runtime.Thread.Turns.LastOrDefault(candidate => string.Equals(candidate.Id, turnId, StringComparison.Ordinal))!;
        return turn != null && runtime.TryGetTurn(turnId, out turnRuntime!);
    }

    private static bool HasCallId(SessionItem item, string callId) => item.Payload switch
    {
        ToolCallPayload payload => string.Equals(payload.CallId, callId, StringComparison.Ordinal),
        McpToolCallPayload payload => string.Equals(payload.CallId, callId, StringComparison.Ordinal),
        DynamicToolCallPayload payload => string.Equals(payload.CallId, callId, StringComparison.Ordinal),
        _ => false
    };

    private static ToolSourceProvenancePayload ToSessionProvenance(ToolDefinition definition) => new()
    {
        Kind = definition.Provenance.Kind.ToString(),
        SourceId = definition.Provenance.SourceId,
        Origin = definition.Provenance.Origin,
        SourceToolId = definition.Id.SourceToolId.Value,
        PluginId = definition.Provenance.Kind == ToolSourceKind.PluginNative
            ? definition.Provenance.SourceId
            : null,
        FunctionId = definition.Provenance.Kind == ToolSourceKind.PluginNative
            ? definition.Id.SourceToolId.Value
            : null
    };

    private static ToolPresentationPayload? ToSessionPresentation(ToolPresentationDescriptor? presentation)
    {
        if (presentation is null)
            return null;
        var options = new JsonObject();
        foreach (var (name, value) in presentation.Options)
            options[name] = JsonNode.Parse(value.GetRawText());
        return new ToolPresentationPayload
        {
            PresentationId = presentation.Id.Value,
            Options = options
        };
    }

    private static McpToolCallPayload CompleteMcpPayload(
        McpToolCallPayload started,
        ToolExecutionResult result,
        long durationMs)
    {
        var raw = result.RawSourceResult;
        var content = raw is { ValueKind: JsonValueKind.Object }
                      && raw.Value.TryGetProperty("content", out var rawContent)
                      && rawContent.ValueKind == JsonValueKind.Array
            ? JsonNode.Parse(rawContent.GetRawText()) as JsonArray
            : null;
        var isError = raw is { ValueKind: JsonValueKind.Object }
                      && raw.Value.TryGetProperty("isError", out var rawIsError)
                      && rawIsError.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? rawIsError.GetBoolean()
            : !result.Success;
        return started with
        {
            Status = result.Success ? "completed" : "failed",
            DurationMs = durationMs,
            Content = content,
            ModelContentItems = ToModelContentItems(result.Content),
            StructuredContent = ToJsonNode(result.StructuredContent),
            Meta = ToJsonNode(result.Meta),
            IsError = isError,
            Success = result.Success,
            ErrorCode = result.Error?.Code,
            ErrorMessage = result.Error?.Message
        };
    }

    private static DynamicToolCallPayload CompleteDynamicPayload(
        DynamicToolCallPayload started,
        ToolExecutionResult result,
        long durationMs) => started with
    {
        Status = result.Success ? "completed" : "failed",
        DurationMs = durationMs,
        ContentItems = ExtractDynamicContentItems(result.RawSourceResult) ?? ToModelContentItems(result.Content),
        StructuredContent = ToJsonNode(result.StructuredContent),
        Success = result.Success,
        ErrorCode = result.Error?.Code,
        ErrorMessage = result.Error?.Message
    };

    private static IReadOnlyList<PluginFunctionContentItem>? ToModelContentItems(string? content) =>
        string.IsNullOrEmpty(content)
            ? null
            : [new PluginFunctionContentItem { Type = "text", Text = content }];

    private static JsonNode? ToJsonNode(JsonElement? element) =>
        element is null ? null : JsonNode.Parse(element.Value.GetRawText());

    private static IReadOnlyList<PluginFunctionContentItem>? ExtractDynamicContentItems(JsonElement? raw)
    {
        if (raw is not { ValueKind: JsonValueKind.Object }
            || !raw.Value.TryGetProperty("contentItems", out var items)
            || items.ValueKind != JsonValueKind.Array)
            return null;

        var mapped = new List<PluginFunctionContentItem>();
        foreach (var item in items.EnumerateArray())
        {
            mapped.Add(new PluginFunctionContentItem
            {
                Type = item.TryGetProperty("type", out var type) ? type.GetString() ?? "text" : "text",
                Text = item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
                    ? text.GetString()
                    : null,
                DataBase64 = item.TryGetProperty("dataBase64", out var data) && data.ValueKind == JsonValueKind.String
                    ? data.GetString()
                    : null,
                Url = item.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String
                    ? url.GetString()
                    : null,
                MediaType = item.TryGetProperty("mediaType", out var mediaType) && mediaType.ValueKind == JsonValueKind.String
                    ? mediaType.GetString()
                    : null
            });
        }

        return mapped.Count == 0 ? null : mapped;
    }
}
