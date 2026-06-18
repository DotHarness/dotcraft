using System.Net;
using DotCraft.Auth.OpenAI;

namespace DotCraft.Tests.Auth.OpenAI;

public sealed class OpenAIAuthManagerTests : IDisposable
{
    private readonly string _tempDir;

    public OpenAIAuthManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dotcraft-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    [Fact]
    public void NewManagerWithoutAuthJsonReportsNotSignedIn()
    {
        var manager = new OpenAIAuthManager(new OpenAITokenStore(_tempDir), new HttpClient(new FailingHandler()));
        Assert.False(manager.IsAuthenticated);
        Assert.False(manager.GetStatus().LoggedIn);
    }

    [Fact]
    public async Task GetAccessTokenThrowsWhenNotSignedIn()
    {
        var manager = new OpenAIAuthManager(new OpenAITokenStore(_tempDir), new HttpClient(new FailingHandler()));
        var ex = await Assert.ThrowsAsync<OpenAIAuthException>(() =>
            manager.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None));
        Assert.Equal(OpenAIAuthFailureReason.NotSignedIn, ex.Reason);
    }

    [Fact]
    public async Task GetAccessTokenReturnsCachedTokenWhenFresh()
    {
        var store = new OpenAITokenStore(_tempDir);
        store.Save(new AuthDotJson
        {
            Tokens = new OpenAITokenSet
            {
                IdToken = "id",
                AccessToken = "access-1",
                RefreshToken = "refresh-1"
            },
            LastRefresh = DateTimeOffset.UtcNow.AddMinutes(-1)
        });

        var handler = new RecordingHandler();
        var manager = new OpenAIAuthManager(store, new HttpClient(handler));
        var token = await manager.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);

        Assert.Equal("access-1", token);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetAccessTokenRefreshesWhenLastRefreshIsOld()
    {
        var store = new OpenAITokenStore(_tempDir);
        store.Save(new AuthDotJson
        {
            Tokens = new OpenAITokenSet
            {
                IdToken = "id",
                AccessToken = "old-access",
                RefreshToken = "refresh-1"
            },
            LastRefresh = DateTimeOffset.UtcNow.AddHours(-9)
        });

        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                { "id_token": "id-2", "access_token": "new-access", "refresh_token": "refresh-2" }
                """, System.Text.Encoding.UTF8, "application/json")
        });
        var manager = new OpenAIAuthManager(store, new HttpClient(handler));

        var token = await manager.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);
        Assert.Equal("new-access", token);
        Assert.Single(handler.Requests);
        var sent = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal(OpenAIAuthConstants.TokenUrl, sent.RequestUri!.ToString());
    }

    [Fact]
    public async Task RefreshFailureWithExpiredErrorCodeMapsToReason()
    {
        var store = new OpenAITokenStore(_tempDir);
        store.Save(new AuthDotJson
        {
            Tokens = new OpenAITokenSet { IdToken = "id", AccessToken = "a", RefreshToken = "r" },
            LastRefresh = DateTimeOffset.UtcNow.AddHours(-9)
        });

        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""
                { "error": { "code": "refresh_token_expired", "message": "expired" } }
                """, System.Text.Encoding.UTF8, "application/json")
        });
        var manager = new OpenAIAuthManager(store, new HttpClient(handler));
        var ex = await Assert.ThrowsAsync<OpenAIAuthException>(() =>
            manager.GetAccessTokenAsync(forceRefresh: true, CancellationToken.None));
        Assert.Equal(OpenAIAuthFailureReason.RefreshTokenExpired, ex.Reason);
    }

    [Fact]
    public async Task LogoutDeletesAuthJsonEvenWhenRevokeFails()
    {
        var store = new OpenAITokenStore(_tempDir);
        store.Save(new AuthDotJson
        {
            Tokens = new OpenAITokenSet { IdToken = "id", AccessToken = "a", RefreshToken = "r" }
        });

        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var manager = new OpenAIAuthManager(store, new HttpClient(handler));

        await manager.LogoutAsync(CancellationToken.None);
        Assert.False(File.Exists(store.FilePath));
        Assert.False(manager.IsAuthenticated);
    }

    [Fact]
    public async Task RefreshPersistsRotatedRefreshTokenAndUpdatesLastRefresh()
    {
        var store = new OpenAITokenStore(_tempDir);
        var initialRefresh = DateTimeOffset.UtcNow.AddHours(-9);
        store.Save(new AuthDotJson
        {
            Tokens = new OpenAITokenSet { IdToken = "id", AccessToken = "old", RefreshToken = "refresh-1" },
            LastRefresh = initialRefresh
        });

        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                { "id_token": "id-2", "access_token": "new", "refresh_token": "refresh-2" }
                """, System.Text.Encoding.UTF8, "application/json")
        });
        var manager = new OpenAIAuthManager(store, new HttpClient(handler));
        await manager.GetAccessTokenAsync(forceRefresh: true, CancellationToken.None);

        var reloaded = store.Load();
        Assert.NotNull(reloaded);
        Assert.Equal("refresh-2", reloaded!.Tokens!.RefreshToken);
        Assert.Equal("new", reloaded.Tokens.AccessToken);
        Assert.True(reloaded.LastRefresh > initialRefresh);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public RecordingHandler() : this(_ => new HttpResponseMessage(HttpStatusCode.OK)) { }
        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) { _responder = responder; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("HTTP client should not have been used.");
        }
    }
}
