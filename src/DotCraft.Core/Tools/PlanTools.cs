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
                .Select(static todo => (todo.Content, PlanTodoStatus.Pending, (string?)null)));

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
        Create and update a task list for the current session. The list is rendered to the user as your working plan.

        - Whole list: `todos`, every step in order, each with `content`, `status`, and `activeForm` (present-tense label shown while in progress). It replaces the previous list.
        - `scope` is required with `todos` and says what this request is to the session: `first_task` (nothing finished here yet). After that, `new_task` (unrelated work: it builds on nothing done, found, or decided here) or `continuation` (everything else, including the next iteration and acting on what you just investigated or suggested). When unsure, use `continuation`.
        - Next step: `{"advance": true}` completes the in-progress step and starts the next pending one. Out of order: `{"done": [2], "start": 4}` (1-based).
        - Plan changed, including right after the user sends a new instruction mid-task: send `todos` again, the whole new list, so the list never shows an abandoned plan.
        - Keep exactly one step `in_progress` while working.
        """)]
    [Tool]
    [ToolPresentation("core.todo", Operation = "todoWrite")]
    public async Task<ToolExecutionResult> TodoWrite(
        [Description("The whole task list in order. Replaces the previous list.")]
        List<TodoWriteInput>? todos = null,
        [Description("Required with todos: first_task, continuation, or new_task. Omit on advance, done, or start updates.")]
        TodoWriteScope? scope = null,
        [Description("Complete the in-progress step and start the next pending one.")]
        bool? advance = null,
        [Description("1-based step numbers to mark completed.")]
        List<int>? done = null,
        [Description("1-based step number to mark in progress.")]
        int? start = null)
    {
        var sessionId = sessionIdProvider();
        if (string.IsNullOrEmpty(sessionId))
            return ToolExecutionResult.Failed(new ToolError(ToolErrorCodes.Unavailable, "No active session."));

        var update = new TodoListUpdate(advance == true, done ?? [], start);
        var existing = await planStore.LoadStructuredPlanAsync(sessionId);
        var now = DateTimeOffset.UtcNow;
        StructuredPlan plan;
        string result;
        if (todos is not null)
        {
            if (scope is not { } requestScope)
                return Invalid("todos also needs scope: first_task, continuation, or new_task. Send the same todos again with it.");
            if (todos.Count == 0)
                return Invalid("todos needs at least one step.");
            var emptyIndex = todos.FindIndex(static todo => string.IsNullOrWhiteSpace(todo.Content));
            if (emptyIndex >= 0)
                return Invalid($"todos[{emptyIndex}].content is required.");

            var keepsPlan = requestScope != TodoWriteScope.NewTask;
            plan = new StructuredPlan
            {
                Title = keepsPlan ? existing?.Title ?? "" : "",
                Overview = keepsPlan ? existing?.Overview ?? "" : "",
                Content = keepsPlan ? existing?.Content ?? "" : "",
                Todos = CreateTodos(todos.Select(static todo => (todo.Content, ToStatus(todo.Status), todo.ActiveForm))),
                CreatedAt = existing?.CreatedAt ?? now,
                UpdatedAt = now
            };
            result = TodoListUpdate.DescribeReplacement(plan.Todos, update.IsEmpty);
        }
        else
        {
            if (update.IsEmpty)
                return Invalid("Pass todos with scope for the whole list, advance: true, or done and start positions.");
            if (existing is not { Todos.Count: > 0 })
                return Invalid("There is no task list yet. Send the whole list with todos and scope.");
            if (update.HasInvalidPosition(existing.Todos.Count))
                return Invalid($"Nothing changed: done and start take step numbers from 1 to {existing.Todos.Count}. Send the update again with numbers that name a step, or the whole list with todos and scope if the plan changed.");

            plan = existing;
            update.Apply(plan.Todos);
            plan.UpdatedAt = now;
            result = $"Task list updated: {TodoListUpdate.DescribeProgress(plan.Todos)}.";
        }

        await planStore.SaveStructuredPlanAsync(sessionId, plan);
        onPlanUpdated?.Invoke(sessionId, plan);
        return ToolExecutionResult.Succeeded(result);
    }

    private static ToolExecutionResult Invalid(string message) =>
        ToolExecutionResult.Failed(new ToolError(ToolErrorCodes.InputInvalid, message));

    private static List<PlanTodo> CreateTodos(IEnumerable<(string Content, string Status, string? ActiveForm)> todos) =>
        [.. todos.Select(static (todo, index) => new PlanTodo
        {
            Id = $"todo-{index + 1}",
            Content = todo.Content.Trim(),
            Priority = PlanTodoPriority.Medium,
            Status = todo.Status,
            ActiveForm = string.IsNullOrWhiteSpace(todo.ActiveForm) ? null : todo.ActiveForm.Trim()
        })];

    private static string ToStatus(PlanTodoInputStatus status) => status switch
    {
        PlanTodoInputStatus.Pending => PlanTodoStatus.Pending,
        PlanTodoInputStatus.InProgress => PlanTodoStatus.InProgress,
        PlanTodoInputStatus.Completed => PlanTodoStatus.Completed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}

/// <summary>What a whole-list TodoWrite call is to the session.</summary>
public enum TodoWriteScope
{
    [JsonStringEnumMemberName("first_task")]
    FirstTask,
    [JsonStringEnumMemberName("continuation")]
    Continuation,
    [JsonStringEnumMemberName("new_task")]
    NewTask
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
    public string? ActiveForm { get; set; }
}
