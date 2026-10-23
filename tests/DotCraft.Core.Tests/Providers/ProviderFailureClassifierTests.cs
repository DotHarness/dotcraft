using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using DotCraft.Agents;
using Xunit;

namespace DotCraft.Tests.Providers;

public sealed class ProviderFailureClassifierTests
{
    [Theory]
    [InlineData("overloaded_frame", ProviderFailureKind.ServerOverloaded, true)]
    [InlineData("status_529", ProviderFailureKind.ServerOverloaded, true)]
    [InlineData("exhausted_balance", ProviderFailureKind.UsageLimitExceeded, true)]
    [InlineData("capacity_signal", ProviderFailureKind.ServerOverloaded, true)]
    [InlineData("connection_reset", ProviderFailureKind.HttpConnectionFailed, false)]
    public void Anthropic_ClassifiesFailureKindAndTerminality(string scenario, ProviderFailureKind expectedKind, bool terminal)
    {
        Exception exception = scenario switch
        {
            "overloaded_frame" => new IOException("{\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\"}}"),
            "status_529" => new HttpRequestException("overloaded", null, (HttpStatusCode)529),
            "exhausted_balance" => new HttpRequestException(
                "{\"error\":{\"message\":\"Insufficient Balance\"}}",
                null,
                HttpStatusCode.TooManyRequests),
            "capacity_signal" => new InvalidOperationException("model stopped: insufficient_system_resource"),
            _ => new IOException("connection reset by peer")
        };

        var failure = AnthropicProviderFailureClassifier.Instance.Classify(exception);

        Assert.Equal(expectedKind, failure.Kind);
        Assert.Equal(terminal, failure.IsTerminal);
        if (terminal)
            Assert.Null(failure.GetRetryDelay(1));
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
        Assert.Equal(TimeSpan.FromSeconds(3.5), failure.ServerRetryAfter);
        Assert.InRange(failure.GetRetryDelay(1)!.Value, TimeSpan.Zero, TimeSpan.FromSeconds(3.5));
    }

    [Theory]
    [InlineData(true, 503, "{\"error\":{\"code\":\"server_is_overloaded\"}}", ProviderFailureKind.ServerOverloaded, true)]
    [InlineData(true, 503, "{\"error\":{\"code\":\"slow_down\"}}", ProviderFailureKind.RateLimitExceeded, false)]
    [InlineData(true, 500, "{}", ProviderFailureKind.InternalServerError, false)]
    [InlineData(false, 429, "{\"error\":{\"code\":\"insufficient_quota\"}}", ProviderFailureKind.UsageLimitExceeded, true)]
    [InlineData(true, 429, "{\"error\":{\"code\":\"rate_limit_exceeded\"}}", ProviderFailureKind.ResponseTooManyFailedAttempts, true)]
    [InlineData(true, 429, "{\"error\":{\"type\":\"rate_limit_error\",\"code\":\"turn_rate_limit_exceeded\",\"message\":\"Codex is at capacity, please try again later\"}}", ProviderFailureKind.ServerOverloaded, true)]
    public void OpenAI_ClassifiesFailureKindAndTerminality(
        bool isSubscriptionBackend,
        int status,
        string body,
        ProviderFailureKind expectedKind,
        bool terminal)
    {
        var failure = Classify(isSubscriptionBackend, status, body, requestId: "req_abc123");

        Assert.Equal(expectedKind, failure.Kind);
        Assert.Equal(terminal, failure.IsTerminal);
        Assert.Equal(status, failure.HttpStatus);
        Assert.Equal("req_abc123", failure.RequestId);
    }

    [Theory]
    [InlineData(false, 30)]
    [InlineData(true, 5)]
    public void OpenAI_ThrottlingHonorsTheRetryAfterHeader(bool isSubscriptionBackend, int seconds)
    {
        var failure = Classify(
            isSubscriptionBackend,
            status: 429,
            body: "{\"error\":{\"code\":\"rate_limit_exceeded\"}}",
            retryAfter: seconds.ToString());

        Assert.Equal(ProviderFailureKind.RateLimitExceeded, failure.Kind);
        Assert.Equal(TimeSpan.FromSeconds(seconds), failure.ServerRetryAfter);
        Assert.InRange(failure.GetRetryDelay(1)!.Value, TimeSpan.Zero, TimeSpan.FromSeconds(seconds));
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
