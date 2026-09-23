using System.Net;
using System.Net.Http.Json;
using DotCraft.Agents.Remote;
using DotCraft.Auth.OpenAI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DotCraft.ModelService.Tests;

public sealed class SharedSubscriptionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dotcraft-auth-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ConcurrentExpiredRequestsRotateOnceAndRecreatedServiceUsesPersistedToken()
    {
        var store = new OpenAITokenStore(_directory);
        store.Save(Auth("old", DateTimeOffset.UtcNow.AddDays(-20)));
        var calls = 0;
        using var client = new HttpClient(new Handler(async (_, cancellationToken) =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(20, cancellationToken);
            return Refresh("fresh", "rotated");
        }));
        var manager = new OpenAIAuthManager(store, client);
        var values = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => manager.GetAccessTokenAsync(false, CancellationToken.None)));
        Assert.All(values, value => Assert.Equal("fresh", value));
        Assert.Equal(1, calls);
        Assert.Equal("rotated", store.Load()!.Tokens!.RefreshToken);
        var recreated = new OpenAIAuthManager(new OpenAITokenStore(_directory), client);
        Assert.Equal("fresh", await recreated.GetAccessTokenAsync(false, CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task TwoRemoteClientsRecoverUnauthorizedResponsesUsingOnePersistedRotation()
    {
        var store = new OpenAITokenStore(Path.Combine(_directory, "credentials"));
        store.Save(Auth("rejected", DateTimeOffset.UtcNow));
        await File.WriteAllTextAsync(Path.Combine(_directory, "config.json"), """
            {"Providers":{"primary":{"Protocol":"openai-responses","AuthMethod":"chatgptOAuth","EndPoint":"https://upstream.example/v1"}}}
            """);
        var grants = new ModelServiceClientStore(_directory);
        var first = grants.Create("first", ["primary"]);
        var second = grants.Create("second", ["primary"]);
        var calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(Refresh("fresh", "rotated"));
        }));
        var manager = new OpenAIAuthManager(store, client);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IModelServiceAccess>(new FileModelServiceAccess(_directory, manager));
        builder.Services.AddDotCraftModelService();
        var rejected = 0;
        var bothRejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        builder.Services.AddHttpClient("DotCraft.ModelService.Upstream")
            .ConfigurePrimaryHttpMessageHandler(() => new Handler(async (request, cancellationToken) =>
            {
                Assert.Equal("""{"input":"hello"}""", await request.Content!.ReadAsStringAsync(cancellationToken));
                if (request.Headers.Authorization?.Parameter == "rejected")
                {
                    if (Interlocked.Increment(ref rejected) == 2)
                        bothRejected.TrySetResult();
                    await bothRejected.Task.WaitAsync(cancellationToken);
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                }
                Assert.Equal("fresh", request.Headers.Authorization?.Parameter);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("completed") };
            }));
        await using var app = builder.Build();
        app.MapDotCraftModelService();
        await app.StartAsync();
        using var http = app.GetTestClient();
        using var runtimeA = new RemoteProviderTransport(new(new Uri("http://localhost/model-service/"), first.Token), http);
        using var runtimeB = new RemoteProviderTransport(new(new Uri("http://localhost/model-service/"), second.Token), http);
        var results = await Task.WhenAll(new[] { runtimeA, runtimeB }.Select(async runtime =>
        {
            using var response = await runtime.CreateClient("primary", new Uri("https://upstream.example/v1"))
                .PostAsync("https://upstream.example/v1/responses", new StringContent("""{"input":"hello"}"""));
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        })).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, calls);
        Assert.All(results, result => Assert.Equal("completed", result));
        Assert.Equal("rotated", store.Load()!.Tokens!.RefreshToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InFlightRefreshCannotOverwriteLogoutOrNewLogin(bool loginAgain)
    {
        var store = new OpenAITokenStore(_directory);
        store.Save(Auth("old", DateTimeOffset.UtcNow.AddDays(-20)));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new Handler(async (_, cancellationToken) =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(cancellationToken);
            return Refresh("stale-refresh", "stale-rotation");
        }));
        var manager = new OpenAIAuthManager(store, client);
        var pending = manager.GetAccessTokenAsync(false, CancellationToken.None);
        await entered.Task;
        var otherStore = new OpenAITokenStore(_directory);
        if (loginAgain)
            otherStore.Save(Auth("new-login", DateTimeOffset.UtcNow));
        else
            otherStore.Delete();
        release.SetResult();
        if (loginAgain)
        {
            Assert.Equal("new-login", await pending);
            Assert.Equal("new-login", store.Load()!.Tokens!.AccessToken);
        }
        else
        {
            var error = await Assert.ThrowsAsync<OpenAIAuthException>(() => pending);
            Assert.Equal(OpenAIAuthFailureReason.NotSignedIn, error.Reason);
            Assert.False(manager.GetStatus().LoggedIn);
        }
    }

    [Fact]
    public async Task RevokedRefreshRequiresNewLoginAndRetainsNoUsableToken()
    {
        var store = new OpenAITokenStore(_directory);
        store.Save(Auth("old", DateTimeOffset.UtcNow.AddDays(-20)));
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = JsonContent.Create(new { error = "refresh_token_reused" })
        })));
        var manager = new OpenAIAuthManager(store, client);
        var error = await Assert.ThrowsAsync<OpenAIAuthException>(() => manager.GetAccessTokenAsync(false, CancellationToken.None));
        Assert.Equal(OpenAIAuthFailureReason.RefreshTokenReused, error.Reason);
        Assert.False(manager.IsAuthenticated);
        store.Save(Auth("new-login", DateTimeOffset.UtcNow));
        Assert.Equal("new-login", await manager.GetAccessTokenAsync(false, CancellationToken.None));
    }

    private static AuthDotJson Auth(string token, DateTimeOffset refreshed) => new()
    {
        Tokens = new OpenAITokenSet { AccessToken = token, RefreshToken = "refresh-" + token, IdToken = "e30.e30." },
        LastRefresh = refreshed
    };

    private static HttpResponseMessage Refresh(string token, string refresh) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new { access_token = token, refresh_token = refresh })
    };

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
