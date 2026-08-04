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

        if (TryCollectErrorContentText(response, out var errorText))
        {
            return CompactionErrors.IsPromptTooLongMessage(errorText)
                ? MaintenanceForkFallbackReasons.SnapshotTooLarge
                : MaintenanceForkFallbackReasons.EmptyErrorResponse;
        }

        return ResponseContainsToolCall(response)
            ? "tool_call_without_text"
            : "empty_response";
    }

    internal static bool TryCollectErrorContentText(ChatResponse? response, out string text)
    {
        if (response?.Messages is not { Count: > 0 })
        {
            text = string.Empty;
            return false;
        }

        var values = new List<string>();
        var found = false;
        foreach (var message in response.Messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is not ErrorContent error)
                    continue;

                found = true;
                Add(error.ErrorCode);
                Add(error.Message);
                Add(error.Details?.ToString());
            }
        }

        text = values.Count == 0
            ? nameof(ErrorContent)
            : string.Join(" ", values);
        return found;

        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                values.Add(value.Trim());
        }
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
