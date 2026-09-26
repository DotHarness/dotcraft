using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using DotCraft.Agents;
using Xunit;

#pragma warning disable OPENAI001

namespace DotCraft.Tests.Providers;

public sealed class ProviderRetryDeadlineTests
{
    [Fact]
    public void Deadline_UsesElapsedTimeAndSurvivesCopiesAndWrappedFailures()
    {
        var clock = new RetryTestClock();
        var deadline = ProviderRetryDeadline.FromDelay(TimeSpan.FromSeconds(10), clock);
        var original = new ProviderFailure(ProviderFailureKind.RateLimitExceeded, ServerRetryAfter: deadline.Delay) { RetryDeadline = deadline };
        clock.Advance(TimeSpan.FromSeconds(3));
        clock.ShiftUtc(TimeSpan.FromDays(-90));
        var copy = original with { RequestId = "request" };
        var wrapped = new IOException("wrapper", new ProviderFailureException(copy, new IOException("wire")));
        var restored = DefaultProviderFailureClassifier.Instance.ClassifyCaptured(wrapped);
        Assert.Same(copy, restored);
        Assert.Equal(TimeSpan.FromSeconds(7), restored.GetRetryDelay(1));
        clock.ShiftUtc(TimeSpan.FromDays(180));
        clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(TimeSpan.Zero, restored.GetRetryDelay(8));
        Assert.Equal(TimeSpan.FromSeconds(10), restored.ServerRetryAfter);
        Assert.DoesNotContain("RetryDeadline", JsonSerializer.Serialize(restored));
    }

    [Fact]
    public void CachedClassification_DoesNotRestartTextAdviceAcrossWrappers()
    {
        var clock = new RetryTestClock();
        var classifier = new CountingClassifier(clock);
        var failure = new IOException("wire");
        var first = classifier.ClassifyCaptured(failure);
        clock.Advance(TimeSpan.FromSeconds(4));
        var next = classifier.ClassifyCaptured(new IOException("outer", failure));
        Assert.Same(first, next);
        Assert.Equal(1, classifier.Calls);
        Assert.Equal(TimeSpan.FromSeconds(6), next.GetRetryDelay(1));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("4.5", 4.5)]
    [InlineData("Thu, 01 Jan 2026 00:00:05 GMT", 5)]
    [InlineData("Wed, 31 Dec 2025 23:59:59 GMT", 0)]
    public void ValidAdvice_IncludingExpiredAdviceHasAnExplicitDelay(string value, double seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), ProviderFailureParsing.ParseRetryAfter(value, new RetryTestClock().GetUtcNow()));

    [Theory]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e309")]
    [InlineData("9999999999999999999999999")]
    [InlineData("tomorrow")]
    [InlineData("2026-01-01")]
    public void MalformedAdvice_DoesNotThrowAndAllowsFallback(string value) =>
        Assert.Null(ProviderFailureParsing.ParseRetryAfter(value, new RetryTestClock().GetUtcNow()));

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public void HeaderReceipt_AnchorsAdviceBeforeClassificationAndBodyProcessing(int status)
    {
        var clock = new RetryTestClock();
        var response = new RetryResponse(status, "10");
        var deadline = OpenAIRetryAdvicePipelinePolicy.Capture(response, clock);
        clock.Advance(TimeSpan.FromSeconds(8));
        var classifier = new OpenAIProviderFailureClassifier(false);
        var exception = new ClientResultException(response);
        var failure = classifier.Classify(exception);
        Assert.Same(deadline, failure.RetryDeadline);
        Assert.Equal(TimeSpan.FromSeconds(2), failure.GetRetryDelay(1));
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(TimeSpan.Zero, classifier.Classify(exception).GetRetryDelay(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClientPipeline_CapturesRetryAdviceBeforeReturningTheFailure(bool lite)
    {
        using var http = new HttpClient(new RetryHandler());
        var endpoint = new Uri("https://example.test/v1");
        var options = lite ? OpenAIClientProvider.CreateResponsesLiteClientOptions(endpoint, 30)
            : OpenAIClientProvider.CreateClientOptions(endpoint, 30);
        options.Transport = new HttpClientPipelineTransport(http);
        var client = new global::OpenAI.OpenAIClient(new ApiKeyCredential("test"), options);
        var error = await Assert.ThrowsAsync<ClientResultException>(async () => await client.GetResponsesClient().CreateResponseAsync(new global::OpenAI.Responses.CreateResponseOptions { Model = "test", StreamingEnabled = false }));
        var laterClock = new RetryTestClock();
        var deadline = OpenAIRetryAdvicePipelinePolicy.Capture(error.GetRawResponse(), laterClock);
        laterClock.Advance(TimeSpan.FromMinutes(10));
        Assert.NotNull(deadline);
        Assert.True(deadline.Remaining > TimeSpan.Zero);
    }

    [Theory]
    [InlineData(429, true)]
    [InlineData(500, false)]
    [InlineData(503, false)]
    public void TextAdvice_OnlyAppliesToRateLimits(int status, bool expectedAdvice)
    {
        var error = new ClientResultException(new RetryResponse(status, null,
            "{\"error\":{\"message\":\"Please try again in 600 seconds.\"}}"));
        var failure = new OpenAIProviderFailureClassifier(false).Classify(error);

        if (expectedAdvice)
        {
            Assert.Equal(TimeSpan.FromSeconds(600), failure.ServerRetryAfter);
            Assert.NotNull(failure.RetryDeadline);
        }
        else
        {
            Assert.Null(failure.ServerRetryAfter);
            Assert.Null(failure.RetryDeadline);
            Assert.InRange(failure.GetRetryDelay(1)!.Value.TotalMilliseconds, 180, 220);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConnectionFailures_DoNotUseTextAdvice(bool disconnectedStream)
    {
        Exception error = disconnectedStream
            ? new IOException("response ended prematurely; try again in 600 seconds")
            : new HttpRequestException("connection failed; try again in 600 seconds");
        var failure = new OpenAIProviderFailureClassifier(false).Classify(error);

        Assert.Null(failure.ServerRetryAfter);
        Assert.Null(failure.RetryDeadline);
        Assert.InRange(failure.GetRetryDelay(1)!.Value.TotalMilliseconds, 180, 220);
    }

    [Theory]
    [InlineData("10", 10)]
    [InlineData("0", 0)]
    public void HeaderAdvice_TakesPrecedenceOverRateLimitText(string header, int seconds)
    {
        var response = new RetryResponse(429, header,
            "{\"error\":{\"message\":\"Please try again in 600 seconds.\"}}");
        var error = new ClientResultException(response);
        var failure = new OpenAIProviderFailureClassifier(false).Classify(error);

        Assert.Equal(TimeSpan.FromSeconds(seconds), failure.ServerRetryAfter);
        Assert.NotNull(failure.RetryDeadline);
        if (seconds == 0)
            Assert.Equal(TimeSpan.Zero, failure.GetRetryDelay(1));
    }

    private sealed class RetryHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") };
            response.Headers.TryAddWithoutValidation("retry-after", "30");
            return Task.FromResult(response);
        }
    }

    [Fact]
    public void LegacyDurationConstructorAndWithSetterRemainUsable()
    {
        var failure = new ProviderFailure(ProviderFailureKind.RateLimitExceeded, ServerRetryAfter: TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, failure.GetRetryDelay(1));
        var changed = failure with { ServerRetryAfter = TimeSpan.FromSeconds(20) };
        Assert.Equal(TimeSpan.FromSeconds(20), changed.ServerRetryAfter);
        Assert.InRange(changed.GetRetryDelay(1)!.Value.TotalSeconds, 19, 20);
        var fallback = changed with { ServerRetryAfter = null };
        Assert.InRange(fallback.GetRetryDelay(1)!.Value.TotalMilliseconds, 180, 220);
    }

    private sealed class CountingClassifier(RetryTestClock clock) : IProviderFailureClassifier
    {
        public int Calls { get; private set; }
        public ProviderFailure Classify(Exception exception)
        {
            Calls++;
            var deadline = ProviderRetryDeadline.FromDelay(TimeSpan.FromSeconds(10), clock);
            return new(ProviderFailureKind.RateLimitExceeded, ServerRetryAfter: deadline.Delay) { RetryDeadline = deadline };
        }
    }

    private sealed class RetryResponse(int status, string? retryAfter, string body = "{}") : PipelineResponse
    {
        public override int Status => status;
        public override string ReasonPhrase => "";
        public override BinaryData Content => BinaryData.FromString(body);
        public override Stream? ContentStream { get; set; }
        protected override PipelineResponseHeaders HeadersCore { get; } = new RetryHeaders(retryAfter);
        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Content);
        public override void Dispose() { }
    }

    private sealed class RetryHeaders(string? value) : PipelineResponseHeaders
    {
        public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
            (value is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["retry-after"] = value }).GetEnumerator();
        public override bool TryGetValue(string name, out string? result)
        { result = name.Equals("retry-after", StringComparison.OrdinalIgnoreCase) ? value : null; return result != null; }
        public override bool TryGetValues(string name, out IEnumerable<string>? result)
        { var found = TryGetValue(name, out var item); result = found ? [item!] : null; return found; }
    }
}

internal sealed class RetryTestClock : TimeProvider
{
    private long _timestamp;
    private DateTimeOffset _utc = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _timestamp;
    public override DateTimeOffset GetUtcNow() => _utc;
    public void Advance(TimeSpan elapsed) { _timestamp += elapsed.Ticks; _utc += elapsed; }
    public void ShiftUtc(TimeSpan delta) => _utc += delta;
}
