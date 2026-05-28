namespace DotCraft.Commands.Core;

/// <summary>
/// Interface for command handlers.
/// </summary>
public interface ICommandHandler
{
    /// <summary>
    /// Gets the command names this handler can process (e.g., "/new", "/clear").
    /// </summary>
    string[] Commands { get; }

    /// <summary>
    /// Optional metadata used by <see cref="CommandRegistry"/> for command discovery.
    /// </summary>
    CommandRegistration? Metadata => null;
    
    /// <summary>
    /// Handles the command.
    /// </summary>
    /// <param name="context">The command context.</param>
    /// <param name="responder">The responder for sending messages.</param>
    /// <returns>The result of command execution.</returns>
    Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder);
}
