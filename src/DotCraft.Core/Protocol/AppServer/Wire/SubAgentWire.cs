using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Cron;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol.AppServer;


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
