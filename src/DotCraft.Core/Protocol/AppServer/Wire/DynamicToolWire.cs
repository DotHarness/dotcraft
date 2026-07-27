using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// A Runtime Dynamic Tool declaration supplied by an AppServer client.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RuntimeDynamicToolFunction), "function")]
[JsonDerivedType(typeof(RuntimeDynamicToolNamespace), "namespace")]
public abstract class RuntimeDynamicToolDeclaration
{
    /// <summary>Model-visible function name or namespace name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable description used for model planning.</summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// A single Runtime Dynamic Function. Top-level functions have no namespace.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RuntimeDynamicToolFunction : RuntimeDynamicToolDeclaration
{
    /// <summary>JSON Schema for the function arguments.</summary>
    public JsonObject? InputSchema { get; set; }

    /// <summary>Whether this function is discovered through its containing namespace.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DeferLoading { get; set; }

    /// <summary>Optional DotCraft approval hint evaluated by the server.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelToolApprovalDescriptor? Approval { get; set; }
}

/// <summary>
/// A named namespace containing Runtime Dynamic Functions.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RuntimeDynamicToolNamespace : RuntimeDynamicToolDeclaration
{
    /// <summary>Functions contributed under <see cref="RuntimeDynamicToolDeclaration.Name"/>.</summary>
    public List<RuntimeDynamicToolDeclaration> Tools { get; set; } = [];
}

public sealed class DynamicToolCallParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;

    public string CallId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Namespace { get; set; }

    public string Tool { get; set; } = string.Empty;

    public JsonObject Arguments { get; set; } = new();
}

/// <summary>
/// Result returned by an AppServer client for a Runtime Dynamic Tool invocation.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RuntimeDynamicToolCallResult
{
    public bool Success { get; set; }

    public List<RuntimeDynamicToolContentItem>? ContentItems { get; set; }

    public JsonNode? StructuredContent { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }
}

/// <summary>
/// A validated text or image content item returned by a Runtime Dynamic Tool.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RuntimeDynamicToolContentItem
{
    public string Type { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MediaType { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DataBase64 { get; set; }
}

/// <summary>
/// Params for <c>ui/resource/read</c> (host → server) and the brokered <c>item/resource/read</c>
/// (server → app). Reads a <c>ui://</c> Interactive Tool UI resource. See
/// <c>specs/protocols/appserver-protocol.md</c> §11.3.1.
/// </summary>
public sealed class UiResourceReadParams
{
    public string ThreadId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Namespace { get; set; }

    public string Uri { get; set; } = string.Empty;
}

/// <summary>
/// Result of a <c>ui://</c> resource read, aligned to MCP <c>resources/read</c>.
/// </summary>
public sealed class UiResourceReadResult
{
    public List<UiResourceContent> Contents { get; set; } = [];
}

/// <summary>
/// A single resource entry returned by a <c>ui://</c> resource read.
/// </summary>
public sealed class UiResourceContent
{
    public string Uri { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }
}

/// <summary>
/// Params for <c>ui/tool/call</c> (host → server): a UI-initiated app-tool invocation
/// (private <c>callTool</c>). Decoupled from the conversation — gated, brokered, audited, with
/// the result returned to the host/UI. See appserver-protocol.md §11.3.2.
/// </summary>
public sealed class UiToolCallParams
{
    public string ThreadId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Namespace { get; set; }

    public string Tool { get; set; } = string.Empty;

    public JsonObject Arguments { get; set; } = new();

    /// <summary>The <c>callId</c> of the <c>dynamicToolCall</c> whose UI initiated this call (provenance).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceCallId { get; set; }
}

/// <summary>Mutating‑tool approval info passed to a <see cref="UiToolApprovalGate"/> (M‑v).</summary>
public sealed record UiToolApprovalInfo(string ApprovalType, string Operation, string Target);

/// <summary>
/// Host‑provided gate that prompts the user to approve a mutating UI‑initiated tool call and returns
/// whether it was approved. Decoupled from the conversation (no turn/item). M‑v.
/// </summary>
public delegate System.Threading.Tasks.ValueTask<bool> UiToolApprovalGate(
    UiToolApprovalInfo info,
    System.Threading.CancellationToken ct);

/// <summary>
/// Params for <c>ui/tool/approval/request</c> (server → host, M‑v): prompt the user to approve a
/// mutating UI‑initiated tool call. The host surfaces it in the shared approval composer and replies
/// with an <see cref="AppServerApprovalResponseResult"/> decision. Decoupled — no turn/item.
/// </summary>
public sealed class UiToolApprovalRequestParams
{
    public string ThreadId { get; set; } = string.Empty;

    /// <summary>Correlates this approval; opaque to the host.</summary>
    public string ApprovalId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Namespace { get; set; }

    public string Tool { get; set; } = string.Empty;

    /// <summary>Approval category (<c>file</c> / <c>shell</c> / <c>remoteResource</c>).</summary>
    public string ApprovalType { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Operation { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Target { get; set; }
}

/// <summary>
/// Params for <c>ui/open-link</c> (host → server): a UI‑initiated request to open an external
/// link. The host enforces the scheme policy (<c>https:</c>/<c>mailto:</c>, plus the bound app's
/// declared <c>nativeApplication.protocol</c> deep‑link scheme — M‑v) and records the open on the
/// App Binding audit trail; the actual navigation happens in the Desktop host. Decoupled — no
/// turn/item. See tools-architecture.md §13 and desktop-client.md §5.8.2.
/// </summary>
public sealed class UiOpenLinkParams
{
    public string ThreadId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Namespace { get; set; }

    public string Url { get; set; } = string.Empty;

    /// <summary>The <c>callId</c> of the <c>dynamicToolCall</c> whose UI initiated this open (provenance).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceCallId { get; set; }
}

/// <summary>Result of <c>ui/open-link</c>: the validated URL the host is cleared to open.</summary>
public sealed class UiOpenLinkResult
{
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// Params for <c>ui/update-model-context</c> (host → server, M‑iii): the UI pushes UI‑derived state
/// for the model's next turn. Recorded as an App Binding context block keyed to the originating
/// <c>dynamicToolCall</c> item (last‑write‑wins); empty/absent <see cref="Content"/> clears it.
/// Decoupled — no turn/item. See tools-architecture.md §13 and desktop-client.md §5.8.2.
/// </summary>
public sealed class UiUpdateModelContextParams
{
    public string ThreadId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Namespace { get; set; }

    /// <summary>The originating <c>dynamicToolCall</c> <c>callId</c>; the context block is keyed to it.</summary>
    public string SourceCallId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    /// <summary>Model‑visible text. Empty/absent removes the block (last‑write‑wins clear).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; set; }
}

/// <summary>Result of <c>ui/update-model-context</c>.</summary>
public sealed class UiUpdateModelContextResult
{
    public string BlockId { get; set; } = string.Empty;

    /// <summary>True when the block was removed (empty content) rather than upserted.</summary>
    public bool Cleared { get; set; }
}
