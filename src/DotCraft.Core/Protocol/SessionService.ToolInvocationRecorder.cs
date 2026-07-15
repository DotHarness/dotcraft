using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Mcp;
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

        SessionItem item;
        bool emitStarted;
        lock (turnRuntime.ToolProjectionLock)
        {
            var existing = turn.Items.LastOrDefault(candidate => HasCallId(candidate, context.CallId));
            emitStarted = existing is null || !HasTrustedProjection(existing);
            item = existing ?? new SessionItem
            {
                Id = SessionIdGenerator.NewItemId(
                    turnRuntime.NextToolItemSequence?.Invoke() ?? turn.Items.Count + 1),
                TurnId = turn.Id,
                CreatedAt = DateTimeOffset.UtcNow
            };
            ApplyStartedProjection(
                item,
                registration,
                turnRuntime.ToolSnapshot?.ProviderFlatNames.GetValueOrDefault(registration.Definition.Name)
                    ?? throw new InvalidOperationException(
                        $"Missing flat provider alias for tool '{registration.Definition.Name}'."),
                context.SnapshotRevision,
                context.CallId,
                arguments);
            if (existing is null)
                turn.Items.Add(item);
            turnRuntime.ToolInvocationItems[context.CallId] = item;
        }

        if (emitStarted)
            runtime.Broker.CreateTurnChannel(turn.Id).EmitItemStarted(item);
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
            || !turnRuntime.ToolInvocationItems.TryGetValue(context.CallId, out var startedItem)
            || !turnRuntime.TerminalToolInvocations.TryAdd(context.CallId, 0))
            return;

        var completedAt = DateTimeOffset.UtcNow;
        var durationMs = Math.Max(0L, (long)duration.TotalMilliseconds);
        var channel = runtime.Broker.CreateTurnChannel(turn.Id);
        lock (turnRuntime.ToolProjectionLock)
        {
            switch (registration.ProjectionShape)
            {
            case ToolProjectionShape.McpLifecycle:
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
            case ToolProjectionShape.DynamicLifecycle:
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
            case ToolProjectionShape.StandardPair:
                startedItem.Status = ItemStatus.Completed;
                startedItem.CompletedAt ??= completedAt;
                channel.EmitItemCompleted(startedItem);
                var resultItem = new SessionItem
                {
                    Id = SessionIdGenerator.NewItemId(
                        turnRuntime.NextToolItemSequence?.Invoke() ?? turn.Items.Count + 1),
                    TurnId = turn.Id,
                    Type = ItemType.ToolResult,
                    Status = ItemStatus.Completed,
                    CreatedAt = completedAt,
                    CompletedAt = completedAt,
                    Payload = new ToolResultPayload
                    {
                        CallId = context.CallId,
                        Namespace = registration.Definition.Name.Namespace,
                        ToolName = registration.Definition.Name.Name,
                        ProviderFlatName = turnRuntime.ToolSnapshot?.ProviderFlatNames
                            .GetValueOrDefault(registration.Definition.Name)
                            ?? throw new InvalidOperationException(
                                $"Missing flat provider alias for tool '{registration.Definition.Name}'."),
                        ToolDefinitionId = registration.Definition.Id.ToString(),
                        RuntimeBindingId = registration.Binding.Id.Value,
                        BindingRevision = registration.Binding.Revision,
                        SnapshotRevision = context.SnapshotRevision,
                        Source = ToSessionProvenance(registration.Definition),
                        Presentation = ToSessionPresentation(registration.Definition.Presentation),
                        DurationMs = durationMs,
                        Result = result.Content ?? result.Error?.Message ?? string.Empty,
                        ContentItems = ToModelContentItems(result.Content),
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
            default:
                throw new InvalidOperationException($"Unknown tool projection shape '{registration.ProjectionShape}'.");
            }
        }

        await PersistThreadIfMaterializedAsync(runtime.Thread, cancellationToken).ConfigureAwait(false);
    }

    private static bool HasTrustedProjection(SessionItem item) => item.Payload switch
    {
        ToolCallPayload payload => !string.IsNullOrWhiteSpace(payload.ToolDefinitionId),
        McpToolCallPayload payload => !string.IsNullOrWhiteSpace(payload.ToolDefinitionId),
        DynamicToolCallPayload payload => !string.IsNullOrWhiteSpace(payload.ToolDefinitionId),
        _ => false
    };

    private static void ApplyStartedProjection(
        SessionItem item,
        ToolRegistration registration,
        string providerFlatName,
        long snapshotRevision,
        string callId,
        JsonObject arguments)
    {
        item.Status = ItemStatus.Started;
        item.CompletedAt = null;
        switch (registration.ProjectionShape)
        {
            case ToolProjectionShape.McpLifecycle:
                item.Type = ItemType.McpToolCall;
                item.Payload = new McpToolCallPayload
                {
                    Namespace = registration.Definition.Name.Namespace,
                    ToolName = registration.Definition.Name.Name,
                    ProviderFlatName = providerFlatName,
                    ToolDefinitionId = registration.Definition.Id.ToString(),
                    RuntimeBindingId = registration.Binding.Id.Value,
                    BindingRevision = registration.Binding.Revision,
                    SnapshotRevision = snapshotRevision,
                    McpGeneration = registration.Binding.Revision,
                    Source = ToSessionProvenance(registration.Definition),
                    Presentation = ToSessionPresentation(registration.Definition.Presentation),
                    McpAppResourceUri = GetMcpAppResourceUri(registration.Definition),
                    Server = registration.Definition.Id.SourceId,
                    Origin = registration.Definition.Provenance.Origin ?? "workspace",
                    SourceToolId = registration.Definition.Id.SourceToolId.Value,
                    CallId = callId,
                    Arguments = arguments.DeepClone().AsObject(),
                    Status = "inProgress"
                };
                break;
            case ToolProjectionShape.DynamicLifecycle:
                item.Type = ItemType.DynamicToolCall;
                item.Payload = new DynamicToolCallPayload
                {
                    Namespace = registration.Definition.Name.Namespace,
                    ToolName = registration.Definition.Name.Name,
                    ProviderFlatName = providerFlatName,
                    ToolDefinitionId = registration.Definition.Id.ToString(),
                    RuntimeBindingId = registration.Binding.Id.Value,
                    BindingRevision = registration.Binding.Revision,
                    SnapshotRevision = snapshotRevision,
                    Source = ToSessionProvenance(registration.Definition),
                    Presentation = ToSessionPresentation(registration.Definition.Presentation),
                    CallId = callId,
                    Arguments = arguments.DeepClone().AsObject(),
                    Status = "inProgress"
                };
                break;
            case ToolProjectionShape.StandardPair:
                item.Type = ItemType.ToolCall;
                item.Payload = new ToolCallPayload
                {
                    Namespace = registration.Definition.Name.Namespace,
                    ToolName = registration.Definition.Name.Name,
                    ProviderFlatName = providerFlatName,
                    ToolDefinitionId = registration.Definition.Id.ToString(),
                    RuntimeBindingId = registration.Binding.Id.Value,
                    BindingRevision = registration.Binding.Revision,
                    SnapshotRevision = snapshotRevision,
                    Source = ToSessionProvenance(registration.Definition),
                    Presentation = ToSessionPresentation(registration.Definition.Presentation),
                    Arguments = arguments.DeepClone().AsObject(),
                    CallId = callId
                };
                break;
            default:
                throw new InvalidOperationException($"Unknown tool projection shape '{registration.ProjectionShape}'.");
        }
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

    private static string? GetMcpAppResourceUri(ToolDefinition definition) =>
        McpAppMetadataParser.TryGetToolMetadata(definition, out var metadata)
            ? metadata.ResourceUri?.AbsoluteUri
            : null;

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
