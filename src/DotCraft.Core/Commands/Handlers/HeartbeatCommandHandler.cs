using DotCraft.Commands.Core;
using DotCraft.Text;

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
            await responder.SendTextAsync(FallbackText.HeartbeatUnavailable);
            return CommandResult.HandledResult();
        }
        
        var args = context.Arguments;
        var subCmd = args.Length > 0 ? args[0] : "trigger";
        
        if (subCmd == "trigger")
        {
            await responder.SendTextAsync(FallbackText.TriggeringHeartbeat);
            var result = await context.HeartbeatService.TriggerNowAsync();
            if (result != null)
                await responder.SendTextAsync($"{FallbackText.HeartbeatResult}:\n{result}");
            else
                await responder.SendTextAsync(FallbackText.HeartbeatNoResponse);
        }
        else
        {
            await responder.SendTextAsync(FallbackText.HeartbeatUsage);
        }
        
        return CommandResult.HandledResult();
    }
}
