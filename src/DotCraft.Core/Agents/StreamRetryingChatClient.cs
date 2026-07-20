using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

internal sealed record StreamRetryOptions(int MaxRetries, TimeSpan IdleTimeout);

/// <summary>
/// Retries dropped streaming provider calls by reissuing the same sampling
/// request before any visible update has been emitted.
/// </summary>
internal sealed class StreamRetryingChatClient(
    IChatClient innerClient,
    StreamRetryOptions retryOptions)
    : DelegatingChatClient(innerClient)
{
    private const int InitialDelayMs = 200;
    private const int FailedAttemptDisposeTimeoutMs = 2_000;

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = chatMessages as IReadOnlyList<ChatMessage> ?? chatMessages.ToList();
        var retries = 0;

        while (true)
        {
            var emittedVisibleUpdate = false;
            var bufferedNonVisibleUpdates = new List<ChatResponseUpdate>();
            Exception? failure = null;

            using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var enumerator = base.GetStreamingResponseAsync(messages, options, attemptCancellation.Token)
                .GetAsyncEnumerator(attemptCancellation.Token);
            try
            {
                while (true)
                {
                    var result = await MoveNextWithIdleTimeoutAsync(
                            enumerator,
                            attemptCancellation,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (result.Exception != null)
                    {
                        failure = result.Exception;
                        break;
                    }

                    if (!result.HasNext)
                        break;

                    var update = enumerator.Current;
                    if (IsVisibleUpdate(update))
                    {
                        if (!emittedVisibleUpdate)
                        {
                            emittedVisibleUpdate = true;
                            foreach (var buffered in bufferedNonVisibleUpdates)
                                yield return buffered;
                            bufferedNonVisibleUpdates.Clear();
                        }

                        yield return update;
                    }
                    else if (emittedVisibleUpdate)
                    {
                        yield return update;
                    }
                    else
                    {
                        bufferedNonVisibleUpdates.Add(update);
                    }
                }
            }
            finally
            {
                if (failure != null)
                    CancelAttempt(attemptCancellation);

                await DisposeEnumeratorAsync(enumerator, failure).ConfigureAwait(false);
            }

            if (failure == null)
            {
                foreach (var update in bufferedNonVisibleUpdates)
                    yield return update;
                yield break;
            }

            if (ShouldRetry(failure, cancellationToken, emittedVisibleUpdate, retries))
            {
                retries++;
                ModelStreamRetryRuntimeScope.Current?.NotifyRetry(
                    retries,
                    retryOptions.MaxRetries,
                    failure);
                await Task.Delay(Backoff(retries), cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (ShouldReportRetrySuppressed(failure, cancellationToken, emittedVisibleUpdate, retries))
                ModelStreamRetryRuntimeScope.Current?.NotifyRetrySuppressed?.Invoke(failure, "visible_output_emitted");

            if (retries > 0)
                ModelStreamRetryRuntimeScope.Current?.NotifyFinalFailure?.Invoke(failure);

            throw failure;
        }
    }

    private async Task<MoveNextResult> MoveNextWithIdleTimeoutAsync(
        IAsyncEnumerator<ChatResponseUpdate> enumerator,
        CancellationTokenSource attemptCancellation,
        CancellationToken cancellationToken)
    {
        try
        {
            var hasNext = await enumerator.MoveNextAsync()
                .AsTask()
                .WaitAsync(retryOptions.IdleTimeout, cancellationToken)
                .ConfigureAwait(false);
            return new MoveNextResult(hasNext, null);
        }
        catch (TimeoutException ex)
        {
            CancelAttempt(attemptCancellation);
            return new MoveNextResult(
                HasNext: false,
                new ModelStreamDisconnectedException(
                    $"Provider stream idle for {retryOptions.IdleTimeout.TotalMilliseconds:0}ms.",
                    ex));
        }
        catch (Exception ex)
        {
            return new MoveNextResult(HasNext: false, ex);
        }
    }

    private static void CancelAttempt(CancellationTokenSource attemptCancellation)
    {
        try
        {
            attemptCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private bool ShouldRetry(
        Exception exception,
        CancellationToken cancellationToken,
        bool emittedVisibleUpdate,
        int retries) =>
        !cancellationToken.IsCancellationRequested
        && !emittedVisibleUpdate
        && retries < retryOptions.MaxRetries
        && IsRetryable(exception);

    private bool ShouldReportRetrySuppressed(
        Exception exception,
        CancellationToken cancellationToken,
        bool emittedVisibleUpdate,
        int retries) =>
        !cancellationToken.IsCancellationRequested
        && emittedVisibleUpdate
        && retries < retryOptions.MaxRetries
        && IsRetryable(exception);

    private static bool IsVisibleUpdate(ChatResponseUpdate update)
    {
        foreach (var content in update.Contents)
        {
            switch (content)
            {
                case UsageContent:
                case FunctionResultContent:
                case ErrorContent:
                    continue;
                case TextContent { Text.Length: > 0 }:
                case TextReasoningContent { Text.Length: > 0 }:
                case FunctionCallContent:
                case ToolCallArgumentsDeltaContent:
                    return true;
                case TextContent:
                case TextReasoningContent:
                    continue;
                default:
                    return true;
            }
        }

        return false;
    }

    private static bool IsRetryable(Exception exception)
    {
        if (exception is ModelStreamDisconnectedException)
            return true;

        if (exception is HttpRequestException httpRequest)
            return IsRetryableStatusCode(httpRequest.StatusCode);

        if (exception is TimeoutException or TaskCanceledException or OperationCanceledException)
            return true;

        if (exception is IOException || ContainsInner<IOException>(exception))
            return true;

        if (exception is SocketException || ContainsInner<SocketException>(exception))
            return true;

        if (LooksLikePrematureResponsesEnd(exception))
            return true;

        var statusCode = TryReadStatusCode(exception);
        return statusCode.HasValue && IsRetryableStatusCode(statusCode.Value);
    }

    private static bool LooksLikePrematureResponsesEnd(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (string.Equals(current.GetType().Name, "ResponseEnded", StringComparison.Ordinal)
                || ContainsInvariant(current.Message, "response ended prematurely")
                || ContainsInvariant(current.Message, "response ended before")
                || ContainsInvariant(current.Message, "stream ended prematurely"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsInvariant(string? value, string needle) =>
        value?.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsRetryableStatusCode(HttpStatusCode? statusCode)
    {
        if (!statusCode.HasValue)
            return true;

        var code = (int)statusCode.Value;
        return code is 408 or 409 or 429 || code >= 500;
    }

    private static HttpStatusCode? TryReadStatusCode(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            var statusCode = TryReadStatusCodeProperty(current, "StatusCode")
                ?? TryReadStatusCodeProperty(current, "Status");
            if (statusCode.HasValue)
                return statusCode.Value;
        }

        return null;
    }

    private static HttpStatusCode? TryReadStatusCodeProperty(Exception exception, string propertyName)
    {
        var property = exception.GetType().GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance);
        if (property == null)
            return null;

        var value = property.GetValue(exception);
        return value switch
        {
            HttpStatusCode statusCode => statusCode,
            int status => (HttpStatusCode)status,
            _ => null
        };
    }

    private static bool ContainsInner<T>(Exception exception)
        where T : Exception
    {
        for (var current = exception.InnerException; current != null; current = current.InnerException)
        {
            if (current is T)
                return true;
        }

        return false;
    }

    private static async Task DisposeEnumeratorAsync(
        IAsyncEnumerator<ChatResponseUpdate> enumerator,
        Exception? primaryFailure)
    {
        try
        {
            var disposeTask = enumerator.DisposeAsync().AsTask();
            if (primaryFailure == null)
            {
                await disposeTask.ConfigureAwait(false);
                return;
            }

            _ = disposeTask.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            await disposeTask
                .WaitAsync(TimeSpan.FromMilliseconds(FailedAttemptDisposeTimeoutMs))
                .ConfigureAwait(false);
        }
        catch when (primaryFailure != null)
        {
            // Preserve the stream failure that drives retry/failure semantics.
        }
    }

    private static TimeSpan Backoff(int attempt)
    {
        var exponent = Math.Pow(2, Math.Max(0, attempt - 1));
        var baseDelay = InitialDelayMs * exponent;
        var jitter = 0.9 + (Random.Shared.NextDouble() * 0.2);
        return TimeSpan.FromMilliseconds(baseDelay * jitter);
    }

    private sealed class ModelStreamDisconnectedException(string message, Exception? innerException = null)
        : IOException(message, innerException);

    private readonly record struct MoveNextResult(bool HasNext, Exception? Exception);
}
