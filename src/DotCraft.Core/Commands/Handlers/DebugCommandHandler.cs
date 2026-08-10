using DotCraft.Commands.Core;
using DotCraft.Text;

namespace DotCraft.Commands.Handlers;

/// <summary>
/// Handles /debug command to toggle debug mode.
/// </summary>
public sealed class DebugCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string[] Commands => ["/debug"];
    
    /// <inheritdoc />
    public async Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        var newState = Diagnostics.DebugModeService.Toggle();
        var statusMsg = newState ? FallbackText.DebugEnabled : FallbackText.DebugDisabled;
        await responder.SendTextAsync(statusMsg);
        
        return CommandResult.HandledResult();
    }
}
