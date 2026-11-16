using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using DotCraft.Agents;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class AnthropicProviderFailureClassifierTests
{
    [Theory]
    [InlineData(false, "rate_limit_error", ProviderFailureKind.RateLimitExceeded)]
    [InlineData(true, "rate_limit_error", ProviderFailureKind.RateLimitExceeded)]
    [InlineData(false, "not_found_error", ProviderFailureKind.BadRequest)]
    [InlineData(true, "not_found_error", ProviderFailureKind.BadRequest)]
    [InlineData(false, "authentication_error", ProviderFailureKind.Unauthorized)]
    [InlineData(true, "authentication_error", ProviderFailureKind.Unauthorized)]
    [InlineData(false, "billing_error", ProviderFailureKind.UsageLimitExceeded)]
    [InlineData(true, "billing_error", ProviderFailureKind.UsageLimitExceeded)]
    [InlineData(false, "overloaded_error", ProviderFailureKind.ServerOverloaded)]
    [InlineData(true, "overloaded_error", ProviderFailureKind.ServerOverloaded)]
    public async Task Sdk_errors_use_structured_type_instead_of_misleading_message(
        bool streaming, string type, ProviderFailureKind expected)
    {
        var exception = await SdkError(streaming, type,
            "Not overloaded_error or insufficient_system_resource. The prompt is not too long. Please try again in 3.5s.");

        var failure = AnthropicProviderFailureClassifier.Instance.Classify(
            new InvalidOperationException("overloaded_error", exception));

        Assert.Equal(expected, failure.Kind);
        Assert.Equal(type, failure.ProviderErrorCode);
        Assert.Equal(streaming ? null : 502, failure.HttpStatus);
        Assert.Equal(streaming ? "req_body" : "req_header", failure.RequestId);
        Assert.Equal(expected == ProviderFailureKind.RateLimitExceeded ? TimeSpan.FromSeconds(3.5) : null,
            failure.ServerRetryAfter);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sdk_error_code_preserves_quota_signal_and_diagnostics(bool streaming)
    {
        var exception = await SdkError(streaming, "rate_limit_error", "Please try again in 3s.",
            "organization_spend_limit_exceeded");

        var failure = AnthropicProviderFailureClassifier.Instance.Classify(exception);

        Assert.Equal(ProviderFailureKind.UsageLimitExceeded, failure.Kind);
        Assert.Equal("organization_spend_limit_exceeded", failure.ProviderErrorCode);
        Assert.Null(failure.ServerRetryAfter);
        Assert.False(failure.AllowsTransportReplay);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sdk_unknown_error_code_uses_its_structured_type(bool streaming)
    {
        var exception = await SdkError(streaming, "rate_limit_error", "Not billing_error.", "future_limit");

        var failure = AnthropicProviderFailureClassifier.Instance.Classify(exception);

        Assert.Equal(ProviderFailureKind.RateLimitExceeded, failure.Kind);
        Assert.Equal("future_limit", failure.ProviderErrorCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sdk_gateway_error_wrapping_Bedrock_validation_is_not_replayed(bool streaming)
    {
        var exception = await SdkError(streaming, "api_error",
            "BadGatewayError - {\"type\":\"bedrock\",\"code\":\"ValidationException\",\"message\":\"Invalid request parameters.\"}");

        var failure = AnthropicProviderFailureClassifier.Instance.Classify(exception);

        Assert.Equal(ProviderFailureKind.BadRequest, failure.Kind);
        Assert.Equal("api_error", failure.ProviderErrorCode);
        Assert.False(failure.AllowsTransportReplay);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sdk_context_rejection_preserves_compaction_classification(bool streaming)
    {
        var exception = await SdkError(streaming, "invalid_request_error", "prompt is too long: 300000 tokens > 200000 maximum");

        var failure = AnthropicProviderFailureClassifier.Instance.Classify(exception);

        Assert.Equal(ProviderFailureKind.ContextWindowExceeded, failure.Kind);
    }

    [Fact]
    public async Task Sdk_http_529_is_overload_without_a_known_error_type()
    {
        var exception = await SdkError(false, "future_error", "Service unavailable",
            status: (HttpStatusCode)529);

        var failure = AnthropicProviderFailureClassifier.Instance.Classify(exception);

        Assert.Equal(ProviderFailureKind.ServerOverloaded, failure.Kind);
        Assert.Equal(529, failure.HttpStatus);
        Assert.True(failure.IsTerminal);
    }

    private static async Task<AnthropicServiceException> SdkError(
        bool streaming, string type, string message, string? code = null,
        HttpStatusCode status = HttpStatusCode.BadGateway)
    {
        var body = JsonSerializer.Serialize(new
        {
            type = "error",
            error = new { type, message, details = code is null ? null : new { error_code = code } },
            request_id = "req_body"
        });
        using var http = new HttpClient(new ErrorHandler(streaming, body, status));
        using var client = new AnthropicClient { HttpClient = http, ApiKey = "test-key", MaxRetries = 0 }
            .Beta.AsIChatClient("test-model", 1024);
        var exception = await Record.ExceptionAsync(async () =>
        {
            var messages = new[] { new ChatMessage(ChatRole.User, "Probe") };
            if (streaming)
            {
                await foreach (var _ in client.GetStreamingResponseAsync(messages)) { }
            }
            else
                await client.GetResponseAsync(messages);
        });
        return Assert.IsAssignableFrom<AnthropicServiceException>(exception);
    }

    private sealed class ErrorHandler(bool streaming, string body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(streaming ? HttpStatusCode.OK : status)
            {
                Content = new StringContent(streaming ? $"event: error\ndata: {body}\n\n" : body,
                    Encoding.UTF8, streaming ? "text/event-stream" : "application/json")
            };
            response.Headers.Add("request-id", "req_header");
            return Task.FromResult(response);
        }
    }
}
