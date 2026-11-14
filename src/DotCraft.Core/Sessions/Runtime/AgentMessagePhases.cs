using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

internal static class AgentMessagePhases
{
    internal const string Commentary = "commentary";
    internal const string PartialAnswer = "partial_answer";
    internal const string FinalAnswer = "final_answer";

    internal static bool IsResponseBoundary(ChatFinishReason? finishReason) =>
        finishReason == ChatFinishReason.Stop || finishReason == ChatFinishReason.Length
        || finishReason == ChatFinishReason.ContentFilter;

    internal static string ResponseEnded(ChatFinishReason? finishReason, bool interrupted) =>
        interrupted || finishReason == ChatFinishReason.Length || finishReason == ChatFinishReason.ContentFilter
            ? PartialAnswer
            : FinalAnswer;

    internal static void ApplyHistoricalTurn(SessionTurn turn)
    {
        SessionItem? pending = null;
        foreach (var item in turn.Items)
        {
            if (item.Payload is AgentMessagePayload message)
            {
                if (message.DeliveryMode == "async")
                {
                    Apply(item, FinalAnswer);
                    continue;
                }

                if (pending is not null)
                    Apply(pending, Commentary);
                pending = item;
            }
            else if (IsProgressBoundary(item.Type))
            {
                if (pending is not null)
                    Apply(pending, Commentary);
                pending = null;
            }
        }

        if (pending is not null)
        {
            if (turn.Status == TurnStatus.Completed)
                Apply(pending, FinalAnswer);
            else if (turn.Status is TurnStatus.Cancelled or TurnStatus.Failed)
                Apply(pending, PartialAnswer);
        }
    }

    private static bool IsProgressBoundary(ItemType type) => type is
        ItemType.ReasoningContent or ItemType.ToolCall or ItemType.McpToolCall or ItemType.DynamicToolCall
        or ItemType.CommandExecution or ItemType.ToolExecution or ItemType.WebSearch or ItemType.ImageGeneration
        or ItemType.UserMessage;

    private static void Apply(SessionItem item, string phase)
    {
        if (item.Payload is AgentMessagePayload { Phase: null } message)
            item.Payload = message with { Phase = phase };
    }
}
