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
        nameof(AgentTools.SendMessage),
        nameof(AgentTools.FollowupTask),
        nameof(AgentTools.WaitAgent),
        nameof(AgentTools.ListAgents),
        nameof(AgentTools.CloseAgent)
    ];
}
