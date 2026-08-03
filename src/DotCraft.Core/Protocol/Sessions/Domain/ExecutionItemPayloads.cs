using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.Plugins;

namespace DotCraft.Sessions;

/// <summary>
/// Payload for CommandExecution items.
/// </summary>
public sealed record CommandExecutionPayload
{
    public string Command { get; init; } = string.Empty;

    public string WorkingDirectory { get; init; } = string.Empty;

    /// <summary>
    /// "host" or "sandbox".
    /// </summary>
    public string Source { get; init; } = "host";

    /// <summary>
    /// "inProgress", "completed", "failed", "cancelled", "backgrounded", "killed", or "lost".
    /// </summary>
    public string Status { get; init; } = "inProgress";

    public string AggregatedOutput { get; init; } = string.Empty;

    public string? SessionId { get; init; }

    public string? OutputPath { get; init; }

    public int? OriginalOutputChars { get; init; }

    public bool? Truncated { get; init; }

    public string? BackgroundReason { get; init; }

    public int? ExitCode { get; init; }

    public long? DurationMs { get; init; }

    /// <summary>
    /// Matches the related ToolCall/ToolResult call id when available.
    /// </summary>
    public string? CallId { get; init; }
}
/// <summary>
/// Delta payload emitted while a command execution is producing output.
/// </summary>
public sealed record CommandExecutionOutputDelta
{
    public string DeltaKind { get; init; } = "commandExecution";

    public string TextDelta { get; init; } = string.Empty;

    /// <summary>
    /// Internal delivery hint for output already published through terminal/outputDelta.
    /// This is never persisted or exposed on the AppServer wire.
    /// </summary>
    [JsonIgnore]
    public bool MirrorsTerminalOutput { get; init; }
}

/// <summary>
/// Payload for ToolExecution runtime lifecycle items.
/// </summary>
public sealed record ToolExecutionPayload
{
    /// <summary>
    /// Matches the related ToolCall/ToolResult call id.
    /// </summary>
    public string CallId { get; init; } = string.Empty;

    public string ToolName { get; init; } = string.Empty;

    /// <summary>
    /// "inProgress", "completed", "failed", or "cancelled".
    /// </summary>
    public string Status { get; init; } = "inProgress";

    public bool? Success { get; init; }

    public long? DurationMs { get; init; }

    public string? ResultPreview { get; init; }

    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Payload for hosted image generation lifecycle items.
/// </summary>
public sealed record ImageGenerationPayload
{
    /// <summary>
    /// Provider image generation call id.
    /// </summary>
    public string CallId { get; init; } = string.Empty;

    /// <summary>
    /// "inProgress", "completed", or "failed".
    /// </summary>
    public string Status { get; init; } = "inProgress";

    public string? RevisedPrompt { get; init; }

    /// <summary>
    /// Base64-encoded image bytes when generation completed successfully.
    /// </summary>
    public string? Result { get; init; }

    public string MediaType { get; init; } = "image/png";

    public string? SavedPath { get; init; }

    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Delta payload emitted while tool-call arguments are still streaming.
/// </summary>
public sealed record ToolCallArgumentsDelta
{
    public string DeltaKind { get; init; } = "toolCallArguments";

    /// <summary>
    /// Tool name (typically present on the first chunk).
    /// </summary>
    public string? ToolName { get; init; }

    /// <summary>
    /// Tool call id (typically present on the first chunk).
    /// </summary>
    public string? CallId { get; init; }

    /// <summary>
    /// Raw JSON fragment emitted by the model for the tool-call arguments.
    /// </summary>
    public string Delta { get; init; } = string.Empty;
}

/// <summary>
/// Payload for ToolCall items.
/// </summary>
public sealed record ToolCallPayload
{
    /// <summary>
    /// Canonical tool namespace. Null denotes a top-level function.
    /// </summary>
    public string? Namespace { get; init; }

    public string ToolName { get; init; } = string.Empty;

    /// <summary>
    /// Stable flat alias used by providers that cannot represent a namespace and local name
    /// separately.
    /// </summary>
    [JsonRequired]
    public string ProviderFlatName { get; init; } = string.Empty;

    /// <summary>
    /// Stable source-qualified definition identity when known.
    /// </summary>
    public string? ToolDefinitionId { get; init; }

    /// <summary>Live runtime binding identity used for this invocation.</summary>
    public string? RuntimeBindingId { get; init; }

    /// <summary>Source binding revision used for this invocation.</summary>
    public long? BindingRevision { get; init; }

    /// <summary>Frozen effective tool snapshot revision used for this invocation.</summary>
    public long? SnapshotRevision { get; init; }

    /// <summary>
    /// Safe source provenance used for diagnostics and trusted presentation selection.
    /// </summary>
    public ToolSourceProvenancePayload? Source { get; init; }

    /// <summary>Trusted local presentation descriptor captured at invocation time.</summary>
    public ToolPresentationPayload? Presentation { get; init; }

    /// <summary>
    /// Tool arguments as a JSON object.
    /// </summary>
    public JsonObject? Arguments { get; init; }

    /// <summary>
    /// Correlation ID linking this ToolCall to its ToolResult.
    /// </summary>
    public string CallId { get; init; } = string.Empty;
}

/// <summary>
/// Payload for Runtime Dynamic Tool call items.
/// </summary>
public sealed record DynamicToolCallPayload
{
    public string? Namespace { get; init; }

    public string ToolName { get; init; } = string.Empty;

    [JsonRequired]
    public string ProviderFlatName { get; init; } = string.Empty;

    /// <summary>Stable source-qualified definition identity when known.</summary>
    public string? ToolDefinitionId { get; init; }

    /// <summary>Live runtime binding identity used for this invocation.</summary>
    public string? RuntimeBindingId { get; init; }

    /// <summary>Source binding revision used for this invocation.</summary>
    public long? BindingRevision { get; init; }

    /// <summary>Frozen effective tool snapshot revision used for this invocation.</summary>
    public long? SnapshotRevision { get; init; }

    /// <summary>Safe source provenance captured at invocation time.</summary>
    public ToolSourceProvenancePayload? Source { get; init; }

    /// <summary>Trusted local presentation descriptor captured at invocation time.</summary>
    public ToolPresentationPayload? Presentation { get; init; }

    public string CallId { get; init; } = string.Empty;

    public JsonObject? Arguments { get; init; }

    public string Status { get; init; } = "inProgress";

    public long? DurationMs { get; init; }

    public IReadOnlyList<PluginFunctionContentItem>? ContentItems { get; init; }

    public JsonNode? StructuredContent { get; init; }

    public bool? Success { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

}

/// <summary>
/// Payload for an MCP tool call. MCP calls use one lifecycle item so the raw MCP result can be
/// retained for clients while a separate normalized fallback is used for model history.
/// </summary>
public sealed record McpToolCallPayload
{
    public string? Namespace { get; init; }

    public string ToolName { get; init; } = string.Empty;

    [JsonRequired]
    public string ProviderFlatName { get; init; } = string.Empty;

    /// <summary>Stable source-qualified definition identity used for this invocation.</summary>
    public string? ToolDefinitionId { get; init; }

    /// <summary>Live runtime binding identity used for this invocation.</summary>
    public string? RuntimeBindingId { get; init; }

    /// <summary>Source binding revision used for this invocation.</summary>
    public long? BindingRevision { get; init; }

    /// <summary>Frozen effective tool snapshot revision used for this invocation.</summary>
    public long? SnapshotRevision { get; init; }

    /// <summary>MCP server session generation used for this invocation.</summary>
    public long? McpGeneration { get; init; }

    /// <summary>Safe source provenance captured at invocation time.</summary>
    public ToolSourceProvenancePayload? Source { get; init; }

    /// <summary>Trusted local presentation descriptor captured at invocation time.</summary>
    public ToolPresentationPayload? Presentation { get; init; }

    public string Server { get; init; } = string.Empty;

    public string Origin { get; init; } = "workspace";

    public string SourceToolId { get; init; } = string.Empty;

    public string CallId { get; init; } = string.Empty;

    public JsonObject? Arguments { get; init; }

    /// <summary>"inProgress", "completed", or "failed".</summary>
    public string Status { get; init; } = "inProgress";

    public long? DurationMs { get; init; }

    /// <summary>Exact MCP content blocks for clients. They are not replayed directly to the model.</summary>
    public JsonArray? Content { get; init; }

    /// <summary>Normalized model-visible content used for provider history reconstruction.</summary>
    public IReadOnlyList<PluginFunctionContentItem>? ModelContentItems { get; init; }

    public JsonNode? StructuredContent { get; init; }

    [JsonPropertyName("_meta")]
    public JsonNode? Meta { get; init; }

    /// <summary>Normalized MCP Apps UI resource associated with the invoked tool definition.</summary>
    public string? McpAppResourceUri { get; init; }

    public bool? IsError { get; init; }

    public bool? Success { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Payload for ToolResult items.
/// </summary>
public sealed record ToolResultPayload
{
    /// <summary>
    /// Matches the ToolCall.CallId.
    /// </summary>
    public string CallId { get; init; } = string.Empty;

    public string? Namespace { get; init; }

    public string ToolName { get; init; } = string.Empty;

    [JsonRequired]
    public string ProviderFlatName { get; init; } = string.Empty;

    public string? ToolDefinitionId { get; init; }

    public string? RuntimeBindingId { get; init; }

    public long? BindingRevision { get; init; }

    public long? SnapshotRevision { get; init; }

    public ToolSourceProvenancePayload? Source { get; init; }

    public ToolPresentationPayload? Presentation { get; init; }

    public long? DurationMs { get; init; }

    /// <summary>
    /// Text fallback for the tool result. Used for model history reconstruction.
    /// </summary>
    public string Result { get; init; } = string.Empty;

    /// <summary>
    /// Optional rich content for client presentation, such as image outputs.
    /// </summary>
    public IReadOnlyList<PluginFunctionContentItem>? ContentItems { get; init; }

    /// <summary>Client-only structured data. Never inserted into model history.</summary>
    public JsonNode? StructuredContent { get; init; }

    /// <summary>Host-only metadata. Never inserted into model history.</summary>
    [JsonPropertyName("_meta")]
    public JsonNode? Meta { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public bool Success { get; init; }
}

/// <summary>
/// Safe, persisted provenance for a tool definition. This payload never contains credentials or
/// live connection state.
/// </summary>
public sealed record ToolSourceProvenancePayload
{
    public string Kind { get; init; } = string.Empty;

    public string SourceId { get; init; } = string.Empty;

    /// <summary>Gets the safe origin such as workspace, plugin, thread, or binding.</summary>
    public string? Origin { get; init; }

    public string? SourceToolId { get; init; }

    public string? PluginId { get; init; }

    public string? FunctionId { get; init; }
}

/// <summary>Safe, persisted trusted local presentation selection.</summary>
public sealed record ToolPresentationPayload
{
    /// <summary>Gets the trusted presentation registration identifier.</summary>
    public string PresentationId { get; init; } = string.Empty;

    /// <summary>Gets bounded renderer-specific options captured from the tool definition.</summary>
    public JsonObject? Options { get; init; }
}
