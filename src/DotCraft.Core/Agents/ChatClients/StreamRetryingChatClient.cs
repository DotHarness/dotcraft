using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;

namespace DotCraft.Agents;

internal sealed record StreamRetryOptions(
    int MaxRetries,
    TimeSpan IdleTimeout,
    int ProviderServerErrorMaxRetries = 0);

/// <summary>
/// Replays a dropped streaming provider call only while no update has been delivered; after that
/// the tool loop owns the retry, because only it can commit what already arrived.
/// </summary>
internal sealed class StreamRetryingChatClient(
    IChatClient innerClient,
    StreamRetryOptions retryOptions)
    : DelegatingChatClient(innerClient), IStreamRetryBudget
{
    private const int FailedAttemptDisposeTimeoutMs = 2_000;

    private IProviderFailureClassifier? _classifier;

    public int MaxStreamRetries => retryOptions.MaxRetries;

    private IProviderFailureClassifier Classifier =>
        _classifier ??= GetService(typeof(IProviderFailureClassifier)) as IProviderFailureClassifier
            ?? DefaultProviderFailureClassifier.Instance;

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = chatMessages as IReadOnlyList<ChatMessage> ?? chatMessages.ToList();
        var transportRetries = 0;
        var providerServerErrorRetries = 0;
        var totalRetries = 0;
        var attemptNumber = 0;
        var nextAttemptRetryLimit = retryOptions.MaxRetries;
        var providerHistoryBridge = ProviderRequestContextScope.Current?.History;

        while (true)
        {
            attemptNumber++;
            var attemptRetryLimit = nextAttemptRetryLimit;
            using var attemptTraceScope = ModelStreamAttemptRuntimeScope.Begin(attemptNumber);
            var attemptStopwatch = Stopwatch.StartNew();
            var providerHistoryAttemptId = providerHistoryBridge?.BeginAttempt();
            await using var providerHistoryAttempt = new ProviderHistoryAttemptLease(
                providerHistoryBridge,
                providerHistoryAttemptId,
                cancellationToken);
            var emittedVisibleUpdate = false;
            var receivedAnyUpdate = false;
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
                    receivedAnyUpdate = true;
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

            if (failure == null && retryOptions.ProviderServerErrorMaxRetries > 0)
                failure = CreateBufferedProviderServerError(bufferedNonVisibleUpdates);

            if (failure == null)
            {
                ReportAttemptCompleted(
                    attemptNumber,
                    attemptRetryLimit,
                    outcome: "succeeded",
                    retryDecision: "none",
                    failure: null,
                    attemptStopwatch.Elapsed.TotalMilliseconds,
                    emittedVisibleUpdate);
                foreach (var update in bufferedNonVisibleUpdates)
                    yield return update;
                providerHistoryAttempt.Complete();
                yield break;
            }

            var providerServerError = failure is ProviderServerErrorException;
            var retryCount = providerServerError ? providerServerErrorRetries : transportRetries;
            var retryLimit = providerServerError
                ? retryOptions.ProviderServerErrorMaxRetries
                : retryOptions.MaxRetries;
            // A stream carrying only an error frame delivered nothing, so its non-visible updates
            // do not block a replay.
            var replayBlocked = providerServerError ? emittedVisibleUpdate : receivedAnyUpdate;
            if (ShouldRetry(failure, cancellationToken, replayBlocked, retryCount, retryLimit))
            {
                await providerHistoryAttempt.AbortAsync().ConfigureAwait(false);
                if (providerServerError)
                    providerServerErrorRetries++;
                else
                    transportRetries++;
                totalRetries++;
                nextAttemptRetryLimit = retryLimit;
                ReportAttemptCompleted(
                    attemptNumber,
                    retryLimit,
                    outcome: "failed",
                    retryDecision: "scheduled",
                    failure,
                    attemptStopwatch.Elapsed.TotalMilliseconds,
                    emittedVisibleUpdate);
                ModelStreamRetryRuntimeScope.Current?.NotifyRetry(new ModelStreamRetryNotification(
                    retryCount + 1,
                    retryLimit,
                    failure,
                    Classifier.Classify(failure)));
                await Task.Delay(ProviderFailure.Backoff(totalRetries), cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            var retryDelegated = ShouldReportRetryDelegated(
                    failure,
                    cancellationToken,
                    replayBlocked,
                    retryCount,
                    retryLimit);
            if (retryDelegated)
                ModelStreamRetryRuntimeScope.Current?.NotifyRetrySuppressed?.Invoke(failure, "delegated_to_turn_loop");

            var canceled = cancellationToken.IsCancellationRequested
                           || failure is OperationCanceledException;
            var retryExhausted = !canceled
                                 && !replayBlocked
                                 && IsTransportRetryable(failure)
                                 && retryCount >= retryLimit;
            ReportAttemptCompleted(
                attemptNumber,
                retryLimit,
                outcome: canceled ? "canceled" : "failed",
                retryDecision: retryDelegated
                    ? "delegated"
                    : retryExhausted
                        ? "exhausted"
                        : "none",
                failure,
                attemptStopwatch.Elapsed.TotalMilliseconds,
                emittedVisibleUpdate);

            // Exhausting this layer's budget is terminal, so the tool loop adds no round of its own.
            var surfaced = retryExhausted
                ? new ProviderFailureException(
                    new ProviderFailure(
                        ProviderFailureKind.ResponseTooManyFailedAttempts,
                        ModelStreamAttemptRuntimeScope.Current?.StatusCode,
                        RequestId: ModelStreamAttemptRuntimeScope.Current?.RequestId),
                    failure)
                : failure;

            if (!retryDelegated)
            {
                // Only the tool loop knows whether a delegated failure is terminal.
                ModelStreamRetryRuntimeScope.Current?.NotifyFailureClassified?.Invoke(
                    Classifier.Classify(surfaced));
                if (totalRetries > 0)
                    ModelStreamRetryRuntimeScope.Current?.NotifyFinalFailure?.Invoke(surfaced);
            }

            providerHistoryAttempt.Complete();
            throw surfaced;
        }
    }

    private void ReportAttemptCompleted(
        int attemptNumber,
        int retryLimit,
        string outcome,
        string retryDecision,
        Exception? failure,
        double durationMs,
        bool visibleOutputEmitted)
    {
        var transport = ModelStreamAttemptRuntimeScope.Current;
        var statusCode = transport?.StatusCode
                         ?? (failure == null
                             ? null
                             : (int?)DefaultProviderFailureClassifier.TryReadStatusCode(failure));
        ModelStreamRetryRuntimeScope.Current?.NotifyAttemptCompleted?.Invoke(
            new ModelStreamAttemptDiagnostic(
                PromptCacheRequestShapeTraceScope.RequestIndex,
                attemptNumber,
                retryLimit,
                outcome,
                retryDecision,
                ClassifyFailure(failure),
                durationMs,
                visibleOutputEmitted,
                statusCode,
                transport?.RequestId,
                transport?.SessionIdHash,
                transport?.ThreadIdHash,
                transport?.PromptCacheKeyHash));
    }

    private async Task<MoveNextResult> MoveNextWithIdleTimeoutAsync(
        IAsyncEnumerator<ChatResponseUpdate> enumerator,
        CancellationTokenSource attemptCancellation,
        CancellationToken cancellationToken)
    {
        var moveNext = enumerator.MoveNextAsync().AsTask();
        try
        {
            var hasNext = await moveNext
                .WaitAsync(retryOptions.IdleTimeout, cancellationToken)
                .ConfigureAwait(false);
            return new MoveNextResult(hasNext, null);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            CancelAttempt(attemptCancellation);
            try
            {
                await moveNext.ConfigureAwait(false);
            }
            catch
            {
            }
            return new MoveNextResult(HasNext: false, ex);
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
        bool replayBlocked,
        int retries,
        int retryLimit) =>
        !cancellationToken.IsCancellationRequested
        && !replayBlocked
        && retries < retryLimit
        && IsTransportRetryable(exception);

    private bool ShouldReportRetryDelegated(
        Exception exception,
        CancellationToken cancellationToken,
        bool replayBlocked,
        int retries,
        int retryLimit) =>
        !cancellationToken.IsCancellationRequested
        && replayBlocked
        && retries < retryLimit
        && IsTransportRetryable(exception);

    private static ProviderServerErrorException? CreateBufferedProviderServerError(
        IEnumerable<ChatResponseUpdate> updates)
    {
        foreach (var error in updates
                     .SelectMany(static update => update.Contents)
                     .OfType<ErrorContent>())
        {
            if (string.Equals(error.ErrorCode, "server_error", StringComparison.OrdinalIgnoreCase))
                return new ProviderServerErrorException(error.Message);
        }

        return null;
    }

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

    private bool IsTransportRetryable(Exception exception) =>
        exception is ModelStreamDisconnectedException
        || Classifier.Classify(exception).AllowsTransportReplay;

    private static string? ClassifyFailure(Exception? exception)
    {
        if (exception == null)
            return null;
        if (exception is ProviderServerErrorException)
            return "provider_server_error";
        if (exception is ModelStreamDisconnectedException)
            return "idle_timeout";
        if (DefaultProviderFailureClassifier.IsStreamDisconnect(exception))
            return "premature_end";
        if (exception is OperationCanceledException or TaskCanceledException)
            return "canceled";
        if (exception is TimeoutException)
            return "timeout";
        if (exception is SocketException
            || DefaultProviderFailureClassifier.ContainsInner<SocketException>(exception))
        {
            return "socket";
        }

        if (exception is IOException
            || DefaultProviderFailureClassifier.ContainsInner<IOException>(exception))
        {
            return "io";
        }

        if (DefaultProviderFailureClassifier.TryReadStatusCode(exception).HasValue
            || exception is HttpRequestException)
        {
            return "http";
        }

        return "unknown";
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

    private sealed class ModelStreamDisconnectedException(string message, Exception? innerException = null)
        : IOException(message, innerException);

    private sealed class ProviderServerErrorException(string message)
        : HttpRequestException(message, null, HttpStatusCode.InternalServerError);

    private sealed class ProviderHistoryAttemptLease(
        IProviderConversationHistory? bridge,
        string? attemptId,
        CancellationToken cancellationToken) : IAsyncDisposable
    {
        private bool _closed = bridge is null || attemptId is null;

        public void Complete()
        {
            if (_closed)
                return;

            bridge!.EndAttempt(attemptId);
            _closed = true;
        }

        public async ValueTask AbortAsync()
        {
            if (_closed)
                return;

            await bridge!.AbortAttemptAsync(attemptId, CancellationToken.None).ConfigureAwait(false);
            _closed = true;
        }

        public ValueTask DisposeAsync()
        {
            if (cancellationToken.IsCancellationRequested)
                Complete();
            return _closed ? ValueTask.CompletedTask : AbortAsync();
        }
    }

    private readonly record struct MoveNextResult(bool HasNext, Exception? Exception);
}
