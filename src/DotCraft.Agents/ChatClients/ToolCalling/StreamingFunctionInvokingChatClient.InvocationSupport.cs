using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

public sealed partial class StreamingFunctionInvokingChatClient
{
    private async Task<bool> TryAppendGuidanceAsync(List<ChatMessage> history, CancellationToken cancellationToken)
    {
        var context = StreamingGuidanceRuntimeScope.Current;
        if (context is null)
            return false;
        var message = await context.TryDrainGuidanceMessageAsync(cancellationToken);
        if (message is null)
            return false;
        history.Add(message);
        return true;
    }

    private static async Task<ChatMessage?> TryDrainMailboxAsync(CancellationToken cancellationToken)
    {
        var callback = StreamingGuidanceRuntimeScope.Current?.TryDrainMailboxMessageAsync;
        return callback is null ? null : await callback(cancellationToken);
    }

    private static async Task<bool> TryAppendMailboxAsync(List<ChatMessage> history, CancellationToken cancellationToken)
    {
        var message = await TryDrainMailboxAsync(cancellationToken);
        if (message is null)
            return false;
        history.Add(message);
        return true;
    }

    private static async Task<bool> TryAppendAnswerBoundaryMessageAsync(
        List<ChatMessage> history,
        CancellationToken cancellationToken)
    {
        var callback = StreamingGuidanceRuntimeScope.Current?.TryDrainAnswerBoundaryMessageAsync;
        if (callback is null)
            return false;
        var message = await callback(cancellationToken);
        if (message is null)
            return false;
        history.Add(message);
        return true;
    }

    private static IReadOnlyList<ChatMessage> CreateHookFeedbackMessages(IReadOnlyList<FunctionInvocationOutcome> results)
    {
        var feedback = results.SelectMany(static result => result.HookFeedback)
            .Where(static item => !string.IsNullOrWhiteSpace(item.Text))
            .ToList();
        return feedback.Count == 0
            ? []
            : [new ChatMessage(ChatRole.User, BuildHookFeedbackReminder(feedback))];
    }

    private static string BuildHookFeedbackReminder(IReadOnlyList<StreamingToolHookFeedback> feedback)
    {
        var sections = new List<string>
        {
            "<system-reminder>",
            "## Lifecycle Hook Feedback",
            "Model-visible context returned by lifecycle hooks."
        };
        foreach (var item in feedback)
        {
            var label = item.IsBlockingFeedback ? "exit-code-2 feedback" : "additionalContext";
            sections.Add($"### {item.Event} {label}\n{item.Text}");
        }
        sections.Add("</system-reminder>");
        return string.Join("\n\n", sections);
    }

    private static async Task NotifyToolHandlerFinishedAsync(
        IStreamingToolInvocationAttempt? toolExecution,
        string toolName,
        string callId,
        CancellationToken cancellationToken)
    {
        if (toolExecution is null)
            return;
        try
        {
            await toolExecution.NotifyHandlerFinishedAsync(toolName, callId, cancellationToken);
        }
        catch
        {
            // Observers cannot change the tool result delivered to the model.
        }
    }

    private static void CompleteDeniedToolCall(
        FunctionCallContent call,
        IStreamingToolInvocationAttempt? toolExecution,
        string message) =>
        toolExecution?.CompleteDenied(call.Name, call.CallId, message);

    private FunctionResultContent CreateFunctionResultContent(FunctionInvocationOutcome result)
    {
        if (result.Status == FunctionInvocationStatus.RanToCompletion)
        {
            if (result.Value is FunctionResultContent content && content.CallId == result.Call.CallId)
                return content;
            return new FunctionResultContent(result.Call.CallId, result.Value ?? "Success: Function completed.");
        }
        if (result.Status == FunctionInvocationStatus.InvalidArguments)
            return CreateInvalidToolArgumentsResult(result.Call.CallId, result.Value?.ToString() ?? "Error: Invalid tool arguments.");
        if (result.Status == FunctionInvocationStatus.NotFound)
            return CreateToolFailureResult(result.Call.CallId, $"Error: Requested function \"{result.Call.Name}\" not found.", "tool_not_found");

        var message = result.Status == FunctionInvocationStatus.Exception
            ? CreateFunctionFailureMessage(result.Exception)
            : "Error: Unknown error.";
        return new FunctionResultContent(result.Call.CallId, message) { Exception = result.Exception };
    }

    private string CreateFunctionFailureMessage(Exception? exception)
    {
        if (!IncludeDetailedErrors || exception is null)
            return "Error: Function failed.";
        var safeReason = SanitizeToolFailureMessage($"{exception.GetType().Name}: {exception.Message}");
        return string.IsNullOrEmpty(safeReason)
            ? "Error: Function failed."
            : $"Error: Function failed. Reason: {safeReason}";
    }

    private static string SanitizeToolFailureMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;
        var sanitized = AnsiEscapeRegex.Replace(message, string.Empty);
        sanitized = BearerSecretRegex.Replace(sanitized, "Bearer ***");
        sanitized = SecretAssignmentRegex.Replace(sanitized, "$1=***");
        sanitized = WhitespaceRegex.Replace(sanitized, " ").Trim();
        return sanitized.Length <= ToolFailureMessageMaxChars
            ? sanitized
            : sanitized[..(ToolFailureMessageMaxChars - 3)] + "...";
    }

    internal static bool IsInvalidToolArgumentsResult(FunctionResultContent content) =>
        content.AdditionalProperties?.TryGetValue(InvalidToolArgumentsMetadataKey, out var value) == true
        && value is true;

    internal static string? GetToolResultErrorCode(FunctionResultContent content) =>
        content.AdditionalProperties?.TryGetValue(ToolResultErrorCodeMetadataKey, out var value) == true
            ? value as string
            : null;

    private static FunctionResultContent CreateInvalidToolArgumentsResult(string callId, string message) =>
        new(callId, message)
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [InvalidToolArgumentsMetadataKey] = true,
                [ToolResultErrorCodeMetadataKey] = "tool_input_invalid"
            }
        };

    internal static FunctionResultContent CreateToolFailureResult(string callId, string message, string errorCode) =>
        new(callId, message)
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ToolResultErrorCodeMetadataKey] = errorCode
            }
        };
}
