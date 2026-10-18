using DotCraft.Commands.Core;
using DotCraft.Text;

namespace DotCraft.Commands.Handlers;

public sealed class CompactCommandHandler : ICommandHandler
{
    public string[] Commands => ["/compact"];

    public async Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        if (context.SessionService is not { } sessions)
            return CommandResult.HandledResult(FallbackText.CommandServiceUnavailable);

        var result = await sessions.CompactThreadAsync(context.SessionId);
        return CommandResult.HandledResult(string.IsNullOrWhiteSpace(result.Message)
            ? FallbackText.CommandCompactResult(result.Outcome)
            : result.Message);
    }
}
