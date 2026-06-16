using System.Text.Json;
using System.Text.Json.Serialization;

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
    /// Whether the client can render Interactive Tool UI (MCP Apps): the host serves a tool's
    /// <c>ui://</c> resource in a sandboxed iframe and drives the <c>ui/*</c> bridge. Default false —
    /// a non-declaring client receives the text fallback, and the host does not honor <c>ui/*</c>
    /// host methods for it. See tool-result-presentation.md §3.
    /// </summary>
    public bool? InteractiveToolUi { get; set; }

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
    /// Server supports the built-in tool catalog method (<c>tool/list</c>, spec Section 18A).
    /// Always true for servers built on this protocol version; the catalog is derived from
    /// server reflection and has no workspace dependency.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ToolCatalog { get; set; }

    /// <summary>
    /// Server supports Agent Profile Markdown management methods
    /// (<c>agent/profiles/list</c>, <c>agent/profiles/upsert</c>, etc.).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AgentProfileManagement { get; set; }

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
