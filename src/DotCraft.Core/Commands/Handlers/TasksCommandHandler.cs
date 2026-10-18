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

        var tasks = await sessions.ListBackgroundTasksAsync(context.SessionId);
        return context.Arguments switch
        {
            [] => List(tasks),
            [var verb, var number] when string.Equals(verb, "stop", StringComparison.OrdinalIgnoreCase)
                => await StopAsync(sessions, context.SessionId, tasks, number),
            _ => CommandResult.HandledResult(FallbackText.CommandTasksUsage)
        };
    }

    private static CommandResult List(IReadOnlyList<BackgroundTask> tasks)
    {
        if (tasks.Count == 0)
            return CommandResult.HandledResult(FallbackText.CommandTasksEmpty);

        var now = DateTimeOffset.UtcNow;
        var text = new StringBuilder(FallbackText.CommandTasksTitle);
        var choices = new List<CommandChoice>();
        for (var index = 0; index < Math.Min(tasks.Count, ListLimit); index++)
        {
            var task = tasks[index];
            var number = index + 1;
            var elapsed = task.StartedAt is { } started
                ? CommandThreads.FormatElapsed((task.EndedAt ?? now) - started)
                : "-";
            text.Append('\n').Append($"{number}. {task.Kind} · {task.Label} · {task.Status} · {elapsed}");
            if (task.Status == BackgroundTaskStatus.Running)
                choices.Add(new CommandChoice(FallbackText.CommandTasksStopChoice(task.Label), $"/tasks stop {number}"));
        }

        return CommandResult.HandledResult(text.ToString(), choices: choices);
    }

    private static async Task<CommandResult> StopAsync(
        ISessionService sessions,
        string threadId,
        IReadOnlyList<BackgroundTask> tasks,
        string number)
    {
        if (!CommandThreads.TryParseIndex(number, tasks.Count, out var index))
            return CommandResult.HandledResult(FallbackText.CommandTasksNotFound(number));

        var stopped = await sessions.StopBackgroundTaskAsync(threadId, tasks[index].TaskId);
        return CommandResult.HandledResult(FallbackText.CommandTasksStopped(stopped.Label, stopped.Status));
    }
}
