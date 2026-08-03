using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Protocol.Contracts.AppServer;

/// <summary>Metadata for one local image attached to a user message.</summary>
public sealed class UserMessageImage : ExtensibleJsonObject
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; init; }

    [JsonPropertyName("fileName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileName { get; init; }
}

/// <summary>Canonical payload for a user message item.</summary>
public sealed class UserMessagePayload : ExtensibleJsonObject
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("deliveryMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeliveryMode { get; init; }

    [JsonPropertyName("nativeInputParts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<InputPart>? NativeInputParts { get; init; }

    [JsonPropertyName("materializedInputParts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<InputPart>? MaterializedInputParts { get; init; }

    [JsonPropertyName("senderId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SenderId { get; init; }

    [JsonPropertyName("senderName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SenderName { get; init; }

    [JsonPropertyName("senderRole")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SenderRole { get; init; }

    [JsonPropertyName("channelName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChannelName { get; init; }

    [JsonPropertyName("channelContext")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChannelContext { get; init; }

    [JsonPropertyName("groupId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GroupId { get; init; }

    [JsonPropertyName("images")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<UserMessageImage>? Images { get; init; }

    [JsonPropertyName("triggerKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TriggerKind { get; init; }

    [JsonPropertyName("triggerLabel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TriggerLabel { get; init; }

    [JsonPropertyName("triggerRefId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TriggerRefId { get; init; }

    [JsonPropertyName("queuedInputId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? QueuedInputId { get; init; }

    [JsonPropertyName("deliveryBindingId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeliveryBindingId { get; init; }

    [JsonPropertyName("sentAsGoal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SentAsGoal { get; init; }
}

/// <summary>Canonical payload for a completed agent message item.</summary>
public sealed class AgentMessagePayload : ExtensibleJsonObject
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

/// <summary>Canonical payload for a reasoning content item.</summary>
public sealed class ReasoningContentPayload : ExtensibleJsonObject
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

/// <summary>Canonical payload for a command execution item.</summary>
public sealed class CommandExecutionPayload : ExtensibleJsonObject
{
    [JsonPropertyName("command")]
    public required string Command { get; init; }

    [JsonPropertyName("workingDirectory")]
    public required string WorkingDirectory { get; init; }

    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("aggregatedOutput")]
    public required string AggregatedOutput { get; init; }

    [JsonPropertyName("sessionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SessionId { get; init; }

    [JsonPropertyName("outputPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputPath { get; init; }

    [JsonPropertyName("originalOutputChars")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? OriginalOutputChars { get; init; }

    [JsonPropertyName("truncated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Truncated { get; init; }

    [JsonPropertyName("backgroundReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BackgroundReason { get; init; }

    [JsonPropertyName("exitCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ExitCode { get; init; }

    [JsonPropertyName("durationMs")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? DurationMs { get; init; }

    [JsonPropertyName("callId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CallId { get; init; }
}

/// <summary>Canonical payload for a tool execution lifecycle item.</summary>
public sealed class ToolExecutionPayload : ExtensibleJsonObject
{
    [JsonPropertyName("callId")]
    public required string CallId { get; init; }

    [JsonPropertyName("toolName")]
    public required string ToolName { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("success")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Success { get; init; }

    [JsonPropertyName("durationMs")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? DurationMs { get; init; }

    [JsonPropertyName("resultPreview")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResultPreview { get; init; }

    [JsonPropertyName("errorMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; init; }
}

/// <summary>Canonical payload for a hosted image generation lifecycle item.</summary>
public sealed class ImageGenerationPayload : ExtensibleJsonObject
{
    [JsonPropertyName("callId")]
    public required string CallId { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("revisedPrompt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RevisedPrompt { get; init; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Result { get; init; }

    [JsonPropertyName("mediaType")]
    public required string MediaType { get; init; }

    [JsonPropertyName("savedPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SavedPath { get; init; }

    [JsonPropertyName("errorMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; init; }
}

/// <summary>Safe persisted provenance for a tool definition.</summary>
public sealed class ToolSourceProvenancePayload : ExtensibleJsonObject
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("sourceId")]
    public required string SourceId { get; init; }

    [JsonPropertyName("origin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Origin { get; init; }

    [JsonPropertyName("sourceToolId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceToolId { get; init; }

    [JsonPropertyName("pluginId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PluginId { get; init; }

    [JsonPropertyName("functionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FunctionId { get; init; }
}

/// <summary>Trusted local presentation selection captured for a tool call.</summary>
public sealed class ToolPresentationPayload : ExtensibleJsonObject
{
    [JsonPropertyName("presentationId")]
    public required string PresentationId { get; init; }

    [JsonPropertyName("options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Options { get; init; }
}

/// <summary>Canonical payload for an ordinary tool call item.</summary>
public sealed class ToolCallPayload : ExtensibleJsonObject
{
    [JsonPropertyName("namespace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Namespace { get; init; }

    [JsonPropertyName("toolName")]
    public required string ToolName { get; init; }

    [JsonPropertyName("providerFlatName")]
    public required string ProviderFlatName { get; init; }

    [JsonPropertyName("toolDefinitionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolDefinitionId { get; init; }

    [JsonPropertyName("runtimeBindingId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RuntimeBindingId { get; init; }

    [JsonPropertyName("bindingRevision")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? BindingRevision { get; init; }

    [JsonPropertyName("snapshotRevision")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? SnapshotRevision { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolSourceProvenancePayload? Source { get; init; }

    [JsonPropertyName("presentation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolPresentationPayload? Presentation { get; init; }

    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Arguments { get; init; }

    [JsonPropertyName("callId")]
    public required string CallId { get; init; }
}

/// <summary>Canonical payload for a runtime dynamic tool call item.</summary>
public sealed class DynamicToolCallPayload : ExtensibleJsonObject
{
    [JsonPropertyName("namespace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Namespace { get; init; }

    [JsonPropertyName("toolName")]
    public required string ToolName { get; init; }

    [JsonPropertyName("providerFlatName")]
    public required string ProviderFlatName { get; init; }

    [JsonPropertyName("toolDefinitionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolDefinitionId { get; init; }

    [JsonPropertyName("runtimeBindingId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RuntimeBindingId { get; init; }

    [JsonPropertyName("bindingRevision")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? BindingRevision { get; init; }

    [JsonPropertyName("snapshotRevision")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? SnapshotRevision { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolSourceProvenancePayload? Source { get; init; }

    [JsonPropertyName("presentation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolPresentationPayload? Presentation { get; init; }

    [JsonPropertyName("callId")]
    public required string CallId { get; init; }

    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Arguments { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("durationMs")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? DurationMs { get; init; }

    [JsonPropertyName("contentItems")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DynamicToolContentItem>? ContentItems { get; init; }

    [JsonPropertyName("structuredContent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? StructuredContent { get; init; }

    [JsonPropertyName("success")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Success { get; init; }

    [JsonPropertyName("errorCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("errorMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; init; }
}

/// <summary>Canonical payload for an MCP tool call lifecycle item.</summary>
public sealed class McpToolCallPayload : ExtensibleJsonObject
{
    [JsonPropertyName("namespace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Namespace { get; init; }

    [JsonPropertyName("toolName")]
    public required string ToolName { get; init; }

    [JsonPropertyName("providerFlatName")]
    public required string ProviderFlatName { get; init; }

    [JsonPropertyName("toolDefinitionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolDefinitionId { get; init; }

    [JsonPropertyName("runtimeBindingId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RuntimeBindingId { get; init; }

    [JsonPropertyName("bindingRevision")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? BindingRevision { get; init; }

    [JsonPropertyName("snapshotRevision")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? SnapshotRevision { get; init; }

    [JsonPropertyName("mcpGeneration")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? McpGeneration { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolSourceProvenancePayload? Source { get; init; }

    [JsonPropertyName("presentation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolPresentationPayload? Presentation { get; init; }

    [JsonPropertyName("server")]
    public required string Server { get; init; }

    [JsonPropertyName("origin")]
    public required string Origin { get; init; }

    [JsonPropertyName("sourceToolId")]
    public required string SourceToolId { get; init; }

    [JsonPropertyName("callId")]
    public required string CallId { get; init; }

    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Arguments { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("durationMs")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? DurationMs { get; init; }

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Content { get; init; }

    [JsonPropertyName("modelContentItems")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DynamicToolContentItem>? ModelContentItems { get; init; }

    [JsonPropertyName("structuredContent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? StructuredContent { get; init; }

    [JsonPropertyName("_meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Meta { get; init; }

    [JsonPropertyName("mcpAppResourceUri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? McpAppResourceUri { get; init; }

    [JsonPropertyName("isError")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsError { get; init; }

    [JsonPropertyName("success")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Success { get; init; }

    [JsonPropertyName("errorCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("errorMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; init; }
}

/// <summary>Canonical payload for a completed tool result item.</summary>
public sealed class ToolResultPayload : ExtensibleJsonObject
{
    [JsonPropertyName("callId")]
    public required string CallId { get; init; }

    [JsonPropertyName("namespace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Namespace { get; init; }

    [JsonPropertyName("toolName")]
    public required string ToolName { get; init; }

    [JsonPropertyName("providerFlatName")]
    public required string ProviderFlatName { get; init; }

    [JsonPropertyName("toolDefinitionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolDefinitionId { get; init; }

    [JsonPropertyName("runtimeBindingId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RuntimeBindingId { get; init; }

    [JsonPropertyName("bindingRevision")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? BindingRevision { get; init; }

    [JsonPropertyName("snapshotRevision")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? SnapshotRevision { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolSourceProvenancePayload? Source { get; init; }

    [JsonPropertyName("presentation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolPresentationPayload? Presentation { get; init; }

    [JsonPropertyName("durationMs")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? DurationMs { get; init; }

    [JsonPropertyName("result")]
    public required string Result { get; init; }

    [JsonPropertyName("contentItems")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DynamicToolContentItem>? ContentItems { get; init; }

    [JsonPropertyName("structuredContent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? StructuredContent { get; init; }

    [JsonPropertyName("_meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Meta { get; init; }

    [JsonPropertyName("errorCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("errorMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; init; }

    [JsonPropertyName("success")]
    public required bool Success { get; init; }
}

/// <summary>Canonical payload for an approval request item.</summary>
public sealed class ApprovalRequestPayload : ExtensibleJsonObject
{
    [JsonPropertyName("approvalType")]
    public required string ApprovalType { get; init; }

    [JsonPropertyName("operation")]
    public required string Operation { get; init; }

    [JsonPropertyName("target")]
    public required string Target { get; init; }

    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    [JsonPropertyName("scopeKey")]
    public required string ScopeKey { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("expiresAt")]
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>Canonical payload for an approval response item.</summary>
public sealed class ApprovalResponsePayload : ExtensibleJsonObject
{
    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    [JsonPropertyName("approved")]
    public required bool Approved { get; init; }

    [JsonPropertyName("decision")]
    public required string Decision { get; init; }
}

/// <summary>Canonical payload for a user-input request item.</summary>
public sealed class UserInputRequestPayload : ExtensibleJsonObject
{
    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    [JsonPropertyName("questions")]
    public required IReadOnlyList<UserInputQuestion> Questions { get; init; }
}

/// <summary>Canonical payload for a user-input response item.</summary>
public sealed class UserInputResponsePayload : ExtensibleJsonObject
{
    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    [JsonPropertyName("response")]
    public required UserInputResponseResult Response { get; init; }
}

/// <summary>Canonical payload for an error item.</summary>
public sealed class ErrorPayload : ExtensibleJsonObject
{
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("fatal")]
    public required bool Fatal { get; init; }
}

/// <summary>Canonical payload for a persistent system notice item.</summary>
public sealed class SystemNoticePayload : ExtensibleJsonObject
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("trigger")]
    public required string Trigger { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("tokensBefore")]
    [JsonSafeInteger]
    public required long TokensBefore { get; init; }

    [JsonPropertyName("tokensAfter")]
    [JsonSafeInteger]
    public required long TokensAfter { get; init; }

    [JsonPropertyName("percentLeftAfter")]
    public required double PercentLeftAfter { get; init; }

    [JsonPropertyName("clearedToolResults")]
    public required int ClearedToolResults { get; init; }

    [JsonPropertyName("sourceThreadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceThreadId { get; init; }
}
