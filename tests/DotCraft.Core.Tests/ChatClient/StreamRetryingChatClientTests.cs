using System.Net;
using DotCraft.Agents;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Agents;

public sealed class StreamRetryingChatClientTests
{
    private const int FastIdleTimeoutMs = 250;

    [Fact]
    public async Task GetStreamingResponseAsync_ReportsOneSuccessfulAttempt()
    {
        var inner = new SequenceChatClient(
            _ => Stream([new ChatResponseUpdate(ChatRole.Assistant, "ok")]));
        var client = new StreamRetryingChatClient(inner, Options(maxRetries: 1));
        var attempts = new List<ModelStreamAttemptDiagnostic>();

        using var scope = ModelStreamRetryRuntimeScope.Set(new ModelStreamRetryRuntimeContext
        {
            NotifyRetry = (_, _, _) => { },
            NotifyAttemptCompleted = attempts.Add
        });

        _ = await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]));

        var attempt = Assert.Single(attempts);
        Assert.Equal(1, attempt.AttemptNumber);
        Assert.Equal("succeeded", attempt.Outcome);
        Assert.Equal("none", attempt.RetryDecision);
        Assert.Null(attempt.FailureKind);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ReportsScheduledRetryThenSuccess()
    {
        var inner = new SequenceChatClient(
            _ => ThrowStream(new IOException("stream closed before completion")),
            _ => Stream([new ChatResponseUpdate(ChatRole.Assistant, "ok")]));
        var client = new StreamRetryingChatClient(inner, Options(maxRetries: 1));
        var attempts = new List<ModelStreamAttemptDiagnostic>();

        using var scope = ModelStreamRetryRuntimeScope.Set(new ModelStreamRetryRuntimeContext
        {
            NotifyRetry = (_, _, _) => { },
            NotifyAttemptCompleted = attempts.Add
        });

        _ = await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]));

        Assert.Equal([1, 2], attempts.Select(static attempt => attempt.AttemptNumber).ToArray());
        Assert.Equal("scheduled", attempts[0].RetryDecision);
        Assert.Equal("io", attempts[0].FailureKind);
        Assert.Equal("succeeded", attempts[1].Outcome);
        Assert.Equal("none", attempts[1].RetryDecision);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_RetriesBeforeVisibleUpdateAndReportsStreamError()
    {
        var inner = new SequenceChatClient(
            _ => ThrowStream(new IOException("stream closed before completion")),
            _ => Stream([new ChatResponseUpdate(ChatRole.Assistant, "ok")]));
        var client = new StreamRetryingChatClient(inner, Options(maxRetries: 1));
        var notifications = new List<string>();

        using var scope = ModelStreamRetryRuntimeScope.Set(new ModelStreamRetryRuntimeContext
        {
            NotifyRetry = (attempt, maxRetries, _) => notifications.Add($"{attempt}/{maxRetries}")
        });

        var updates = await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]));

        Assert.Equal(2, inner.Calls);
        Assert.Equal(["1/1"], notifications);
        Assert.Equal("ok", string.Concat(updates.SelectMany(update => update.Contents).OfType<TextContent>().Select(text => text.Text)));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_RetriesWhenIdleMoveNextAndDisposeHangBeforeVisibleUpdate()
    {
        var inner = new SequenceChatClient(
            _ => new HangingStream(),
            _ => Stream([new ChatResponseUpdate(ChatRole.Assistant, "ok")]));
        var client = new StreamRetryingChatClient(inner, Options(maxRetries: 1, idleTimeoutMs: FastIdleTimeoutMs));
        var notifications = new List<string>();

        using var scope = ModelStreamRetryRuntimeScope.Set(new ModelStreamRetryRuntimeContext
        {
            NotifyRetry = (attempt, maxRetries, _) => notifications.Add($"{attempt}/{maxRetries}")
        });

        var updates = await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, inner.Calls);
        Assert.Equal(["1/1"], notifications);
        Assert.Equal("ok", string.Concat(updates.SelectMany(update => update.Contents).OfType<TextContent>().Select(text => text.Text)));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_RetriesPrematureResponseEndedBeforeVisibleUpdate()
    {
        var inner = new SequenceChatClient(
            _ => ThrowStream(new ResponseEnded("The response ended prematurely.")),
            _ => Stream([new ChatResponseUpdate(ChatRole.Assistant, "ok")]));
        var client = new StreamRetryingChatClient(inner, Options(maxRetries: 1));
        var notifications = new List<string>();

        using var scope = ModelStreamRetryRuntimeScope.Set(new ModelStreamRetryRuntimeContext
        {
            NotifyRetry = (attempt, maxRetries, exception) => notifications.Add($"{attempt}/{maxRetries}:{exception.GetType().Name}")
        });

        var updates = await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]));

        Assert.Equal(2, inner.Calls);
        Assert.Equal(["1/1:ResponseEnded"], notifications);
        Assert.Equal("ok", string.Concat(updates.SelectMany(update => update.Contents).OfType<TextContent>().Select(text => text.Text)));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_DoesNotRetryAfterVisibleUpdate()
    {
        var inner = new SequenceChatClient(
            _ => StreamThenThrow(
                [new ChatResponseUpdate(ChatRole.Assistant, "partial")],
                new IOException("connection reset")),
            _ => Stream([new ChatResponseUpdate(ChatRole.Assistant, "retry")]));
        var client = new StreamRetryingChatClient(inner, Options(maxRetries: 1));
        var seen = new List<ChatResponseUpdate>();
        var suppressed = new List<string>();
        var attempts = new List<ModelStreamAttemptDiagnostic>();

        using var scope = ModelStreamRetryRuntimeScope.Set(new ModelStreamRetryRuntimeContext
        {
            NotifyRetry = (_, _, _) => { },
            NotifyRetrySuppressed = (exception, reason) => suppressed.Add($"{exception.GetType().Name}:{reason}"),
            NotifyAttemptCompleted = attempts.Add
        });

        await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
                seen.Add(update);
        });

        Assert.Equal(1, inner.Calls);
        Assert.Equal("partial", string.Concat(seen.SelectMany(update => update.Contents).OfType<TextContent>().Select(text => text.Text)));
        Assert.Equal(["IOException:visible_output_emitted"], suppressed);
        var attempt = Assert.Single(attempts);
        Assert.Equal("failed", attempt.Outcome);
        Assert.Equal("suppressed", attempt.RetryDecision);
        Assert.True(attempt.VisibleOutputEmitted);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_DoesNotRetryAfterVisibleUpdateWhenIdleDisposeHangs()
    {
        var inner = new SequenceChatClient(
            _ => new HangingStream([new ChatResponseUpdate(ChatRole.Assistant, "partial")]),
            _ => Stream([new ChatResponseUpdate(ChatRole.Assistant, "retry")]));
        var client = new StreamRetryingChatClient(inner, Options(maxRetries: 1, idleTimeoutMs: FastIdleTimeoutMs));
        var seen = new List<ChatResponseUpdate>();

        var exception = await Record.ExceptionAsync(async () =>
        {
            await ConsumeAsync().WaitAsync(TimeSpan.FromSeconds(5));
        });

        Assert.IsAssignableFrom<IOException>(exception);
        Assert.Contains("Provider stream idle", exception.Message);
        Assert.Equal(1, inner.Calls);
        Assert.Equal("partial", string.Concat(seen.SelectMany(update => update.Contents).OfType<TextContent>().Select(text => text.Text)));

        async Task ConsumeAsync()
        {
            await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
                seen.Add(update);
        }
    }

    [Fact]
    public async Task GetStreamingResponseAsync_DiscardsUsageFromFailedAttempt()
    {
        var failedUsage = new UsageContent(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 1 });
        var inner = new SequenceChatClient(
            _ => StreamThenThrow(
                [new ChatResponseUpdate(ChatRole.Assistant, [failedUsage])],
                new IOException("connection reset")),
            _ => Stream([new ChatResponseUpdate(ChatRole.Assistant, "ok")]));
        var client = new StreamRetryingChatClient(inner, Options(maxRetries: 1));

        var updates = await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]));

        Assert.Equal(2, inner.Calls);
        Assert.DoesNotContain(updates.SelectMany(update => update.Contents), content => content is UsageContent);
        Assert.Equal("ok", string.Concat(updates.SelectMany(update => update.Contents).OfType<TextContent>().Select(text => text.Text)));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_RetriesAfterToolResultAndProviderErrorContent()
    {
        var failedUpdates = new ChatResponseUpdate(ChatRole.Tool, [
            new FunctionResultContent("call-1", "tool result"),
            new ErrorContent("server_error") { ErrorCode = "server_error" }
        ]);
        var inner = new SequenceChatClient(
            _ => StreamThenThrow(
                [failedUpdates],
                new HttpRequestException("server error", null, HttpStatusCode.InternalServerError)),
            _ => Stream([new ChatResponseUpdate(ChatRole.Assistant, "ok")]));
        var client = new StreamRetryingChatClient(inner, Options(maxRetries: 1));

        var updates = await CollectAsync(client.GetStreamingResponseAsync([
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "tool result")])
        ]));

        Assert.Equal(2, inner.Calls);
        Assert.DoesNotContain(updates.SelectMany(update => update.Contents),
            content => content is FunctionResultContent or ErrorContent);
        Assert.Equal("ok", string.Concat(updates.SelectMany(update => update.Contents)
            .OfType<TextContent>().Select(text => text.Text)));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_RetriesCompletedServerErrorOnceWithoutVisibleOutput()
    {
        var inner = new SequenceChatClient(
            _ => Stream([
                new ChatResponseUpdate(ChatRole.Tool, [
                    new FunctionResultContent("call-1", "tool result"),
                    new ErrorContent("request failed; request ID req_first")
                    {
                        ErrorCode = "server_error"
                    }
                ])
            ]),
            _ => Stream([new ChatResponseUpdate(ChatRole.Assistant, "ok")]));
        var client = new StreamRetryingChatClient(
            inner,
            new StreamRetryOptions(
                MaxRetries: 0,
                IdleTimeout: TimeSpan.FromSeconds(30),
                ProviderServerErrorMaxRetries: 1));
        var attempts = new List<ModelStreamAttemptDiagnostic>();

        using var scope = ModelStreamRetryRuntimeScope.Set(new ModelStreamRetryRuntimeContext
        {
            NotifyRetry = (_, _, _) => { },
            NotifyAttemptCompleted = attempts.Add
        });

        var updates = await CollectAsync(client.GetStreamingResponseAsync([
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "tool result")])
        ]));

        Assert.Equal(2, inner.Calls);
        Assert.DoesNotContain(
            updates.SelectMany(update => update.Contents),
            content => content is FunctionResultContent or ErrorContent);
        Assert.Equal("ok", Assert.Single(updates.SelectMany(update => update.Contents).OfType<TextContent>()).Text);
        Assert.Equal([1, 2], attempts.Select(static attempt => attempt.AttemptNumber).ToArray());
        Assert.Equal("provider_server_error", attempts[0].FailureKind);
        Assert.Equal(1, attempts[0].RetryLimit);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_SurfacesFinalServerErrorRequestIdAfterSingleRetry()
    {
        var inner = new SequenceChatClient(
            _ => Stream([
                new ChatResponseUpdate(ChatRole.Assistant, [
                    new ErrorContent("request failed; request ID req_first") { ErrorCode = "server_error" }
                ])
            ]),
            _ => Stream([
                new ChatResponseUpdate(ChatRole.Assistant, [
                    new ErrorContent("request failed; request ID req_final") { ErrorCode = "server_error" }
                ])
            ]));
        var client = new StreamRetryingChatClient(
            inner,
            new StreamRetryOptions(
                MaxRetries: 0,
                IdleTimeout: TimeSpan.FromSeconds(30),
                ProviderServerErrorMaxRetries: 1));

        var exception = await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
            {
            }
        });

        Assert.Equal(2, inner.Calls);
        Assert.Contains("req_final", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_DoesNotRetryCompletedServerErrorAfterVisibleOutput()
    {
        var inner = new SequenceChatClient(
            _ => Stream([
                new ChatResponseUpdate(ChatRole.Assistant, "partial"),
                new ChatResponseUpdate(ChatRole.Assistant, [
                    new ErrorContent("request failed; request ID req_visible") { ErrorCode = "server_error" }
                ])
            ]),
            _ => Stream([new ChatResponseUpdate(ChatRole.Assistant, "retry")]));
        var client = new StreamRetryingChatClient(
            inner,
            new StreamRetryOptions(
                MaxRetries: 0,
                IdleTimeout: TimeSpan.FromSeconds(30),
                ProviderServerErrorMaxRetries: 1));

        var updates = await CollectAsync(
            client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]));

        Assert.Equal(1, inner.Calls);
        Assert.Equal("partial", Assert.Single(updates.SelectMany(update => update.Contents).OfType<TextContent>()).Text);
        Assert.Contains(
            updates.SelectMany(update => update.Contents).OfType<ErrorContent>(),
            error => error.Message.Contains("req_visible", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_PreservesNonVisibleUpdateOrderBeforeFirstVisibleUpdate()
    {
        var metadata = new ChatResponseUpdate { Role = ChatRole.Assistant };
        var functionCall = new FunctionCallContent("call-1", "ReadFile", new Dictionary<string, object?>());
        var toolUse = new ChatResponseUpdate(ChatRole.Assistant, [functionCall]);
        var usage = new ChatResponseUpdate(ChatRole.Assistant, [
            new UsageContent(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 1 })
        ]);
        var inner = new SequenceChatClient(_ => Stream([metadata, toolUse, usage]));
        var client = new StreamRetryingChatClient(inner, Options(maxRetries: 1));

        var updates = await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]));

        Assert.Equal(3, updates.Count);
        Assert.Same(metadata, updates[0]);
        Assert.Same(toolUse, updates[1]);
        Assert.Same(usage, updates[2]);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_DoesNotRetryUserCancellation()
    {
        using var cts = new CancellationTokenSource();
        var inner = new SequenceChatClient(_ => ThrowStream(new OperationCanceledException(cts.Token)));
        var client = new StreamRetryingChatClient(inner, Options(maxRetries: 1));
        cts.Cancel();

        var exception = await Record.ExceptionAsync(async () =>
        {
            await foreach (var _ in client.GetStreamingResponseAsync(
                               [new ChatMessage(ChatRole.User, "hi")],
                               cancellationToken: cts.Token))
            {
            }
        });

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_DoesNotRetryBadRequest()
    {
        var inner = new SequenceChatClient(
            _ => ThrowStream(new HttpRequestException("bad request", null, HttpStatusCode.BadRequest)),
            _ => Stream([new ChatResponseUpdate(ChatRole.Assistant, "retry")]));
        var client = new StreamRetryingChatClient(inner, Options(maxRetries: 1));

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
            {
            }
        });

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ThrowsAfterRetryBudgetExhausted()
    {
        var inner = new SequenceChatClient(
            _ => ThrowStream(new IOException("first")),
            _ => ThrowStream(new IOException("second")));
        var client = new StreamRetryingChatClient(inner, Options(maxRetries: 1));
        var finalFailures = new List<string>();
        var attempts = new List<ModelStreamAttemptDiagnostic>();

        using var scope = ModelStreamRetryRuntimeScope.Set(new ModelStreamRetryRuntimeContext
        {
            NotifyRetry = (_, _, _) => { },
            NotifyFinalFailure = exception => finalFailures.Add($"{exception.GetType().Name}:{exception.Message}"),
            NotifyAttemptCompleted = attempts.Add
        });

        var exception = await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
            {
            }
        });

        Assert.Equal("second", exception.Message);
        Assert.Equal(2, inner.Calls);
        Assert.Equal(["IOException:second"], finalFailures);
        Assert.Equal(["scheduled", "exhausted"], attempts.Select(static attempt => attempt.RetryDecision).ToArray());
    }

    [Fact]
    public async Task GetStreamingResponseAsync_PreservesStreamFailureWhenEnumeratorDisposeThrows()
    {
        var inner = new SequenceChatClient(
            _ => new DisposeThrowingStream(new IOException("stream closed"), new NotSupportedException("dispose unsupported")),
            _ => Stream([new ChatResponseUpdate(ChatRole.Assistant, "ok")]));
        var client = new StreamRetryingChatClient(inner, Options(maxRetries: 1));

        var updates = await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]));

        Assert.Equal(2, inner.Calls);
        Assert.Equal("ok", string.Concat(updates.SelectMany(update => update.Contents).OfType<TextContent>().Select(text => text.Text)));
    }

    private static StreamRetryOptions Options(int maxRetries, int idleTimeoutMs = 30_000) =>
        new(maxRetries, TimeSpan.FromMilliseconds(idleTimeoutMs));

    private static async Task<List<ChatResponseUpdate>> CollectAsync(IAsyncEnumerable<ChatResponseUpdate> updates)
    {
        var collected = new List<ChatResponseUpdate>();
        await foreach (var update in updates)
            collected.Add(update);
        return collected;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Stream(IReadOnlyList<ChatResponseUpdate> updates)
    {
        foreach (var update in updates)
        {
            await Task.Yield();
            yield return update;
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamThenThrow(
        IReadOnlyList<ChatResponseUpdate> updates,
        Exception exception)
    {
        foreach (var update in updates)
        {
            await Task.Yield();
            yield return update;
        }

        throw exception;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ThrowStream(Exception exception)
    {
        await Task.Yield();
        throw exception;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private sealed class SequenceChatClient(params Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>[] streams)
        : IChatClient
    {
        private int _calls;

        public int Calls => _calls;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var callIndex = Interlocked.Increment(ref _calls) - 1;
            var stream = streams[Math.Min(callIndex, streams.Length - 1)];
            return stream(cancellationToken);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ResponseEnded(string message) : Exception(message);

    private sealed class DisposeThrowingStream(Exception moveNextException, Exception disposeException)
        : IAsyncEnumerable<ChatResponseUpdate>, IAsyncEnumerator<ChatResponseUpdate>
    {
        public ChatResponseUpdate Current => new(ChatRole.Assistant, string.Empty);

        public IAsyncEnumerator<ChatResponseUpdate> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

        public ValueTask<bool> MoveNextAsync() => ValueTask.FromException<bool>(moveNextException);

        public ValueTask DisposeAsync() => ValueTask.FromException(disposeException);
    }

    private sealed class HangingStream(IReadOnlyList<ChatResponseUpdate>? prefix = null)
        : IAsyncEnumerable<ChatResponseUpdate>, IAsyncEnumerator<ChatResponseUpdate>
    {
        private readonly IReadOnlyList<ChatResponseUpdate> _prefix = prefix ?? [];
        private readonly TaskCompletionSource<bool> _moveNextCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _disposeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _index;

        public ChatResponseUpdate Current { get; private set; } = new(ChatRole.Assistant, string.Empty);

        public IAsyncEnumerator<ChatResponseUpdate> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

        public ValueTask<bool> MoveNextAsync()
        {
            if (_index < _prefix.Count)
            {
                Current = _prefix[_index++];
                return ValueTask.FromResult(true);
            }

            return new ValueTask<bool>(_moveNextCompletion.Task);
        }

        public ValueTask DisposeAsync() => new(_disposeCompletion.Task);
    }
}
