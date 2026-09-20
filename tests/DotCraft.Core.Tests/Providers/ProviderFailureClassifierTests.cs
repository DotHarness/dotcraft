using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using DotCraft.Agents;
using Xunit;

namespace DotCraft.Tests.Providers;

public sealed class ProviderFailureClassifierTests
{
    [Fact]
    public void Anthropic_OverloadedFrameIsTerminalCapacity()
    {
        var failure = AnthropicProviderFailureClassifier.Instance.Classify(
            new IOException("{\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\"}}"));

        Assert.Equal(ProviderFailureKind.ServerOverloaded, failure.Kind);
        Assert.True(failure.IsTerminal);
        Assert.Null(failure.GetRetryDelay(1));
    }

    [Fact]
    public void Anthropic_Status529IsTerminalCapacity()
    {
        var failure = AnthropicProviderFailureClassifier.Instance.Classify(
            new HttpRequestException("overloaded", null, (HttpStatusCode)529));

        Assert.Equal(ProviderFailureKind.ServerOverloaded, failure.Kind);
        Assert.True(failure.IsTerminal);
    }

    [Fact]
    public void Anthropic_RateLimitIsRetryableAndHonorsAdvisedDelay()
    {
        var failure = AnthropicProviderFailureClassifier.Instance.Classify(
            new HttpRequestException(
                "{\"error\":{\"type\":\"rate_limit_error\",\"message\":\"Please try again in 3.5s.\"}}",
                null,
                HttpStatusCode.TooManyRequests));

        Assert.Equal(ProviderFailureKind.RateLimitExceeded, failure.Kind);
        Assert.False(failure.IsTerminal);
        Assert.Equal(TimeSpan.FromSeconds(3.5), failure.GetRetryDelay(1));
    }

    [Fact]
    public void Anthropic_ExhaustedBalanceIsTerminalEvenOnThrottlingStatus()
    {
        var failure = AnthropicProviderFailureClassifier.Instance.Classify(
            new HttpRequestException(
                "{\"error\":{\"message\":\"Insufficient Balance\"}}",
                null,
                HttpStatusCode.TooManyRequests));

        Assert.Equal(ProviderFailureKind.UsageLimitExceeded, failure.Kind);
        Assert.True(failure.IsTerminal);
    }

    [Fact]
    public void Anthropic_CapacitySignalOnASuccessfulStatusIsStillCapacity()
    {
        var failure = AnthropicProviderFailureClassifier.Instance.Classify(
            new InvalidOperationException("model stopped: insufficient_system_resource"));

        Assert.Equal(ProviderFailureKind.ServerOverloaded, failure.Kind);
        Assert.True(failure.IsTerminal);
    }

    [Fact]
    public void Anthropic_TransportFailureFallsBackToConnectionKind()
    {
        var failure = AnthropicProviderFailureClassifier.Instance.Classify(
            new IOException("connection reset by peer"));

        Assert.Equal(ProviderFailureKind.HttpConnectionFailed, failure.Kind);
        Assert.False(failure.IsTerminal);
    }

    [Fact]
    public void OpenAI_ExplicitCapacityRejectionIsTerminal()
    {
        var failure = Classify(
            isSubscriptionBackend: true,
            status: 503,
            body: "{\"error\":{\"code\":\"server_is_overloaded\"}}");

        Assert.Equal(ProviderFailureKind.ServerOverloaded, failure.Kind);
        Assert.True(failure.IsTerminal);
    }

    [Fact]
    public void OpenAI_SlowDownIsRetryableThrottling()
    {
        var failure = Classify(
            isSubscriptionBackend: true,
            status: 503,
            body: "{\"error\":{\"code\":\"slow_down\"}}");

        Assert.Equal(ProviderFailureKind.RateLimitExceeded, failure.Kind);
        Assert.False(failure.IsTerminal);
    }

    [Fact]
    public void OpenAI_InternalServerErrorIsRetryable()
    {
        var failure = Classify(isSubscriptionBackend: true, status: 500, body: "{}");

        Assert.Equal(ProviderFailureKind.InternalServerError, failure.Kind);
        Assert.False(failure.IsTerminal);
    }

    [Fact]
    public void OpenAI_ExhaustedQuotaIsTerminal()
    {
        var failure = Classify(
            isSubscriptionBackend: false,
            status: 429,
            body: "{\"error\":{\"code\":\"insufficient_quota\"}}");

        Assert.Equal(ProviderFailureKind.UsageLimitExceeded, failure.Kind);
        Assert.True(failure.IsTerminal);
    }

    [Fact]
    public void OpenAI_ThrottlingHonorsTheRetryAfterHeader()
    {
        var failure = Classify(
            isSubscriptionBackend: false,
            status: 429,
            body: "{\"error\":{\"code\":\"rate_limit_exceeded\"}}",
            retryAfter: "30");

        Assert.Equal(ProviderFailureKind.RateLimitExceeded, failure.Kind);
        Assert.Equal(TimeSpan.FromSeconds(30), failure.GetRetryDelay(1));
    }

    [Fact]
    public void OpenAI_SubscriptionThrottlingIsSurfacedRatherThanRetried()
    {
        var failure = Classify(
            isSubscriptionBackend: true,
            status: 429,
            body: "{\"error\":{\"code\":\"rate_limit_exceeded\"}}");

        Assert.Equal(ProviderFailureKind.ResponseTooManyFailedAttempts, failure.Kind);
        Assert.True(failure.IsTerminal);
    }

    [Fact]
    public void OpenAI_RequestIdSurvivesClassification()
    {
        var failure = Classify(
            isSubscriptionBackend: false,
            status: 500,
            body: "{}",
            requestId: "req_abc123");

        Assert.Equal("req_abc123", failure.RequestId);
        Assert.Equal(500, failure.HttpStatus);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("not-a-delay")]
    public void RetryAfterRejectsValuesThatCannotSchedule(string value)
    {
        Assert.Null(ProviderFailureParsing.ParseRetryAfter(value, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void RetryAfterAcceptsAnHttpDateInTheFuture()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var delay = ProviderFailureParsing.ParseRetryAfter("Thu, 01 Jan 2026 00:00:45 GMT", now);

        Assert.Equal(TimeSpan.FromSeconds(45), delay);
    }

    [Fact]
    public void RetryAfterDropsAnHttpDateInThePast()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 1, 0, TimeSpan.Zero);

        Assert.Null(ProviderFailureParsing.ParseRetryAfter("Thu, 01 Jan 2026 00:00:45 GMT", now));
    }

    private static ProviderFailure Classify(
        bool isSubscriptionBackend,
        int status,
        string body,
        string? retryAfter = null,
        string? requestId = null)
    {
        var headers = new Dictionary<string, string>();
        if (retryAfter != null)
            headers["retry-after"] = retryAfter;
        if (requestId != null)
            headers["x-request-id"] = requestId;

        var response = new FakePipelineResponse(status, body, headers);
        var exception = new ClientResultException(response);
        return new OpenAIProviderFailureClassifier(isSubscriptionBackend).Classify(exception);
    }

    private sealed class FakePipelineResponse(
        int status,
        string body,
        IReadOnlyDictionary<string, string> headers) : PipelineResponse
    {
        private readonly BinaryData _content = BinaryData.FromString(body);

        public override int Status => status;

        public override string ReasonPhrase => string.Empty;

        public override BinaryData Content => _content;

        public override Stream? ContentStream { get; set; }

        protected override PipelineResponseHeaders HeadersCore { get; } = new FakeHeaders(headers);

        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => _content;

        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_content);

        public override void Dispose()
        {
        }

        private sealed class FakeHeaders(IReadOnlyDictionary<string, string> headers) : PipelineResponseHeaders
        {
            public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
                headers.GetEnumerator();

            public override bool TryGetValue(string name, out string? value) =>
                headers.TryGetValue(name, out value);

            public override bool TryGetValues(string name, out IEnumerable<string>? values)
            {
                if (headers.TryGetValue(name, out var value))
                {
                    values = [value];
                    return true;
                }

                values = null;
                return false;
            }
        }
    }
}
