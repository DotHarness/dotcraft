using DotCraft.Cron;

namespace DotCraft.Automations.Templates;

/// <summary>
/// Built-in local automation task templates. Each template is a pre-filled preset for <c>task.md</c> + <c>workflow.md</c>
/// used by the desktop "New Task" dialog template gallery.
/// </summary>
public static class LocalTaskTemplates
{
    public static IReadOnlyList<LocalTaskTemplate> All { get; } = BuildTemplates();

    public static LocalTaskTemplate? FindById(string id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));

    public static IReadOnlyList<LocalTaskTemplate> ForLocale(string? locale) => All;

    private static List<LocalTaskTemplate> BuildTemplates() =>
    [
        new(
            Id: "scan-commits-for-bugs",
            Title: "Scan recent commits for bugs",
            Description: "Daily sweep of commits since the last run (or 24h) that flags suspicious changes and suggests minimal fixes.",
            Icon: "🐛",
            Category: "review",
            DefaultTitle: "Scan recent commits for bugs",
            DefaultDescription: "Look at commits since the last run (or the last 24h if this is the first run). For each commit, note any suspicious changes and suggest a minimal fix.",
            WorkflowMarkdown: BuildWorkflow(
                "project",
                "You are the bug-scanning automation. Every run, review commits since the last invocation (or 24h) and report suspicious changes with minimal-fix suggestions."),
            DefaultSchedule: Daily(9, 0),
            DefaultWorkspaceMode: "project",
            DefaultApprovalPolicy: "workspaceScope",
            NeedsThreadBinding: false),

        new(
            Id: "standup-digest",
            Title: "Standup digest from yesterday's git",
            Description: "Summarize yesterday's branches, PRs, and commits into a short standup blurb.",
            Icon: "📝",
            Category: "digest",
            DefaultTitle: "Yesterday's standup digest",
            DefaultDescription: "Summarize yesterday's git activity (branches, commits, PRs) into a short standup paragraph plus a bullet list of what is still in-flight.",
            WorkflowMarkdown: BuildWorkflow(
                "project",
                "You are the standup-digest automation. Summarize yesterday's git activity (branches, commits, PRs) and list what is still in-flight."),
            DefaultSchedule: Daily(9, 0),
            DefaultWorkspaceMode: "project",
            DefaultApprovalPolicy: "workspaceScope",
            NeedsThreadBinding: false),

        new(
            Id: "skill-growth-suggestions",
            Title: "Skill growth from recent PRs/reviews",
            Description: "Weekly reflection on recent PRs/review comments, suggesting one or two areas to strengthen.",
            Icon: "📈",
            Category: "insight",
            DefaultTitle: "What should I level up next?",
            DefaultDescription: "Look at my recent PRs and review comments. Pick one or two specific areas to strengthen this week and explain why.",
            WorkflowMarkdown: BuildWorkflow(
                "project",
                "You are the skill-growth automation. Each week, analyze the user's recent PRs + review comments and suggest one or two concrete skill areas to focus on."),
            DefaultSchedule: Every(7L * 24 * 60 * 60 * 1000),
            DefaultWorkspaceMode: "project",
            DefaultApprovalPolicy: "workspaceScope",
            NeedsThreadBinding: false),

        new(
            Id: "weekly-report",
            Title: "Weekly activity report",
            Description: "Synthesize the week's PRs, rollouts, and incidents into a single report.",
            Icon: "📅",
            Category: "digest",
            DefaultTitle: "Weekly activity report",
            DefaultDescription: "Compose a weekly report covering PRs merged, rollouts shipped, and incidents handled. Keep it under 300 words and group by area.",
            WorkflowMarkdown: BuildWorkflow(
                "project",
                "You are the weekly-report automation. Produce a concise weekly report of PRs, rollouts, and incidents."),
            DefaultSchedule: Every(7L * 24 * 60 * 60 * 1000),
            DefaultWorkspaceMode: "project",
            DefaultApprovalPolicy: "workspaceScope",
            NeedsThreadBinding: false),

        new(
            Id: "regression-alert",
            Title: "Regression early-warning",
            Description: "Compare recent changes against benchmarks/traces and alert before regressions ship.",
            Icon: "📉",
            Category: "review",
            DefaultTitle: "Regression early-warning",
            DefaultDescription: "Compare recent changes against benchmark and trace data. Raise a warning (with a minimal repro suggestion) before suspected regressions ship.",
            WorkflowMarkdown: BuildWorkflow(
                "project",
                "You are the regression-alert automation. Review recent changes against benchmarks and traces; raise warnings with repro suggestions."),
            DefaultSchedule: Daily(10, 0),
            DefaultWorkspaceMode: "project",
            DefaultApprovalPolicy: "workspaceScope",
            NeedsThreadBinding: false),

        new(
            Id: "ci-triage",
            Title: "Triage CI failures",
            Description: "Hourly scan of CI failures, grouped by root cause with minimal fix suggestions.",
            Icon: "🛠️",
            Category: "review",
            DefaultTitle: "Triage CI failures",
            DefaultDescription: "Scan recent CI failures, group them by root cause, and suggest the minimal fix for each group. Skip transient/flaky ones.",
            WorkflowMarkdown: BuildWorkflow(
                "project",
                "You are the CI-triage automation. Cluster CI failures by root cause and suggest minimal fixes; ignore transient flakes."),
            DefaultSchedule: Every(60L * 60 * 1000),
            DefaultWorkspaceMode: "project",
            DefaultApprovalPolicy: "workspaceScope",
            NeedsThreadBinding: false),

        new(
            Id: "issue-triage",
            Title: "Triage new issues",
            Description: "Hourly review of new issues, suggesting owner, priority, and labels.",
            Icon: "🎯",
            Category: "review",
            DefaultTitle: "Triage new issues",
            DefaultDescription: "For each newly-opened issue, suggest an owner, priority, and labels. Quote the evidence from the issue body.",
            WorkflowMarkdown: BuildWorkflow(
                "project",
                "You are the issue-triage automation. Recommend owner / priority / labels for new issues with quoted evidence."),
            DefaultSchedule: Every(60L * 60 * 1000),
            DefaultWorkspaceMode: "project",
            DefaultApprovalPolicy: "workspaceScope",
            NeedsThreadBinding: false),

        new(
            Id: "watch-thread-replies",
            Title: "Watch a thread for replies",
            Description: "Periodically check a bound thread for new user replies and respond automatically.",
            Icon: "💬",
            Category: "watch",
            DefaultTitle: "Watch thread for replies",
            DefaultDescription: "Check the bound thread for new user replies since the last tick. If there are any, respond helpfully; if not, stay silent.",
            WorkflowMarkdown: BuildWorkflow(
                "project",
                "You are the thread-watcher automation. When new user replies appear in this thread since the last tick, respond helpfully; otherwise stay silent."),
            DefaultSchedule: Every(5L * 60 * 1000),
            DefaultWorkspaceMode: "project",
            DefaultApprovalPolicy: "workspaceScope",
            NeedsThreadBinding: true),

        new(
            Id: "classic-mini-game",
            Title: "Create a classic mini-game",
            Description: "One-shot creative task that scaffolds a small classic-style game.",
            Icon: "🎮",
            Category: "creative",
            DefaultTitle: "Build a classic mini-game",
            DefaultDescription: "Design and scaffold a tiny classic-style game (Pong/Snake/Breakout). Output the code and a one-paragraph explanation.",
            WorkflowMarkdown: BuildWorkflow(
                "worktree",
                "You are a one-shot automation that scaffolds a small classic game (Pong / Snake / Breakout). Output code + a short explanation."),
            DefaultSchedule: null,
            DefaultWorkspaceMode: "worktree",
            DefaultApprovalPolicy: "workspaceScope",
            NeedsThreadBinding: false)
    ];

    private static string BuildWorkflow(string workspace, string systemLine)
    {
        // Non-interpolated raw string so Liquid placeholders ({{ task.id }}) stay intact.
        const string template = """
            ---
            max_rounds: 10
            workspace: __WORKSPACE__
            ---

            __SYSTEM__

            ## Task

            - **ID**: {{ task.id }}
            - **Title**: {{ task.title }}

            ## Instructions

            {{ task.description }}

            When finished, call the **`CompleteLocalTask`** tool with a short summary.
            """;

        return template
            .Replace("__WORKSPACE__", workspace)
            .Replace("__SYSTEM__", systemLine);
    }

    private static CronSchedule Daily(int hour, int minute) =>
        new() { Kind = "daily", DailyHour = hour, DailyMinute = minute, Tz = TimeZoneInfo.Local.Id };

    private static CronSchedule Every(long ms) =>
        new() { Kind = "every", EveryMs = ms };
}

/// <summary>
/// In-process representation of a template. Built-ins come from <see cref="LocalTaskTemplates.All"/>
/// (with <see cref="IsUser"/> = <c>false</c>); user-authored templates are loaded from disk by
/// <see cref="UserTemplateFileStore"/> and carry <see cref="IsUser"/> = <c>true</c>. Both are
/// projected to <see cref="DotCraft.Protocol.AppServer.AutomationTemplateWire"/> by the handler.
/// </summary>
public sealed record LocalTaskTemplate(
    string Id,
    string Title,
    string Description,
    string Icon,
    string Category,
    string WorkflowMarkdown,
    CronSchedule? DefaultSchedule,
    string? DefaultWorkspaceMode,
    string? DefaultApprovalPolicy,
    bool NeedsThreadBinding,
    string? DefaultTitle = null,
    string? DefaultDescription = null,
    bool IsUser = false,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? UpdatedAt = null,
    string? DefaultAgentProfileId = null);
