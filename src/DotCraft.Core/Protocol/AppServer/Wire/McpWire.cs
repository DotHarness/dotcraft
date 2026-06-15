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
