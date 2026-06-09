using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Cron;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol.AppServer;

// ───── Inbound message (parsed from wire) ─────

/// <summary>
/// Represents any incoming JSON-RPC 2.0 message from a wire client.
/// The handler discriminates by the presence/absence of <see cref="Method"/> and <see cref="Id"/>.
/// </summary>
public sealed class AppServerIncomingMessage
{
    [JsonPropertyName("jsonrpc")]
    public string? JsonRpc { get; set; }

    [JsonPropertyName("id")]
    public JsonElement? Id { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; }

    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }

    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }

    [JsonPropertyName("error")]
    public JsonElement? Error { get; set; }

    /// <summary>Incoming message is a response to a server-initiated request (e.g. approval).</summary>
    public bool IsResponse => Method == null && Id.HasValue && Id.Value.ValueKind != JsonValueKind.Undefined;

    /// <summary>Incoming message is a notification (has method, no id).</summary>
    public bool IsNotification => Method != null && (!Id.HasValue || Id.Value.ValueKind == JsonValueKind.Null || Id.Value.ValueKind == JsonValueKind.Undefined);

    /// <summary>Incoming message is a request (has method and id).</summary>
    public bool IsRequest => Method != null && Id.HasValue && Id.Value.ValueKind != JsonValueKind.Null && Id.Value.ValueKind != JsonValueKind.Undefined;
}

// ───── initialize ─────

public sealed class AppServerInitializeParams
{
    public AppServerClientInfo ClientInfo { get; set; } = new();

    public AppServerClientCapabilities? Capabilities { get; set; }
}

public sealed class AppServerClientInfo
{
    public string Name { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string Version { get; set; } = string.Empty;
}

public sealed class AppServerClientCapabilities
{
    /// <summary>Whether the client can handle server-initiated approval requests. Default true.</summary>
    public bool? ApprovalSupport { get; set; }

    /// <summary>Whether the client can handle server-initiated model question requests. Default false.</summary>
    public bool? RequestUserInputSupport { get; set; }

    /// <summary>Whether the client can consume streaming delta notifications. Default true.</summary>
    public bool? StreamingSupport { get; set; }

    /// <summary>
    /// Whether the client can consume commandExecution items and item/commandExecution/outputDelta.
    /// Default false to preserve legacy toolCall/toolResult-based clients.
    /// </summary>
    public bool? CommandExecutionStreaming { get; set; }

    /// <summary>
    /// Whether the client can consume toolExecution lifecycle items.
    /// Default false to preserve legacy toolCall/toolResult-based clients.
    /// </summary>
    public bool? ToolExecutionLifecycle { get; set; }

    /// <summary>
    /// Whether the client can consume background terminal management notifications.
    /// </summary>
    public bool? BackgroundTerminals { get; set; }

    /// <summary>Exact notification method names to suppress for this connection.</summary>
    public List<string>? OptOutNotificationMethods { get; set; }

    /// <summary>
    /// Whether the client wants to receive <c>workspace/configChanged</c> notifications.
    /// Default true when omitted.
    /// </summary>
    public bool? ConfigChange { get; set; }

    /// <summary>
    /// Channel adapter capability (external-channel-adapter.md §5.1).
    /// Null for regular clients (CLI, VS Code, etc.).
    /// When present, identifies this connection as an external channel adapter.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelAdapterCapability? ChannelAdapter { get; set; }

    /// <summary>
    /// ACP tool proxy capabilities (appserver-protocol.md §3.2, §11.2).
    /// When set, the client can receive server-initiated <c>ext/acp/*</c> requests.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AcpExtensionCapability? AcpExtensions { get; set; }

    /// <summary>
    /// Node REPL runtime capability. When set with <see cref="BrowserUse"/>, the client can
    /// receive server-initiated <c>ext/nodeRepl/*</c> requests for thread-bound browser automation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NodeReplCapability? NodeRepl { get; set; }

    /// <summary>
    /// Browser IAB capability. When set with <see cref="NodeRepl"/>, the client can back
    /// the persistent Node REPL with Desktop embedded browser automation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BrowserUseCapability? BrowserUse { get; set; }
}

/// <summary>
/// Client-declared ACP extension support during <c>initialize</c>.
/// </summary>
public sealed class AcpExtensionCapability
{
    public bool? FsReadTextFile { get; set; }

    public bool? FsWriteTextFile { get; set; }

    public bool? TerminalCreate { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Extensions { get; set; }
}

/// <summary>
/// Client-declared Desktop Node REPL support during <c>initialize</c>.
/// </summary>
public sealed class NodeReplCapability
{
    public string Backend { get; set; } = string.Empty;
}

/// <summary>
/// Client-declared Desktop browser IAB support during <c>initialize</c>.
/// </summary>
public sealed class BrowserUseCapability
{
    public string Backend { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Backends { get; set; }

    public int? ProtocolVersion { get; set; }

    public bool? SupportsCancel { get; set; }

    public int? BrowserSessionProtocolVersion { get; set; }

    public bool? SupportsCommandCancel { get; set; }

    public int? MaxBrowserResultBytes { get; set; }

    public int? DefaultCommandTimeoutMs { get; set; }

    public int? MaxCommandTimeoutMs { get; set; }

    public bool? SupportsTypedFinalize { get; set; }

    public bool? SupportsChromeDiagnostics { get; set; }
}

/// <summary>
/// Declares that this client is an external channel adapter.
/// Sent inside <see cref="AppServerClientCapabilities"/> during the initialize handshake.
/// See external-channel-adapter.md §5.1.
/// </summary>
public sealed class ChannelAdapterCapability
{
    /// <summary>
    /// Canonical channel name (e.g. "telegram"). Must match the server-side
    /// ExternalChannels configuration key.
    /// </summary>
    public string ChannelName { get; set; } = string.Empty;

    /// <summary>
    /// Structured delivery capability descriptor for <c>ext/channel/send</c>.
    /// Text and media delivery both use the unified send contract.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelDeliveryCapabilities? DeliveryCapabilities { get; set; }

    /// <summary>
    /// Optional channel-scoped tools declared by the adapter during initialize.
    /// These tools are only injected into matching-origin threads while the adapter remains connected.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ChannelToolDescriptor>? ChannelTools { get; set; }
}

public sealed class ChannelToolDescriptor
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// JSON Schema describing the input arguments accepted by the tool.
    /// </summary>
    public JsonObject? InputSchema { get; set; }

    /// <summary>
    /// Optional JSON Schema describing the structured result returned by the tool.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? OutputSchema { get; set; }

    /// <summary>
    /// Optional adapter-provided display metadata for richer tool UIs.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelToolDisplay? Display { get; set; }

    /// <summary>
    /// Optional approval metadata describing which argument should be intercepted by the server
    /// before dispatching <c>ext/channel/toolCall</c>.
    /// This describes approval targets only; policy remains server-owned.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelToolApprovalDescriptor? Approval { get; set; }

    public bool RequiresChatContext { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DeferLoading { get; set; }
}

public sealed class DynamicToolSpec
{
    public string? Namespace { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public JsonObject? InputSchema { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DeferLoading { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelToolApprovalDescriptor? Approval { get; set; }

    /// <summary>
    /// Extensible <c>_meta</c> envelope (MCP Apps). Carries Interactive Tool UI metadata; UI/host-only.
    /// </summary>
    [JsonPropertyName("_meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DynamicToolMeta? Meta { get; set; }
}

/// <summary>
/// Extensible <c>_meta</c> envelope on a dynamic tool spec (MCP Apps).
/// </summary>
public sealed class DynamicToolMeta
{
    /// <summary>Interactive Tool UI descriptor (<c>_meta.ui</c>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UiToolMeta? Ui { get; set; }
}

/// <summary>
/// Interactive Tool UI descriptor (<c>_meta.ui</c>), aligned to MCP Apps / SEP-1865.
/// See <c>specs/protocols/tool-result-presentation.md</c> §4.
/// </summary>
public sealed class UiToolMeta
{
    /// <summary>The <c>ui://</c> resource URI whose HTML renders this tool's result.</summary>
    public string ResourceUri { get; set; } = string.Empty;

    /// <summary>
    /// Audience for the tool. <c>["model","app"]</c> = model-callable and UI-rendered;
    /// <c>["app"]</c> = UI-only (hidden from the model, invocable only via <c>ui/tool/call</c>).
    /// Absent =&gt; model-visible by default.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Visibility { get; set; }

    /// <summary>Content-Security-Policy allowances for the rendered iframe.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UiToolCsp? Csp { get; set; }

    /// <summary>Optional host permissions the UI requests.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Permissions { get; set; }

    /// <summary>Whether the host should draw a default border/frame around the UI.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? PrefersBorder { get; set; }

    /// <summary>Optional app domain identifier (provenance / grouping).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Domain { get; set; }
}

/// <summary>
/// Content-Security-Policy allowances for an Interactive Tool UI iframe.
/// </summary>
public sealed class UiToolCsp
{
    /// <summary>Origins the UI may <c>connect-src</c> to (fetch / XHR / WebSocket).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ConnectDomains { get; set; }

    /// <summary>Origins the UI may load passive resources from (img / style / font / media).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ResourceDomains { get; set; }

    /// <summary>Origins the UI may embed as nested frames.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? FrameDomains { get; set; }
}

/// <summary>
/// Audience constants and helpers for <see cref="UiToolMeta.Visibility"/> (MCP Apps).
/// </summary>
public static class UiToolVisibility
{
    public const string Model = "model";
    public const string App = "app";

    /// <summary>
    /// True when the tool should be exposed to the model. A tool with no UI metadata or no
    /// explicit visibility list is model-visible; an explicit list must contain <c>"model"</c>.
    /// </summary>
    public static bool IsModelVisible(UiToolMeta? ui)
        => ui?.Visibility is not { Count: > 0 } visibility
           || visibility.Contains(Model, StringComparer.Ordinal);

    /// <summary>
    /// True when the tool may be invoked by its UI via <c>ui/tool/call</c>. A tool with no UI
    /// metadata is not app-invocable; an explicit list must contain <c>"app"</c>, and a tool that
    /// declares a UI resource without a visibility list is app-invocable by default.
    /// </summary>
    public static bool IsAppVisible(UiToolMeta? ui)
        => ui is not null
           && (ui.Visibility is not { Count: > 0 } visibility
               || visibility.Contains(App, StringComparer.Ordinal));
}

public sealed class ChannelToolApprovalDescriptor
{
    /// <summary>
    /// Server approval category, for example <c>file</c> or <c>shell</c>.
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Name of the tool argument that contains the primary approval target.
    /// </summary>
    public string TargetArgument { get; set; } = string.Empty;

    /// <summary>
    /// Optional static operation label forwarded to the approval service.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Operation { get; set; }

    /// <summary>
    /// Optional argument name whose runtime value is forwarded as the operation string.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OperationArgument { get; set; }
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

public sealed class DynamicToolCallResult
{
    public bool Success { get; set; }

    public List<ExtChannelToolContentItem>? ContentItems { get; set; }

    public JsonNode? StructuredResult { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>
    /// UI-only metadata (MCP Apps <c>_meta</c>). Forwarded to the host/UI for rendering;
    /// never included in the model-visible result.
    /// </summary>
    [JsonPropertyName("_meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Meta { get; set; }
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

    /// <summary>
    /// The owning tool's resolved <c>_meta.ui.csp</c> (host‑populated, M‑iii). Lets the host build the
    /// per‑resource iframe CSP (data path B widening) from the server‑validated descriptor — never from
    /// the iframe. Absent =&gt; restrictive default (network‑denied). The brokered app response does not
    /// set this; the host fills it from the attached tool's descriptor before returning to the renderer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UiToolCsp? Csp { get; set; }
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
/// (MCP Apps <c>callTool</c>). Decoupled from the conversation — gated, brokered, audited, with
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
/// Params for <c>ui/open-link</c> (host → server, M‑iii): a UI‑initiated request to open an
/// external link. The host enforces a fixed scheme policy (<c>https:</c>/<c>mailto:</c> only) and
/// records the open on the App Binding audit trail; the actual navigation happens in the Desktop
/// host. Decoupled — no turn/item. See tool-result-presentation-m3.md §3.
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
/// Decoupled — no turn/item. See tool-result-presentation-m3.md §3.
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

/// <summary>
/// Params for <c>item/widget-state/set</c> (host → server, M-iv): the host persists an Interactive
/// Tool UI item's UI-only <c>widgetState</c>, keyed by <see cref="CallId"/>. Decoupled — no
/// turn/item. Null/absent <see cref="WidgetState"/> clears it. UI-only; never reaches the model.
/// </summary>
public sealed class ItemWidgetStateSetParams
{
    public string ThreadId { get; set; } = string.Empty;

    /// <summary>The <c>callId</c> of the originating <c>dynamicToolCall</c> item.</summary>
    public string CallId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? WidgetState { get; set; }
}

/// <summary>Result of <c>item/widget-state/set</c>.</summary>
public sealed class ItemWidgetStateSetResult
{
    /// <summary>True when the stored state was removed (empty) rather than upserted.</summary>
    public bool Cleared { get; set; }
}

public sealed class ChannelToolDisplay
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Subtitle { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }
}

public sealed class ExtChannelToolCallParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;

    public string CallId { get; set; } = string.Empty;

    public string Tool { get; set; } = string.Empty;

    public JsonObject Arguments { get; set; } = [];

    public ExtChannelToolCallContext Context { get; set; } = new();
}

public sealed class ExtChannelToolCallContext
{
    public string ChannelName { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChannelContext { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SenderId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GroupId { get; set; }
}

public sealed class ExtChannelToolCallResult
{
    public bool Success { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ExtChannelToolContentItem>? ContentItems { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? StructuredResult { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; set; }
}

public sealed class ExtChannelToolContentItem
{
    public string Type { get; set; } = "text";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DataBase64 { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MediaType { get; set; }
}

public sealed class ChannelDeliveryCapabilities
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? StructuredDelivery { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelMediaCapabilitySet? Media { get; set; }
}

public sealed class ChannelMediaCapabilitySet
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelMediaConstraints? File { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelMediaConstraints? Audio { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelMediaConstraints? Image { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelMediaConstraints? Video { get; set; }
}

public sealed class ChannelMediaConstraints
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MaxBytes { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? AllowedMimeTypes { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? AllowedExtensions { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SupportsHostPath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SupportsUrl { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SupportsBase64 { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SupportsCaption { get; set; }
}

public sealed class ChannelMediaSource
{
    public string Kind { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HostPath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DataBase64 { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ArtifactId { get; set; }
}

public sealed class ChannelOutboundMessage
{
    public string Kind { get; set; } = "text";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Caption { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MediaType { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelMediaSource? Source { get; set; }
}

public sealed class ExtChannelSendParams
{
    public string Target { get; set; } = string.Empty;

    public ChannelOutboundMessage Message { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Metadata { get; set; }
}

public sealed class ExtChannelSendResult
{
    public bool Delivered { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RemoteMessageId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RemoteMediaId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; set; }
}

public sealed class AppServerInitializeResult
{
    public AppServerServerInfo ServerInfo { get; set; } = new();

    public AppServerServerCapabilities Capabilities { get; set; } = new();

    /// <summary>
    /// DashBoard UI URL when the server hosts it (…/dashboard); omitted when disabled.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("dashboardUrl")]
    public string? DashboardUrl { get; set; }
}

public sealed class AppServerServerInfo
{
    public string Name { get; set; } = "dotcraft";

    public string Version { get; set; } = string.Empty;

    /// <summary>Wire protocol version. Currently "1".</summary>
    public string ProtocolVersion { get; set; } = "1";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Extensions { get; set; }
}

public sealed class AppServerServerCapabilities
{
    public bool ThreadManagement { get; set; } = true;

    public bool ThreadSubscriptions { get; set; } = true;

    /// <summary>
    /// Server supports persistent per-thread goal methods (thread/goal/*).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ThreadGoals { get; set; }

    /// <summary>
    /// Server supports creating a sibling thread from an existing thread via <c>thread/fork</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ThreadFork { get; set; }

    /// <summary>
    /// Server supports DotCraft-managed Git worktree handoff methods (<c>worktree/*</c>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool GitWorktrees { get; set; }

    /// <summary>
    /// Server supports manual thread context compaction via <c>thread/compact/start</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ManualCompaction { get; set; }

    /// <summary>
    /// Server supports manual long-term memory consolidation via <c>thread/memory/consolidate/start</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ManualMemoryConsolidation { get; set; }

    /// <summary>
    /// Server supports rebinding Runtime Dynamic Tools via <c>thread/resume.dynamicTools</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool DynamicToolRebind { get; set; }

    /// <summary>
    /// Server supports thread-bound runtime context supplied by the AppServer client.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RuntimeAdditionalContext { get; set; }

    /// <summary>
    /// Server supports App Binding methods (<c>app/*</c> and <c>thread/appBindings/*</c>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AppBinding { get; set; }

    /// <summary>
    /// Server supports AppBinding-owned thread app context blocks.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AppContextBlocks { get; set; }

    /// <summary>
    /// Server supports AppBinding-safe app-triggered queued input via <c>app/threadInput/enqueue</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AppThreadInputEnqueue { get; set; }

    /// <summary>
    /// Server supports interrupting active thread maintenance via <c>thread/maintenance/interrupt</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ThreadMaintenanceInterrupt { get; set; }

    public bool ApprovalFlow { get; set; } = true;

    public bool RequestUserInput { get; set; } = true;

    public bool ModeSwitch { get; set; } = true;

    public bool ConfigOverride { get; set; } = true;

    /// <summary>
    /// Server supports background terminal management methods (terminal/list/read/write/stop/clean).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool BackgroundTerminals { get; set; }

    /// <summary>
    /// Server supports cron management methods (cron/list, cron/remove, cron/enable, cron/run).
    /// False when the cron service is not configured. See spec Section 16.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CronManagement { get; set; }

    /// <summary>
    /// Server supports heartbeat management methods (heartbeat/trigger).
    /// False when the heartbeat service is not configured. See spec Section 17.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HeartbeatManagement { get; set; }

    /// <summary>
    /// Server supports skills management methods (skills/list, skills/read, skills/view, skills/restoreOriginal, skills/setEnabled, skills/uninstall). See spec Section 18.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SkillsManagement { get; set; }

    /// <summary>
    /// Server supports plugin management methods (plugin/list, plugin/view, plugin/setEnabled).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PluginManagement { get; set; }

    /// <summary>
    /// Server has skill variants enabled for effective skill views and restoring source skills from workspace adaptations.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SkillVariants { get; set; }

    /// <summary>
    /// Server supports command management methods (command/list, command/execute). See spec Section 19.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CommandManagement { get; set; }

    /// <summary>
    /// Server supports automation task methods (automation/task/*).
    /// False when the Automations module is not loaded.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Automations { get; set; }

    /// <summary>
    /// Server supports <c>channel/status</c> (spec Section 20).
    /// True when a <see cref="IChannelStatusProvider"/> is registered with the request handler.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ChannelStatus { get; set; }

    /// <summary>
    /// Server supports model catalog methods (<c>model/list</c>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ModelCatalogManagement { get; set; }

    /// <summary>
    /// Server supports personal model provider management methods (<c>provider/list</c>, <c>provider/create</c>, <c>provider/test</c>, etc.).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ProviderManagement { get; set; }

    /// <summary>
    /// Server supports workspace config write methods (<c>workspace/config/update</c>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool WorkspaceConfigManagement { get; set; }

    /// <summary>
    /// Server supports workspace memory management methods (<c>memory/reset</c>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool MemoryManagement { get; set; }

    /// <summary>
    /// Server supports workspace Dreams methods (<c>dreams/status</c>, <c>dreams/run</c>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Dreams { get; set; }

    /// <summary>
    /// Server supports MCP configuration management methods (<c>mcp/list</c>, <c>mcp/upsert</c>, etc.).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool McpManagement { get; set; }

    /// <summary>
    /// Server annotates MCP config/status DTOs with workspace/plugin origin metadata.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool McpServerOrigins { get; set; }

    /// <summary>
    /// Server supports external channel configuration management methods
    /// (<c>externalChannel/list</c>, <c>externalChannel/upsert</c>, etc.).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ExternalChannelManagement { get; set; }

    /// <summary>
    /// Server supports SubAgent profile management methods
    /// (<c>subagent/profiles/list</c>, <c>subagent/profiles/setEnabled</c>, etc.).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SubAgentManagement { get; set; }

    /// <summary>
    /// Server supports session-backed SubAgent child threads.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SubAgentSessions { get; set; }

    /// <summary>
    /// Server supports MCP runtime status methods/notifications.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool McpStatus { get; set; }

    /// <summary>
    /// Server supports the aggregate usage telemetry method (<c>usage/summary</c>).
    /// False when tracing is disabled (no trace store available). See spec Section 27A.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool UsageTelemetry { get; set; }

    /// <summary>
    /// Server supports the OpenAI ChatGPT subscription auth methods (<c>auth/openai/login</c>, etc.).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AuthOpenAiOAuth { get; set; }

    /// <summary>
    /// Server exposes usage / rate-limit telemetry for ChatGPT subscription accounts
    /// (<c>auth/openai/usage</c> + <c>auth/openai/usageChanged</c>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AuthOpenAiUsage { get; set; }

    /// <summary>
    /// Module-provided capabilities keyed by extension name.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object>? Extensions { get; set; }
}

// ───── thread/start ─────

public sealed class ThreadStartParams
{
    public SessionIdentity Identity { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadConfiguration? Config { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DynamicToolSpec>? DynamicTools { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, RuntimeAdditionalContextEntry>? AdditionalContext { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HistoryMode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Optional id of the thread that started this thread on the user's behalf (e.g. the
    /// Desktop CreateThread tool called from another thread). Recorded as a non-subagent
    /// origin on the new thread so its first user message can link back to the source.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SpawnedFromThreadId { get; set; }
}

// ───── thread/fork ─────

public sealed class ThreadForkParams
{
    public string ThreadId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadForkPoint? ForkPoint { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionIdentity? Identity { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadConfiguration? Config { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DynamicToolSpec>? DynamicTools { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, RuntimeAdditionalContextEntry>? AdditionalContext { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    public bool? Ephemeral { get; set; }

    public bool? ExcludeTurns { get; set; }
}

// ───── worktree/createAndFork ─────

public sealed class WorktreeCreateAndForkParams
{
    public string SourceThreadId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadForkPoint? ForkPoint { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionIdentity? Identity { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadConfiguration? Config { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DynamicToolSpec>? DynamicTools { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, RuntimeAdditionalContextEntry>? AdditionalContext { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BranchName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BaseRef { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; set; }

    public bool? CopyDirtyChanges { get; set; }

    public bool? ExcludeTurns { get; set; }
}

public sealed class WorktreeCreateAndForkResponse
{
    public SessionWireThread Thread { get; set; } = new();

    public ThreadWorktreeInfo Worktree { get; set; } = new();
}

public sealed class WorktreeCreateAndStartParams
{
    public SessionIdentity Identity { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadConfiguration? Config { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DynamicToolSpec>? DynamicTools { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, RuntimeAdditionalContextEntry>? AdditionalContext { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HistoryMode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BranchName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BaseRef { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; set; }

    public bool? CopyDirtyChanges { get; set; }
}

public sealed class WorktreeCreateAndStartResponse
{
    public SessionWireThread Thread { get; set; } = new();

    public ThreadWorktreeInfo Worktree { get; set; } = new();
}

public sealed class ThreadWorktreeHandoffParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string Mode { get; set; } = WorktreeHandoffModes.Worktree;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BranchName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BaseRef { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; set; }

    public bool? CopyDirtyChanges { get; set; }
}

public sealed class ThreadWorktreeHandoffResponse
{
    public SessionWireThread Thread { get; set; } = new();

    public string Mode { get; set; } = WorktreeHandoffModes.Local;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public ThreadWorktreeInfo? Worktree { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadWorktreeDirtyHandoffInfo? DirtyHandoff { get; set; }
}

public sealed class WorktreeListParams
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionIdentity? Identity { get; set; }
}

public sealed class WorktreeListResult
{
    public List<ThreadWorktreeStatus> Data { get; set; } = [];
}

public sealed class WorktreeStatusParams
{
    public string ThreadId { get; set; } = string.Empty;
}

public sealed class WorktreeStatusResult
{
    public ThreadWorktreeStatus Status { get; set; } = new();
}

// ───── thread/resume ─────

public sealed class ThreadResumeParams
{
    public string ThreadId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DynamicToolSpec>? DynamicTools { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, RuntimeAdditionalContextEntry>? AdditionalContext { get; set; }
}

public static class RuntimeAdditionalContextKinds
{
    public const string Application = "application";
}

public sealed class RuntimeAdditionalContextEntry
{
    public string Kind { get; set; } = RuntimeAdditionalContextKinds.Application;

    public string Value { get; set; } = string.Empty;
}

// ───── thread/list ─────

public sealed class ThreadListParams
{
    public SessionIdentity Identity { get; set; } = new();

    public bool? IncludeArchived { get; set; }

    public bool? IncludeSubAgents { get; set; }

    public bool? IncludeInternal { get; set; }

    /// <summary>
    /// When set, only threads whose <c>originChannel</c> matches (case-insensitive) are returned.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChannelName { get; set; }

    /// <summary>
    /// When non-null, passed to <see cref="ISessionService.FindThreadsAsync"/> as cross-channel origins.
    /// When null (JSON omitted), no cross-channel list is applied.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? CrossChannelOrigins { get; set; }

    /// <summary>
    /// Optional case-insensitive filter applied before pagination.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Query { get; set; }

    /// <summary>
    /// Optional page size. When omitted with <see cref="Cursor"/>, the server default is used.
    /// </summary>
    public int? Limit { get; set; }

    /// <summary>
    /// Opaque cursor returned by a previous <c>thread/list</c> call.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cursor { get; set; }
}

public sealed class ThreadListResult
{
    public List<ThreadSummary> Data { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextCursor { get; set; }

    public int? TotalMatched { get; set; }
}

// ───── subagent/children/list ─────

public sealed class SubAgentChildrenListParams
{
    public string ParentThreadId { get; set; } = string.Empty;

    public bool? IncludeClosed { get; set; }

    public bool? IncludeThreads { get; set; }
}

public sealed class SubAgentChildWire
{
    public ThreadSpawnEdge Edge { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionWireThread? Thread { get; set; }
}

public sealed class SubAgentChildrenListResult
{
    public List<SubAgentChildWire> Data { get; set; } = [];
}

public sealed class SubAgentThreadParams
{
    public string ParentThreadId { get; set; } = string.Empty;

    public string ChildThreadId { get; set; } = string.Empty;
}

public sealed class SubAgentTargetParams
{
    public string ParentThreadId { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;
}

public sealed class SubAgentTargetMessageParams
{
    public string ParentThreadId { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public SubAgentFollowupDeliveryMode DeliveryMode { get; set; } = SubAgentFollowupDeliveryMode.Queue;
}

// ───── thread/read ─────

public sealed class ThreadReadParams
{
    public string ThreadId { get; set; } = string.Empty;

    public bool? IncludeTurns { get; set; }

    public int? TurnLimit { get; set; }

    /// <summary>
    /// Opaque cursor returned by a previous paged <c>thread/read</c> call.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cursor { get; set; }
}

public sealed class ThreadReadResult
{
    public SessionWireThread Thread { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadReadTurnPage? TurnPage { get; set; }
}

public sealed class ThreadReadTurnPage
{
    public string Order { get; set; } = "oldestFirst";

    public int Limit { get; set; }

    public int TotalTurns { get; set; }

    public int StartOrdinal { get; set; }

    public int EndOrdinal { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextCursor { get; set; }

    public bool HasMore { get; set; }
}

// ───── thread/goal/* ─────

public sealed class ThreadGoalGetParams
{
    public string ThreadId { get; set; } = string.Empty;
}

public sealed class ThreadGoalGetResult
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public ThreadGoal? Goal { get; set; }
}

public sealed class ThreadGoalSetParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string? Objective { get; set; }

    public string? Status { get; set; }

    public JsonElement? TokenBudget { get; set; }

    public string? Mode { get; set; }
}

public sealed class ThreadGoalSetResult
{
    public ThreadGoal Goal { get; set; } = null!;
}

public sealed class ThreadGoalClearParams
{
    public string ThreadId { get; set; } = string.Empty;
}

public sealed class ThreadGoalClearResultWire
{
    public bool Cleared { get; set; }
}

// ───── thread/compact/start ─────

public sealed class ThreadCompactStartParams
{
    public string ThreadId { get; set; } = string.Empty;
}

public sealed class ThreadCompactStartResponse
{
    public string Outcome { get; set; } = "skipped";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContextUsageSnapshot? ContextUsage { get; set; }
}

// ───── thread/memory/consolidate/start ─────

public sealed class ThreadMemoryConsolidateStartParams
{
    public string ThreadId { get; set; } = string.Empty;
}

public sealed class ThreadMemoryConsolidateStartResponse
{
    public string Outcome { get; set; } = "skipped";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    public bool MemoryWritten { get; set; }

    public bool HistoryWritten { get; set; }
}

// ───── thread/maintenance/interrupt ─────

public sealed class ThreadMaintenanceInterruptParams
{
    public string ThreadId { get; set; } = string.Empty;
}

// ───── thread/rollback ─────

public sealed class ThreadRollbackParams
{
    public string ThreadId { get; set; } = string.Empty;

    /// <summary>
    /// Number of turns to drop from the end of the thread. Must be >= 1.
    /// This only modifies conversation history and does not revert filesystem changes.
    /// </summary>
    public int NumTurns { get; set; }
}

public sealed class ThreadRollbackResponse
{
    public SessionWireThread Thread { get; set; } = new();
}

// ───── thread/subscribe ─────

public sealed class ThreadSubscribeParams
{
    public string ThreadId { get; set; } = string.Empty;

    public bool? ReplayRecent { get; set; }
}

// ───── thread/unsubscribe ─────

public sealed class ThreadUnsubscribeParams
{
    public string ThreadId { get; set; } = string.Empty;
}

// ───── thread/pause, archive, delete ─────

public sealed class ThreadPauseParams
{
    public string ThreadId { get; set; } = string.Empty;
}

public sealed class ThreadArchiveParams
{
    public string ThreadId { get; set; } = string.Empty;
}

public sealed class ThreadUnarchiveParams
{
    public string ThreadId { get; set; } = string.Empty;
}

public sealed class ThreadDeleteParams
{
    public string ThreadId { get; set; } = string.Empty;
}

// ───── thread/rename ─────

public sealed class ThreadRenameParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}

// ───── thread/mode/set ─────

public sealed class ThreadModeSetParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string Mode { get; set; } = string.Empty;
}

// ───── thread/config/update ─────

public sealed class ThreadConfigUpdateParams
{
    public string ThreadId { get; set; } = string.Empty;

    public ThreadConfiguration Config { get; set; } = new();
}

// ───── thread/runtimeChanged (Server → Client notification) ─────

/// <summary>
/// Lightweight runtime snapshot for a thread, broadcast to all initialized connections.
/// </summary>
public sealed class ThreadRuntimeState
{
    /// <summary>
    /// True when the thread currently has a running turn.
    /// </summary>
    public bool Running { get; set; }

    /// <summary>
    /// True when the thread currently has one or more unresolved approval requests.
    /// </summary>
    public bool WaitingOnApproval { get; set; }

    /// <summary>
    /// True when the thread currently has one or more unresolved model question requests.
    /// </summary>
    public bool WaitingOnInput { get; set; }

    /// <summary>
    /// True after a plan-mode turn ends with a successful terminal CreatePlan tool call,
    /// until the next turn starts on the same thread.
    /// </summary>
    public bool WaitingOnPlanConfirmation { get; set; }

    /// <summary>
    /// True when the thread cannot start a new turn because a turn, approval, user input, or maintenance task is active.
    /// </summary>
    public bool Busy { get; set; }

    /// <summary>
    /// Current thread maintenance kind, such as <c>compacting</c> or <c>consolidating</c>.
    /// Null when no thread maintenance is active.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MaintenanceKind { get; set; }
}

/// <summary>
/// Notification payload for <c>thread/runtimeChanged</c>.
/// </summary>
public sealed class ThreadRuntimeChangedParams
{
    public string ThreadId { get; set; } = string.Empty;

    public ThreadRuntimeState Runtime { get; set; } = new();
}

// ───── turn/start ─────

public sealed class TurnStartParams
{
    public string ThreadId { get; set; } = string.Empty;

    public List<SessionWireInputPart> Input { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SenderContext? Sender { get; set; }

    /// <summary>
    /// Client-provided conversation history for HistoryMode.Client threads.
    /// Kept as raw JsonElement to defer deserialization of ChatMessage[].
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Messages { get; set; }
}

// ───── turn/enqueue ─────

public sealed class TurnEnqueueParams
{
    public string ThreadId { get; set; } = string.Empty;

    public List<SessionWireInputPart> Input { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SenderContext? Sender { get; set; }
}

public sealed class TurnEnqueueResponse
{
    public QueuedTurnInput QueuedInput { get; set; } = new();

    public List<QueuedTurnInput> QueuedInputs { get; set; } = [];
}

public sealed class TurnQueueRemoveParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string QueuedInputId { get; set; } = string.Empty;
}

public sealed class TurnQueueRemoveResponse
{
    public List<QueuedTurnInput> QueuedInputs { get; set; } = [];
}

public sealed class TurnQueueReorderParams
{
    public string ThreadId { get; set; } = string.Empty;

    public List<string> OrderedQueuedInputIds { get; set; } = [];
}

public sealed class TurnQueueReorderResponse
{
    public List<QueuedTurnInput> QueuedInputs { get; set; } = [];
}

public sealed class TurnSteerParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string ExpectedTurnId { get; set; } = string.Empty;

    public string QueuedInputId { get; set; } = string.Empty;

    public List<SessionWireInputPart> Input { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SenderContext? Sender { get; set; }
}

public sealed class TurnSteerResponse
{
    public string TurnId { get; set; } = string.Empty;

    public List<QueuedTurnInput> QueuedInputs { get; set; } = [];
}

// ───── turn/interrupt ─────

public sealed class TurnInterruptParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;
}

// ───── terminal/* ─────

public sealed class TerminalListParams
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThreadId { get; set; }
}

public sealed class TerminalReadParams
{
    public string SessionId { get; set; } = string.Empty;

    public int? WaitMs { get; set; }

    public int? MaxOutputChars { get; set; }
}

public sealed class TerminalWriteParams
{
    public string SessionId { get; set; } = string.Empty;

    public string Input { get; set; } = string.Empty;

    public int? YieldTimeMs { get; set; }

    public int? MaxOutputChars { get; set; }
}

public sealed class TerminalStopParams
{
    public string SessionId { get; set; } = string.Empty;
}

public sealed class TerminalCleanParams
{
    public string ThreadId { get; set; } = string.Empty;
}

// ───── command/* (spec Section 19) ─────

public sealed class CommandListParams
{
    /// <summary>
    /// Deprecated compatibility field. The server ignores this value and always
    /// returns English fallback text plus stable message keys.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Language { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IncludeBuiltins { get; set; }
}

public sealed class CommandListResult
{
    public List<CommandInfoWire> Commands { get; set; } = [];
}

public sealed class CommandInfoWire
{
    public string Name { get; set; } = string.Empty;

    public string[] Aliases { get; set; } = [];

    public string DescriptionKey { get; set; } = string.Empty;

    public string FallbackDescription { get; set; } = string.Empty;

    /// <summary>
    /// Compatibility alias for <see cref="FallbackDescription"/>.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "builtin";

    public bool RequiresAdmin { get; set; }
}

public sealed class CommandExecuteParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Arguments { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SenderContext? Sender { get; set; }
}

public sealed class CommandExecuteResult
{
    public bool Handled { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    public bool IsMarkdown { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpandedPrompt { get; set; }

    /// <summary>
    /// True when command handling reset the conversation and switched to a new thread.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SessionReset { get; set; }

    /// <summary>
    /// Fresh thread metadata returned by reset-style commands (for example <c>/new</c>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionWireThread? Thread { get; set; }

    /// <summary>
    /// Thread ids archived as part of reset-style commands.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ArchivedThreadIds { get; set; }

    /// <summary>
    /// Whether the newly created thread is lazily materialized on disk.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CreatedLazily { get; set; }
}

// ───── item/approval/request (Server → Client request) ─────

public sealed class AppServerApprovalRequestParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;

    public string ItemId { get; set; } = string.Empty;

    public string RequestId { get; set; } = string.Empty;

    /// <summary>"shell" or "file"</summary>
    public string ApprovalType { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    public string ScopeKey { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }
}

// ───── item/approval/request response (Client → Server) ─────

public sealed class AppServerApprovalResponseResult
{
    /// <summary>One of: "accept", "acceptForSession", "decline", "cancel".</summary>
    public string Decision { get; set; } = string.Empty;
}

// ───── item/tool/requestUserInput (Server → Client request) ─────

public sealed class AppServerRequestUserInputParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;

    public string ItemId { get; set; } = string.Empty;

    public string RequestId { get; set; } = string.Empty;

    public List<RequestUserInputQuestion> Questions { get; set; } = [];
}

// ───── item/tool/requestUserInput response (Client → Server) ─────

public sealed class AppServerRequestUserInputResponseResult
{
    public Dictionary<string, RequestUserInputAnswer> Answers { get; set; } = new(StringComparer.Ordinal);
}

// ───── cron/list ─────

public sealed class CronListParams
{
    /// <summary>When true, disabled jobs are included. Default false.</summary>
    public bool IncludeDisabled { get; set; }
}

public sealed class CronListResult
{
    public List<CronJobWireInfo> Jobs { get; set; } = [];
}

// ───── cron/remove ─────

public sealed class CronRemoveParams
{
    public string JobId { get; set; } = string.Empty;
}

public sealed class CronRemoveResult
{
    public bool Removed { get; set; }
}

// ───── cron/enable ─────

public sealed class CronEnableParams
{
    public string JobId { get; set; } = string.Empty;

    public bool Enabled { get; set; }
}

public sealed class CronEnableResult
{
    public CronJobWireInfo Job { get; set; } = new();
}

// ───── cron/run ─────

public sealed class CronRunParams
{
    public string JobId { get; set; } = string.Empty;
}

public sealed class CronRunResult
{
    public bool Queued { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CronJobWireInfo? Job { get; set; }
}

// ───── usage/summary (spec Section 27A.2) ─────

/// <summary>
/// Aggregate usage telemetry across all traced sessions. Mirrors the shape the hosted
/// Dashboard serves at <c>GET /dashboard/api/summary</c>. All numeric fields are <c>0</c>
/// when no sessions have been traced yet.
/// </summary>
public sealed class UsageSummaryResult
{
    public int SessionCount { get; set; }

    public int TotalRequests { get; set; }

    public int TotalResponses { get; set; }

    public int TotalToolCalls { get; set; }

    public int TotalErrors { get; set; }

    public int TotalContextCompactions { get; set; }

    public long TotalInputTokens { get; set; }

    public long TotalOutputTokens { get; set; }

    public long TotalCachedInputTokens { get; set; }

    public long TotalCacheWriteInputTokens { get; set; }

    public long TotalFreshInputTokens { get; set; }

    public long TotalNonCachedInputTokens { get; set; }

    public long TotalReasoningOutputTokens { get; set; }

    public long TotalToolDurationMs { get; set; }

    public double AvgToolDurationMs { get; set; }

    public long MaxToolDurationMs { get; set; }

    /// <summary>Cached input tokens / total input tokens, in [0, 1]. 0 when no input tokens.</summary>
    public double CacheHitRate { get; set; }

    public long TotalTokens { get; set; }
}

// ───── usage/timeseries (spec Section 27A.3) ─────

/// <summary>
/// Params for <c>usage/timeseries</c>. All fields optional; an absent bound means
/// "no bound" and an absent <see cref="TzOffsetMinutes"/> means UTC bucketing.
/// </summary>
public sealed class UsageTimeseriesParams
{
    /// <summary>Inclusive lower bound, <c>YYYY-MM-DD</c> in the client's local frame.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? From { get; set; }

    /// <summary>Inclusive upper bound, <c>YYYY-MM-DD</c> in the client's local frame.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? To { get; set; }

    /// <summary>Minutes to add to UTC to obtain the client's local time (<c>-getTimezoneOffset()</c>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TzOffsetMinutes { get; set; }
}

/// <summary>
/// Per-day token usage across all traced sessions, for activity charts (spec Section 27A.3).
/// The <see cref="Days"/> series is sparse (only days with activity) and ascending by date.
/// </summary>
public sealed class UsageTimeseriesResult
{
    /// <summary>The (clamped) offset in minutes the server used to bucket sessions by local day.</summary>
    public int TzOffsetMinutes { get; set; }

    /// <summary>
    /// Longest single Turn (one unit of agent work) across the workspace, in milliseconds.
    /// Lifetime maximum, independent of the requested date range. 0 when none recorded.
    /// </summary>
    public long LongestTaskMs { get; set; }

    public List<UsageTimeseriesDay> Days { get; set; } = [];
}

public sealed class UsageTimeseriesDay
{
    /// <summary><c>YYYY-MM-DD</c> in the client's local frame.</summary>
    public string Date { get; set; } = string.Empty;

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    public long TotalTokens { get; set; }

    public int SessionCount { get; set; }
}

// ───── profile/insights (spec Section 27A.5) ─────

/// <summary>Params for <c>profile/insights</c>. All fields optional.</summary>
public sealed class ProfileInsightsParams
{
    /// <summary>Max number of skills to return in <see cref="ProfileInsightsResult.Skills"/> (default 5, clamped 1..20).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TopSkills { get; set; }
}

/// <summary>
/// Profile "activity insights" for the current workspace (spec Section 27A.5). Model usage
/// reflects all persisted history; reasoning and skill metrics are forward-only and may be
/// empty until new activity accrues. <see cref="TopModel"/>/<see cref="TopReasoning"/> are
/// null when no data exists.
/// </summary>
public sealed class ProfileInsightsResult
{
    /// <summary>Most-used model id (ranked by LLM-response count), or null when none recorded.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RankedMetric? TopModel { get; set; }

    /// <summary>Most-used reasoning effort (e.g. "high"), or null when reasoning was never used.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RankedMetric? TopReasoning { get; set; }

    /// <summary>Distinct skills referenced at least once ("skills explored").</summary>
    public int SkillsExplored { get; set; }

    /// <summary>Total skill references across all turns ("total skills used").</summary>
    public long TotalSkillsUsed { get; set; }

    /// <summary>Total non-internal threads in this workspace (active + archived).</summary>
    public int TotalThreads { get; set; }

    /// <summary>Top referenced skills, descending by run count.</summary>
    public List<SkillUsageWire> Skills { get; set; } = [];
}

/// <summary>A leading value with its observation count and the total it was drawn from (for share%).</summary>
public sealed class RankedMetric
{
    public string Key { get; set; } = string.Empty;

    public long Count { get; set; }

    /// <summary>Total observations the leader was drawn from; <c>Count / Total</c> yields the share.</summary>
    public long Total { get; set; }
}

/// <summary>One referenced skill, its run count, and (when it came from a plugin) its plugin attribution.</summary>
public sealed class SkillUsageWire
{
    public string Name { get; set; } = string.Empty;

    public long Count { get; set; }

    /// <summary>Owning plugin id when the skill currently resolves to a plugin source; otherwise null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PluginId { get; set; }

    /// <summary>Human-readable plugin name for the badge, when from a plugin; otherwise null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PluginDisplayName { get; set; }
}

// ───── heartbeat/trigger (spec Section 17.2) ─────

public sealed class HeartbeatTriggerResult
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Result { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }
}

// ───── CronJobInfo wire DTO (spec Section 16.2) ─────

/// <summary>
/// Transport-safe projection of the internal CronJob domain model.
/// Used in cron/list and cron/enable results.
/// </summary>
public sealed class CronJobWireInfo
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public CronScheduleWireInfo Schedule { get; set; } = new();

    public bool Enabled { get; set; }

    public long CreatedAtMs { get; set; }

    public bool DeleteAfterRun { get; set; }

    public CronJobStateWireInfo State { get; set; } = new();
}

public sealed class CronScheduleWireInfo
{
    /// <summary>"every", "at", or "daily"</summary>
    public string Kind { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? EveryMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? AtMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? InitialDelayMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DailyHour { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DailyMinute { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tz { get; set; }
}

public sealed class CronJobStateWireInfo
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? NextRunAtMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? LastRunAtMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastStatus { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastError { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastThreadId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastResult { get; set; }
}

/// <summary>
/// Maps <see cref="CronJob"/> domain objects to wire DTOs (spec §16.2).
/// </summary>
public static class CronJobWireMapping
{
    public static CronJobWireInfo ToWire(CronJob job) => new()
    {
        Id = job.Id,
        Name = job.Name,
        Schedule = new CronScheduleWireInfo
        {
            Kind = job.Schedule.Kind,
            EveryMs = job.Schedule.EveryMs,
            AtMs = job.Schedule.AtMs,
            InitialDelayMs = job.Schedule.InitialDelayMs,
            DailyHour = job.Schedule.DailyHour,
            DailyMinute = job.Schedule.DailyMinute,
            Tz = job.Schedule.Tz
        },
        Enabled = job.Enabled,
        CreatedAtMs = job.CreatedAtMs,
        DeleteAfterRun = job.DeleteAfterRun,
        State = new CronJobStateWireInfo
        {
            NextRunAtMs = job.State.NextRunAtMs,
            LastRunAtMs = job.State.LastRunAtMs,
            LastStatus = job.State.LastStatus,
            LastError = job.State.LastError,
            LastThreadId = job.State.LastThreadId,
            LastResult = job.State.LastResult
        }
    };
}

// ───── skills/* (spec Section 18) ─────

public sealed class SkillsListParams
{
    /// <summary>When false, skills with unmet requirements are excluded. Default true.</summary>
    public bool? IncludeUnavailable { get; set; }
}

public sealed class SkillsListResult
{
    public List<SkillInfoWire> Skills { get; set; } = [];
}

/// <summary>
/// Wire projection of <see cref="DotCraft.Skills.SkillsLoader.SkillInfo"/> for skills/list and skills/setEnabled.
/// </summary>
public sealed class SkillInfoWire
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShortDescription { get; set; }

    public string Source { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PluginId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PluginDisplayName { get; set; }

    public bool Available { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UnavailableReason { get; set; }

    public bool Enabled { get; set; } = true;

    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// True when the current runtime resolves this skill through a workspace variant.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HasVariant { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IconSmallDataUrl { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IconLargeDataUrl { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultPrompt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Metadata { get; set; }
}

public sealed class SkillsReadParams
{
    public string Name { get; set; } = string.Empty;
}

public sealed class SkillsReadResult
{
    public string Name { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Metadata { get; set; }
}

public sealed class SkillsViewParams
{
    public string Name { get; set; } = string.Empty;
}

public sealed class SkillsViewResult
{
    public string Name { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
}

public sealed class SkillsRestoreOriginalParams
{
    public string Name { get; set; } = string.Empty;
}

public sealed class SkillsRestoreOriginalResult
{
    public string Name { get; set; } = string.Empty;

    public bool Restored { get; set; }
}

public sealed class SkillsSetEnabledParams
{
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; }
}

public sealed class SkillsSetEnabledResult
{
    public SkillInfoWire Skill { get; set; } = new();
}

public sealed class SkillsUninstallParams
{
    public string Name { get; set; } = string.Empty;
}

public sealed class SkillsUninstallResult
{
    public string Name { get; set; } = string.Empty;

    public bool Uninstalled { get; set; }

    public string Source { get; set; } = string.Empty;

    public string RemovedSourcePath { get; set; } = string.Empty;

    public int RemovedVariantCount { get; set; }
}

// ───── plugin/* ─────

public sealed class PluginListParams
{
    public bool? IncludeDisabled { get; set; }
}

public sealed class PluginListResult
{
    public List<PluginInfoWire> Plugins { get; set; } = [];

    public List<PluginDiagnosticWire> Diagnostics { get; set; } = [];
}

public sealed class PluginViewParams
{
    public string Id { get; set; } = string.Empty;
}

public sealed class PluginViewResult
{
    public PluginInfoWire Plugin { get; set; } = new();
}

public sealed class PluginInstallParams
{
    public string Id { get; set; } = string.Empty;
}

public sealed class PluginInstallResult
{
    public PluginInfoWire Plugin { get; set; } = new();
}

public sealed class PluginRemoveParams
{
    public string Id { get; set; } = string.Empty;
}

public sealed class PluginRemoveResult
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PluginInfoWire? Plugin { get; set; }
}

public sealed class PluginSetEnabledParams
{
    public string Id { get; set; } = string.Empty;

    public bool Enabled { get; set; }
}

public sealed class PluginSetEnabledResult
{
    public PluginInfoWire Plugin { get; set; } = new();
}

public sealed class PluginInfoWire
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; set; }

    public bool Enabled { get; set; }

    public bool Installed { get; set; } = true;

    public bool Installable { get; set; }

    public bool Removable { get; set; }

    public string Source { get; set; } = string.Empty;

    public string RootPath { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PluginInterfaceWire? Interface { get; set; }

    public List<PluginFunctionInfoWire> Functions { get; set; } = [];

    public List<PluginSkillInfoWire> Skills { get; set; } = [];

    public List<PluginAppInfoWire> Apps { get; set; } = [];

    public List<PluginDesktopExtensionInfoWire> DesktopExtensions { get; set; } = [];

    public List<PluginMcpServerInfoWire> McpServers { get; set; } = [];

    public List<PluginLspServerInfoWire> LspServers { get; set; } = [];

    public List<PluginDiagnosticWire> Diagnostics { get; set; } = [];
}

public sealed class PluginDesktopExtensionInfoWire
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    public string Entry { get; set; } = string.Empty;

    public List<string> Styles { get; set; } = [];

    public List<PluginDesktopExtensionSurfaceWire> Surfaces { get; set; } = [];

    public List<string> RequiredAppIds { get; set; } = [];

    public List<string> ConnectOrigins { get; set; } = [];

    public List<string> SurfaceWriteScopes { get; set; } = [];
}

public sealed class PluginDesktopExtensionSurfaceWire
{
    public string Type { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ViewId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? LocalizedLabel { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Placement { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Order { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Slot { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RendererId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActionId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SettingsId { get; set; }
}

public sealed class PluginAppInfoWire
{
    public string AppId { get; set; } = string.Empty;

    public string ToolNamespace { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string DeveloperName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReleasePage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PluginAppNativeApplicationWire? NativeApplication { get; set; }

    public List<PluginAppToolInfoWire> ToolCatalog { get; set; } = [];

    public PluginAppDynamicToolCatalogWire DynamicToolCatalog { get; set; } = new();
}

public sealed class PluginAppNativeApplicationWire
{
    public string DisplayName { get; set; } = string.Empty;

    public string Protocol { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstallUrl { get; set; }
}

public sealed class PluginAppToolInfoWire
{
    public string Name { get; set; } = string.Empty;

    public string Scope { get; set; } = string.Empty;

    public string Risk { get; set; } = string.Empty;

    public string DefaultExposure { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
}

public sealed class PluginAppDynamicToolCatalogWire
{
    public bool Enabled { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
}

public sealed class PluginMcpServerInfoWire
{
    public string Name { get; set; } = string.Empty;

    public string RuntimeName { get; set; } = string.Empty;

    public string Transport { get; set; } = "stdio";

    public bool Enabled { get; set; }

    public bool Active { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShadowedBy { get; set; }
}

public sealed class PluginLspServerInfoWire
{
    public string Name { get; set; } = string.Empty;

    public string RuntimeName { get; set; } = string.Empty;

    public string Transport { get; set; } = "stdio";

    public bool Enabled { get; set; }

    public bool Active { get; set; }

    public List<string> Extensions { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShadowedBy { get; set; }
}

public sealed class PluginInterfaceWire
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShortDescription { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LongDescription { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeveloperName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; set; }

    public List<string> Capabilities { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultPrompt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BrandColor { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ComposerIconDataUrl { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LogoDataUrl { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WebsiteUrl { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PrivacyPolicyUrl { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TermsOfServiceUrl { get; set; }
}

public sealed class PluginFunctionInfoWire
{
    public string Name { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Namespace { get; set; }

    public string Description { get; set; } = string.Empty;
}

public sealed class PluginSkillInfoWire
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShortDescription { get; set; }

    public bool Enabled { get; set; }
}

public sealed class PluginDiagnosticWire
{
    public string Severity { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PluginId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; set; }
}

// ───── automation/task/* DTOs ─────

public sealed class AutomationTaskWire
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThreadId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentSummary { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Local task tool boundary: <c>workspaceScope</c> (default, reject outside thread workspace) or <c>fullAuto</c> (legacy <c>autoApprove</c>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ApprovalPolicy { get; set; }

    /// <summary>
    /// Optional recurring schedule. When omitted, the task is one-shot (runs once from <c>pending</c>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AutomationScheduleWire? Schedule { get; set; }

    /// <summary>
    /// Optional binding to a pre-existing thread; the orchestrator submits workflow turns directly into that thread.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AutomationThreadBindingWire? ThreadBinding { get; set; }

    /// <summary>
    /// Scheduled next-run time (UTC). Null when the task has no <see cref="Schedule"/> or is ready to dispatch immediately.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? NextRunAt { get; set; }
}

/// <summary>
/// Wire-level projection of <c>CronSchedule</c> reused by automation tasks for serialization.
/// Kind: <c>once</c> | <c>every</c> | <c>at</c> | <c>daily</c> (weekly reserved).
/// </summary>
public sealed class AutomationScheduleWire
{
    public string Kind { get; set; } = "once";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? AtMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? EveryMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? InitialDelayMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DailyHour { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DailyMinute { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Expr { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tz { get; set; }
}

public sealed class AutomationThreadBindingWire
{
    public string ThreadId { get; set; } = string.Empty;

    /// <summary><c>run-in-thread</c> (default).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mode { get; set; }
}

public sealed class AutomationTaskListParams
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkspacePath { get; set; }
}

public sealed class AutomationTaskListResult
{
    public List<AutomationTaskWire> Tasks { get; set; } = [];
}

public sealed class AutomationTaskReadParams
{
    public string WorkspacePath { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
}

public sealed class AutomationTaskCreateParams
{
    public string WorkspacePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkflowTemplate { get; set; }

    /// <summary>
    /// <c>workspaceScope</c> (default) or <c>fullAuto</c>. Legacy <c>autoApprove</c> / <c>default</c> are accepted when reading tasks.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ApprovalPolicy { get; set; }

    /// <summary>
    /// When <see cref="WorkflowTemplate"/> is omitted, written into generated <c>workflow.md</c> as <c>workspace: project|isolated</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkspaceMode { get; set; }

    /// <summary>Optional recurring schedule. Null/absent = one-shot task.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AutomationScheduleWire? Schedule { get; set; }

    /// <summary>Optional existing thread to bind this task to.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AutomationThreadBindingWire? ThreadBinding { get; set; }

    /// <summary>
    /// Optional template id the dialog selected; persisted in front-matter for telemetry / re-apply.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TemplateId { get; set; }
}

public sealed class AutomationTaskCreateResult
{
    public string TaskId { get; set; } = string.Empty;
    public string TaskDirectory { get; set; } = string.Empty;
}

public sealed class AutomationTaskDeleteParams
{
    public string WorkspacePath { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
}

public sealed class AutomationTaskRunParams
{
    public string WorkspacePath { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
}

public sealed class AutomationTaskRunResult
{
    public AutomationTaskWire Task { get; set; } = new();
}

/// <summary>
/// Params for <see cref="AppServerMethods.AutomationTaskUpdateBinding"/>.
/// When <see cref="ThreadBinding"/> is null the task is unbound (reverts to isolated automation thread).
/// </summary>
public sealed class AutomationTaskUpdateBindingParams
{
    public string WorkspacePath { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AutomationThreadBindingWire? ThreadBinding { get; set; }
}

public sealed class AutomationTaskUpdateBindingResult
{
    public AutomationTaskWire Task { get; set; } = new();
}

/// <summary>
/// Params for <see cref="AppServerMethods.AutomationTemplateList"/>. Currently takes no fields;
/// reserved for future locale / capability filters.
/// </summary>
public sealed class AutomationTemplateListParams
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Locale { get; set; }
}

public sealed class AutomationTemplateWire
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; set; }

    /// <summary>Complete <c>workflow.md</c> contents (includes front matter).</summary>
    public string WorkflowMarkdown { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AutomationScheduleWire? DefaultSchedule { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultWorkspaceMode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultApprovalPolicy { get; set; }

    /// <summary>
    /// Suggests to the UI that this template benefits from being bound to an existing thread
    /// (e.g. feishu-reply watchers). The dialog may surface the thread picker up-front.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? NeedsThreadBinding { get; set; }

    /// <summary>Seed text for the new task description / prompt body.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultDescription { get; set; }

    /// <summary>Seed title for the New Task dialog.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultTitle { get; set; }

    /// <summary>
    /// True when this template is user-authored (editable / deletable). Absent or false for
    /// built-in templates shipped by the server. Surfaced to the desktop gallery so it can
    /// render edit / delete controls on the right cards only.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsUser { get; set; }

    /// <summary>ISO-8601 UTC, only populated for user templates.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>ISO-8601 UTC, only populated for user templates.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class AutomationTemplateListResult
{
    public List<AutomationTemplateWire> Templates { get; set; } = [];
}

/// <summary>
/// Params for <see cref="AppServerMethods.AutomationTemplateSave"/>.
/// Upsert semantics: when <see cref="Id"/> is supplied and matches an existing user template,
/// the save overwrites that template; otherwise the server assigns a fresh id.
/// </summary>
public sealed class AutomationTemplateSaveParams
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    public string Title { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; set; }

    /// <summary>Complete <c>workflow.md</c> contents (with front matter).</summary>
    public string WorkflowMarkdown { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AutomationScheduleWire? DefaultSchedule { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultWorkspaceMode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultApprovalPolicy { get; set; }

    public bool NeedsThreadBinding { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultTitle { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultDescription { get; set; }
}

/// <summary>Result of <see cref="AppServerMethods.AutomationTemplateSave"/>.</summary>
public sealed class AutomationTemplateSaveResult
{
    public AutomationTemplateWire Template { get; set; } = new();
}

/// <summary>Params for <see cref="AppServerMethods.AutomationTemplateDelete"/>.</summary>
public sealed class AutomationTemplateDeleteParams
{
    public string Id { get; set; } = string.Empty;
}

/// <summary>Result of <see cref="AppServerMethods.AutomationTemplateDelete"/>.</summary>
public sealed class AutomationTemplateDeleteResult
{
    public bool Ok { get; set; } = true;
}

/// <summary>
/// Params for <see cref="AppServerMethods.WorkspaceCommitMessageSuggest"/>.
/// </summary>
public sealed class WorkspaceCommitMessageSuggestParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string[] Paths { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxDiffChars { get; set; }
}

/// <summary>
/// Result for <see cref="AppServerMethods.WorkspaceCommitMessageSuggest"/>.
/// </summary>
public sealed class WorkspaceCommitMessageSuggestResult
{
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Params for <see cref="AppServerMethods.WelcomeSuggestions"/>.
/// </summary>
public sealed class WelcomeSuggestionsParams
{
    public SessionIdentity Identity { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxItems { get; set; }
}

public sealed class WelcomeSuggestionItem
{
    public string Title { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Result for <see cref="AppServerMethods.WelcomeSuggestions"/>.
/// </summary>
public sealed class WelcomeSuggestionsResult
{
    public List<WelcomeSuggestionItem> Items { get; set; } = [];

    public string Source { get; set; } = "none";

    public DateTimeOffset GeneratedAt { get; set; }

    public string Fingerprint { get; set; } = string.Empty;
}

// ───── channel/status (Desktop runtime status, spec Section 20) ─────

/// <summary>
/// Runtime status for one social or external channel.
/// See <see cref="AppServerMethods.ChannelStatus"/> and spec Section 20.
/// </summary>
public sealed class ChannelStatusInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// UI grouping: <c>social</c> for native C# channels; <c>external</c> for adapter channels.
    /// </summary>
    public string Category { get; set; } = "social";

    /// <summary>
    /// <c>true</c> when the channel section in merged workspace config has Enabled = true.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// <c>true</c> when the channel service was actually started and is currently registered / connected.
    /// </summary>
    public bool Running { get; set; }
}

/// <summary>
/// Result for <see cref="AppServerMethods.ChannelStatus"/>.
/// </summary>
public sealed class ChannelStatusResult
{
    public List<ChannelStatusInfo> Channels { get; set; } = [];
}

// ───── channel/list (Desktop cross-channel picker) ─────

/// <summary>
/// One discoverable session channel for <see cref="AppServerMethods.ChannelList"/> (originChannel values).
/// </summary>
public sealed class ChannelInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// UI grouping: <c>builtin</c>, <c>social</c>, <c>system</c>, or <c>external</c>.
    /// </summary>
    public string Category { get; set; } = "builtin";
}

/// <summary>
/// Result for <see cref="AppServerMethods.ChannelList"/>.
/// </summary>
public sealed class ChannelListResult
{
    public List<ChannelInfo> Channels { get; set; } = [];
}

// ───── model/list (model catalog management) ─────

// ───── provider/* (personal model provider management) ─────

public sealed class ProviderCapabilitiesWire
{
    public bool StreamingChat { get; set; }

    public bool ToolCalling { get; set; }

    public bool ModelListing { get; set; }

    public bool TokenUsageReporting { get; set; }

    public bool CachedInputUsageReporting { get; set; }

    public bool PromptCacheRequestShaping { get; set; }

    public bool ExtendedThinking { get; set; }

    public bool ToolChoiceControls { get; set; }

    public bool RawMetadataPassthrough { get; set; }

    public bool ResponsesApi { get; set; }

    public bool NativeDeferredToolLoading { get; set; }
}

public sealed class ProviderInfo
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Protocol { get; set; } = ModelProviderProtocols.OpenAIChatCompletions;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ApiKey { get; set; }

    public bool HasApiKey { get; set; }

    public string EndPoint { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? NetworkTimeoutSeconds { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxOutputTokens { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? StreamMaxRetries { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? StreamIdleTimeoutMs { get; set; }

    public bool IsImplicit { get; set; }

    /// <summary>
    /// Authentication method ("apiKey" or "chatgptOAuth"). Defaults to apiKey.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string AuthMethod { get; set; } = ModelProviderAuthMethods.ApiKey;

    /// <summary>ChatGPT account id when <see cref="AuthMethod"/> is chatgptOAuth.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChatGptAccountId { get; set; }

    /// <summary>ChatGPT plan tier (free, plus, pro, business, enterprise, edu).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChatGptPlanType { get; set; }

    public ProviderCapabilitiesWire Capabilities { get; set; } = new();
}

public sealed class ProviderListParams
{
}

public sealed class ProviderListResult
{
    public List<ProviderInfo> Providers { get; set; } = [];
}

public sealed class ProviderCreateParams
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Protocol { get; set; } = ModelProviderProtocols.OpenAIChatCompletions;

    public string ApiKey { get; set; } = string.Empty;

    public string EndPoint { get; set; } = string.Empty;

    public int? NetworkTimeoutSeconds { get; set; }

    public int? MaxOutputTokens { get; set; }

    public int? StreamMaxRetries { get; set; }

    public int? StreamIdleTimeoutMs { get; set; }

    public string? AuthMethod { get; set; }
}

public sealed class ProviderUpdateParams
{
    public string Id { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string? Protocol { get; set; }

    public string? ApiKey { get; set; }

    public string? EndPoint { get; set; }

    public int? NetworkTimeoutSeconds { get; set; }

    public int? MaxOutputTokens { get; set; }

    public int? StreamMaxRetries { get; set; }

    public int? StreamIdleTimeoutMs { get; set; }

    public string? AuthMethod { get; set; }
}

public sealed class ProviderDeleteParams
{
    public string Id { get; set; } = string.Empty;
}

public sealed class ProviderDeleteResult
{
    public bool Deleted { get; set; }
}

public sealed class ProviderMutationResult
{
    public ProviderInfo Provider { get; set; } = new();
}

public sealed class ProviderTestParams
{
    public string? ProviderId { get; set; }

    public string? Protocol { get; set; }

    public string? ApiKey { get; set; }

    public string? EndPoint { get; set; }

    public int? NetworkTimeoutSeconds { get; set; }

    public int? MaxOutputTokens { get; set; }

    public int? StreamMaxRetries { get; set; }

    public int? StreamIdleTimeoutMs { get; set; }
}

public sealed class ProviderTestResult
{
    public bool Success { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderId { get; set; }

    public string Protocol { get; set; } = ModelProviderProtocols.OpenAIChatCompletions;

    public List<ModelCatalogItem> Models { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; set; }
}

/// <summary>Params for <see cref="AppServerMethods.AuthOpenAiLogin"/>.</summary>
public sealed class AuthOpenAiLoginParams
{
    /// <summary>Provider id to bind to ChatGPT subscription; defaults to "openai".</summary>
    public string? ProviderId { get; set; }

    /// <summary>When false, suppresses browser launch — only the URL is returned via the authorizeUrl notification.</summary>
    public bool? OpenBrowser { get; set; }
}

/// <summary>Params for <see cref="AppServerMethods.AuthOpenAiLogout"/>.</summary>
public sealed class AuthOpenAiLogoutParams
{
    public string? ProviderId { get; set; }
}

/// <summary>Server → Client notification payload for <see cref="AppServerMethods.AuthOpenAiAuthorizeUrl"/>.</summary>
public sealed class AuthOpenAiAuthorizeUrlNotification
{
    public string Url { get; set; } = string.Empty;
    public int CallbackPort { get; set; }
}

/// <summary>One rate-limit window snapshot (5h primary or weekly secondary).</summary>
public sealed class AuthOpenAiUsageWindow
{
    /// <summary>0-100 percent of the window's quota consumed.</summary>
    public int UsedPercent { get; set; }
    /// <summary>Window length in seconds (e.g. 18000 for 5h, 604800 for 7d).</summary>
    public int WindowSeconds { get; set; }
    /// <summary>UTC timestamp when this window rolls over and the counter resets to zero.</summary>
    public DateTimeOffset ResetAt { get; set; }
}

/// <summary>Credit balance (only populated for credit-based accounts).</summary>
public sealed class AuthOpenAiUsageCredits
{
    public bool HasCredits { get; set; }
    public bool Unlimited { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Balance { get; set; }
}

/// <summary>
/// Result of <see cref="AppServerMethods.AuthOpenAiUsage"/> and payload of
/// <see cref="AppServerMethods.AuthOpenAiUsageChanged"/>.
/// </summary>
public sealed class AuthOpenAiUsageResult
{
    /// <summary>True when a snapshot is available; false when not signed in or no fetch has succeeded yet.</summary>
    public bool Available { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlanType { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AuthOpenAiUsageWindow? Primary { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AuthOpenAiUsageWindow? Secondary { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AuthOpenAiUsageCredits? Credits { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LimitReachedKind { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? FetchedAt { get; set; }
}

/// <summary>Common status result returned by login/logout/status.</summary>
public sealed class AuthOpenAiStatusResult
{
    public bool LoggedIn { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccountId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlanType { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Email { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? LastRefresh { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }

    /// <summary>Provider id this status applies to (after a successful login). Omitted for logout.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderId { get; set; }
}

/// <summary>
/// Params for <see cref="AppServerMethods.ModelList"/>.
/// </summary>
public sealed class ModelListParams
{
    /// <summary>
    /// Optional provider id. When omitted, the workspace-selected provider is used.
    /// </summary>
    public string? ProviderId { get; set; }
}

/// <summary>
/// One provider model entry.
/// </summary>
public sealed class ModelCatalogItem
{
    public string Id { get; set; } = string.Empty;

    public string OwnedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ModelReasoningCapability? Reasoning { get; set; }
}

public sealed class ModelReasoningCapability
{
    public bool SupportsDisable { get; set; } = true;

    public List<ModelReasoningEffortOption> SupportedEfforts { get; set; } = [];

    public ReasoningEffort DefaultEffort { get; set; } = ReasoningEffort.Medium;

    public List<ReasoningOutput> SupportedOutputs { get; set; } = [];

    public ReasoningOutput DefaultOutput { get; set; } = ReasoningOutput.Full;
}

public sealed class ModelReasoningEffortOption
{
    public ReasoningEffort Effort { get; set; }

    public string Label { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Result for <see cref="AppServerMethods.ModelList"/>.
/// </summary>
public sealed class ModelListResult
{
    public bool Success { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Protocol { get; set; }

    public List<ModelCatalogItem> Models { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; set; }
}

// ───── workspace/config/update (workspace config management) ─────

/// <summary>
/// Params for <see cref="AppServerMethods.WorkspaceConfigUpdate"/>.
/// </summary>
public sealed class WorkspaceConfigUpdateParams
{
    /// <summary>
    /// Workspace-selected provider id. Null/empty removes the workspace provider key.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ProviderId { get; set; }

    /// <summary>
    /// Workspace default model. Null/empty/"Default" removes the workspace model key.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Model { get; set; }

    /// <summary>
    /// Workspace-level toggle for personalized welcome suggestions. Null removes the workspace override.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? WelcomeSuggestionsEnabled { get; set; }

    /// <summary>
    /// Workspace-level toggle for agent self-learning. Null removes the workspace override.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? SkillsSelfLearningEnabled { get; set; }

    /// <summary>
    /// Workspace-level toggle for long-term memory consolidation. Null removes the workspace override.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? MemoryAutoConsolidateEnabled { get; set; }

    /// <summary>
    /// Workspace-level toggle for scheduled Dreams. Null removes the workspace override.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? DreamsEnabled { get; set; }

    /// <summary>
    /// Workspace-level Dreams interval as a positive TimeSpan string. Null removes the workspace override.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? DreamsInterval { get; set; }

    /// <summary>
    /// Workspace-level Dreams recent thread lookback. Null removes the workspace override.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? DreamsThreadLookbackCount { get; set; }

    /// <summary>
    /// Workspace-level Dreams auto-apply toggle. Null removes the workspace override.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? DreamsAutoApply { get; set; }

    /// <summary>
    /// Workspace default approval policy. Null removes the workspace override.
    /// Supported values: default, autoApprove.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultApprovalPolicy { get; set; }

    /// <summary>
    /// Workspace-level toggle for the built-in LSP tool. Null leaves the value unchanged.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? ToolsLspEnabled { get; set; }

    /// <summary>
    /// Workspace-level reasoning configuration. Null removes the workspace Reasoning section.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public AppConfig.ReasoningConfig? Reasoning { get; set; }
}

/// <summary>
/// Result for <see cref="AppServerMethods.WorkspaceConfigUpdate"/>.
/// </summary>
public sealed class WorkspaceConfigUpdateResult
{
    /// <summary>
    /// Persisted workspace provider id after normalization.
    /// Null means the provider key was removed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ProviderId { get; set; }

    /// <summary>
    /// Persisted workspace model after normalization.
    /// Null means the model key was removed (workspace default behavior).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Model { get; set; }

    /// <summary>
    /// Persisted workspace personalized-welcome-suggestions toggle after normalization.
    /// Null means the workspace override was removed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? WelcomeSuggestionsEnabled { get; set; }

    /// <summary>
    /// Persisted workspace self-learning toggle after normalization.
    /// Null means the workspace override was removed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? SkillsSelfLearningEnabled { get; set; }

    /// <summary>
    /// Persisted workspace long-term memory consolidation toggle after normalization.
    /// Null means the workspace override was removed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? MemoryAutoConsolidateEnabled { get; set; }

    /// <summary>
    /// Persisted workspace Dreams toggle after normalization.
    /// Null means the workspace override was removed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? DreamsEnabled { get; set; }

    /// <summary>
    /// Persisted workspace Dreams interval after normalization.
    /// Null means the workspace override was removed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? DreamsInterval { get; set; }

    /// <summary>
    /// Persisted workspace Dreams recent thread lookback after normalization.
    /// Null means the workspace override was removed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? DreamsThreadLookbackCount { get; set; }

    /// <summary>
    /// Persisted workspace Dreams auto-apply toggle after normalization.
    /// Null means the workspace override was removed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? DreamsAutoApply { get; set; }

    /// <summary>
    /// Persisted workspace default approval policy after normalization.
    /// Null means the workspace override was removed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? DefaultApprovalPolicy { get; set; }

    /// <summary>
    /// Persisted workspace LSP tool toggle after normalization.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? ToolsLspEnabled { get; set; }

    /// <summary>
    /// Persisted workspace reasoning configuration after normalization.
    /// Null means the workspace Reasoning section was removed or is absent.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public AppConfig.ReasoningConfig? Reasoning { get; set; }
}

/// <summary>
/// Params for <see cref="AppServerMethods.WorkspaceConfigSchema"/>.
/// Reserved for future filters.
/// </summary>
public sealed class WorkspaceConfigSchemaParams
{
}

// ───── dreams/* (workspace Dreams management) ─────

/// <summary>
/// Params for <see cref="AppServerMethods.DreamsStatus"/>.
/// </summary>
public sealed class DreamsStatusParams
{
}

/// <summary>
/// Params for <see cref="AppServerMethods.DreamsRun"/>.
/// </summary>
public sealed class DreamsRunParams
{
}

/// <summary>
/// Params for <see cref="AppServerMethods.DreamsCreate"/>.
/// </summary>
public sealed class DreamsCreateParams
{
    public List<string>? ThreadIds { get; set; }

    public int? ThreadLookbackCount { get; set; }

    public string? Instructions { get; set; }

    public string? Model { get; set; }
}

public sealed class DreamsRunIdParams
{
    public string RunId { get; set; } = string.Empty;
}

public sealed class DreamsListParams
{
    public bool IncludeArchived { get; set; }
}

/// <summary>
/// Result for <see cref="AppServerMethods.DreamsStatus"/> and <see cref="AppServerMethods.DreamsRun"/>.
/// </summary>
public sealed class DreamsStatusResult
{
    public bool Enabled { get; set; }

    public string Interval { get; set; } = "24:00:00";

    public int ThreadLookbackCount { get; set; }

    public bool AutoApply { get; set; }

    public int HistoryTailChars { get; set; }

    public int MinCompletedTurnsSinceLastRun { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? NextRunAt { get; set; }

    public bool Running { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActiveDreamStoreId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public DreamsRunStateWire? LastRun { get; set; }
}

public sealed class DreamsRunResult
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public DreamsRunStateWire? Run { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActiveDreamStoreId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DreamsRunPreviewWire? Preview { get; set; }
}

public sealed class DreamsListResult
{
    public List<DreamsRunStateWire> Runs { get; set; } = [];
}

/// <summary>
/// Wire projection of the latest Dreams run state.
/// </summary>
public sealed class DreamsRunStateWire
{
    public string Id { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? EndedAt { get; set; }

    public int ProcessedThreadCount { get; set; }

    public int CandidateThreadCount { get; set; }

    public bool DreamWritten { get; set; }

    public bool HistoryWritten { get; set; }

    public int TopicFilesWritten { get; set; }

    public int TopicFilesDeleted { get; set; }

    public int EvidenceSearchCount { get; set; }

    public int EvidenceReadCount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputStoreId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReviewStatus { get; set; }

    public bool AutoApplied { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorType { get; set; }

    public List<string> EvidenceThreadIds { get; set; } = [];

    public List<string> WrittenPaths { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThreadId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TurnId { get; set; }

    public List<string> TurnIds { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Trigger { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TokenUsageInfo? Usage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InputManifestPath { get; set; }
}

public sealed class DreamsRunPreviewWire
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActiveStoreId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputStoreId { get; set; }

    public string ActiveIndexMarkdown { get; set; } = string.Empty;

    public string OutputIndexMarkdown { get; set; } = string.Empty;

    public List<string> ActiveTopicPaths { get; set; } = [];

    public List<string> OutputTopicPaths { get; set; } = [];
}

// ───── memory/reset (memory management) ─────

/// <summary>
/// Result for <see cref="AppServerMethods.MemoryReset"/>.
/// </summary>
public sealed class MemoryResetResult
{
}

/// <summary>
/// Result for <see cref="AppServerMethods.WorkspaceConfigSchema"/>.
/// </summary>
public sealed class WorkspaceConfigSchemaResult
{
    public List<ConfigSchemaSection> Sections { get; set; } = [];
}

/// <summary>
/// Params for <see cref="AppServerMethods.WorkspaceConfigChanged"/>.
/// </summary>
public sealed class WorkspaceConfigChangedParams
{
    /// <summary>
    /// RPC source method that triggered the workspace config change.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Logical workspace config regions changed by this mutation.
    /// </summary>
    public List<string> Regions { get; set; } = [];

    /// <summary>
    /// Server-side UTC timestamp when the change event was emitted.
    /// </summary>
    public DateTimeOffset ChangedAt { get; set; }
}

// ───── mcp/* (MCP server management) ─────

public sealed class McpServerConfigWire
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string Transport { get; set; } = "stdio";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Command { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BuiltinModule { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Args { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Env { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? EnvVars { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cwd { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BearerTokenEnvVar { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? HttpHeaders { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? EnvHttpHeaders { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? StartupTimeoutSec { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ToolTimeoutSec { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public McpServerOriginWire? Origin { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ReadOnly { get; set; }
}

public sealed class McpServerOriginWire
{
    public string Kind { get; set; } = "workspace";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PluginId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PluginDisplayName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeclaredName { get; set; }
}

public sealed class McpListResult
{
    public List<McpServerConfigWire> Servers { get; set; } = [];
}

public sealed class McpGetParams
{
    public string Name { get; set; } = string.Empty;
}

public sealed class McpGetResult
{
    public McpServerConfigWire Server { get; set; } = new();
}

public sealed class McpUpsertParams
{
    public McpServerConfigWire Server { get; set; } = new();
}

public sealed class McpUpsertResult
{
    public McpServerConfigWire Server { get; set; } = new();
}

public sealed class McpRemoveParams
{
    public string Name { get; set; } = string.Empty;
}

public sealed class McpRemoveResult
{
    public bool Removed { get; set; }
}

public sealed class McpStatusInfoWire
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string StartupState { get; set; } = "idle";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ToolCount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ResourceCount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ResourceTemplateCount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastError { get; set; }

    public string Transport { get; set; } = "stdio";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public McpServerOriginWire? Origin { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ReadOnly { get; set; }
}

public sealed class McpStatusListResult
{
    public List<McpStatusInfoWire> Servers { get; set; } = [];
}

public sealed class McpTestParams
{
    public McpServerConfigWire Server { get; set; } = new();
}

public sealed class McpTestResult
{
    public bool Success { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ToolCount { get; set; }
}

// ───── externalChannel/* (external channel management) ─────

public sealed class ExternalChannelConfigWire
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string Transport { get; set; } = "subprocess";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Command { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BuiltinModule { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Args { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkingDirectory { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Env { get; set; }
}

public sealed class ExternalChannelListResult
{
    public List<ExternalChannelConfigWire> Channels { get; set; } = [];
}

public sealed class ExternalChannelGetParams
{
    public string Name { get; set; } = string.Empty;
}

public sealed class ExternalChannelGetResult
{
    public ExternalChannelConfigWire Channel { get; set; } = new();
}

public sealed class ExternalChannelUpsertParams
{
    public ExternalChannelConfigWire Channel { get; set; } = new();
}

public sealed class ExternalChannelUpsertResult
{
    public ExternalChannelConfigWire Channel { get; set; } = new();
}

public sealed class ExternalChannelRemoveParams
{
    public string Name { get; set; } = string.Empty;
}

public sealed class ExternalChannelRemoveResult
{
    public bool Removed { get; set; }
}

public sealed class ExternalChannelLogsParams
{
    public string Name { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Tail { get; set; }
}

public sealed class ExternalChannelLogsResult
{
    public string Name { get; set; } = string.Empty;
    public List<string> Lines { get; set; } = [];
}

// ───── subagent/profiles/* (SubAgent profile management) ─────

public sealed class SubAgentProfileWriteWire
{
    public string Runtime { get; set; } = "native";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Bin { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Args { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Env { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? EnvPassthrough { get; set; }

    public string WorkingDirectoryMode { get; set; } = "workspace";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SupportsStreaming { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SupportsResume { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SupportsModelSelection { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InputFormat { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputFormat { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InputMode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InputArgTemplate { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InputEnvKey { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResumeArgTemplate { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResumeSessionIdJsonPath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResumeSessionIdRegex { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputJsonPath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputInputTokensJsonPath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputOutputTokensJsonPath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputTotalTokensJsonPath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputFileArgTemplate { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ReadOutputFile { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DeleteOutputFileAfterRead { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxOutputBytes { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Timeout { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TrustLevel { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? PermissionModeMapping { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? SanitizationRules { get; set; }
}

public sealed class SubAgentProfileDiagnosticWire
{
    public bool Enabled { get; set; }

    public bool BinaryResolved { get; set; }

    public bool HiddenFromPrompt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HiddenReason { get; set; }

    public List<string> Warnings { get; set; } = [];
}

public sealed class SubAgentProfileEntryWire
{
    public string Name { get; set; } = string.Empty;

    public bool IsBuiltIn { get; set; }

    public bool IsTemplate { get; set; }

    public bool HasWorkspaceOverride { get; set; }

    public bool IsDefault { get; set; }

    public bool Enabled { get; set; }

    public SubAgentProfileWriteWire Definition { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SubAgentProfileWriteWire? BuiltInDefaults { get; set; }

    public SubAgentProfileDiagnosticWire Diagnostic { get; set; } = new();
}

public sealed class SubAgentProfileListResult
{
    public List<SubAgentProfileEntryWire> Profiles { get; set; } = [];

    public string DefaultName { get; set; } = string.Empty;

    public SubAgentSettingsWire Settings { get; set; } = new();
}

public sealed class SubAgentSettingsWire
{
    public bool ExternalCliSessionResumeEnabled { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; set; }

    public int MinWaitTimeoutMs { get; set; } = SubAgentWaitAgentTimeoutOptions.BuiltInMinTimeoutMs;

    public int DefaultWaitTimeoutMs { get; set; } = SubAgentWaitAgentTimeoutOptions.BuiltInDefaultTimeoutMs;

    public int MaxWaitTimeoutMs { get; set; } = SubAgentWaitAgentTimeoutOptions.BuiltInMaxTimeoutMs;
}

public sealed class SubAgentSettingsUpdateParams
{
    public bool? ExternalCliSessionResumeEnabled { get; set; }

    public string? Model { get; set; }

    public int? MinWaitTimeoutMs { get; set; }

    public int? DefaultWaitTimeoutMs { get; set; }

    public int? MaxWaitTimeoutMs { get; set; }
}

public sealed class SubAgentSettingsUpdateResult
{
    public SubAgentSettingsWire Settings { get; set; } = new();
}

public sealed class SubAgentProfileSetEnabledParams
{
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; }
}

public sealed class SubAgentProfileSetEnabledResult
{
    public SubAgentProfileEntryWire Profile { get; set; } = new();
}

public sealed class SubAgentProfileUpsertParams
{
    public string Name { get; set; } = string.Empty;

    public SubAgentProfileWriteWire Definition { get; set; } = new();
}

public sealed class SubAgentProfileUpsertResult
{
    public SubAgentProfileEntryWire Profile { get; set; } = new();
}

public sealed class SubAgentProfileRemoveParams
{
    public string Name { get; set; } = string.Empty;
}

public sealed class SubAgentProfileRemoveResult
{
    public bool Removed { get; set; }
}

// ───── Wire protocol method name constants ─────

public static class AppServerMethods
{
    // Client → Server requests
    public const string Initialize = "initialize";

    /// <summary>
    /// Lists known origin channels for cross-channel thread visibility (Desktop settings).
    /// </summary>
    public const string ChannelList = "channel/list";

    /// <summary>
    /// Returns runtime enabled/running status for all configured social and external channels (Desktop channels panel).
    /// See spec Section 20.
    /// </summary>
    public const string ChannelStatus = "channel/status";
    public const string ProviderList = "provider/list";
    public const string ProviderCreate = "provider/create";
    public const string ProviderUpdate = "provider/update";
    public const string ProviderDelete = "provider/delete";
    public const string ProviderTest = "provider/test";
    public const string ModelList = "model/list";
    public const string AuthOpenAiStatus = "auth/openai/status";
    public const string AuthOpenAiLogin = "auth/openai/login";
    public const string AuthOpenAiLogout = "auth/openai/logout";
    public const string AuthOpenAiUsage = "auth/openai/usage";
    public const string ThreadStart = "thread/start";
    public const string ThreadFork = "thread/fork";
    public const string WorktreeCreateAndFork = "worktree/createAndFork";
    public const string WorktreeCreateAndStart = "worktree/createAndStart";
    public const string ThreadWorktreeHandoff = "thread/worktree/handoff";
    public const string WorktreeList = "worktree/list";
    public const string WorktreeStatus = "worktree/status";
    public const string ThreadResume = "thread/resume";
    public const string ThreadList = "thread/list";
    public const string ThreadRead = "thread/read";
    public const string ThreadGoalGet = "thread/goal/get";
    public const string ThreadGoalSet = "thread/goal/set";
    public const string ItemWidgetStateSet = "item/widget-state/set";
    public const string ThreadGoalClear = "thread/goal/clear";
    public const string ThreadCompactStart = "thread/compact/start";
    public const string ThreadMemoryConsolidateStart = "thread/memory/consolidate/start";
    public const string ThreadMaintenanceInterrupt = "thread/maintenance/interrupt";
    public const string ThreadRollback = "thread/rollback";
    public const string ThreadSubscribe = "thread/subscribe";
    public const string ThreadUnsubscribe = "thread/unsubscribe";
    public const string ThreadPause = "thread/pause";
    public const string ThreadArchive = "thread/archive";
    public const string ThreadUnarchive = "thread/unarchive";
    public const string ThreadDelete = "thread/delete";
    public const string ThreadRename = "thread/rename";
    public const string ThreadModeSet = "thread/mode/set";
    public const string ThreadConfigUpdate = "thread/config/update";
    public const string TurnStart = "turn/start";
    public const string TurnEnqueue = "turn/enqueue";
    public const string TurnQueueRemove = "turn/queue/remove";
    public const string TurnQueueReorder = "turn/queue/reorder";
    public const string TurnSteer = "turn/steer";
    public const string TurnInterrupt = "turn/interrupt";
    public const string TerminalList = "terminal/list";
    public const string TerminalRead = "terminal/read";
    public const string TerminalWrite = "terminal/write";
    public const string TerminalStop = "terminal/stop";
    public const string TerminalClean = "terminal/clean";

    /// <summary>Generate a suggested git commit message from thread context and diff (Desktop).</summary>
    public const string WorkspaceCommitMessageSuggest = "workspace/commitMessage/suggest";
    public const string WelcomeSuggestions = "welcome/suggestions";
    public const string WorkspaceConfigSchema = "workspace/config/schema";
    public const string WorkspaceConfigUpdate = "workspace/config/update";
    public const string WorkspaceConfigChanged = "workspace/configChanged";
    public const string DreamsStatus = "dreams/status";
    public const string DreamsRun = "dreams/run";
    public const string DreamsCreate = "dreams/create";
    public const string DreamsGet = "dreams/get";
    public const string DreamsList = "dreams/list";
    public const string DreamsCancel = "dreams/cancel";
    public const string DreamsArchive = "dreams/archive";
    public const string DreamsApply = "dreams/apply";
    public const string DreamsDiscard = "dreams/discard";
    public const string MemoryReset = "memory/reset";

    /// <summary>Aggregate usage telemetry pull (spec Section 27A). Available when tracing is enabled.</summary>
    public const string UsageSummary = "usage/summary";

    /// <summary>Per-day token usage for activity charts (spec Section 27A.3). Available when tracing is enabled.</summary>
    public const string UsageTimeseries = "usage/timeseries";

    /// <summary>Profile activity insights: most-used model/reasoning, skill counts, thread count (spec Section 27A.5). Available when tracing is enabled.</summary>
    public const string ProfileInsights = "profile/insights";

    public const string McpList = "mcp/list";
    public const string McpGet = "mcp/get";
    public const string McpUpsert = "mcp/upsert";
    public const string McpRemove = "mcp/remove";
    public const string ExternalChannelList = "externalChannel/list";
    public const string ExternalChannelGet = "externalChannel/get";
    public const string ExternalChannelUpsert = "externalChannel/upsert";
    public const string ExternalChannelRemove = "externalChannel/remove";
    public const string ExternalChannelLogs = "externalChannel/logs";
    public const string SubAgentProfileList = "subagent/profiles/list";
    public const string SubAgentSettingsUpdate = "subagent/settings/update";
    public const string SubAgentProfileSetEnabled = "subagent/profiles/setEnabled";
    public const string SubAgentProfileUpsert = "subagent/profiles/upsert";
    public const string SubAgentProfileRemove = "subagent/profiles/remove";
    public const string SubAgentChildrenList = "subagent/children/list";
    public const string SubAgentSendMessage = "subagent/sendMessage";
    public const string SubAgentFollowupTask = "subagent/followupTask";
    public const string SubAgentClose = "subagent/close";
    public const string McpStatusList = "mcp/status/list";
    public const string McpTest = "mcp/test";

    // Client → Server notification (no id)
    public const string Initialized = "initialized";

    // Server → Client notifications
    public const string ThreadStarted = "thread/started";
    public const string ThreadUpdated = "thread/updated";
    public const string ThreadDeleted = "thread/deleted";
    public const string ThreadResumed = "thread/resumed";
    public const string ThreadStatusChanged = "thread/statusChanged";
    /// <summary>Workspace-level runtime snapshot broadcast for sidebar activity indicators.</summary>
    public const string ThreadRuntimeChanged = "thread/runtimeChanged";
    public const string ThreadQueueUpdated = "thread/queue/updated";
    /// <summary>Server broadcast when a thread's display name changes (rename RPC or first-message title).</summary>
    public const string ThreadRenamed = "thread/renamed";
    public const string ThreadGoalUpdated = "thread/goal/updated";
    public const string ThreadGoalCleared = "thread/goal/cleared";
    public const string TurnStarted = "turn/started";
    public const string TurnCompleted = "turn/completed";
    public const string TurnFailed = "turn/failed";
    public const string TurnCancelled = "turn/cancelled";
    public const string ItemStarted = "item/started";
    public const string ItemAgentMessageDelta = "item/agentMessage/delta";
    public const string ItemReasoningDelta = "item/reasoning/delta";
    public const string ItemCommandExecutionOutputDelta = "item/commandExecution/outputDelta";
    public const string ItemToolCallArgumentsDelta = "item/toolCall/argumentsDelta";
    public const string ItemCompleted = "item/completed";
    public const string ItemApprovalResolved = "item/approval/resolved";
    public const string ItemRequestUserInputResolved = "item/tool/requestUserInput/resolved";
    public const string TerminalStarted = "terminal/started";
    public const string TerminalOutputDelta = "terminal/outputDelta";
    public const string TerminalCompleted = "terminal/completed";
    public const string TerminalStalled = "terminal/stalled";
    public const string TerminalCleaned = "terminal/cleaned";

    // Server → Client request (bidirectional approval)
    public const string ItemApprovalRequest = "item/approval/request";
    public const string ItemRequestUserInput = "item/tool/requestUserInput";
    public const string ItemToolCall = "item/tool/call";

    /// <summary>
    /// Server → App request: read a <c>ui://</c> Interactive Tool UI resource owned by the app.
    /// Brokered from the host's <c>ui/resource/read</c>. See appserver-protocol.md §11.3.1.
    /// </summary>
    public const string ItemResourceRead = "item/resource/read";

    // Server → Client notification (SubAgent progress)
    public const string SubAgentProgress = "subagent/progress";
    public const string SubAgentGraphChanged = "subagent/graphChanged";

    // Server → Client notification (incremental token usage)
    public const string ItemUsageDelta = "item/usage/delta";

    // Server → Client notification (system maintenance events)
    public const string SystemEvent = "system/event";

    // Server → Client notification (plan/todo progress updates, spec Section 6.8)
    public const string PlanUpdated = "plan/updated";

    // Server → Client notification (cron/heartbeat job result, spec Section 6.9)
    public const string SystemJobResult = "system/jobResult";

    // Server → Client notification (cron job list sync, spec Section 16.7)
    public const string CronStateChanged = "cron/stateChanged";
    public const string McpStatusUpdated = "mcp/status/updated";

    /// <summary>
    /// Server → Client notification fired during <c>auth/openai/login</c> with the authorization URL.
    /// Lets the desktop UI render a "Copy URL" affordance while the request is pending.
    /// </summary>
    public const string AuthOpenAiAuthorizeUrl = "auth/openai/authorizeUrl";

    /// <summary>
    /// Server → Client notification fired whenever the ChatGPT usage / rate-limit snapshot changes.
    /// Payload follows <see cref="AuthOpenAiUsageResult"/>; null payload means "no longer available".
    /// </summary>
    public const string AuthOpenAiUsageChanged = "auth/openai/usageChanged";

    // Server → Client requests (external channel adapter, ext-channel-adapter spec §6)
    public const string ExtChannelSend = "ext/channel/send";
    public const string ExtChannelToolCall = "ext/channel/toolCall";
    public const string ExtChannelHeartbeat = "ext/channel/heartbeat";

    // Server → Client requests (ACP tool proxy, appserver-protocol.md §11.2)
    public const string ExtAcpFsReadTextFile = "ext/acp/fs/readTextFile";
    public const string ExtAcpFsWriteTextFile = "ext/acp/fs/writeTextFile";
    public const string ExtAcpTerminalCreate = "ext/acp/terminal/create";
    public const string ExtAcpTerminalGetOutput = "ext/acp/terminal/getOutput";
    public const string ExtAcpTerminalWaitForExit = "ext/acp/terminal/waitForExit";
    public const string ExtAcpTerminalKill = "ext/acp/terminal/kill";
    public const string ExtAcpTerminalRelease = "ext/acp/terminal/release";

    // Server → Client requests (Desktop Node REPL + IAB browser runtime)
    public const string ExtNodeReplEvaluate = "ext/nodeRepl/evaluate";
    public const string ExtNodeReplCancel = "ext/nodeRepl/cancel";

    // Client → Server requests (cron management, spec Section 16)
    public const string CronList = "cron/list";
    public const string CronRemove = "cron/remove";
    public const string CronEnable = "cron/enable";
    public const string CronRun = "cron/run";

    // Client → Server requests (heartbeat management, spec Section 17)
    public const string HeartbeatTrigger = "heartbeat/trigger";

    // Client → Server requests (skills management, spec Section 18)
    public const string SkillsList = "skills/list";
    public const string SkillsRead = "skills/read";
    public const string SkillsView = "skills/view";
    public const string SkillsRestoreOriginal = "skills/restoreOriginal";
    public const string SkillsSetEnabled = "skills/setEnabled";
    public const string SkillsUninstall = "skills/uninstall";

    // Client → Server requests (plugin management)
    public const string PluginList = "plugin/list";
    public const string PluginView = "plugin/view";
    public const string PluginInstall = "plugin/install";
    public const string PluginRemove = "plugin/remove";
    public const string PluginSetEnabled = "plugin/setEnabled";

    // Client → Server requests (command management, spec Section 19)
    public const string CommandList = "command/list";
    public const string CommandExecute = "command/execute";

    // Client → Server requests (automations)
    public const string AutomationTaskList = "automation/task/list";
    public const string AutomationTaskRead = "automation/task/read";
    public const string AutomationTaskCreate = "automation/task/create";
    public const string AutomationTaskRun = "automation/task/run";
    public const string AutomationTaskDelete = "automation/task/delete";

    /// <summary>Replaces or clears a task's thread binding without rewriting other fields.</summary>
    public const string AutomationTaskUpdateBinding = "automation/task/updateBinding";

    /// <summary>Returns the catalog of built-in and user local task templates (gallery + create-dialog preset source).</summary>
    public const string AutomationTemplateList = "automation/template/list";

    /// <summary>Creates or updates a user-authored automation template (upsert by id).</summary>
    public const string AutomationTemplateSave = "automation/template/save";

    /// <summary>Deletes a user-authored automation template. Built-in ids are rejected.</summary>
    public const string AutomationTemplateDelete = "automation/template/delete";

    // Server → Client notification (automations)
    public const string AutomationTaskUpdated = "automation/task/updated";
}
