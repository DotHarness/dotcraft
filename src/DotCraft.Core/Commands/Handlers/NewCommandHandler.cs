using DotCraft.Commands.Core;
using DotCraft.Text;

namespace DotCraft.Commands.Handlers;

public sealed class NewCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string[] Commands => ["/new"];

    /// <inheritdoc />
    public async Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        if (context.SessionService is not { } sessions)
            return CommandResult.HandledResult(FallbackText.CommandServiceUnavailable);

        var current = await sessions.GetThreadAsync(context.SessionId);
        var thread = await sessions.CreateThreadAsync(CommandThreads.IdentityOf(current));
        await sessions.ArchiveThreadAsync(current.Id);
        return CommandResult.SwitchThread(thread.Id, FallbackText.CommandNewStarted);
    }
}
