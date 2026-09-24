using System.Net;
using DotCraft.Auth.OpenAI;
using Xunit;

namespace DotCraft.Tests.Auth.OpenAI;

public sealed class LoopbackOAuthServerTests
{
    private const string ExpectedState = "expected-state";

    [Fact]
    public async Task CallbacksWithoutTheExpectedStateDoNotEndSignIn()
    {
        using var server = LoopbackOAuthServer.Start();
        var result = server.AwaitCallbackAsync(ExpectedState, CancellationToken.None);
        using var http = CreateClient();

        var probe = await http.GetAsync(server.RedirectUri);
        var foreign = await http.GetAsync($"{server.RedirectUri}?state=other&error=access_denied");

        Assert.Equal(HttpStatusCode.BadRequest, probe.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.False(result.IsCompleted);

        var callback = await http.GetAsync($"{server.RedirectUri}?state={ExpectedState}&code=auth-code");
        var outcome = await result.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        Assert.True(outcome.Success);
        Assert.Equal("auth-code", outcome.AuthorizationCode);
    }

    [Fact]
    public async Task ProviderErrorWithTheExpectedStateEndsSignIn()
    {
        using var server = LoopbackOAuthServer.Start();
        var result = server.AwaitCallbackAsync(ExpectedState, CancellationToken.None);
        using var http = CreateClient();

        await http.GetAsync($"{server.RedirectUri}?state={ExpectedState}&error=access_denied");
        var outcome = await result.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(outcome.Success);
        Assert.Equal("access_denied", outcome.Error);
    }

    private static HttpClient CreateClient() => new(new HttpClientHandler { UseProxy = false });
}
