using System.Text;
using DotCraft.Commands.Core;
using DotCraft.Sessions;
using DotCraft.Text;

namespace DotCraft.Commands.Handlers;

public sealed class TasksCommandHandler : ICommandHandler
{
    private const int ListLimit = 20;

    public string[] Commands => ["/tasks"];

    public async Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        if (context.SessionService is not { } sessions)
            return CommandResult.HandledResult(FallbackText.CommandServiceUnavailable);

        var tasks = CommandThreads.Number(
            await sessions.ListBackgroundTasksAsync(context.SessionId),
            static task => task.StartedAt ?? DateTimeOffset.MinValue,
            static task => task.TaskId);
        return context.Arguments switch
        {
            [] => List(tasks),
            [var verb, var selector] when string.Equals(verb, "stop", StringComparison.OrdinalIgnoreCase)
                => await StopAsync(sessions, context.SessionId, tasks, selector),
            _ => CommandResult.HandledResult(FallbackText.CommandTasksUsage)
        };
    }

    private static CommandResult List(IReadOnlyList<Numbered<BackgroundTask>> tasks)
    {
        if (tasks.Count == 0)
            return CommandResult.HandledResult(FallbackText.CommandTasksEmpty);

        var now = DateTimeOffset.UtcNow;
        var text = new StringBuilder(FallbackText.CommandTasksTitle);
        var choices = new List<CommandChoice>();
        foreach (var (number, task) in tasks.Reverse().Take(ListLimit))
        {
            var elapsed = task.StartedAt is { } started
                ? CommandThreads.FormatElapsed((task.EndedAt ?? now) - started)
                : "-";
            text.Append('\n').Append($"{number}. {task.Kind} · {task.Label} · {task.Status} · {elapsed}");
            if (task.Status == BackgroundTaskStatus.Running)
                choices.Add(new CommandChoice(FallbackText.CommandTasksStopChoice(task.Label), $"/tasks stop {task.TaskId}"));
        }

        return CommandResult.HandledResult(text.ToString(), choices: choices);
    }

    private static async Task<CommandResult> StopAsync(
        ISessionService sessions,
        string threadId,
        IReadOnlyList<Numbered<BackgroundTask>> tasks,
        string selector)
    {
        if (CommandThreads.Select(tasks, selector, static task => task.TaskId) is not { } selected)
            return CommandResult.HandledResult(FallbackText.CommandTasksNotFound(selector));

        var stopped = await sessions.StopBackgroundTaskAsync(threadId, selected.Value.TaskId);
        return CommandResult.HandledResult(FallbackText.CommandTasksStopped(stopped.Label, stopped.Status));
    }
}
