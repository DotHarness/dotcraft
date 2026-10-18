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

        var commands = context.CommandRegistry.ListCommands(context)
            .Where(command => context.IsAdmin || !command.RequiresAdmin)
            .ToList();
        var builtins = commands.Where(c => !string.Equals(c.Category, "custom", StringComparison.OrdinalIgnoreCase)).ToList();
        var customs = commands.Where(c => string.Equals(c.Category, "custom", StringComparison.OrdinalIgnoreCase)).ToList();

        var sb = new StringBuilder();
        sb.AppendLine(FallbackText.CommandHelpTitle);
        foreach (var command in builtins)
        {
            var aliases = command.Aliases.Length > 0 ? $", {string.Join(", ", command.Aliases)}" : string.Empty;
            var admin = command.RequiresAdmin ? $" {FallbackText.CommandHelpAdminSuffix}" : string.Empty;
            sb.AppendLine($"{command.Name}{aliases} - {command.Description}{admin}");
        }

        if (customs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(FallbackText.CommandHelpCustomSection);
            foreach (var command in customs)
                sb.AppendLine($"{command.Name} - {command.Description}");
        }

        return Task.FromResult(CommandResult.HandledResult(sb.ToString().TrimEnd()));
    }
}
