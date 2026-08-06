using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

public sealed partial class StreamingFunctionInvokingChatClient
{
    private static void CopyFunctionCalls(IList<AIContent> contents, List<FunctionCallContent> calls)
    {
        foreach (var content in contents)
        {
            if (content is FunctionCallContent { InformationalOnly: false } functionCall)
                calls.Add(functionCall);
        }
    }

    private static void NormalizeFunctionCallArguments(IList<AIContent> contents)
    {
        foreach (var content in contents)
        {
            if (content is FunctionCallContent { Arguments: null } functionCall)
                functionCall.Arguments = new Dictionary<string, object?>();
        }
    }

    private static bool HasEffectiveProviderOutput(IEnumerable<ChatResponseUpdate> updates)
    {
        foreach (var update in updates)
        {
            if (update.FinishReason == ChatFinishReason.Length)
                return true;

            foreach (var content in update.Contents)
            {
                if (IsEffectiveProviderOutput(content))
                    return true;
            }
        }

        return false;
    }

    private static bool IsEffectiveProviderOutput(AIContent content) =>
        content switch
        {
            UsageContent => false,
            ErrorContent => false,
            TextContent text => !string.IsNullOrEmpty(text.Text),
            TextReasoningContent reasoning => ReasoningContentHelper.TryGetText(reasoning, out _),
            ToolCallArgumentsDeltaContent delta => !string.IsNullOrEmpty(delta.ArgumentsDelta)
                || !string.IsNullOrWhiteSpace(delta.ToolName)
                || !string.IsNullOrWhiteSpace(delta.CallId),
            FunctionCallContent { InformationalOnly: false } => true,
            FunctionCallContent => false,
            _ => true
        };

    private static string? CollectErrorContentText(IEnumerable<ChatResponseUpdate> updates)
    {
        List<string>? values = null;
        foreach (var update in updates)
        {
            foreach (var content in update.Contents)
            {
                if (content is not ErrorContent error)
                    continue;

                values ??= [];
                Add(error.ErrorCode);
                Add(error.Message);
                Add(error.Details?.ToString());
            }
        }

        return values is { Count: > 0 } ? string.Join(" ", values) : null;

        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                values!.Add(value.Trim());
        }
    }

    private static string BuildEmptyProviderResponseMessage(string fallbackMessage, string? providerErrorText) =>
        string.IsNullOrWhiteSpace(providerErrorText)
            ? fallbackMessage
            : fallbackMessage + " Provider error: " + providerErrorText;
}
