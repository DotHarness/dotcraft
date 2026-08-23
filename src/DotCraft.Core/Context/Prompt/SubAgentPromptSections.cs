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
            controls.Add("Use `SendMessage` for mailbox-only coordination; it records a message for the target and does not start a turn.");
        if (context.IsToolAvailable("FollowupTask"))
            controls.Add("Use `FollowupTask` to start or queue a target agent turn; set `deliveryMode` to `steer` only when a running native target should receive same-turn guidance, otherwise keep the default `queue`. Pending mailbox messages for that target are delivered with the task.");
        if (context.IsToolAvailable("WaitAgent"))
            controls.Add($"Use `WaitAgent` to wait for a mailbox update from any live agent; it does not return content; `timeoutMs` is milliseconds, defaults to {timeoutOptions.DefaultTimeoutMs}, and must be between {timeoutOptions.MinTimeoutMs} and {timeoutOptions.MaxTimeoutMs}.");
        if (context.IsToolAvailable("CloseAgent"))
            controls.Add("Close a child agent (and its open descendants) with `CloseAgent` once it is no longer needed. Completed agents stay open and count toward the concurrency limit until closed, so don't leave idle agents open.");

        var controlsText = controls.Count == 0
            ? "- Track spawned agent paths and manage their results explicitly with the tools currently available."
            : "- " + string.Join("\n- ", controls);

        return
$$"""
## SubAgent Lifecycle

Use `SpawnAgent` for concrete sidecar work that can run while the parent keeps the critical path moving.

- Keep immediate blockers local; spawn parallel exploration, verification, or disjoint implementation work.
- Make each child prompt specific and self-contained; use `agentRole: "explorer"` for read-only research and `agentRole: "worker"` for bounded execution.
- Set a lowercase `taskName` using only letters, digits, and underscores; the child is addressed by `agentPath`, while `agentNickname` only controls display naming.
{{controlsText}}
- When a child finishes, review and integrate its result without redoing the same work.
""";
    }
}
