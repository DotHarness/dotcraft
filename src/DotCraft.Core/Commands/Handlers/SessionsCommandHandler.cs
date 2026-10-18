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
        var threads = CommandThreads.RecentFirst(await CommandThreads.NumberConversationAsync(sessions, current));
        var now = DateTimeOffset.UtcNow;
        var text = new StringBuilder(FallbackText.CommandSessionsTitle);
        var choices = new List<CommandChoice>();
        foreach (var (number, thread) in threads)
        {
            var title = CommandThreads.Title(thread.DisplayName, thread.Id);
            var marker = thread.Id == current.Id
                ? $" ({FallbackText.CommandSessionsCurrent})"
                : thread.Status == ThreadStatus.Archived ? $" ({FallbackText.CommandSessionsArchived})" : string.Empty;
            text.Append('\n').Append($"{number}. {title}{marker} · {FallbackText.CommandSessionsAge(CommandThreads.FormatElapsed(now - thread.LastActiveAt))}");
            if (thread.Id != current.Id)
                choices.Add(new CommandChoice(title, $"/resume {thread.Id}"));
        }

        return CommandResult.HandledResult(text.ToString(), choices: choices);
    }
}
