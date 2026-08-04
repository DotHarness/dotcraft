using DotCraft.Commands.Core;
using DotCraft.Text;
using DotCraft.Sessions;

namespace DotCraft.Commands.Handlers;

/// <summary>
/// Handles /stop command to cancel an in-progress agent run for the current session.
/// Only admins may use this command.
/// </summary>
public sealed class StopCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string[] Commands => ["/stop"];

    /// <inheritdoc />
    public async Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        if (context.SessionService == null || !context.SessionId.StartsWith("thread_", StringComparison.Ordinal))
        {
            await responder.SendTextAsync(FallbackText.CommandStopNoActiveRun);
            return CommandResult.HandledResult();
        }

        var thread = await context.SessionService.GetThreadAsync(context.SessionId);
        var activeTurn = thread.Turns.LastOrDefault(t =>
            t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput);
        if (activeTurn == null)
        {
            await responder.SendTextAsync(FallbackText.CommandStopNoActiveRun);
            return CommandResult.HandledResult();
        }

        await context.SessionService.CancelTurnAsync(thread.Id, activeTurn.Id);
        await responder.SendTextAsync(FallbackText.CommandStopStopped);
        return CommandResult.HandledResult();
    }
}
