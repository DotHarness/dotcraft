using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private async Task CheckOffTurnTodosAsync(string threadId, SessionTurn turn)
    {
        var lastWrite = turn.Items.FindLastIndex(static item => item.Payload is ToolResultPayload
        {
            Namespace: null,
            ToolName: "TodoWrite",
            Success: true
        });
        if (lastWrite < 0)
            return;

        var completeAll = turn.Items.FindIndex(lastWrite + 1, static item => item.Payload is DynamicToolCallPayload
        {
            Namespace: "desktop_conversation",
            ToolName: "WrapUp",
            Success: true
        }) >= 0;

        try
        {
            await agentFactory.CheckOffTodosAsync(threadId, completeAll);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Closing the to-do list failed for thread {ThreadId}", threadId);
        }
    }
}
