using System.Text.Json.Serialization;
using DotCraft.Agents;
using DotCraft.Protocol;

namespace DotCraft.Protocol.AppServer;

public sealed class AgentProfileDiagnosticWire
{
    public string Severity { get; set; } = "error";

    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

public sealed class AgentProfileSummaryWire
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
}

public sealed class AgentProfileEntryWire
{
    public string Id { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    public string Source { get; set; } = AgentProfileSources.BuiltIn;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PluginId { get; set; }

    public string Fingerprint { get; set; } = string.Empty;

    public bool Valid { get; set; }

    public bool IsBuiltIn { get; set; }

    public bool ReadOnly { get; set; }

    public bool Shadowed { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShadowedBy { get; set; }

    public List<string> SourceStack { get; set; } = [];

    public List<string> LockedFields { get; set; } = [];

    public List<string> RestrictedFields { get; set; } = [];

    public bool TrustRestricted { get; set; }

    public List<string> StaleThreadIds { get; set; } = [];

    public List<AgentProfileDiagnosticWire> Diagnostics { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RawContent { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadConfiguration? CompiledConfig { get; set; }
}

public sealed class AgentProfileListParams
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; set; }

    public bool? IncludeInvalid { get; set; }
}

public sealed class AgentProfileListResult
{
    public List<AgentProfileEntryWire> Profiles { get; set; } = [];
}

public sealed class AgentProfileReadParams
{
    public string Id { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; set; }
}

public sealed class AgentProfileReadResult
{
    public AgentProfileEntryWire Profile { get; set; } = new();
}

public sealed class AgentProfileValidateParams
{
    public string RawContent { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; set; }
}

public sealed class AgentProfileValidateResult
{
    public bool Valid { get; set; }

    public List<AgentProfileDiagnosticWire> Diagnostics { get; set; } = [];

    public AgentProfileSummaryWire Summary { get; set; } = new();

    public List<string> LockedFields { get; set; } = [];

    public List<string> RestrictedFields { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadConfiguration? CompiledConfig { get; set; }
}

public sealed class AgentProfileUpsertParams
{
    public string Id { get; set; } = string.Empty;

    public string Source { get; set; } = AgentProfileSources.Workspace;

    public string RawContent { get; set; } = string.Empty;
}

public sealed class AgentProfileUpsertResult
{
    public AgentProfileEntryWire Profile { get; set; } = new();
}

public sealed class AgentProfileRemoveParams
{
    public string Id { get; set; } = string.Empty;

    public string Source { get; set; } = AgentProfileSources.Workspace;
}

public sealed class AgentProfileRemoveResult
{
    public bool Removed { get; set; }
}

public sealed class AgentProfileRefreshThreadParams
{
    public string ThreadId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProfileId { get; set; }
}

public sealed class AgentProfileRefreshThreadResult
{
    public string ThreadId { get; set; } = string.Empty;

    public AgentProfileEntryWire Profile { get; set; } = new();

    public ThreadConfiguration Config { get; set; } = new();

    public bool WasStale { get; set; }

    public AgentProfileAuditWire Audit { get; set; } = new();
}

public sealed class AgentProfileAuditWire
{
    public string Event { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProfileId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThreadId { get; set; }

    public string Status { get; set; } = "success";

    public Dictionary<string, string> Fields { get; set; } = [];
}
