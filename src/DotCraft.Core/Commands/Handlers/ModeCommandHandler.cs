using DotCraft.Commands.Core;
using DotCraft.Text;

namespace DotCraft.Commands.Handlers;

public sealed class ModeCommandHandler : ICommandHandler
{
    private static readonly string[] Modes = ["agent", "plan"];

    public string[] Commands => ["/mode"];

    public async Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        if (context.SessionService is not { } sessions)
            return CommandResult.HandledResult(FallbackText.CommandServiceUnavailable);

        var requested = context.Arguments.FirstOrDefault()?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(requested))
        {
            var thread = await sessions.GetThreadAsync(context.SessionId);
            var choices = Modes.Select(static mode => new CommandChoice(mode, $"/mode {mode}")).ToArray();
            return CommandResult.HandledResult(FallbackText.CommandModeCurrent(CurrentMode(thread)), choices: choices);
        }

        if (!Modes.Contains(requested) || context.Arguments.Length > 1)
            return CommandResult.HandledResult(FallbackText.CommandModeUsage);

        await sessions.SetThreadModeAsync(context.SessionId, requested);
        return CommandResult.HandledResult(FallbackText.CommandModeSet(requested));
    }

    internal static string CurrentMode(Sessions.SessionThread thread) =>
        string.IsNullOrWhiteSpace(thread.Configuration?.Mode) ? "agent" : thread.Configuration.Mode;
}
