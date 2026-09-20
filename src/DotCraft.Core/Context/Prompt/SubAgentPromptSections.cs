using DotCraft.Configuration;
using DotCraft.Contributions;

namespace DotCraft.Context;

/// <summary>The built-in SubAgent sections: available profiles, and lifecycle guidance whose control list follows the tools exposed this turn.</summary>
internal static class SubAgentPromptSections
{
    /// <summary>Builds the <c>subagent-profiles</c> section from the pre-rendered profile text.</summary>
    internal static string? Profiles(SystemPromptSectionContext context) =>
        context.RequireSources().SubAgentProfilesSection;

    /// <summary>Builds the <c>subagent-lifecycle</c> section, or omits it without <c>SpawnAgent</c>.</summary>
    internal static string? Lifecycle(SystemPromptSectionContext context)
    {
        if (!context.IsToolAvailable("SpawnAgent"))
            return null;

        var timeoutOptions = context.RequireSources().SubAgentWaitAgentTimeoutOptions
            ?? SubAgentWaitAgentTimeoutOptions.Defaults;

        var controls = new List<string>();
        if (context.IsToolAvailable("ListAgents"))
            controls.Add("Use `ListAgents` to list live agents in the current root thread tree.");
        if (context.IsToolAvailable("SendMessage"))
            controls.Add("Use `SendMessage` to send a message without starting a target agent turn.");
        if (context.IsToolAvailable("FollowupTask"))
            controls.Add("Use `FollowupTask` to start or queue a target agent turn. Set `deliveryMode` to `steer` only for same-turn guidance to a running native agent. Otherwise use the default `queue`. Pending messages are delivered with the task.");
        if (context.IsToolAvailable("WaitAgent"))
            controls.Add($"Use `WaitAgent` to wait for a message update from any live agent. It does not return message content. `timeoutMs` is in milliseconds, defaults to {timeoutOptions.DefaultTimeoutMs}, and must be between {timeoutOptions.MinTimeoutMs} and {timeoutOptions.MaxTimeoutMs}.");
        if (context.IsToolAvailable("CloseAgent"))
            controls.Add("Close a child agent (and its open descendants) with `CloseAgent` once it is no longer needed. Completed agents stay open and count toward the concurrency limit until closed, so don't leave idle agents open.");

        var controlsText = controls.Count == 0
            ? "- Track spawned agent paths and manage their results explicitly with the tools currently available."
            : "- " + string.Join("\n- ", controls);

        return
$$"""
## SubAgent Lifecycle

Use `SpawnAgent` for independent work that can run while you continue the main task.

- Handle immediate blockers yourself. Delegate parallel exploration, verification, or implementation in separate areas.
- Give each child a specific, self-contained task. Use `agentRole: "explorer"` for read-only research and `agentRole: "worker"` for bounded execution.
- Set `taskName` using lowercase letters, digits, and underscores. Address the child by `agentPath`. `agentNickname` controls display naming only.
- Full-history forks (`forkTurns` omitted or `"all"`) inherit the parent model and reasoning effort and do not accept overrides. Set `model` or `reasoningEffort` only when explicitly requested by the user, applicable `AGENTS.md` instructions, or skill instructions. For overrides, set `forkTurns` to `"none"` or a positive integer string.
{{controlsText}}
- When a child finishes, review and integrate its result without redoing the same work.
""";
    }
}
