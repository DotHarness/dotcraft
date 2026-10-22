using System.ComponentModel;
using System.Text.Json.Serialization;
using DotCraft.Diagnostics;
using DotCraft.Memory;

namespace DotCraft.Tools;

/// <summary>
/// Tools for creating and managing structured plans in Plan mode.
/// </summary>
public sealed class PlanTools(
    PlanStore planStore,
    Func<string?> sessionIdProvider,
    Action<string, StructuredPlan>? onPlanUpdated = null)
{
    [Description("Create or replace the structured plan for the current session. Call this tool to present your finalized plan to the user. The plan parameter must be one complete, compact decision-complete Markdown document.")]
    [Tool]
    [ToolPresentation("core.create-plan")]
    public async Task<string> CreatePlan(
        [Description("Complete Markdown plan. Start with one H1 title, then concise sections such as Summary, Implementation Changes, Test Plan, and Assumptions. Do not split the body into separate overview/content fields.")] string plan,
        [Description("3-7 high-level actionable implementation tasks, each with its task description. Do not include search, reading, or explanation-only steps.")] List<PlanTodoInput> todos)
    {
        try
        {
            var sessionId = sessionIdProvider();
            if (string.IsNullOrEmpty(sessionId))
            {
                DebugModeService.LogIfEnabled("[PlanTools] CreatePlan: sessionId is null or empty");
                return "Error: No active session.";
            }

            if (string.IsNullOrWhiteSpace(plan))
            {
                DebugModeService.LogIfEnabled("[PlanTools] CreatePlan: plan is null or empty");
                return "Error: CreatePlan.plan must contain the complete Markdown plan.";
            }

            var parsedPlan = PlanMarkdownParser.Parse(plan);
            var todoList = CreateTodos(todos
                .Where(static todo => !string.IsNullOrWhiteSpace(todo.Content))
                .Select(static todo => (todo.Content, PlanTodoStatus.Pending)));

            var now = DateTimeOffset.UtcNow;
            var existing = await planStore.LoadStructuredPlanAsync(sessionId);

            var structured = new StructuredPlan
            {
                Title = parsedPlan.Title,
                Overview = parsedPlan.Overview,
                Content = parsedPlan.Content,
                Todos = todoList,
                CreatedAt = existing?.CreatedAt ?? now,
                UpdatedAt = now
            };

            await planStore.SaveStructuredPlanAsync(sessionId, structured);
            onPlanUpdated?.Invoke(sessionId, structured);

            var taskSummary = todoList.Count > 0
                ? $" with {todoList.Count} task(s)"
                : "";
            return $"Plan \"{parsedPlan.Title}\" saved successfully{taskSummary}. Switch to agent mode to execute.";
        }
        catch (Exception ex)
        {
            DebugModeService.LogIfEnabled($"[PlanTools] CreatePlan exception: {ex.Message}");
            return $"Error: Failed to create plan - {ex.Message}";
        }
    }

    [Description("""
        Write the task list for the current session. This is a conditional organizational tool for complex multi-step work, not a default progress tracker. If the task does not genuinely benefit from a list, just do the work directly.

        When to use this tool:
        - Complex multi-step tasks (3+ genuinely distinct steps)
        - Non-trivial tasks requiring planning or multiple operations
        - User provides multiple tasks (numbered or comma-separated)
        - After initial exploration reveals the scope is larger than first expected

        When NOT to use this tool:
        - Single, straightforward tasks
        - Trivial tasks completable in fewer than 3 non-trivial steps
        - A single obvious change in one well-understood file
        - Purely conversational or informational requests
        - Do NOT include operational steps like linting, testing, or searching the codebase as todo items

        Timing:
        - For non-trivial tasks, gather a minimum of context first (a quick read or search) so items are concrete and file-specific, then create the list before the main implementation work.
        - A list written from guesses is worse than no list. Prefer one good TodoWrite after brief exploration over an immediate speculative one.

        Each call replaces the whole list, so send every item with its current status. Remove items that no longer apply.

        Rules:
        - Mark a task in_progress before starting it, and keep only ONE task in_progress at a time
        - Mark tasks completed IMMEDIATELY after finishing (do not batch completions)
        - Never mark a task completed while its work is partial or failing
        - Keep items high-level and actionable
        """)]
    [Tool]
    [ToolPresentation("core.todo", Operation = "todoWrite")]
    public async Task<string> TodoWrite(
        [Description("The complete task list in order, each with its description and status.")]
        List<TodoWriteInput> todos)
    {
        try
        {
            var sessionId = sessionIdProvider();
            if (string.IsNullOrEmpty(sessionId))
            {
                DebugModeService.LogIfEnabled("[PlanTools] TodoWrite: sessionId is null or empty");
                return "Error: No active session.";
            }

            var emptyIndex = todos.FindIndex(static todo => string.IsNullOrWhiteSpace(todo.Content));
            if (emptyIndex >= 0)
                return $"Error: todos[{emptyIndex}].content is required.";

            var now = DateTimeOffset.UtcNow;
            var existing = await planStore.LoadStructuredPlanAsync(sessionId);
            var plan = new StructuredPlan
            {
                Title = existing?.Title ?? "",
                Overview = existing?.Overview ?? "",
                Content = existing?.Content ?? "",
                Todos = CreateTodos(todos.Select(static todo => (todo.Content, ToStatus(todo.Status)))),
                CreatedAt = existing?.CreatedAt ?? now,
                UpdatedAt = now
            };

            await planStore.SaveStructuredPlanAsync(sessionId, plan);
            onPlanUpdated?.Invoke(sessionId, plan);
            return "Plan updated";
        }
        catch (Exception ex)
        {
            DebugModeService.LogIfEnabled($"[PlanTools] TodoWrite exception: {ex.Message}\n{ex.StackTrace}");
            return $"Error: Failed to write todos - {ex.Message}";
        }
    }

    private static List<PlanTodo> CreateTodos(IEnumerable<(string Content, string Status)> todos) =>
        [.. todos.Select(static (todo, index) => new PlanTodo
        {
            Id = $"todo-{index + 1}",
            Content = todo.Content.Trim(),
            Priority = PlanTodoPriority.Medium,
            Status = todo.Status
        })];

    private static string ToStatus(PlanTodoInputStatus status) => status switch
    {
        PlanTodoInputStatus.Pending => PlanTodoStatus.Pending,
        PlanTodoInputStatus.InProgress => PlanTodoStatus.InProgress,
        PlanTodoInputStatus.Completed => PlanTodoStatus.Completed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}

/// <summary>Status accepted by plan todo tools.</summary>
public enum PlanTodoInputStatus
{
    Pending,
    [JsonStringEnumMemberName("in_progress")]
    InProgress,
    Completed
}

public sealed record ParsedPlanMarkdown(string Title, string Overview, string Content);

public static class PlanMarkdownParser
{
    public static ParsedPlanMarkdown Parse(string markdown)
    {
        var normalized = (markdown ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return new ParsedPlanMarkdown("Plan", string.Empty, string.Empty);

        var lines = normalized.Split('\n').ToList();
        var titleIndex = lines.FindIndex(IsH1);
        var fallbackTitleIndex = lines.FindIndex(line => !string.IsNullOrWhiteSpace(line));
        var title = titleIndex >= 0
            ? StripHeading(lines[titleIndex])
            : ResolveFallbackTitle(lines, fallbackTitleIndex);
        var content = RemoveTitleLine(lines, titleIndex);
        var overview = titleIndex >= 0
            ? ExtractFirstSectionOverview(content)
            : ExtractFirstParagraphAfterTitle(lines, fallbackTitleIndex);
        if (string.IsNullOrWhiteSpace(overview))
        {
            overview = titleIndex >= 0
                ? ExtractFirstParagraphAfterTitle(lines, titleIndex)
                : ExtractFirstSectionOverview(content);
        }

        return new ParsedPlanMarkdown(
            string.IsNullOrWhiteSpace(title) ? "Plan" : title.Trim(),
            overview.Trim(),
            content.Trim());
    }

    private static bool IsH1(string line)
        => line.TrimStart().StartsWith("# ", StringComparison.Ordinal);

    private static bool IsHeading(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("# ", StringComparison.Ordinal)
            || trimmed.StartsWith("## ", StringComparison.Ordinal)
            || trimmed.StartsWith("### ", StringComparison.Ordinal)
            || trimmed.StartsWith("#### ", StringComparison.Ordinal)
            || trimmed.StartsWith("##### ", StringComparison.Ordinal)
            || trimmed.StartsWith("###### ", StringComparison.Ordinal);
    }

    private static string StripHeading(string line)
    {
        var trimmed = line.Trim();
        while (trimmed.StartsWith('#'))
            trimmed = trimmed[1..].TrimStart();
        return trimmed.Trim().TrimEnd('#').Trim();
    }

    private static string ResolveFallbackTitle(IReadOnlyList<string> lines, int fallbackTitleIndex)
    {
        var first = fallbackTitleIndex >= 0 ? lines[fallbackTitleIndex] : null;
        return string.IsNullOrWhiteSpace(first) ? "Plan" : StripInlineMarkdown(first);
    }

    private static string RemoveTitleLine(IReadOnlyList<string> lines, int titleIndex)
    {
        if (titleIndex < 0)
            return string.Join('\n', lines).Trim();

        var contentLines = lines.Where((_, index) => index != titleIndex).ToList();
        while (contentLines.Count > 0 && string.IsNullOrWhiteSpace(contentLines[0]))
            contentLines.RemoveAt(0);
        return string.Join('\n', contentLines).Trim();
    }

    private static string ExtractFirstSectionOverview(string content)
    {
        var lines = content.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!IsHeading(lines[i]))
                continue;

            var paragraph = ReadFirstParagraph(lines, i + 1, stopAtHeading: true);
            if (!string.IsNullOrWhiteSpace(paragraph))
                return paragraph;
        }

        return string.Empty;
    }

    private static string ExtractFirstParagraphAfterTitle(IReadOnlyList<string> lines, int titleIndex)
    {
        var start = titleIndex >= 0 ? titleIndex + 1 : 0;
        return ReadFirstParagraph(lines, start, stopAtHeading: false);
    }

    private static string ReadFirstParagraph(IReadOnlyList<string> lines, int start, bool stopAtHeading)
    {
        var paragraph = new List<string>();
        for (var i = start; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                if (paragraph.Count > 0)
                    break;
                continue;
            }

            if (IsHeading(line))
            {
                if (paragraph.Count > 0)
                    break;
                if (stopAtHeading)
                    break;
                continue;
            }

            paragraph.Add(StripInlineMarkdown(line));
        }

        return string.Join(' ', paragraph).Trim();
    }

    private static string StripInlineMarkdown(string line)
    {
        var trimmed = line.Trim();
        while (trimmed.StartsWith('>'))
            trimmed = trimmed[1..].TrimStart();
        if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            trimmed = trimmed[2..].TrimStart();
        return trimmed.Trim();
    }
}

/// <summary>
/// Input DTO for a single todo item in CreatePlan.
/// </summary>
public sealed class PlanTodoInput
{
    public required string Content { get; set; }
}

/// <summary>
/// Input DTO for a single todo item in TodoWrite.
/// </summary>
public sealed class TodoWriteInput
{
    public required string Content { get; set; }
    public required PlanTodoInputStatus Status { get; set; }
}
