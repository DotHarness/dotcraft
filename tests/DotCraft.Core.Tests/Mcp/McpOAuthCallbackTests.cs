using System.Collections.Specialized;
using DotCraft.Mcp;
using Xunit;

namespace DotCraft.Tests.Mcp;

public sealed class McpOAuthCallbackTests
{
    [Fact]
    public void ParseAuthorizationResult_PreservesCodeStateAndIssuer()
    {
        var result = McpOAuthLoginCoordinator.ParseAuthorizationResult(new NameValueCollection
        {
            ["code"] = "code-1",
            ["state"] = "state-1",
            ["iss"] = "https://issuer.example"
        });

        Assert.Equal("code-1", result.Code);
        Assert.Equal("state-1", result.State);
        Assert.Equal("https://issuer.example", result.Iss);
    }

    [Theory]
    [InlineData(null, "state-1", null, "authorization code")]
    [InlineData("code-1", null, null, "state")]
    [InlineData("code-1", "state-1", "access_denied", "access_denied")]
    public void ParseAuthorizationResult_RejectsInvalidCallback(
        string? code,
        string? state,
        string? error,
        string expectedMessage)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            McpOAuthLoginCoordinator.ParseAuthorizationResult(new NameValueCollection
            {
                ["code"] = code,
                ["state"] = state,
                ["error"] = error
            }));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }
}
