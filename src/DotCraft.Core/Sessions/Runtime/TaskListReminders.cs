using DotCraft.Memory;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

internal static class TaskListReminders
{
    public const string StaleKind = "task_list_reminder";
    public const string CompactionKind = "task_list";

    public const string MidTurnInput = """
        <system-reminder>
        The user sent the message above while you were working. Address it, and do not ignore it.
        </system-reminder>
        """;

    private const int AssistantMessagesBeforeReminder = 10;

    public static bool IsOpen(StructuredPlan? plan) => plan?.Todos.Any(static todo => todo.IsOpen) == true;

    public static bool IsItem(ChatMessage message) =>
        ThreadContextItems.IsKind(message, StaleKind) || ThreadContextItems.IsKind(message, CompactionKind);

    public static bool IsStale(IReadOnlyList<ChatMessage> history)
    {
        var sinceWrite = -1;
        var sinceReminder = -1;
        var assistantMessages = 0;
        for (var i = history.Count - 1; i >= 0 && (sinceWrite < 0 || sinceReminder < 0); i--)
        {
            var message = history[i];
            if (sinceReminder < 0 && ThreadContextItems.IsKind(message, StaleKind))
                sinceReminder = assistantMessages;
            if (message.Role != ChatRole.Assistant)
                continue;
            if (sinceWrite < 0 && message.Contents.OfType<FunctionCallContent>().Any(static call => call.Name == "TodoWrite"))
                sinceWrite = assistantMessages;
            assistantMessages++;
        }

        return (sinceWrite < 0 ? assistantMessages : sinceWrite) >= AssistantMessagesBeforeReminder
            && (sinceReminder < 0 ? assistantMessages : sinceReminder) >= AssistantMessagesBeforeReminder;
    }

    public static string Stale(StructuredPlan plan) => $"""
        The TodoWrite tool hasn't been used recently. If you're working on tasks that would benefit from tracking progress, use it to keep the task list current. Also clean up the task list if it has become stale and no longer matches what you are working on. Ignore this if it does not apply, and do not mention this reminder to the user.

        Here are the existing contents of your task list:
        {Steps(plan)}
        """;

    public static string AfterCompaction(StructuredPlan plan) => $"""
        Also still to do in this turn, on top of what the summary above lists: keep the task list on the user's screen current. Its steps stand like this:
        {Steps(plan)}
        When you start the next step, send TodoWrite {"{"}"advance": true{"}"} in the same response as that step's first tool call. If your work is already past the step shown in progress, send {"{"}"done": [...], "start": n{"}"} with 1-based step numbers. If the summary does not show what each step stands for, do not guess: send the whole list again with scope "continuation" in the same response as your next tool call, finished steps completed and the current one in progress. A request you have not started yet, and every new request from the user, gets its own task list as your instructions say. Do not mention this note to the user.
        """;

    private static string Steps(StructuredPlan plan) =>
        string.Join('\n', plan.Todos.Select(static (todo, index) => $"{index + 1}. [{todo.Status}] {todo.Content.Trim()}"));
}
