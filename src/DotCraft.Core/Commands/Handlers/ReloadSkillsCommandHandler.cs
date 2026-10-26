using DotCraft.Commands.Core;
using DotCraft.Sessions;
using DotCraft.Text;

namespace DotCraft.Commands.Handlers;

public sealed class ReloadSkillsCommandHandler : ICommandHandler
{
    public string[] Commands => ["/reload-skills"];

    public Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        if (context.SessionService is not ISkillReloadService skills)
            return Task.FromResult(CommandResult.HandledResult(FallbackText.CommandServiceUnavailable));

        skills.ReloadSkills();
        return Task.FromResult(CommandResult.HandledResult(FallbackText.CommandReloadSkillsResult));
    }
}
