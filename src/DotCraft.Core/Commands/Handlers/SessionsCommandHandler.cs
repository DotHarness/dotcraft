using System.Text;
using DotCraft.Commands.Core;
using DotCraft.Sessions;
using DotCraft.Text;

namespace DotCraft.Commands.Handlers;

public sealed class SessionsCommandHandler : ICommandHandler
{
    public string[] Commands => ["/sessions"];

    public async Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        if (context.SessionService is not { } sessions)
            return CommandResult.HandledResult(FallbackText.CommandServiceUnavailable);

        var current = await sessions.GetThreadAsync(context.SessionId);
        var threads = await CommandThreads.ListConversationAsync(sessions, current);
        var now = DateTimeOffset.UtcNow;
        var text = new StringBuilder(FallbackText.CommandSessionsTitle);
        var choices = new List<CommandChoice>();
        for (var index = 0; index < threads.Count; index++)
        {
            var thread = threads[index];
            var number = index + 1;
            var title = CommandThreads.Title(thread.DisplayName, thread.Id);
            var marker = thread.Id == current.Id
                ? $" ({FallbackText.CommandSessionsCurrent})"
                : thread.Status == ThreadStatus.Archived ? $" ({FallbackText.CommandSessionsArchived})" : string.Empty;
            text.Append('\n').Append($"{number}. {title}{marker} · {FallbackText.CommandSessionsAge(CommandThreads.FormatElapsed(now - thread.LastActiveAt))}");
            if (thread.Id != current.Id)
                choices.Add(new CommandChoice(title, $"/resume {number}"));
        }

        return CommandResult.HandledResult(text.ToString(), choices: choices);
    }
}
