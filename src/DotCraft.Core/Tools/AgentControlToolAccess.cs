namespace DotCraft.Tools;

/// <summary>
/// Defines how DotCraft agent-control tools are exposed for a tool provider context.
/// </summary>
public enum AgentControlToolAccess
{
    /// <summary>
    /// Expose all DotCraft agent-control tools.
    /// </summary>
    Full,

    /// <summary>
    /// Expose no DotCraft agent-control tools.
    /// </summary>
    Disabled,

    /// <summary>
    /// Expose only the tool names listed by the context.
    /// </summary>
    AllowList
}
