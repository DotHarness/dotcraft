using DotCraft.Commands.Core;
using DotCraft.Localization;
using DotCraft.Protocol;
using Spectre.Console;

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
        if (context.SessionService != null && context.SessionId.StartsWith("thread_", StringComparison.Ordinal))
        {
            var thread = await context.SessionService.GetThreadAsync(context.SessionId);
            var activeTurn = thread.Turns.LastOrDefault(t =>
                t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput);
            if (activeTurn == null)
            {
                await responder.SendTextAsync(Strings.CommandStopNoActiveRun);
                return CommandResult.HandledResult();
            }

            await context.SessionService.CancelTurnAsync(thread.Id, activeTurn.Id);
            await responder.SendTextAsync(Strings.CommandStopStopped);
            return CommandResult.HandledResult();
        }

        var registry = context.ActiveRunRegistry;
        if (registry == null || !registry.TryCancelAndRemove(context.SessionId))
        {
            await responder.SendTextAsync(Strings.CommandStopNoActiveRun);
            AnsiConsole.MarkupLine($"[grey][[{context.Source}]][/] /stop: no active run for {Markup.Escape(context.SessionId)}");
            return CommandResult.HandledResult();
        }

        await responder.SendTextAsync(Strings.CommandStopStopped);
        AnsiConsole.MarkupLine($"[grey][[{context.Source}]][/] [yellow]/stop:[/] cancelled run for {Markup.Escape(context.SessionId)}");
        return CommandResult.HandledResult();
    }
}
