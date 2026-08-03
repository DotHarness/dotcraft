using System.ComponentModel;
using DotCraft.Diagnostics;
using DotCraft.Memory;
using DotCraft.Sessions;

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
    [Tool(Icon = "📋", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.CreatePlan))]
    public async Task<string> CreatePlan(
        [Description("Complete Markdown plan. Start with one H1 title, then concise sections such as Summary, Implementation Changes, Test Plan, and Assumptions. Do not split the body into separate overview/content fields.")] string plan,
        [Description("3-7 high-level actionable implementation tasks. Each item has 'id' (short kebab-case) and 'content' (task description). Do not include search, reading, or explanation-only steps.")] List<PlanTodoInput> todos)
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
            var todoList = todos
                .Where(t => !string.IsNullOrWhiteSpace(t.Id) && !string.IsNullOrWhiteSpace(t.Content))
                .Select(t => new PlanTodo
                {
                    Id = t.Id.Trim(),
                    Content = t.Content.Trim(),
                    Priority = PlanTodoPriority.Medium,
                    Status = PlanTodoStatus.Pending
                })
                .ToList();

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

    [Description("Update the status of one or more tasks in the current plan. Call this to mark tasks as in_progress when you start working on them and completed when done.")]
    [Tool(Icon = "✅", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.UpdateTodos))]
    public async Task<string> UpdateTodos(
        [Description("Status updates. Each item has 'id' (task id) and 'status' (pending | in_progress | completed | cancelled).")]
        List<TodoStatusUpdateInput> updates)
    {
        try
        {
            var sessionId = sessionIdProvider();
            if (string.IsNullOrEmpty(sessionId))
            {
                DebugModeService.LogIfEnabled("[PlanTools] UpdateTodos: sessionId is null or empty");
                return "Error: No active session. Please ensure you are in agent mode with an active session.";
            }

            var plan = await planStore.LoadStructuredPlanAsync(sessionId);
            if (plan == null)
            {
                DebugModeService.LogIfEnabled($"[PlanTools] UpdateTodos: No plan exists for session {sessionId}");
                return "Error: No plan exists for the current session. Create a plan first using CreatePlan in plan mode.";
            }

            if (updates.Count == 0)
            {
                DebugModeService.LogIfEnabled("[PlanTools] UpdateTodos: No updates provided");
                return "Error: No updates provided.";
            }

            var results = new List<string>();
            foreach (var upd in updates)
            {
                if (string.IsNullOrWhiteSpace(upd.Id) || string.IsNullOrWhiteSpace(upd.Status))
                    continue;

                var todo = plan.Todos.FirstOrDefault(t => t.Id == upd.Id.Trim());
                if (todo == null)
                {
                    results.Add($"{upd.Id} -> not found");
                    continue;
                }

                var normalizedStatus = upd.Status.Trim().ToLowerInvariant();
                if (normalizedStatus is not (PlanTodoStatus.Pending or PlanTodoStatus.InProgress
                    or PlanTodoStatus.Completed or PlanTodoStatus.Cancelled))
                {
                    results.Add($"{upd.Id} -> invalid status '{upd.Status}'");
                    continue;
                }

                todo.Status = normalizedStatus;
                results.Add($"{upd.Id} -> {normalizedStatus}");
            }

            if (results.Count == 0)
            {
                DebugModeService.LogIfEnabled("[PlanTools] UpdateTodos: No tasks were updated");
                return "No tasks were updated.";
            }

            plan.UpdatedAt = DateTimeOffset.UtcNow;
            await planStore.SaveStructuredPlanAsync(sessionId, plan);
            onPlanUpdated?.Invoke(sessionId, plan);
            DebugModeService.LogIfEnabled($"[PlanTools] UpdateTodos: Updated {results.Count} task(s) for session {sessionId}");
            return "Plan updated";
        }
        catch (Exception ex)
        {
            DebugModeService.LogIfEnabled($"[PlanTools] UpdateTodos exception: {ex.Message}\n{ex.StackTrace}");
            return $"Error: Failed to update todos - {ex.Message}";
        }
    }

    [Description("""
        Create or update a structured task list for the current session. This is a conditional organizational tool for complex multi-step work, not a default progress tracker. If the task does not genuinely benefit from a list, just do the work directly.

        When to use this tool:
        - Complex multi-step tasks (3+ genuinely distinct steps)
        - Non-trivial tasks requiring planning or multiple operations
        - User provides multiple tasks (numbered or comma-separated)
        - After initial exploration reveals the scope is larger than first expected
        - When starting a task, mark it as in_progress; when done, mark it as completed

        When NOT to use this tool:
        - Single, straightforward tasks
        - Trivial tasks completable in fewer than 3 non-trivial steps
        - A single obvious change in one well-understood file
        - Purely conversational or informational requests
        - Do NOT include operational steps like linting, testing, or searching the codebase as todo items

        Timing:
        - For non-trivial tasks, gather a minimum of context first (a quick read or search) so items are concrete and file-specific, then create the list before the main implementation work.
        - Do not create a list for trivial single-step tasks.
        - A list written from guesses is worse than no list. Prefer one good TodoWrite after brief exploration over an immediate speculative one.

        Parameter 'merge':
        - false (default): Replace the entire todo list with the provided items
        - true: Merge updates into the existing list by id. Matched items are updated; new items are added; unmentioned items are left unchanged

        Task states: pending, in_progress, completed, cancelled

        Rules:
        - Mark tasks completed IMMEDIATELY after finishing (do not batch completions)
        - Only ONE task should be in_progress at a time
        - Keep items high-level and actionable
        """)]
    [Tool(Icon = "📝", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.TodoWrite))]
    public async Task<string> TodoWrite(
        [Description("Array of todo items. Each item has 'id' (short kebab-case), 'content' (task description), and 'status' (pending | in_progress | completed | cancelled).")]
        List<TodoWriteInput> todos,
        [Description("When false (default), replace the entire todo list. When true, merge updates into the existing list by id.")]
        bool merge = false)
    {
        try
        {
            var sessionId = sessionIdProvider();
            if (string.IsNullOrEmpty(sessionId))
            {
                DebugModeService.LogIfEnabled("[PlanTools] TodoWrite: sessionId is null or empty");
                return "Error: No active session.";
            }

            var validItems = todos
                .Where(t => !string.IsNullOrWhiteSpace(t.Id) && !string.IsNullOrWhiteSpace(t.Content))
                .ToList();

            if (validItems.Count == 0)
            {
                DebugModeService.LogIfEnabled("[PlanTools] TodoWrite: No valid items provided");
                return "Error: No valid todo items provided.";
            }

            var now = DateTimeOffset.UtcNow;
            var existing = await planStore.LoadStructuredPlanAsync(sessionId);

            StructuredPlan plan;

            if (merge && existing != null)
            {
                // Merge: update matched items by id, append new ones
                plan = existing;
                foreach (var item in validItems)
                {
                    var normalizedStatus = NormalizeStatus(item.Status);
                    var existingTodo = plan.Todos.FirstOrDefault(t => t.Id == item.Id.Trim());
                    if (existingTodo != null)
                    {
                        if (!string.IsNullOrWhiteSpace(item.Content))
                            existingTodo.Content = item.Content.Trim();
                        if (!string.IsNullOrWhiteSpace(normalizedStatus))
                            existingTodo.Status = normalizedStatus;
                    }
                    else
                    {
                        plan.Todos.Add(new PlanTodo
                        {
                            Id = item.Id.Trim(),
                            Content = item.Content.Trim(),
                            Priority = PlanTodoPriority.Medium,
                            Status = normalizedStatus is { Length: > 0 } s ? s : PlanTodoStatus.Pending
                        });
                    }
                }
                plan.UpdatedAt = now;
            }
            else
            {
                // Replace: build a fresh todo list; create the plan if it doesn't exist yet
                var todoList = validItems
                    .Select(t => new PlanTodo
                    {
                        Id = t.Id.Trim(),
                        Content = t.Content.Trim(),
                        Priority = PlanTodoPriority.Medium,
                        Status = NormalizeStatus(t.Status) is { Length: > 0 } s ? s : PlanTodoStatus.Pending
                    })
                    .ToList();

                plan = new StructuredPlan
                {
                    Title = existing?.Title ?? "Task Tracking",
                    Overview = existing?.Overview ?? "",
                    Content = existing?.Content ?? "",
                    Todos = todoList,
                    CreatedAt = existing?.CreatedAt ?? now,
                    UpdatedAt = now
                };
            }

            await planStore.SaveStructuredPlanAsync(sessionId, plan);
            onPlanUpdated?.Invoke(sessionId, plan);

            var action = merge && existing != null ? "Updated" : "Created";
            DebugModeService.LogIfEnabled($"[PlanTools] TodoWrite: {action} {plan.Todos.Count} task(s) for session {sessionId}");
            return "Plan updated";
        }
        catch (Exception ex)
        {
            DebugModeService.LogIfEnabled($"[PlanTools] TodoWrite exception: {ex.Message}\n{ex.StackTrace}");
            return $"Error: Failed to write todos - {ex.Message}";
        }
    }

    private static string NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return "";
        var s = status.Trim().ToLowerInvariant();
        return s is PlanTodoStatus.Pending or PlanTodoStatus.InProgress
            or PlanTodoStatus.Completed or PlanTodoStatus.Cancelled
            ? s : "";
    }
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
    public string Id { get; set; } = "";
    public string Content { get; set; } = "";
}

/// <summary>
/// Input DTO for a single todo status update in UpdateTodos.
/// </summary>
public sealed class TodoStatusUpdateInput
{
    public string Id { get; set; } = "";
    public string Status { get; set; } = "";
}

/// <summary>
/// Input DTO for a single todo item in TodoWrite.
/// </summary>
public sealed class TodoWriteInput
{
    public string Id { get; set; } = "";
    public string Content { get; set; } = "";
    public string Status { get; set; } = PlanTodoStatus.Pending;
}
