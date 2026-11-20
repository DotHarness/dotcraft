using DotCraft.Configuration;
using DotCraft.Tools;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;

namespace DotCraft.Sessions;

/// <summary>Per-thread agent configuration. New threads capture workspace defaults at creation.</summary>
public sealed class ThreadConfiguration
{
    /// <summary>The Agent Profile this configuration was resolved from. Runtime uses the resolved fields, not the profile file.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentProfileId { get; set; }

    /// <summary>Names the Thread's own memory store. Unset, the Thread uses the workspace store.</summary>
    public string? MemoryScope { get; set; }

    /// <summary>Stores attached beside the Thread's own, in model-visible order. A replacement that leaves them unset keeps the current ones.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<DotCraft.Memory.MemoryStoreAttachment>? MemoryStores { get; set; }

    /// <summary>Captured from the workspace at creation. Unset means enabled.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? MemoryEnabled { get; set; }

    /// <summary>Captured from the workspace at creation. Unset means disabled.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? MemoryAutoExtract { get; set; }

    /// <summary>Captured from the workspace at creation. Unset means enabled.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? MemoryPersonal { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentProfileSource { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentProfileFingerprint { get; set; }

    /// <summary>When set, the thread runs the profile builder for this Agent Profile and is left out of ordinary thread listings.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentBuilderTargetId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentBuilderTargetSource { get; set; }

    /// <summary>Null means the workspace MCP configuration.</summary>
    public McpServerConfig[]? McpServers { get; set; }

    public string Mode { get; set; } = "agent";

    /// <summary>Extension prefixes the ACP client declared, such as <c>_unity</c>. Null for other channels.</summary>
    public string[]? Extensions { get; set; }

    /// <summary>Tools enabled beyond the mode's default set.</summary>
    public string[]? CustomTools { get; set; }

    /// <summary>Captured from the effective configuration when empty at creation.</summary>
    public string? ProviderId { get; set; }

    /// <summary>Captured from the provider's <c>ProviderPreferences</c> entry when empty at creation.</summary>
    public string? Model { get; set; }

    /// <summary>Resolved at each Agent build. Never persisted or sent to clients.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public SubAgentModelCatalogSnapshot? SubAgentModelCatalogSnapshot { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public InferenceSpeed? Speed { get; set; }

    /// <summary>Null means the effective provider preference.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public AppConfig.ReasoningConfig? Reasoning { get; set; }

    /// <summary>Tools operate on this path, while the thread stays listed under the AppServer root workspace.</summary>
    public string? WorkspaceOverride { get; set; }

    /// <summary>Relative tool paths resolve from here without changing the thread's state workspace.</summary>
    public string? Cwd { get; set; }

    /// <summary>Null defaults to the effective cwd, and an empty array provides no runtime roots.</summary>
    public string[]? RuntimeWorkspaceRoots { get; set; }

    /// <summary>Tools run here while thread state stays rooted at <see cref="SessionThread.WorkspacePath"/>, as in a Git worktree handoff.</summary>
    public string? ExecutionWorkspaceOverride { get; set; }

    /// <summary>Replaces the mode's default tools with this registered tool profile.</summary>
    public string? ToolProfile { get; set; }

    /// <summary>With <see cref="ToolProfile"/>, the agent gets only the profile tools.</summary>
    public bool UseToolProfileOnly { get; set; }

    /// <summary>When set, these instructions are the whole system prompt.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentInstructions { get; set; }

    /// <summary>Resolved from a SubAgent role. Null or empty keeps every assembled tool that is not denied.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? ToolAllowList { get; set; }

    /// <summary>Tool namespaces kept off the code mode nested surface, so their tools stay direct model tools.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? CodeModeExcludedNamespaces { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? ToolDenyList { get; set; }

    /// <summary>Composes with <see cref="ToolAllowList"/> and <see cref="ToolDenyList"/>.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ThreadToolPolicy? ToolPolicy { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ThreadMcpPolicy? McpPolicy { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ThreadPluginPolicy? PluginPolicy { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ThreadSkillsPolicy? SkillsPolicy { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public AgentControlToolAccess? AgentControlToolAccess { get; set; }

    /// <summary>Used when <see cref="AgentControlToolAccess"/> is allow-list.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? AllowedAgentControlTools { get; set; }

    /// <summary>Delivered as a thread context item. A full-history SubAgent keeps its parent's.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? RoleInstructions { get; set; }

    /// <summary>A native SubAgent role's text, kept apart from <see cref="RoleInstructions"/> and not sent to clients.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? SubAgentRoleInstructions { get; set; }

    /// <summary>Delivered after <see cref="RoleInstructions"/> as a thread context item. Only a full-history SubAgent inherits them.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? DeveloperInstructions { get; set; }

    /// <summary>When true, <see cref="RoleInstructions"/> replaces the generated prompt.</summary>
    public bool OverrideBasePrompt { get; set; }

    [System.Text.Json.Serialization.JsonConverter(typeof(ApprovalPolicyJsonConverter))]
    public ApprovalPolicy ApprovalPolicy { get; set; } = ApprovalPolicy.Default;

    /// <summary>The local automation task directory that holds <c>task.md</c>.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AutomationTaskDirectory { get; set; }

    /// <summary>Overrides <c>Tools.File.RequireApprovalOutsideWorkspace</c> for this thread. False rejects outside-workspace operations without prompting.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? RequireApprovalOutsideWorkspace { get; set; }
}

public sealed class SubAgentModelCatalogSnapshot
{
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>The fresh and bounded child default as <c>providerId/modelId</c>, or null when children inherit the parent model.</summary>
    public string? DefaultModel { get; set; }

    public List<SubAgentModelCatalogItem> Models { get; set; } = [];
}

public sealed class SubAgentModelCatalogItem
{
    /// <summary>The model-visible name, <c>providerId/modelId</c>.</summary>
    public string Id { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public List<ModelReasoningEffort> SupportedReasoningEfforts { get; set; } = [];

    public ModelReasoningEffort? DefaultReasoningEffort { get; set; }
}

public sealed class ThreadToolPolicy
{
    /// <summary>Null means no allow-list, and an empty array allows no tools.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Allow { get; set; }

    /// <summary>Deny wins over allow.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Deny { get; set; }

    /// <summary><c>disabled</c>, <c>full</c>, or <c>allowList</c>.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentControl { get; set; }

    /// <summary>Used when <see cref="AgentControl"/> is <c>allowList</c>.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? AllowedAgentControlTools { get; set; }
}

public sealed class ThreadMcpPolicy
{
    /// <summary>Null means no server allow-list, and empty allows none.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Servers { get; set; }

    /// <summary>Supports wildcards in MCP tool names.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ThreadNamePolicy? Tools { get; set; }
}

public sealed class ThreadNamePolicy
{
    /// <summary>Null means no allow-list, and empty allows none.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Allow { get; set; }

    /// <summary>Deny wins over allow.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Deny { get; set; }
}

/// <summary>Policy for plugin-provided functions and app-provided dynamic tools.</summary>
public sealed class ThreadPluginPolicy
{
    /// <summary>Null means no allow-list, and empty allows none.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Allow { get; set; }

    /// <summary>Deny wins over allow.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Deny { get; set; }
}

public sealed class ThreadSkillsPolicy
{
    /// <summary>Null means no allow-list, and empty allows none.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Allow { get; set; }

    /// <summary>Skills the agent may not read or mutate. Deny wins over allow.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Deny { get; set; }

    public bool Denies(string skillName) => Lists(Deny, skillName);

    public bool Allows(string skillName) => Allow == null || Lists(Allow, skillName);

    public bool Permits(string skillName) => !Denies(skillName) && Allows(skillName);

    private static bool Lists(string[]? names, string skillName) =>
        names != null
        && names.Any(name => !string.IsNullOrWhiteSpace(name)
                             && string.Equals(skillName, name.Trim(), StringComparison.OrdinalIgnoreCase));
}
