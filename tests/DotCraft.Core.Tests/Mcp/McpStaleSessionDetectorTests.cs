using DotCraft.Mcp;
using System.Net;
using Xunit;

namespace DotCraft.Tests.Mcp;

public sealed class McpStaleSessionDetectorTests
{
    [Fact]
    public void IsStaleSessionFailure_MatchesHttp404WithSessionNotFound()
    {
        var exception = new HttpRequestException(
            "Response status code does not indicate success: 404 (Not Found). Response body: {\"error\":{\"code\":-32001,\"message\":\"Session not found\"}}",
            inner: null,
            HttpStatusCode.NotFound);

        Assert.True(McpStaleSessionDetector.IsStaleSessionFailure(exception));
    }

    [Fact]
    public void IsStaleSessionFailure_MatchesNestedCodeMinus32001()
    {
        var inner = new InvalidOperationException(
            "Response body: { \"error\": { \"code\": -32001, \"message\": \"gone\" } }");
        var exception = new HttpRequestException(
            "Response status code does not indicate success: 404 (Not Found).",
            inner,
            HttpStatusCode.NotFound);

        Assert.True(McpStaleSessionDetector.IsStaleSessionFailure(exception));
    }

    [Fact]
    public void IsStaleSessionFailure_DoesNotMatchPlain404()
    {
        var exception = new HttpRequestException(
            "Response status code does not indicate success: 404 (Not Found). Response body: route missing",
            inner: null,
            HttpStatusCode.NotFound);

        Assert.False(McpStaleSessionDetector.IsStaleSessionFailure(exception));
    }

    [Fact]
    public void IsStaleSessionFailure_MatchesPlain404WhenRequestHadSessionId()
    {
        var exception = new HttpRequestException(
            "Response status code does not indicate success: 404 (Not Found).",
            inner: null,
            HttpStatusCode.NotFound);

        Assert.True(McpStaleSessionDetector.IsStaleSessionFailure(exception, requestHadSessionId: true));
    }

    [Fact]
    public void IsStaleSessionFailure_DoesNotMatchSessionMessageWithout404()
    {
        var exception = new InvalidOperationException("Session not found");

        Assert.False(McpStaleSessionDetector.IsStaleSessionFailure(exception));
    }

    [Fact]
    public void IsStaleSessionFailure_DoesNotMatchNonStaleHttpFailures()
    {
        var exception = new HttpRequestException(
            "Response status code does not indicate success: 401 (Unauthorized).",
            inner: null,
            HttpStatusCode.Unauthorized);

        Assert.False(McpStaleSessionDetector.IsStaleSessionFailure(exception));
    }
}
