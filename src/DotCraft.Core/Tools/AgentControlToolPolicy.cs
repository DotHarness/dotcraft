using DotCraft.Abstractions;

namespace DotCraft.Tools;

/// <summary>
/// Evaluates whether DotCraft agent-control tools may be exposed for a context.
/// </summary>
public static class AgentControlToolPolicy
{
    /// <summary>
    /// Gets the canonical names of DotCraft agent-control tools.
    /// </summary>
    public static IReadOnlyList<string> AllToolNames { get; } =
    [
        nameof(AgentTools.SpawnAgent),
        nameof(AgentTools.SendInput),
        nameof(AgentTools.WaitAgent),
        nameof(AgentTools.ResumeAgent),
        nameof(AgentTools.CloseAgent)
    ];

    /// <summary>
    /// Returns true when at least one agent-control tool may be exposed.
    /// </summary>
    public static bool AllowsAny(ToolProviderContext context) =>
        AllToolNames.Any(toolName => Allows(context, toolName));

    /// <summary>
    /// Returns true when the named agent-control tool may be exposed.
    /// </summary>
    public static bool Allows(ToolProviderContext context, string toolName) =>
        context.AgentControlToolAccess switch
        {
            AgentControlToolAccess.Full => true,
            AgentControlToolAccess.Disabled => false,
            AgentControlToolAccess.AllowList => context.AllowedAgentControlTools?.Contains(toolName) == true,
            _ => false
        };
}
