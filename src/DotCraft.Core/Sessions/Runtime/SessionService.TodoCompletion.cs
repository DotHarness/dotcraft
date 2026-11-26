using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private async Task CheckOffTurnTodosAsync(string threadId, SessionTurn turn)
    {
        if (!turn.Items.Any(static item => item.Payload is ToolResultPayload
            {
                Namespace: null,
                ToolName: "TodoWrite",
                Success: true
            }))
            return;

        var lastWrapUp = turn.Items
            .Where(static item => item.CompletedAt is not null && item.Payload is DynamicToolCallPayload
            {
                Namespace: "desktop_conversation",
                ToolName: "WrapUp",
                Success: true
            })
            .MaxBy(static item => item.CompletedAt);
        if (lastWrapUp?.AsDynamicToolCall?.Arguments?["blocked"] is JsonValue blocked
            && blocked.TryGetValue<bool>(out var isBlocked) && isBlocked)
            return;

        try
        {
            await agentFactory.CheckOffTodosAsync(threadId);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Closing the to-do list failed for thread {ThreadId}", threadId);
        }
    }
}
