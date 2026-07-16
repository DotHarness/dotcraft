using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

/// <summary>Opens a live MCP App View for a terminal MCP tool item.</summary>
public sealed class McpAppViewOpenParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;

    public string ItemId { get; set; } = string.Empty;
}

/// <summary>Opaque View capability and its validated initial document and invocation.</summary>
public sealed class McpAppViewOpenResult
{
    public string ViewHandle { get; set; } = string.Empty;

    public McpAppResourceWire Resource { get; set; } = new();

    public JsonObject ToolInput { get; set; } = [];

    public McpAppToolResultWire ToolResult { get; set; } = new();
}

/// <summary>Validated MCP App HTML resource returned to the trusted Desktop host.</summary>
public sealed class McpAppResourceWire
{
    public string Uri { get; set; } = string.Empty;

    public string MimeType { get; set; } = string.Empty;

    public string Html { get; set; } = string.Empty;

    public McpAppResourceMetadataWire Ui { get; set; } = new();
}

/// <summary>Security metadata that the Desktop host may use only to further restrict a View.</summary>
public sealed class McpAppResourceMetadataWire
{
    public McpAppResourceCspWire Csp { get; set; } = new();

    public bool PrefersBorder { get; set; }

    public string? RequestedDomain { get; set; }
}

/// <summary>Normalized, injection-safe CSP origin lists.</summary>
public sealed class McpAppResourceCspWire
{
    public List<string> ConnectDomains { get; set; } = [];

    public List<string> ResourceDomains { get; set; } = [];

    public List<string> FrameDomains { get; set; } = [];

    public List<string> BaseUriDomains { get; set; } = [];
}

/// <summary>Reads another resource through a live View's owning MCP generation.</summary>
public sealed class McpAppViewResourceReadParams
{
    public string ViewHandle { get; set; } = string.Empty;

    public string Uri { get; set; } = string.Empty;
}

public sealed class McpAppViewResourceReadResult
{
    public JsonArray Contents { get; set; } = [];
}

/// <summary>Lists app-visible tools on the View's owning MCP server.</summary>
public sealed class McpAppViewToolsListParams
{
    public string ViewHandle { get; set; } = string.Empty;
}

public sealed class McpAppViewToolsListResult
{
    public List<McpAppToolWire> Tools { get; set; } = [];
}

public sealed class McpAppToolWire
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public JsonObject InputSchema { get; set; } = [];
}

/// <summary>Invokes an app-visible same-server tool through the common dispatcher.</summary>
public sealed class McpAppViewToolCallParams
{
    public string ViewHandle { get; set; } = string.Empty;

    public string Tool { get; set; } = string.Empty;

    public JsonObject Arguments { get; set; } = [];
}

public sealed class McpAppToolResultWire
{
    public JsonArray Content { get; set; } = [];

    public JsonNode? StructuredContent { get; set; }

    [JsonPropertyName("_meta")]
    public JsonNode? Meta { get; set; }

    public bool IsError { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }
}

/// <summary>Submits stable MCP Apps user text to the bound thread.</summary>
public sealed class McpAppViewMessageParams
{
    public string ViewHandle { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public McpAppMessageContentWire Content { get; set; } = new();
}

public sealed class McpAppMessageContentWire
{
    public string Type { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

public sealed class McpAppViewMessageResult
{
    public string QueuedInputId { get; set; } = string.Empty;
}

/// <summary>Replaces or clears one View's transient next-turn model context.</summary>
public sealed class McpAppViewModelContextUpdateParams
{
    public string ViewHandle { get; set; } = string.Empty;

    public JsonArray? Content { get; set; }

    public JsonObject? StructuredContent { get; set; }
}

public sealed class McpAppViewModelContextUpdateResult
{
    public bool Cleared { get; set; }
}

/// <summary>Asks Core to validate an external URL before Desktop opens it.</summary>
public sealed class McpAppViewOpenLinkParams
{
    public string ViewHandle { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;
}

public sealed class McpAppViewOpenLinkResult
{
    public string Url { get; set; } = string.Empty;
}

public sealed class McpAppViewCloseParams
{
    public string ViewHandle { get; set; } = string.Empty;
}

public sealed class McpAppViewCloseResult
{
    public bool Closed { get; set; }
}

/// <summary>Connection-scoped View availability change.</summary>
public sealed class McpAppViewStatusUpdatedParams
{
    public string ViewHandle { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string FallbackText { get; set; } = string.Empty;
}
