using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private async Task CheckOffTurnTodosAsync(string threadId, SessionTurn turn)
    {
        var lastWrite = turn.Items
            .Where(static item => item.Payload is ToolResultPayload
            {
                Namespace: null,
                ToolName: "TodoWrite",
                Success: true
            })
            .Max(static item => item.CompletedAt);
        if (lastWrite is null)
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

        var completeAll = lastWrapUp?.CompletedAt > lastWrite;

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
