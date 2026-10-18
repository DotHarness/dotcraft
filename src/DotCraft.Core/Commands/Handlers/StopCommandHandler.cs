using DotCraft.Commands.Core;
using DotCraft.Sessions;
using DotCraft.Text;

namespace DotCraft.Commands.Handlers;

public sealed class StopCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string[] Commands => ["/stop"];

    /// <inheritdoc />
    public async Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        if (context.SessionService is not { } sessions)
            return CommandResult.HandledResult(FallbackText.CommandServiceUnavailable);

        var all = string.Equals(context.Arguments.FirstOrDefault(), "all", StringComparison.OrdinalIgnoreCase);
        return all
            ? await StopAllAsync(sessions, context.SessionId)
            : await StopTurnAsync(sessions, context.SessionId);
    }

    private static async Task<CommandResult> StopTurnAsync(ISessionService sessions, string threadId)
    {
        var thread = await sessions.GetThreadAsync(threadId);
        if (CommandThreads.ActiveTurn(thread) is not { } active)
            return CommandResult.HandledResult(FallbackText.CommandStopNoActiveRun);

        await sessions.CancelTurnAsync(thread.Id, active.Id);
        return CommandResult.HandledResult(FallbackText.CommandStopStopped);
    }

    private static async Task<CommandResult> StopAllAsync(ISessionService sessions, string threadId)
    {
        var removed = await ClearQueueAsync(sessions, threadId);
        var thread = await sessions.GetThreadAsync(threadId);
        if (CommandThreads.ActiveTurn(thread) is { } active)
            await sessions.CancelTurnAsync(thread.Id, active.Id);

        var running = (await sessions.ListBackgroundTasksAsync(threadId))
            .Where(static task => task.Status == BackgroundTaskStatus.Running)
            .ToArray();
        await Task.WhenAll(running.Select(task => sessions.StopBackgroundTaskAsync(threadId, task.TaskId)));
        removed += await ClearQueueAsync(sessions, threadId);

        return CommandResult.HandledResult(FallbackText.CommandStopAllStopped(removed, running.Length));
    }

    private static async Task<int> ClearQueueAsync(ISessionService sessions, string threadId)
    {
        var queued = (await sessions.GetThreadAsync(threadId)).QueuedInputs.Select(static input => input.Id).ToArray();
        var removed = 0;
        foreach (var id in queued)
        {
            try
            {
                await sessions.RemoveQueuedTurnInputAsync(threadId, id);
                removed++;
            }
            catch (KeyNotFoundException)
            {
            }
        }
        return removed;
    }
}
