using DotCraft.Agents;
using DotCraft.Sessions;
using DotCraft.Tests.Providers;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed partial class StreamingFunctionInvokingChatClientTests
{
    [Fact]
    public async Task RetryDeadline_ToolAndNotificationTimeConsumeTheOriginalWait()
    {
        var clock = new RetryTestClock();
        var deadline = ProviderRetryDeadline.FromDelay(TimeSpan.FromSeconds(30), clock);
        var failure = new ProviderFailure(ProviderFailureKind.InternalServerError, 503, ServerRetryAfter: deadline.Delay) { RetryDeadline = deadline };
        var inner = new ScriptedStreamChatClient(
            ([ToolCall("call-1", "GetWeather")], new ProviderFailureException(failure, new IOException("wire"))),
            ([Text("done")], null));
        var client = new StreamingFunctionInvokingChatClient(new StreamRetryingChatClient(inner, new(1, TimeSpan.FromSeconds(10))))
        {
            AdditionalTools = [AIFunctionFactory.Create(() => "sunny", name: "GetWeather")]
        };
        var invocations = 0;
        client.FunctionInvoker = (_, _) =>
        {
            invocations++;
            clock.Advance(TimeSpan.FromSeconds(20));
            return ValueTask.FromResult<object?>("sunny");
        };
        using var scope = ModelStreamRetryRuntimeScope.Set(new()
        {
            NotifyRetry = notification =>
            {
                Assert.Same(failure, notification.Classification);
                Assert.Equal(TimeSpan.FromSeconds(10), failure.GetRetryDelay(1));
                clock.Advance(TimeSpan.FromSeconds(10));
            }
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "weather")], cancellationToken: timeout.Token));
        Assert.Equal(1, invocations);
        Assert.Equal(2, inner.Requests.Count);
        Assert.Equal(TimeSpan.Zero, failure.GetRetryDelay(2));
    }

    [Fact]
    public async Task TransportReplay_RespectsAdviceInsteadOfShortLocalBackoff()
    {
        var failure = new ProviderFailure(ProviderFailureKind.InternalServerError, 503, ServerRetryAfter: TimeSpan.FromSeconds(30));
        var inner = new ScriptedStreamChatClient(([], new ProviderFailureException(failure, new IOException("wire"))), ([Text("unused")], null));
        var client = new StreamRetryingChatClient(inner, new(1, TimeSpan.FromSeconds(10)));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CollectAsync(client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "work")], cancellationToken: timeout.Token)));
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task TransportReplay_ExpiredAdviceRetriesWithoutRestartingTheWait()
    {
        var clock = new RetryTestClock();
        var deadline = ProviderRetryDeadline.FromDelay(TimeSpan.FromSeconds(30), clock);
        var failure = new ProviderFailure(ProviderFailureKind.InternalServerError, 503, ServerRetryAfter: deadline.Delay) { RetryDeadline = deadline };
        var inner = new ScriptedStreamChatClient(([], new ProviderFailureException(failure, new IOException("wire"))), ([Text("done")], null));
        var client = new StreamRetryingChatClient(inner, new(1, TimeSpan.FromSeconds(10)));
        using var scope = ModelStreamRetryRuntimeScope.Set(new() { NotifyRetry = _ => clock.Advance(TimeSpan.FromSeconds(31)) });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "work")], cancellationToken: timeout.Token));
        Assert.Equal(2, inner.Requests.Count);
    }
    [Fact]
    public async Task TransportDisposal_ConsumesAdviceCapturedBeforeEnumeratorCleanup()
    {
        var clock = new RetryTestClock();
        var inner = new ScriptedStreamChatClient(([], new HttpRequestException("wire", null, System.Net.HttpStatusCode.ServiceUnavailable)), ([Text("done")], null));
        var classifier = new ClockClassifier(clock);
        var transport = new StreamRetryingChatClient(new DisposalClockClient(inner, clock, classifier), new(1, TimeSpan.FromSeconds(10)));
        using var scope = ModelStreamRetryRuntimeScope.Set(new()
        {
            NotifyRetry = notice => Assert.Equal(TimeSpan.Zero, notice.Classification!.GetRetryDelay(1))
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await CollectAsync(transport.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "work")], cancellationToken: timeout.Token));
        Assert.Equal(1, classifier.Calls);
        Assert.Equal(2, inner.Requests.Count);
    }

    private sealed class ClockClassifier(RetryTestClock clock) : IProviderFailureClassifier
    {
        public int Calls { get; private set; }
        public ProviderFailure Classify(Exception exception)
        {
            Calls++;
            var deadline = ProviderRetryDeadline.FromDelay(TimeSpan.FromSeconds(30), clock);
            return new(ProviderFailureKind.InternalServerError, 503, ServerRetryAfter: deadline.Delay) { RetryDeadline = deadline };
        }
    }

    private sealed class DisposalClockClient(IChatClient inner, RetryTestClock clock, IProviderFailureClassifier classifier) : DelegatingChatClient(inner)
    {
        public override object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(IProviderFailureClassifier) ? classifier : base.GetService(serviceType, serviceKey);
        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            new DisposalClockStream(base.GetStreamingResponseAsync(messages, options, cancellationToken), clock);
    }

    private sealed class DisposalClockStream(IAsyncEnumerable<ChatResponseUpdate> inner, RetryTestClock clock) : IAsyncEnumerable<ChatResponseUpdate>
    {
        public IAsyncEnumerator<ChatResponseUpdate> GetAsyncEnumerator(CancellationToken cancellationToken = default) => new Enumerator(inner.GetAsyncEnumerator(cancellationToken), clock);
        private sealed class Enumerator(IAsyncEnumerator<ChatResponseUpdate> inner, RetryTestClock clock) : IAsyncEnumerator<ChatResponseUpdate>
        {
            public ChatResponseUpdate Current => inner.Current;
            public ValueTask<bool> MoveNextAsync() => inner.MoveNextAsync();
            public async ValueTask DisposeAsync()
            {
                clock.Advance(TimeSpan.FromSeconds(31));
                await inner.DisposeAsync();
            }
        }
    }

}
