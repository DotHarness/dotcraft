using DotCraft.Commands.Core;
using DotCraft.Text;
using DotCraft.Sessions;

namespace DotCraft.Commands.Handlers;

/// <summary>
/// Handles /new command to archive existing threads for an identity and switch to a fresh thread.
/// </summary>
public sealed class NewCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string[] Commands => ["/new"];

    /// <inheritdoc />
    public async Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        ThreadResetResult? reset = null;
        if (context.SessionService != null)
        {
            var identity = new SessionIdentity
            {
                ChannelName = context.Source.ToLowerInvariant(),
                UserId = context.UserId,
                ChannelContext = context.ChannelContext,
                WorkspacePath = context.WorkspacePath
            };

            reset = await context.SessionService.ResetConversationAsync(identity);
            context.AgentFactory?.RemoveTokenTracker(context.SessionId);
            context.AgentFactory?.RemoveTokenTracker(reset.Thread.Id);
        }

        await responder.SendTextAsync(FallbackText.CommandNewCleared);
        if (reset != null)
        {
            return CommandResult.SessionResetResult(
                reset.Thread.Id,
                reset.ArchivedThreadIds,
                reset.CreatedLazily);
        }

        return CommandResult.HandledResult();
    }
}
