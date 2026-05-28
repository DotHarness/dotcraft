using DotCraft.Tracing;
using Microsoft.Extensions.AI;

namespace DotCraft.Context.Compaction;

internal static class CompactionTrace
{
    public static string ResolveSessionKey(string? threadId)
    {
        if (!string.IsNullOrWhiteSpace(threadId))
            return threadId!;

        var active = TracingChatClient.CurrentSessionKey ?? TracingChatClient.GetActiveSessionKey();
        if (!string.IsNullOrWhiteSpace(active))
            return active!;

        return "maintenance:" + Guid.NewGuid().ToString("N")[..12];
    }

    public static string? ClassifyFallbackReason(ChatResponse? response)
    {
        if (response is null || !string.IsNullOrWhiteSpace(response.Text))
            return null;

        return ResponseContainsToolCall(response)
            ? "tool_call_without_text"
            : "empty_response";
    }

    private static bool ResponseContainsToolCall(ChatResponse response)
    {
        foreach (var message in response.Messages)
        {
            if (message.Contents.OfType<FunctionCallContent>().Any())
                return true;
        }

        return false;
    }
}
