using Microsoft.Extensions.AI;

namespace DotCraft.Context.Compaction;

internal static class CompactionPreflightTrimmer
{
    public static List<ChatMessage> TrimSummaryInput(
        IReadOnlyList<ChatMessage> candidate,
        string summaryPrompt,
        CompactionConfig config,
        ChatMessage? taskMessage = null)
    {
        var result = candidate.ToList();
        var budget = Math.Max(1, config.EffectiveContextWindow());
        while (result.Count > 0 && EstimateSummaryRequest(result, summaryPrompt, taskMessage) > budget)
        {
            if (CompactionMessageTruncator.TryTruncateOldestToolResult(result, out var toolTrimmed))
            {
                result = toolTrimmed;
                continue;
            }

            var trimmed = CompactionMessageTruncator.TruncateOldestGroups(result);
            if (trimmed.Count == 0 || trimmed.Count >= result.Count)
                break;

            result = trimmed;
        }

        return result;
    }

    private static int EstimateSummaryRequest(
        IReadOnlyList<ChatMessage> messages,
        string summaryPrompt,
        ChatMessage? taskMessage)
    {
        var request = new List<ChatMessage>(messages.Count + (taskMessage is null ? 1 : 2))
        {
            new(ChatRole.System, summaryPrompt)
        };
        request.AddRange(messages);
        if (taskMessage is not null)
            request.Add(taskMessage);
        return MessageTokenEstimator.Estimate(request);
    }
}
