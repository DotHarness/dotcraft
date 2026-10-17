using DotCraft.Sessions;

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
        nameof(AgentTools.ListAgents),
        nameof(BackgroundTaskTools.TaskStop)
    ];

    /// <summary>Returns true when a call needs Agent-control authority; <c>TaskStop</c> needs it only for Agent tasks.</summary>
    public static bool Governs(string toolName, string? taskId) =>
        string.Equals(toolName, nameof(BackgroundTaskTools.TaskStop), StringComparison.Ordinal)
            ? !string.IsNullOrWhiteSpace(taskId) && BackgroundTaskKinds.Of(taskId.Trim()) == BackgroundTaskKinds.Agent
            : AllToolNames.Contains(toolName, StringComparer.Ordinal);
}
