using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerOpenAiAuthStatusTests
{
    [Fact]
    public async Task IncludeTokenReturnsCurrentAccessToken()
    {
        var auth = new FakeOpenAIAuthService();

        var result = await RequestStatusAsync(auth, new { includeToken = true });

        Assert.True(result.GetProperty("loggedIn").GetBoolean());
        Assert.Equal("access-token", result.GetProperty("authToken").GetString());
        Assert.Equal([false], auth.TokenRequests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task StatusOmitsAccessTokenUnlessRequested(bool? includeToken)
    {
        var auth = new FakeOpenAIAuthService();

        var result = await RequestStatusAsync(auth, includeToken is null ? null : new { includeToken });

        Assert.True(result.GetProperty("loggedIn").GetBoolean());
        Assert.False(result.TryGetProperty("authToken", out _));
        Assert.Empty(auth.TokenRequests);
    }

    [Fact]
    public async Task RefreshTokenForcesRefresh()
    {
        var auth = new FakeOpenAIAuthService();

        var result = await RequestStatusAsync(auth, new { includeToken = true, refreshToken = true });

        Assert.Equal("refreshed-token", result.GetProperty("authToken").GetString());
        Assert.Equal([true], auth.TokenRequests);
    }

    [Fact]
    public async Task TokenFailureAnswersWithoutAccessToken()
    {
        var auth = new FakeOpenAIAuthService
        {
            Failure = new OpenAIAuthException(OpenAIAuthFailureReason.Network, "offline")
        };

        var result = await RequestStatusAsync(auth, new { includeToken = true, refreshToken = true });

        Assert.True(result.GetProperty("loggedIn").GetBoolean());
        Assert.False(result.TryGetProperty("authToken", out _));
    }

    [Fact]
    public async Task ModelServiceConnectionNeverReturnsAccessToken()
    {
        var auth = new FakeOpenAIAuthService();
        using var harness = new CoreAppServerTestHarness(openAIClientProvider: new OpenAIClientProvider(auth));
        var config = harness.Monitor.Current;
        config.ModelService = new AppConfig.ModelServiceConfig
            { Endpoint = "https://models.example/model-service", Token = "client-test" };
        config.Providers[config.ProviderId].RemoteAuthentication = new ProviderAuthenticationStatus(true);

        var result = await RequestStatusAsync(harness, new { includeToken = true, refreshToken = true });

        Assert.True(result.GetProperty("loggedIn").GetBoolean());
        Assert.False(result.TryGetProperty("authToken", out _));
        Assert.Empty(auth.TokenRequests);
    }

    private static async Task<JsonElement> RequestStatusAsync(FakeOpenAIAuthService auth, object? parameters)
    {
        using var harness = new CoreAppServerTestHarness(openAIClientProvider: new OpenAIClientProvider(auth));
        return await RequestStatusAsync(harness, parameters);
    }

    private static async Task<JsonElement> RequestStatusAsync(CoreAppServerTestHarness harness, object? parameters)
    {
        await harness.InitializeAsync();
        await harness.ExecuteRequestAsync(harness.BuildRequest("auth/openai/status", parameters));
        var response = Assert.Single(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        return response.RootElement.GetProperty("result");
    }

    private sealed class FakeOpenAIAuthService : IOpenAIAuthService
    {
        public List<bool> TokenRequests { get; } = [];

        public OpenAIAuthException? Failure { get; init; }

        public bool IsAuthenticated => true;

        public event Action<OpenAIAuthStatus>? LoggedIn
        {
            add { }
            remove { }
        }

        public event Action? LoggedOut
        {
            add { }
            remove { }
        }

        public OpenAIAuthStatus GetStatus() => new(true, "acct_test", "pro", "test@example.com", null, null);

        public Task<OpenAIAuthStatus> LoginAsync(
            bool openBrowser,
            Action<string>? onAuthorizationUrl,
            CancellationToken cancellationToken) =>
            Task.FromResult(GetStatus());

        public Task LogoutAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            TokenRequests.Add(forceRefresh);
            return Failure is null
                ? Task.FromResult(forceRefresh ? "refreshed-token" : "access-token")
                : Task.FromException<string>(Failure);
        }

        public string? GetAccountId() => "acct_test";
    }
}
