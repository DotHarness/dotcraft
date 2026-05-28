using DotCraft.Commands.Core;
using DotCraft.Localization;

namespace DotCraft.Commands.Handlers;

/// <summary>
/// Handles /heartbeat command to trigger heartbeat checks.
/// </summary>
public sealed class HeartbeatCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string[] Commands => ["/heartbeat"];
    
    /// <inheritdoc />
    public async Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        if (context.HeartbeatService == null)
        {
            await responder.SendTextAsync(Strings.HeartbeatUnavailable);
            return CommandResult.HandledResult();
        }
        
        var args = context.Arguments;
        var subCmd = args.Length > 0 ? args[0] : "trigger";
        
        if (subCmd == "trigger")
        {
            await responder.SendTextAsync(Strings.TriggeringHeartbeat);
            var result = await context.HeartbeatService.TriggerNowAsync();
            if (result != null)
                await responder.SendTextAsync($"{Strings.HeartbeatResult}:\n{result}");
            else
                await responder.SendTextAsync(Strings.HeartbeatNoResponse);
        }
        else
        {
            await responder.SendTextAsync(Strings.HeartbeatUsage);
        }
        
        return CommandResult.HandledResult();
    }
}
