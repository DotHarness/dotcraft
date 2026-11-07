using System.Text;
using DotCraft.Commands.Core;
using DotCraft.Text;

namespace DotCraft.Commands.Handlers;

public sealed class HelpCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string[] Commands => ["/help"];

    /// <inheritdoc />
    public Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        if (context.CommandRegistry == null)
            return Task.FromResult(CommandResult.HandledResult(FallbackText.CommandServiceUnavailable));

        var commands = context.CommandRegistry.ListCommands()
            .Where(command => context.IsAdmin || !command.RequiresAdmin)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine(FallbackText.CommandHelpTitle);
        foreach (var command in commands)
        {
            var aliases = command.Aliases.Length > 0 ? $", {string.Join(", ", command.Aliases)}" : string.Empty;
            var admin = command.RequiresAdmin ? $" {FallbackText.CommandHelpAdminSuffix}" : string.Empty;
            sb.AppendLine($"{command.Name}{aliases} - {command.Description}{admin}");
        }

        return Task.FromResult(CommandResult.HandledResult(sb.ToString().TrimEnd()));
    }
}
