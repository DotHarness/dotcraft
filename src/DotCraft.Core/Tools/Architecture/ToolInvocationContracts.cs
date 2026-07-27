using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>Identifies the trusted host surface that initiated a direct tool invocation.</summary>
public sealed record ToolInvocationOrigin
{
    /// <summary>Creates an invocation origin.</summary>
    /// <param name="kind">A stable origin kind such as <c>mcpApp</c>.</param>
    /// <param name="sourceItemId">An optional safe Session item correlation identifier.</param>
    public ToolInvocationOrigin(string kind, string? sourceItemId = null)
    {
        if (string.IsNullOrWhiteSpace(kind))
            throw new ArgumentException("An invocation origin kind is required.", nameof(kind));
        Kind = kind;
        SourceItemId = sourceItemId;
    }

    /// <summary>Gets the stable origin kind.</summary>
    public string Kind { get; }

    /// <summary>Gets the optional safe Session item correlation identifier.</summary>
    public string? SourceItemId { get; }
}

/// <summary>Caller metadata supplied before the dispatcher resolves a tool.</summary>
public sealed record ToolInvocationRequest(
    string ThreadId,
    string? TurnId,
    string CallId,
    ToolInvocationAudience Audience,
    ToolInvocationOrigin? Origin = null,
    string? WorkspacePath = null);

/// <summary>Resolved immutable invocation metadata supplied to a tool runtime.</summary>
public sealed record ToolInvocationContext(
    string ThreadId,
    string? TurnId,
    string CallId,
    ToolInvocationAudience Audience,
    ToolName ToolName,
    ToolDefinitionId DefinitionId,
    RuntimeBindingId RuntimeBindingId,
    long SnapshotRevision,
    DateTimeOffset StartedAt,
    ToolInvocationOrigin? Origin = null,
    string? WorkspacePath = null);

/// <summary>A stable source-neutral tool execution error.</summary>
public sealed class ToolError
{
    /// <summary>Creates an error with an English fallback message.</summary>
    public ToolError(
        string code,
        string message,
        IReadOnlyDictionary<string, JsonElement>? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("A stable error code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("An English fallback error message is required.", nameof(message));
        Code = code;
        Message = message;
        Parameters = JsonCollections.Clone(parameters);
    }

    /// <summary>Gets the stable machine-readable error code.</summary>
    public string Code { get; }
    /// <summary>Gets the English fallback error message.</summary>
    public string Message { get; }
    /// <summary>Gets immutable structured error parameters.</summary>
    public IReadOnlyDictionary<string, JsonElement> Parameters { get; }
}

/// <summary>Stable error codes produced by the common dispatcher.</summary>
public static class ToolErrorCodes
{
    /// <summary>No matching registration exists in the snapshot.</summary>
    public const string NotFound = "tool_not_found";
    /// <summary>The runtime is absent, disconnected, revoked, or otherwise unavailable.</summary>
    public const string Unavailable = "tool_unavailable";
    /// <summary>The requested invocation audience is not authorized.</summary>
    public const string Unauthorized = "tool_unauthorized";
    /// <summary>The invocation arguments do not satisfy the definition schema.</summary>
    public const string InputInvalid = "tool_input_invalid";
    /// <summary>The required approval was declined or cancelled.</summary>
    public const string ApprovalRejected = "tool_approval_rejected";
    /// <summary>A server-authoritative workspace or blacklist guard denied the operation.</summary>
    public const string AccessDenied = "tool_access_denied";
    /// <summary>The invocation exceeded its configured deadline.</summary>
    public const string Timeout = "tool_timeout";
    /// <summary>The runtime failed while executing the tool.</summary>
    public const string ExecutionFailed = "tool_execution_failed";
    /// <summary>The caller cancelled the invocation.</summary>
    public const string Cancelled = "tool_cancelled";
    /// <summary>The runtime returned a result that violates the common contract.</summary>
    public const string ResultInvalid = "tool_result_invalid";
    /// <summary>A Runtime Dynamic client disconnected before completing the call.</summary>
    public const string DynamicDisconnected = "dynamic_tool_disconnected";
    /// <summary>A Runtime Dynamic client returned an invalid protocol response.</summary>
    public const string DynamicProtocolError = "dynamic_tool_protocol_error";
    /// <summary>An MCP server requires authentication or reauthentication.</summary>
    public const string McpReauthenticationRequired = "mcp_reauthentication_required";
    /// <summary>An MCP server returned an invalid protocol response.</summary>
    public const string McpProtocolError = "mcp_protocol_error";
}

/// <summary>A source-neutral result with explicit model, client, and host audiences.</summary>
public sealed class ToolExecutionResult
{
    /// <summary>Creates an execution result.</summary>
    public ToolExecutionResult(
        bool success,
        string? content,
        JsonElement? structuredContent = null,
        JsonElement? meta = null,
        JsonElement? rawSourceResult = null,
        ToolError? error = null,
        object? providerResult = null,
        IReadOnlyList<AIContent>? contentItems = null)
    {
        Success = success;
        Content = content;
        StructuredContent = structuredContent?.Clone();
        Meta = meta?.Clone();
        RawSourceResult = rawSourceResult?.Clone();
        Error = error;
        ProviderResult = providerResult;
        ContentItems = contentItems is { Count: > 0 } ? contentItems.ToArray() : null;
    }

    /// <summary>Gets whether execution succeeded.</summary>
    public bool Success { get; }
    /// <summary>Gets model-visible text content.</summary>
    public string? Content { get; }
    /// <summary>Gets client-only structured content.</summary>
    public JsonElement? StructuredContent { get; }
    /// <summary>Gets host-private metadata.</summary>
    public JsonElement? Meta { get; }
    /// <summary>Gets an optional raw source result retained for specialized projection.</summary>
    public JsonElement? RawSourceResult { get; }
    /// <summary>Gets the stable error when execution failed.</summary>
    public ToolError? Error { get; }
    /// <summary>Gets an optional transient provider-native result. It is never persisted or exposed to clients.</summary>
    public object? ProviderResult { get; }
    /// <summary>Gets optional model-safe rich content preserved for model history and client projection.</summary>
    public IReadOnlyList<AIContent>? ContentItems { get; }

    /// <summary>Creates a successful result.</summary>
    public static ToolExecutionResult Succeeded(
        string? content,
        JsonElement? structuredContent = null,
        JsonElement? meta = null,
        JsonElement? rawSourceResult = null,
        object? providerResult = null,
        IReadOnlyList<AIContent>? contentItems = null) =>
        new(true, content, structuredContent, meta, rawSourceResult, providerResult: providerResult, contentItems: contentItems);

    /// <summary>Creates a failed result.</summary>
    public static ToolExecutionResult Failed(
        ToolError error,
        string? content = null,
        IReadOnlyList<AIContent>? contentItems = null) =>
        new(
            false,
            content,
            error: error ?? throw new ArgumentNullException(nameof(error)),
            contentItems: contentItems);
}

/// <summary>The outcome of checking a binding's current live lease.</summary>
internal static class JsonCollections
{
    public static IReadOnlyDictionary<string, JsonElement> Clone(
        IReadOnlyDictionary<string, JsonElement>? source) =>
        source is null || source.Count == 0
            ? FrozenDictionary<string, JsonElement>.Empty
            : source.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
}
