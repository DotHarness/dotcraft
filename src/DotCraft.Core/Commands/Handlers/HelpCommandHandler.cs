using System.Text;
using DotCraft.Commands.Core;
using DotCraft.Localization;

namespace DotCraft.Commands.Handlers;

/// <summary>
/// Handles /help command to display available commands.
/// </summary>
public sealed class HelpCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string[] Commands => ["/help"];
    
    /// <inheritdoc />
    public async Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        if (context.CommandRegistry == null)
        {
            await responder.SendTextAsync("/help is unavailable.");
            return CommandResult.HandledResult();
        }

        var commands = context.CommandRegistry.ListCommands(context);
        var builtins = commands.Where(c => !string.Equals(c.Category, "custom", StringComparison.OrdinalIgnoreCase)).ToList();
        var customs = commands.Where(c => string.Equals(c.Category, "custom", StringComparison.OrdinalIgnoreCase)).ToList();

        var sb = new StringBuilder();
        sb.AppendLine(Strings.CommandHelpTitle);
        foreach (var command in builtins)
        {
            var aliases = command.Aliases.Length > 0 ? $", {string.Join(", ", command.Aliases)}" : string.Empty;
            var admin = command.RequiresAdmin ? $" {Strings.CommandHelpAdminSuffix}" : string.Empty;
            sb.AppendLine($"{command.Name}{aliases} - {command.Description}{admin}");
        }

        if (customs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Strings.CommandHelpCustomSection);
            foreach (var command in customs)
                sb.AppendLine($"{command.Name} - {command.Description}");
        }
        
        await responder.SendTextAsync(sb.ToString().TrimEnd());
        return CommandResult.HandledResult();
    }
}
