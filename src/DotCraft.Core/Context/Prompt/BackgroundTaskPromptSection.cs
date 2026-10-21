using DotCraft.Contributions;

namespace DotCraft.Context;

internal static class BackgroundTaskPromptSection
{
    /// <summary>Builds the <c>background-tasks</c> section from the tools that start background work.</summary>
    internal static string? Build(SystemPromptSectionContext context)
    {
        var kinds = new List<string>();
        if (context.IsToolAvailable("Exec"))
            kinds.Add("commands still running after `Exec` returns");
        if (context.IsToolAvailable("SpawnAgent"))
            kinds.Add("subagents");
        if (context.IsToolAvailable("Workflow"))
            kinds.Add("Workflow runs");
        if (kinds.Count == 0)
            return null;

        var subject = kinds.Count == 1
            ? kinds[0]
            : string.Join(", ", kinds.Take(kinds.Count - 1)) + " and " + kinds[^1];
        var lines = new List<string>
        {
            "## Background Tasks",
            string.Empty,
            $"Background tasks are {subject}. You receive a `<task_notification>` when each one ends, and it starts a new turn if yours has ended. Notifications are system events, not user input.",
            string.Empty,
            "- After starting a task you know nothing about its result. Never predict or report it. If the user asks before the notification arrives, say it is still running.",
            "- Do not poll or sleep while it runs, and do not duplicate its work, such as researching a question you delegated.",
            "- Continue other work. When none remains, briefly tell the user what you started and end your response."
        };
        if (context.IsToolAvailable("TaskStop"))
            lines.Add("- Stop tasks you no longer need with `TaskStop`.");
        return string.Join(Environment.NewLine, lines);
    }
}
