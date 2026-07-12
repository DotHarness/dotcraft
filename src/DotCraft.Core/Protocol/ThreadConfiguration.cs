using DotCraft.Configuration;
using DotCraft.Mcp;
using DotCraft.Tools;

namespace DotCraft.Protocol;

/// <summary>
/// Per-thread agent configuration. New threads capture workspace defaults at creation time.
/// </summary>
public sealed class ThreadConfiguration
{
    /// <summary>
    /// Optional identifier of the Agent Profile snapshot that produced this configuration.
    /// Runtime enforcement uses the resolved fields on this object, not the profile file.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentProfileId { get; set; }

    /// <summary>
    /// Optional source of the Agent Profile snapshot, such as <c>builtIn</c>, <c>user</c>, or <c>workspace</c>.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentProfileSource { get; set; }

    /// <summary>
    /// Optional fingerprint of the profile content that produced this resolved configuration.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentProfileFingerprint { get; set; }

    /// <summary>
    /// When set, this thread runs the conversational profile-builder agent editing the named
    /// Agent Profile (see specs/features/agent-profiles.md §12A). It exposes the builder tools and a
    /// thread-scoped working draft and is excluded from ordinary thread listings.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentBuilderTargetId { get; set; }

    /// <summary>Source (user / workspace) of the Agent Profile being edited; pairs with <see cref="AgentBuilderTargetId"/>.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentBuilderTargetSource { get; set; }

    /// <summary>
    /// Per-thread MCP server connections. Null means use workspace-level MCP configuration.
    /// </summary>
    public McpServerConfig[]? McpServers { get; set; }

    /// <summary>
    /// Agent mode: "agent" (full tools, default), "plan" (read-only tools), etc.
    /// </summary>
    public string Mode { get; set; } = "agent";

    /// <summary>
    /// Active extension prefixes declared by the client during ACP initialization
    /// (e.g., ["_unity"]). Null for non-ACP channels.
    /// </summary>
    public string[]? Extensions { get; set; }

    /// <summary>
    /// Additional tool names to enable beyond the mode's default tool set.
    /// </summary>
    public string[]? CustomTools { get; set; }

    /// <summary>
    /// Per-thread provider id. When empty during thread creation, Session Core captures
    /// the current effective workspace/global provider id.
    /// </summary>
    public string? ProviderId { get; set; }

    /// <summary>
    /// Per-thread model. When empty during thread creation, Session Core captures
    /// the current effective workspace/global <c>AppConfig.Model</c>.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>Inference-speed snapshot used by future turns in this thread.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public InferenceSpeed? Speed { get; set; }

    /// <summary>
    /// Per-thread reasoning configuration. Null means use the current effective
    /// workspace/global <see cref="AppConfig.Reasoning"/> value.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public AppConfig.ReasoningConfig? Reasoning { get; set; }

    /// <summary>
    /// Per-thread context-window mode. Null means default compaction behavior.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ThreadContextWindowConfig? ContextWindow { get; set; }

    /// <summary>
    /// When set, all tools for this thread operate on this workspace path
    /// instead of the AppServer's root workspace path.
    /// The thread is still registered under the AppServer's root workspace
    /// for discoverability via thread/list.
    /// </summary>
    public string? WorkspaceOverride { get; set; }

    /// <summary>
    /// When set, tools for this thread execute against this workspace path while
    /// thread state, memory, skills, plans, goals, and app bindings remain rooted
    /// at <see cref="SessionThread.WorkspacePath"/>.
    /// Used for Git worktree handoff.
    /// </summary>
    public string? ExecutionWorkspaceOverride { get; set; }

    /// <summary>
    /// When set, the agent uses the tool set registered under this profile name
    /// instead of the default tools for the thread's <see cref="Mode"/>.
    /// Requires the profile to be registered in <c>IToolProfileRegistry</c>.
    /// </summary>
    public string? ToolProfile { get; set; }

    /// <summary>
    /// When <c>true</c> together with <see cref="ToolProfile"/>, the agent uses <b>only</b>
    /// the profile tools (no mode default tools). Used for ephemeral internal threads
    /// such as commit-message suggestion.
    /// </summary>
    public bool UseToolProfileOnly { get; set; }

    /// <summary>
    /// Optional system instructions for the agent (e.g. commit-message assistant).
    /// When set, passed to <see cref="Agents.AgentFactory"/> as chat instructions.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentInstructions { get; set; }

    /// <summary>
    /// Optional exact tool allow-list resolved from a SubAgent role.
    /// Empty or null means all assembled tools remain eligible unless denied.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? ToolAllowList { get; set; }

    /// <summary>
    /// Optional exact tool deny-list resolved from a SubAgent role.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? ToolDenyList { get; set; }

    /// <summary>
    /// Structured tool policy compiled from an Agent Profile or supplied directly by a client.
    /// Composes with legacy <see cref="ToolAllowList"/> and <see cref="ToolDenyList"/>.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ThreadToolPolicy? ToolPolicy { get; set; }

    /// <summary>
    /// Structured MCP server and MCP tool policy for this thread.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ThreadMcpPolicy? McpPolicy { get; set; }

    /// <summary>
    /// Structured plugin and app policy for this thread.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ThreadPluginPolicy? PluginPolicy { get; set; }

    /// <summary>
    /// Structured skills policy for this thread.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ThreadSkillsPolicy? SkillsPolicy { get; set; }

    /// <summary>
    /// Structured Agent Teams policy for this thread.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ThreadTeamsPolicy? TeamsPolicy { get; set; }

    /// <summary>
    /// Optional per-thread override for DotCraft agent-control tools.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public AgentControlToolAccess? AgentControlToolAccess { get; set; }

    /// <summary>
    /// Optional exact agent-control allow-list used when <see cref="AgentControlToolAccess"/> is allow-list.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? AllowedAgentControlTools { get; set; }

    /// <summary>
    /// Optional prompt profile. Session-backed SubAgents default to a lightweight profile.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? PromptProfile { get; set; }

    /// <summary>
    /// Role-specific instructions appended to the generated system prompt.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? RoleInstructions { get; set; }

    /// <summary>
    /// When true, <see cref="RoleInstructions"/> replaces the generated prompt for this thread.
    /// </summary>
    public bool OverrideBasePrompt { get; set; }

    /// <summary>
    /// Overrides the process-level approval service for this thread only.
    /// </summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(ApprovalPolicyJsonConverter))]
    public ApprovalPolicy ApprovalPolicy { get; set; } = ApprovalPolicy.Default;

    /// <summary>
    /// Absolute path to the local automation task directory (contains <c>task.md</c>).
    /// Used by automation-specific tools when <see cref="WorkspaceOverride"/> is the project root.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AutomationTaskDirectory { get; set; }

    /// <summary>
    /// When set, overrides <see cref="Configuration.AppConfig.Tools.File.RequireApprovalOutsideWorkspace"/> (and shell)
    /// for core file/shell tools. Used by local automation: <c>false</c> = reject operations outside the thread workspace
    /// without prompting; <c>true</c> = allow outside-workspace paths when combined with auto-approve policy.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? RequireApprovalOutsideWorkspace { get; set; }
}

/// <summary>
/// Per-thread context-window selection.
/// </summary>
public sealed class ThreadContextWindowConfig
{
    /// <summary>
    /// Context-window mode for this thread.
    /// </summary>
    public ContextWindowMode Mode { get; set; } = ContextWindowMode.Default;
}

/// <summary>
/// Exact-name policy for general model-visible tools.
/// </summary>
public sealed class ThreadToolPolicy
{
    /// <summary>
    /// Tool names allowed by the profile. Null means no allow-list; an empty array means no tools.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Allow { get; set; }

    /// <summary>
    /// Tool names denied by the profile. Deny rules win over allow rules.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Deny { get; set; }

    /// <summary>
    /// Optional agent-control policy: <c>disabled</c>, <c>full</c>, or <c>allowList</c>.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentControl { get; set; }

    /// <summary>
    /// Optional agent-control tool allow-list used when <see cref="AgentControl"/> is <c>allowList</c>.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? AllowedAgentControlTools { get; set; }
}

/// <summary>
/// Policy for MCP servers and tools available to a thread.
/// </summary>
public sealed class ThreadMcpPolicy
{
    /// <summary>
    /// MCP server names allowed for this thread. Null means no server allow-list; empty means none.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Servers { get; set; }

    /// <summary>
    /// MCP tool-name policy. Wildcards are supported for MCP tool names.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ThreadNamePolicy? Tools { get; set; }
}

/// <summary>
/// Reusable allow/deny name policy.
/// </summary>
public sealed class ThreadNamePolicy
{
    /// <summary>
    /// Allowed names or patterns. Null means no allow-list; empty means none.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Allow { get; set; }

    /// <summary>
    /// Denied names or patterns. Deny rules win over allow rules.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Deny { get; set; }
}

/// <summary>
/// Policy for plugin-provided functions and app-provided dynamic tools.
/// </summary>
public sealed class ThreadPluginPolicy
{
    /// <summary>
    /// Allowed plugin or app ids. Null means no allow-list; empty means none.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Allow { get; set; }

    /// <summary>
    /// Denied plugin or app ids. Deny rules win over allow rules.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Deny { get; set; }
}

/// <summary>
/// Policy for agent-facing skill access.
/// </summary>
public sealed class ThreadSkillsPolicy
{
    /// <summary>
    /// Skill names that should be preloaded into prompt context by profile-aware prompt rendering.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Preload { get; set; }

    /// <summary>
    /// Skill names the agent may read. Null means no allow-list; empty means none.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Allow { get; set; }

    /// <summary>
    /// Skill names the agent may not read or mutate. Deny rules win over allow rules.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Deny { get; set; }

    /// <summary>
    /// Whether skill management tools may be exposed and invoked.
    /// Null means the existing runtime default applies.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? AllowManage { get; set; }
}

/// <summary>
/// Policy for Agent Teams runtime-owned capabilities.
/// </summary>
public sealed class ThreadTeamsPolicy
{
    /// <summary>
    /// Reserved Teams tool behavior. <c>keep</c> preserves Teams-owned runtime tools for Teams threads.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ReservedTools { get; set; }
}
