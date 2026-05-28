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

    /// <summary>
    /// Per-thread reasoning configuration. Null means use the current effective
    /// workspace/global <see cref="AppConfig.Reasoning"/> value.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public AppConfig.ReasoningConfig? Reasoning { get; set; }

    /// <summary>
    /// When set, all tools for this thread operate on this workspace path
    /// instead of the AppServer's root workspace path.
    /// The thread is still registered under the AppServer's root workspace
    /// for discoverability via thread/list.
    /// </summary>
    public string? WorkspaceOverride { get; set; }

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
