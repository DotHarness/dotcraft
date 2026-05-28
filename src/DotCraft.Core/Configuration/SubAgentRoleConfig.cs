using DotCraft.Tools;

namespace DotCraft.Configuration;

/// <summary>
/// Configures the behavior and tool surface for a session-backed SubAgent role.
/// </summary>
public sealed class SubAgentRoleConfig
{
    /// <summary>
    /// Canonical role name used by <c>SpawnAgent.agentRole</c>.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Short role description exposed in the agent prompt.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional exact tool allow-list. Empty means all mode/default tools remain eligible.
    /// </summary>
    public List<string> ToolAllowList { get; set; } = [];

    /// <summary>
    /// Exact tool names removed after all default/profile/MCP/channel tools are assembled.
    /// </summary>
    public List<string> ToolDenyList { get; set; } = [];

    /// <summary>
    /// Controls whether DotCraft agent-control tools are exposed for this role.
    /// </summary>
    public AgentControlToolAccess AgentControlToolAccess { get; set; } = AgentControlToolAccess.Disabled;

    /// <summary>
    /// Optional allow-list of DotCraft agent-control tool names when <see cref="AgentControlToolAccess"/> is allow-list.
    /// </summary>
    public List<string> AllowedAgentControlTools { get; set; } = [];

    /// <summary>
    /// Prompt profile for native session-backed SubAgents.
    /// </summary>
    public string PromptProfile { get; set; } = SubAgentPromptProfiles.Light;

    /// <summary>
    /// Role-specific instructions appended to the SubAgent prompt.
    /// </summary>
    public string? Instructions { get; set; }

    /// <summary>
    /// Optional per-role mode override.
    /// </summary>
    public string? Mode { get; set; }

    /// <summary>
    /// Optional per-role model override.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// When true, <see cref="Instructions"/> replaces the generated prompt instead of augmenting it.
    /// </summary>
    public bool OverrideBasePrompt { get; set; }

    public SubAgentRoleConfig Clone() =>
        new()
        {
            Name = Name,
            Description = Description,
            ToolAllowList = [.. ToolAllowList],
            ToolDenyList = [.. ToolDenyList],
            AgentControlToolAccess = AgentControlToolAccess,
            AllowedAgentControlTools = [.. AllowedAgentControlTools],
            PromptProfile = PromptProfile,
            Instructions = Instructions,
            Mode = Mode,
            Model = Model,
            OverrideBasePrompt = OverrideBasePrompt
        };
}

public static class SubAgentRoleNames
{
    public const string Default = "default";
    public const string Worker = "worker";
    public const string Explorer = "explorer";
}

public static class SubAgentPromptProfiles
{
    public const string Full = "full";
    public const string Light = "subagent-light";
}

/// <summary>
/// Resolves built-in and configured SubAgent roles.
/// </summary>
public sealed class SubAgentRoleRegistry
{
    private readonly IReadOnlyDictionary<string, SubAgentRoleConfig> _roles;

    public SubAgentRoleRegistry(IEnumerable<SubAgentRoleConfig>? configuredRoles)
    {
        var roles = CreateBuiltInRoles()
            .ToDictionary(role => role.Name, StringComparer.OrdinalIgnoreCase);

        if (configuredRoles != null)
        {
            foreach (var role in configuredRoles)
            {
                if (string.IsNullOrWhiteSpace(role.Name))
                    continue;

                roles[role.Name.Trim()] = role.Clone();
            }
        }

        _roles = roles;
    }

    public IReadOnlyCollection<SubAgentRoleConfig> Roles => _roles.Values.ToArray();

    public bool TryGet(string? roleName, out SubAgentRoleConfig role)
    {
        var effectiveName = string.IsNullOrWhiteSpace(roleName)
            ? SubAgentRoleNames.Default
            : roleName.Trim();

        if (_roles.TryGetValue(effectiveName, out var existing))
        {
            role = existing.Clone();
            return true;
        }

        role = new SubAgentRoleConfig();
        return false;
    }

    public static IReadOnlyList<SubAgentRoleConfig> CreateBuiltInRoles() =>
    [
        new()
        {
            Name = SubAgentRoleNames.Default,
            Description = "Default SubAgent role for bounded first-level delegation.",
            AgentControlToolAccess = AgentControlToolAccess.Disabled,
            PromptProfile = SubAgentPromptProfiles.Light,
            Instructions = """
You are a SubAgent working for the parent DotCraft agent.
Stay focused on the assigned task and return concise, actionable findings.
Do not spawn additional agents.
"""
        },
        new()
        {
            Name = SubAgentRoleNames.Worker,
            Description = "Execution role with read/write, shell, and web tools. Recursive agent control remains depth-limited.",
            AgentControlToolAccess = AgentControlToolAccess.Full,
            PromptProfile = SubAgentPromptProfiles.Light,
            ToolDenyList = ["UpdateTodos", "TodoWrite", "CreatePlan"],
            Instructions = """
You are a worker SubAgent. Complete the assigned implementation or verification task within the ownership boundaries given by the parent agent.
If you change files, summarize the paths changed and the validation you ran.
You may use agent-control tools only when they are exposed by the current depth and configuration.
"""
        },
        new()
        {
            Name = SubAgentRoleNames.Explorer,
            Description = "Read-only exploration role for codebase and web research.",
            AgentControlToolAccess = AgentControlToolAccess.Disabled,
            PromptProfile = SubAgentPromptProfiles.Light,
            ToolAllowList =
            [
                "ReadFile",
                "GrepFiles",
                "FindFiles",
                "LSP",
                "WebSearch",
                "WebFetch",
                "SkillView"
            ],
            Instructions = """
You are an explorer SubAgent. Answer specific research questions using read-only inspection and web research when available.
Do not edit files, execute shell commands, manage skills, or spawn other agents.
Return concrete findings with relevant file paths or source names.
"""
        }
    ];
}
