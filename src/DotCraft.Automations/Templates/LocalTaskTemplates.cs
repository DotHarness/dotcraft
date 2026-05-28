using DotCraft.Cron;

namespace DotCraft.Automations.Templates;

/// <summary>
/// Built-in local automation task templates. Each template is a pre-filled preset for <c>task.md</c> + <c>workflow.md</c>
/// used by the desktop "New Task" dialog template gallery.
/// </summary>
public static class LocalTaskTemplates
{
    private const string DefaultLocale = "en";
    private const string ZhHansLocale = "zh-Hans";

    public static IReadOnlyList<LocalTaskTemplate> All { get; } = BuildTemplates(DefaultLocale);

    public static LocalTaskTemplate? FindById(string id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));

    public static IReadOnlyList<LocalTaskTemplate> ForLocale(string? locale) =>
        BuildTemplates(NormalizeLocale(locale));

    public static string NormalizeLocale(string? locale)
    {
        if (string.Equals(locale, "zh", StringComparison.OrdinalIgnoreCase)
            || string.Equals(locale, "zh-CN", StringComparison.OrdinalIgnoreCase)
            || string.Equals(locale, ZhHansLocale, StringComparison.OrdinalIgnoreCase))
        {
            return ZhHansLocale;
        }

        return DefaultLocale;
    }

    private static List<LocalTaskTemplate> BuildTemplates(string locale)
    {
        var zh = string.Equals(NormalizeLocale(locale), ZhHansLocale, StringComparison.Ordinal);

        return
    [
        new(
            Id: "scan-commits-for-bugs",
            Title: zh ? "扫描近期提交中的潜在缺陷" : "Scan recent commits for bugs",
            Description: zh
                ? "每天检查上次运行以来（或过去 24 小时）的提交，标记可疑变更并给出最小修复建议。"
                : "Daily sweep of commits since the last run (or 24h) that flags suspicious changes and suggests minimal fixes.",
            Icon: "🐛",
            Category: "review",
            DefaultTitle: zh ? "扫描近期提交中的潜在缺陷" : "Scan recent commits for bugs",
            DefaultDescription: zh
                ? "查看上次运行以来的提交；如果这是第一次运行，则查看过去 24 小时。对每个提交记录可疑变更，并给出最小修复建议。"
                : "Look at commits since the last run (or the last 24h if this is the first run). For each commit, note any suspicious changes and suggest a minimal fix.",
            WorkflowMarkdown: BuildWorkflow(
                locale,
                "project",
                zh
                    ? "你是缺陷扫描自动化。每次运行时，检查上次调用以来（或过去 24 小时）的提交，并报告可疑变更和最小修复建议。"
                    : "You are the bug-scanning automation. Every run, review commits since the last invocation (or 24h) and report suspicious changes with minimal-fix suggestions."),
            DefaultSchedule: Daily(9, 0),
            DefaultWorkspaceMode: "project",
            DefaultApprovalPolicy: "workspaceScope",
            NeedsThreadBinding: false),

        new(
            Id: "standup-digest",
            Title: zh ? "根据昨日 Git 生成站会摘要" : "Standup digest from yesterday's git",
            Description: zh
                ? "将昨天的分支、PR 和提交总结成一段简短站会同步内容。"
                : "Summarize yesterday's branches, PRs, and commits into a short standup blurb.",
            Icon: "📝",
            Category: "digest",
            DefaultTitle: zh ? "昨日站会摘要" : "Yesterday's standup digest",
            DefaultDescription: zh
                ? "总结昨天的 Git 活动（分支、提交、PR），生成一段简短站会说明，并列出仍在进行中的事项。"
                : "Summarize yesterday's git activity (branches, commits, PRs) into a short standup paragraph plus a bullet list of what is still in-flight.",
            WorkflowMarkdown: BuildWorkflow(
                locale,
                "project",
                zh
                    ? "你是站会摘要自动化。总结昨天的 Git 活动（分支、提交、PR），并列出仍在进行中的事项。"
                    : "You are the standup-digest automation. Summarize yesterday's git activity (branches, commits, PRs) and list what is still in-flight."),
            DefaultSchedule: Daily(9, 0),
            DefaultWorkspaceMode: "project",
            DefaultApprovalPolicy: "workspaceScope",
            NeedsThreadBinding: false),

        new(
            Id: "skill-growth-suggestions",
            Title: zh ? "从近期 PR/评审中提炼成长建议" : "Skill growth from recent PRs/reviews",
            Description: zh
                ? "每周回顾近期 PR 和评审意见，建议一到两个值得加强的方向。"
                : "Weekly reflection on recent PRs/review comments, suggesting one or two areas to strengthen.",
            Icon: "📈",
            Category: "insight",
            DefaultTitle: zh ? "下一步我该提升什么？" : "What should I level up next?",
            DefaultDescription: zh
                ? "查看我近期的 PR 和评审评论。挑选一到两个本周值得加强的具体方向，并解释原因。"
                : "Look at my recent PRs and review comments. Pick one or two specific areas to strengthen this week and explain why.",
            WorkflowMarkdown: BuildWorkflow(
                locale,
                "project",
                zh
                    ? "你是技能成长自动化。每周分析用户近期的 PR 和评审评论，并建议一到两个具体的技能提升方向。"
                    : "You are the skill-growth automation. Each week, analyze the user's recent PRs + review comments and suggest one or two concrete skill areas to focus on."),
            DefaultSchedule: Every(7L * 24 * 60 * 60 * 1000),
            DefaultWorkspaceMode: "project",
            DefaultApprovalPolicy: "workspaceScope",
            NeedsThreadBinding: false),

        new(
            Id: "weekly-report",
            Title: zh ? "每周活动报告" : "Weekly activity report",
            Description: zh
                ? "将本周的 PR、发布和事故处理整理成一份报告。"
                : "Synthesize the week's PRs, rollouts, and incidents into a single report.",
            Icon: "📅",
            Category: "digest",
            DefaultTitle: zh ? "每周活动报告" : "Weekly activity report",
            DefaultDescription: zh
                ? "撰写一份周报，覆盖已合并的 PR、已发布的内容和已处理的事故。按领域分组，控制在 300 字以内。"
                : "Compose a weekly report covering PRs merged, rollouts shipped, and incidents handled. Keep it under 300 words and group by area.",
            WorkflowMarkdown: BuildWorkflow(
                locale,
                "project",
                zh
                    ? "你是周报自动化。生成一份简洁的周报，覆盖 PR、发布和事故。"
                    : "You are the weekly-report automation. Produce a concise weekly report of PRs, rollouts, and incidents."),
            DefaultSchedule: Every(7L * 24 * 60 * 60 * 1000),
            DefaultWorkspaceMode: "project",
            DefaultApprovalPolicy: "workspaceScope",
            NeedsThreadBinding: false),

        new(
            Id: "regression-alert",
            Title: zh ? "回归风险预警" : "Regression early-warning",
            Description: zh
                ? "将近期变更与基准/追踪数据对比，在回归发布前发出提醒。"
                : "Compare recent changes against benchmarks/traces and alert before regressions ship.",
            Icon: "📉",
            Category: "review",
            DefaultTitle: zh ? "回归风险预警" : "Regression early-warning",
            DefaultDescription: zh
                ? "将近期变更与基准和追踪数据对比。在疑似回归发布前发出预警，并附上最小复现建议。"
                : "Compare recent changes against benchmark and trace data. Raise a warning (with a minimal repro suggestion) before suspected regressions ship.",
            WorkflowMarkdown: BuildWorkflow(
                locale,
                "project",
                zh
                    ? "你是回归预警自动化。根据基准和追踪数据审查近期变更；发现风险时给出预警和复现建议。"
                    : "You are the regression-alert automation. Review recent changes against benchmarks and traces; raise warnings with repro suggestions."),
            DefaultSchedule: Daily(10, 0),
            DefaultWorkspaceMode: "project",
            DefaultApprovalPolicy: "workspaceScope",
            NeedsThreadBinding: false),

        new(
            Id: "ci-triage",
            Title: zh ? "分诊 CI 失败" : "Triage CI failures",
            Description: zh
                ? "每小时扫描 CI 失败，按根因分组并给出最小修复建议。"
                : "Hourly scan of CI failures, grouped by root cause with minimal fix suggestions.",
            Icon: "🛠️",
            Category: "review",
            DefaultTitle: zh ? "分诊 CI 失败" : "Triage CI failures",
            DefaultDescription: zh
                ? "扫描近期 CI 失败，按根因分组，并为每组建议最小修复方案。跳过临时性或不稳定失败。"
                : "Scan recent CI failures, group them by root cause, and suggest the minimal fix for each group. Skip transient/flaky ones.",
            WorkflowMarkdown: BuildWorkflow(
                locale,
                "project",
                zh
                    ? "你是 CI 分诊自动化。按根因聚类 CI 失败并建议最小修复；忽略临时性不稳定失败。"
                    : "You are the CI-triage automation. Cluster CI failures by root cause and suggest minimal fixes; ignore transient flakes."),
            DefaultSchedule: Every(60L * 60 * 1000),
            DefaultWorkspaceMode: "project",
            DefaultApprovalPolicy: "workspaceScope",
            NeedsThreadBinding: false),

        new(
            Id: "issue-triage",
            Title: zh ? "分诊新 Issue" : "Triage new issues",
            Description: zh
                ? "每小时审查新 Issue，建议负责人、优先级和标签。"
                : "Hourly review of new issues, suggesting owner, priority, and labels.",
            Icon: "🎯",
            Category: "review",
            DefaultTitle: zh ? "分诊新 Issue" : "Triage new issues",
            DefaultDescription: zh
                ? "为每个新打开的 Issue 建议负责人、优先级和标签，并引用 Issue 正文中的依据。"
                : "For each newly-opened issue, suggest an owner, priority, and labels. Quote the evidence from the issue body.",
            WorkflowMarkdown: BuildWorkflow(
                locale,
                "project",
                zh
                    ? "你是 Issue 分诊自动化。为新 Issue 推荐负责人、优先级和标签，并引用证据。"
                    : "You are the issue-triage automation. Recommend owner / priority / labels for new issues with quoted evidence."),
            DefaultSchedule: Every(60L * 60 * 1000),
            DefaultWorkspaceMode: "project",
            DefaultApprovalPolicy: "workspaceScope",
            NeedsThreadBinding: false),

        new(
            Id: "watch-thread-replies",
            Title: zh ? "监听线程新回复" : "Watch a thread for replies",
            Description: zh
                ? "定期检查绑定线程中的用户新回复，并自动响应。"
                : "Periodically check a bound thread for new user replies and respond automatically.",
            Icon: "💬",
            Category: "watch",
            DefaultTitle: zh ? "监听线程新回复" : "Watch thread for replies",
            DefaultDescription: zh
                ? "检查绑定线程自上次 tick 以来是否有新的用户回复。如果有，请给出有帮助的响应；如果没有，保持静默。"
                : "Check the bound thread for new user replies since the last tick. If there are any, respond helpfully; if not, stay silent.",
            WorkflowMarkdown: BuildWorkflow(
                locale,
                "project",
                zh
                    ? "你是线程监听自动化。当此线程自上次 tick 以来出现新的用户回复时，请给出有帮助的响应；否则保持静默。"
                    : "You are the thread-watcher automation. When new user replies appear in this thread since the last tick, respond helpfully; otherwise stay silent."),
            DefaultSchedule: Every(5L * 60 * 1000),
            DefaultWorkspaceMode: "project",
            DefaultApprovalPolicy: "workspaceScope",
            NeedsThreadBinding: true),

        new(
            Id: "classic-mini-game",
            Title: zh ? "创建经典小游戏" : "Create a classic mini-game",
            Description: zh
                ? "一次性创意任务：搭建一个小型经典风格游戏。"
                : "One-shot creative task that scaffolds a small classic-style game.",
            Icon: "🎮",
            Category: "creative",
            DefaultTitle: zh ? "构建一个经典小游戏" : "Build a classic mini-game",
            DefaultDescription: zh
                ? "设计并搭建一个小型经典风格游戏（Pong/Snake/Breakout）。输出代码和一段简短说明。"
                : "Design and scaffold a tiny classic-style game (Pong/Snake/Breakout). Output the code and a one-paragraph explanation.",
            WorkflowMarkdown: BuildWorkflow(
                locale,
                "isolated",
                zh
                    ? "你是一次性自动化任务，用于搭建一个小型经典游戏（Pong / Snake / Breakout）。输出代码和简短说明。"
                    : "You are a one-shot automation that scaffolds a small classic game (Pong / Snake / Breakout). Output code + a short explanation."),
            DefaultSchedule: null,
            DefaultWorkspaceMode: "isolated",
            DefaultApprovalPolicy: "workspaceScope",
            NeedsThreadBinding: false)
    ];
    }

    private static string BuildWorkflow(string locale, string workspace, string systemLine)
    {
        // Non-interpolated raw string so Liquid placeholders ({{ task.id }}) stay intact; the two {WORKSPACE}
        // / {SYSTEM} tokens are swapped in via Replace rather than string interpolation.
        var template = string.Equals(NormalizeLocale(locale), ZhHansLocale, StringComparison.Ordinal)
            ? """
            ---
            max_rounds: 10
            workspace: __WORKSPACE__
            ---

            __SYSTEM__

            ## 任务

            - **ID**: {{ task.id }}
            - **标题**: {{ task.title }}

            ## 说明

            {{ task.description }}

            完成后，请调用 **`CompleteLocalTask`** 工具并附上一句简短总结。
            """
            : """
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
    DateTimeOffset? UpdatedAt = null);
