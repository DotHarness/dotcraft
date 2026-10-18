using DotCraft.Commands.Core;
using DotCraft.Sessions;
using DotCraft.Text;

namespace DotCraft.Commands.Handlers;

public sealed class ResumeCommandHandler : ICommandHandler
{
    public string[] Commands => ["/resume"];

    public async Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        if (context.SessionService is not { } sessions)
            return CommandResult.HandledResult(FallbackText.CommandServiceUnavailable);

        var current = await sessions.GetThreadAsync(context.SessionId);
        var threads = await CommandThreads.ListConversationAsync(sessions, current);
        if (context.Arguments.Length != 1 || !CommandThreads.TryParseIndex(context.Arguments[0], threads.Count, out var index))
            return CommandResult.HandledResult(FallbackText.CommandResumeUsage);

        var target = threads[index];
        if (target.Id == current.Id)
            return CommandResult.HandledResult(FallbackText.CommandResumeAlreadyCurrent);

        if (target.Status == ThreadStatus.Archived)
            await sessions.UnarchiveThreadAsync(target.Id);
        await sessions.ArchiveThreadAsync(current.Id);
        return CommandResult.SwitchThread(
            target.Id,
            FallbackText.CommandResumeResumed(CommandThreads.Title(target.DisplayName, target.Id)));
    }
}
