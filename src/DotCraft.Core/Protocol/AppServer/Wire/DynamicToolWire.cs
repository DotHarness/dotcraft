using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace DotCraft.Protocol.AppServer;


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
/// Params for <c>ui/open-link</c> (host → server): a UI‑initiated request to open an external
/// link. The host enforces the scheme policy (<c>https:</c>/<c>mailto:</c>, plus the bound app's
/// declared <c>nativeApplication.protocol</c> deep‑link scheme — M‑v) and records the open on the
/// App Binding audit trail; the actual navigation happens in the Desktop host. Decoupled — no
/// turn/item. See tool-result-presentation.md §7, §11.
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
/// Decoupled — no turn/item. See tool-result-presentation.md §7.
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
