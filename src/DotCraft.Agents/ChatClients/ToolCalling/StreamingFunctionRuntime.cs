using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>Provider-neutral result of Session Core's pre-sampling preparation.</summary>
public sealed record StreamingSamplingPreparation(
    IReadOnlyList<ChatMessage> Messages,
    bool NeutralHistoryWasReplaced,
    bool HistoryWasReplaced);

/// <summary>Flows Session Core's compaction preparation into the foundation tool loop.</summary>
public static class StreamingSamplingRuntimeScope
{
    private static readonly AsyncLocal<Func<
        IReadOnlyList<ChatMessage>,
        ChatOptions?,
        CancellationToken,
        Task<StreamingSamplingPreparation>>?> CurrentHandler = new();

    public static Func<
        IReadOnlyList<ChatMessage>,
        ChatOptions?,
        CancellationToken,
        Task<StreamingSamplingPreparation>>? Current => CurrentHandler.Value;

    public static IDisposable Set(Func<
        IReadOnlyList<ChatMessage>,
        ChatOptions?,
        CancellationToken,
        Task<StreamingSamplingPreparation>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var previous = CurrentHandler.Value;
        CurrentHandler.Value = handler;
        return new RestoreScope(() => CurrentHandler.Value = previous);
    }
}

/// <summary>Provider-neutral callbacks for steerable model/tool-loop boundaries.</summary>
public sealed class StreamingGuidanceRuntimeContext
{
    public required Func<CancellationToken, Task<ChatMessage?>> TryDrainGuidanceMessageAsync { get; init; }
    public Func<CancellationToken, Task<ChatMessage?>>? TryDrainMailboxMessageAsync { get; init; }
    public Func<CancellationToken, Task<ChatMessage?>>? TryDrainAnswerBoundaryMessageAsync { get; init; }
}

/// <summary>Flows Session Core guidance callbacks into the foundation tool loop.</summary>
public static class StreamingGuidanceRuntimeScope
{
    private static readonly AsyncLocal<StreamingGuidanceRuntimeContext?> CurrentContext = new();

    public static StreamingGuidanceRuntimeContext? Current => CurrentContext.Value;

    public static IDisposable Set(StreamingGuidanceRuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        return new RestoreScope(() => CurrentContext.Value = previous);
    }
}

/// <summary>Model-visible feedback collected while a tool handler is running.</summary>
public sealed record StreamingToolHookFeedback(string Event, string Text, bool IsBlockingFeedback);

/// <summary>Collects model-visible lifecycle feedback without depending on Core hook types.</summary>
public static class StreamingToolFeedbackRuntimeScope
{
    private static readonly AsyncLocal<Action<StreamingToolHookFeedback>?> CurrentSink = new();

    public static IDisposable Set(Action<StreamingToolHookFeedback> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        var previous = CurrentSink.Value;
        CurrentSink.Value = sink;
        return new RestoreScope(() => CurrentSink.Value = previous);
    }

    public static void Report(string eventName, string text, bool isBlockingFeedback)
    {
        if (!string.IsNullOrWhiteSpace(text))
            CurrentSink.Value?.Invoke(new StreamingToolHookFeedback(
                eventName,
                text.Trim(),
                isBlockingFeedback));
    }
}

/// <summary>One Session Core observation lease for a function invocation.</summary>
public interface IStreamingToolInvocationAttempt
{
    void CompleteSuccess(object? result);
    void CompleteFailure(string errorMessage, object? result = null);
    void CompleteCancelled(string? errorMessage = null);
    void CompleteDenied(string toolName, string callId, string message);
    Task NotifyHandlerFinishedAsync(string toolName, string callId, CancellationToken cancellationToken);
}

/// <summary>Creates Session Core observation leases without coupling the tool loop to Session types.</summary>
public interface IStreamingToolInvocationObserver
{
    IStreamingToolInvocationAttempt Begin(string callId);
}

/// <summary>Flows the Session Core tool observer into the foundation tool loop.</summary>
public static class StreamingToolInvocationRuntimeScope
{
    private static readonly AsyncLocal<IStreamingToolInvocationObserver?> CurrentObserver = new();

    public static IStreamingToolInvocationObserver? Current => CurrentObserver.Value;

    public static IDisposable Set(IStreamingToolInvocationObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var previous = CurrentObserver.Value;
        CurrentObserver.Value = observer;
        return new RestoreScope(() => CurrentObserver.Value = previous);
    }
}

internal sealed class RestoreScope(Action restore) : IDisposable
{
    private int _disposed;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            restore();
    }
}
