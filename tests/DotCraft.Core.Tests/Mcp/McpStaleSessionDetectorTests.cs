using DotCraft.Mcp;
using System.Net;
using Xunit;

namespace DotCraft.Tests.Mcp;

public sealed class McpStaleSessionDetectorTests
{
    [Fact]
    public void IsStaleSessionFailure_MatchesA404WithSessionNotFoundOrAKnownSession()
    {
        var bodyMessage = new HttpRequestException(
            "Response status code does not indicate success: 404 (Not Found). Response body: {\"error\":{\"code\":-32001,\"message\":\"Session not found\"}}",
            inner: null,
            HttpStatusCode.NotFound);
        var nestedCode = new HttpRequestException(
            "Response status code does not indicate success: 404 (Not Found).",
            new InvalidOperationException("Response body: { \"error\": { \"code\": -32001, \"message\": \"gone\" } }"),
            HttpStatusCode.NotFound);
        var plain404 = new HttpRequestException(
            "Response status code does not indicate success: 404 (Not Found).",
            inner: null,
            HttpStatusCode.NotFound);

        Assert.True(McpStaleSessionDetector.IsStaleSessionFailure(bodyMessage));
        Assert.True(McpStaleSessionDetector.IsStaleSessionFailure(nestedCode));
        Assert.True(McpStaleSessionDetector.IsStaleSessionFailure(plain404, requestHadSessionId: true));
    }

    [Fact]
    public void IsStaleSessionFailure_DoesNotMatchOther404sSessionMessagesOrStatuses()
    {
        var routeMissing = new HttpRequestException(
            "Response status code does not indicate success: 404 (Not Found). Response body: route missing",
            inner: null,
            HttpStatusCode.NotFound);
        var unauthorized = new HttpRequestException(
            "Response status code does not indicate success: 401 (Unauthorized).",
            inner: null,
            HttpStatusCode.Unauthorized);

        Assert.False(McpStaleSessionDetector.IsStaleSessionFailure(routeMissing));
        Assert.False(McpStaleSessionDetector.IsStaleSessionFailure(new InvalidOperationException("Session not found")));
        Assert.False(McpStaleSessionDetector.IsStaleSessionFailure(unauthorized));
    }
}
