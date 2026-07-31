using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;


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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThreadId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BindingId { get; set; }
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
    public string AuthStatus { get; set; } = "unsupported";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FailureReason { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public McpServerOriginWire? Origin { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ReadOnly { get; set; }
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

// ───── mcpServer/* (live MCP runtime) ─────

/// <summary>Parameters for the paginated live MCP server status query.</summary>
public sealed class McpServerStatusListParams
{
    public string? ThreadId { get; set; }
    public string? Cursor { get; set; }
    public int? Limit { get; set; }
    public string? Detail { get; set; }
}

/// <summary>Live MCP runtime status including safe origin and authentication state.</summary>
public sealed class McpServerRuntimeStatusWire
{
    public string Name { get; set; } = string.Empty;
    public string DeclaredName { get; set; } = string.Empty;
    public string RuntimeName { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string StartupState { get; set; } = "idle";
    public string? LastError { get; set; }
    public string AuthState { get; set; } = "notRequired";
    public object? ServerInfo { get; set; }
    public Dictionary<string, McpRuntimeToolWire> Tools { get; set; } = new(StringComparer.Ordinal);
    public List<object> Resources { get; set; } = [];
    public List<object> ResourceTemplates { get; set; } = [];
    public string AuthStatus { get; set; } = "unsupported";
    public string Transport { get; set; } = "stdio";
    public int ToolCount { get; set; }
    public int ResourceCount { get; set; }
    public int ResourceTemplateCount { get; set; }
    public long? Generation { get; set; }
    public McpServerOriginWire? Origin { get; set; }
    public string? FailureReason { get; set; }
}

/// <summary>Stable tool inventory entry returned with an MCP runtime status.</summary>
public sealed class McpRuntimeToolWire
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("description"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Description { get; set; }
    [JsonPropertyName("inputSchema")] public JsonElement InputSchema { get; set; }
    [JsonPropertyName("outputSchema"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public JsonElement? OutputSchema { get; set; }
}

/// <summary>Result for <c>mcpServerStatus/list</c>.</summary>
public sealed class McpServerStatusListResult
{
    public List<McpServerRuntimeStatusWire> Data { get; set; } = [];
    public string? NextCursor { get; set; }
}

/// <summary>Parameters for a raw MCP resource read.</summary>
public sealed class McpServerResourceReadParams
{
    public string? ThreadId { get; set; }
    public string Server { get; set; } = string.Empty;
    public string Uri { get; set; } = string.Empty;
}

/// <summary>Raw resource contents returned by the MCP server.</summary>
public sealed class McpServerResourceReadResult
{
    public object? Contents { get; set; }
}

/// <summary>Parameters for a host-initiated MCP tool call.</summary>
public sealed class McpServerToolCallParams
{
    public string ThreadId { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public string Tool { get; set; } = string.Empty;
    public Dictionary<string, JsonElement>? Arguments { get; set; }

    [JsonPropertyName("_meta")]
    public JsonElement? Meta { get; set; }
}

/// <summary>Raw MCP tool result. Projection into model history is intentionally separate.</summary>
public sealed class McpServerToolCallResult
{
    public object? Content { get; set; }
    public object? StructuredContent { get; set; }
    public bool IsError { get; set; }

    [JsonPropertyName("_meta")]
    public object? Meta { get; set; }
}

/// <summary>Reloads the effective MCP runtime configuration.</summary>
/// <summary>Reload acknowledgement.</summary>
public sealed class McpServerReloadResult;

/// <summary>Parameters for starting an MCP OAuth login.</summary>
public sealed class McpServerOAuthLoginParams
{
    public string Name { get; set; } = string.Empty;
    public string? ThreadId { get; set; }
    public List<string>? Scopes { get; set; }
    public double? TimeoutSecs { get; set; }
}

/// <summary>Authorization URL returned when an MCP OAuth login begins.</summary>
public sealed class McpServerOAuthLoginResult
{
    public string AuthorizationUrl { get; set; } = string.Empty;
}

/// <summary>Terminal OAuth login notification.</summary>
public sealed class McpServerOAuthLoginCompletedNotification
{
    public string Name { get; set; } = string.Empty;
    public string? ThreadId { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}

/// <summary>Server-to-client MCP elicitation request.</summary>
public sealed class McpServerElicitationRequestParams
{
    public string? ThreadId { get; set; }
    public string? TurnId { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public string Mode { get; set; } = "form";
    public string? ElicitationId { get; set; }
    public string? Message { get; set; }
    public string? Url { get; set; }
    public object? RequestedSchema { get; set; }
}

/// <summary>Client response to an MCP elicitation request.</summary>
public sealed class McpServerElicitationResponse
{
    public string Action { get; set; } = "cancel";
    public Dictionary<string, JsonElement>? Content { get; set; }
}
