using System.Text.Json.Serialization;
using DotCraft.Configuration;

namespace DotCraft.Protocol.AppServer;


/// <summary>
/// Params for <see cref="AppServerMethods.WorkspaceCommitMessageSuggest"/>.
/// </summary>
public sealed class WorkspaceCommitMessageSuggestParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string[] Paths { get; set; } = [];

    public string Provider { get; set; } = "git";

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

    /// <summary>
    /// Workspace default context-window mode for newly created threads. Null removes the workspace override.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public ThreadContextWindowConfig? ContextWindow { get; set; }
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

    /// <summary>
    /// Persisted workspace context-window mode after normalization.
    /// Null means the workspace Compaction.ContextWindowMode override was removed or is absent.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public ThreadContextWindowConfig? ContextWindow { get; set; }
}

/// <summary>
/// Params for <see cref="AppServerMethods.WorkspaceConfigSchema"/>.
/// Reserved for future filters.
/// </summary>
public sealed class WorkspaceConfigSchemaParams
{
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
