using DotCraft.Commands.Core;
using DotCraft.Localization;
using Spectre.Console;

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
        var statusMsg = newState ? Strings.DebugEnabled : Strings.DebugDisabled;
        await responder.SendTextAsync(statusMsg);
        
        AnsiConsole.MarkupLine(
            $"[grey][[{context.Source}]][/] [yellow]Debug mode {(newState ? "enabled" : "disabled")}[/] by [green]{Markup.Escape(context.UserName)}[/] (uid={context.UserId})");
        
        return CommandResult.HandledResult();
    }
}
