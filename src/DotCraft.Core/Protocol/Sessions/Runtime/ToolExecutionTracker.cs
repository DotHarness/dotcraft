using DotCraft.Agents;

namespace DotCraft.Protocol;

internal sealed class ToolExecutionTracker
{
    private const int ResultPreviewMaxChars = 4096;

    private readonly SessionItem _item;
    private readonly DateTimeOffset _startedAt;
    private readonly Action<SessionItem> _emitItemCompleted;
    private int _completed;

    private ToolExecutionTracker(
        SessionItem item,
        DateTimeOffset startedAt,
        Action<SessionItem> emitItemCompleted)
    {
        _item = item;
        _startedAt = startedAt;
        _emitItemCompleted = emitItemCompleted;
    }

    public static ToolExecutionTracker? Claim(string callId)
    {
        var runtime = ToolExecutionRuntimeScope.Current;
        if (runtime is null || !runtime.SupportsToolExecutionLifecycle)
            return null;

        var registration = runtime.TryClaimPending(callId);
        if (registration is null)
            return null;

        return new ToolExecutionTracker(
            registration.Item,
            registration.Item.CreatedAt,
            runtime.EmitItemCompleted);
    }

    public void CompleteSuccess(object? result)
    {
        Complete(
            status: "completed",
            success: true,
            resultPreview: ToResultPreview(result),
            errorMessage: null);
    }

    public void CompleteFailure(string errorMessage, object? result = null)
    {
        Complete(
            status: "failed",
            success: false,
            resultPreview: ToResultPreview(result) ?? errorMessage,
            errorMessage: errorMessage);
    }

    public void CompleteCancelled(string? errorMessage = null)
    {
        Complete(
            status: "cancelled",
            success: false,
            resultPreview: errorMessage,
            errorMessage: errorMessage);
    }

    private void Complete(
        string status,
        bool success,
        string? resultPreview,
        string? errorMessage)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
            return;

        var currentPayload = _item.AsToolExecution;
        var completedAt = DateTimeOffset.UtcNow;
        _item.Status = ItemStatus.Completed;
        _item.CompletedAt = completedAt;
        _item.Payload = new ToolExecutionPayload
        {
            CallId = currentPayload?.CallId ?? string.Empty,
            ToolName = currentPayload?.ToolName ?? string.Empty,
            Status = status,
            Success = success,
            DurationMs = (long)Math.Max(0, (completedAt - _startedAt).TotalMilliseconds),
            ResultPreview = Truncate(resultPreview),
            ErrorMessage = errorMessage
        };
        _emitItemCompleted(_item);
    }

    private static string? ToResultPreview(object? result)
    {
        var text = ImageContentSanitizingChatClient.DescribeResult(result);
        return string.IsNullOrEmpty(text) ? null : Truncate(text);
    }

    private static string? Truncate(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return text.Length <= ResultPreviewMaxChars
            ? text
            : text[..ResultPreviewMaxChars];
    }
}
