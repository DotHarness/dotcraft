using System.Net;
using System.Text;
using DotCraft.Agents;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Configuration;

public sealed class OpenAIModelCatalogOAuthTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"openai_oauth_models_{Guid.NewGuid():N}");

    public OpenAIModelCatalogOAuthTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public async Task ChatGptOAuthFetchesRemoteCatalogWithAuthHeadersAndFiltersPickerModels()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, """
            {
              "models": [
                { "slug": "hidden-model", "visibility": "hidden", "priority": 0, "minimal_client_version": [0, 124, 0] },
                { "slug": "remote-slow", "visibility": "list", "priority": 2, "minimal_client_version": "0.98.0" },
                { "slug": "remote-fast", "visibility": "list", "priority": 1, "minimal_client_version": "0.98.0" }
              ]
            }
            """));
        var provider = new OpenAIClientProvider(new FakeOpenAIAuthService(), handler);

        var result = await OpenAIModelCatalog.FetchAsync(
            Config(),
            Runtime(),
            openAIClientProvider: provider);

        Assert.True(result.Success);
        Assert.Equal(["remote-fast", "remote-slow"], result.Models.Select(model => model.Id));
        Assert.Single(handler.Requests);
        var request = handler.Requests[0];
        Assert.Equal("/backend-api/codex/models", request.Path);
        Assert.Equal("?client_version=0.144.0", request.Query);
        Assert.Equal("Bearer access-token", request.Authorization);
        Assert.Equal("acct_test", request.Headers[OpenAIAuthConstants.AccountIdHeader]);
        Assert.Equal(OpenAIAuthConstants.Originator, request.Headers[OpenAIAuthConstants.OriginatorHeader]);
    }

    [Fact]
    public async Task ChatGptOAuthFetchesRemoteCatalogUsesAuthServiceAccountWhenRuntimeIsStale()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, """
            {
              "models": [
                { "slug": "remote-model", "visibility": "list", "priority": 1, "minimal_client_version": "0.98.0" }
              ]
            }
            """));
        var provider = new OpenAIClientProvider(new FakeOpenAIAuthService("acct_token"), handler);

        var result = await OpenAIModelCatalog.FetchAsync(
            Config(),
            Runtime(accountId: "acct_config_stale"),
            openAIClientProvider: provider);

        Assert.True(result.Success);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("acct_token", request.Headers[OpenAIAuthConstants.AccountIdHeader]);
    }

    [Fact]
    public async Task ChatGptOAuthRefreshesTokenOnceOnUnauthorized()
    {
        var auth = new FakeOpenAIAuthService();
        var handler = new RecordingHandler(
            (HttpStatusCode.Unauthorized, "{}"),
            (HttpStatusCode.OK, """
                {
                  "models": [
                    { "slug": "remote-model", "visibility": "list", "priority": 1, "minimal_client_version": "0.98.0" }
                  ]
                }
                """));
        var provider = new OpenAIClientProvider(auth, handler);

        var result = await OpenAIModelCatalog.FetchAsync(
            Config(),
            Runtime(),
            openAIClientProvider: provider);

        Assert.True(result.Success);
        Assert.Equal(["remote-model"], result.Models.Select(model => model.Id));
        Assert.Equal([false, true], auth.ForceRefreshCalls);
        Assert.Equal("Bearer access-token", handler.Requests[0].Authorization);
        Assert.Equal("Bearer refreshed-token", handler.Requests[1].Authorization);
    }

    [Fact]
    public async Task ChatGptOAuthUsesFreshCacheBeforeFetchingRemoteCatalog()
    {
        var config = Config();
        var runtime = Runtime();
        var firstHandler = new RecordingHandler((HttpStatusCode.OK, """
            {
              "models": [
                { "slug": "cached-model", "visibility": "list", "priority": 1, "minimal_client_version": "0.98.0" }
              ]
            }
            """));

        var first = await OpenAIModelCatalog.FetchAsync(
            config,
            runtime,
            openAIClientProvider: new OpenAIClientProvider(new FakeOpenAIAuthService(), firstHandler));

        Assert.True(first.Success);
        Assert.Single(firstHandler.Requests);

        var secondHandler = new RecordingHandler();
        var second = await OpenAIModelCatalog.FetchAsync(
            config,
            runtime,
            openAIClientProvider: new OpenAIClientProvider(new FakeOpenAIAuthService(), secondHandler));

        Assert.True(second.Success);
        Assert.Equal(["cached-model"], second.Models.Select(model => model.Id));
        Assert.Empty(secondHandler.Requests);
    }

    [Fact]
    public async Task ChatGptOAuthFallsBackToBundledModelsWithoutCache()
    {
        var handler = new RecordingHandler((HttpStatusCode.InternalServerError, "{}"));

        var result = await OpenAIModelCatalog.FetchAsync(
            Config(),
            Runtime(),
            openAIClientProvider: new OpenAIClientProvider(new FakeOpenAIAuthService(), handler));

        var ids = result.Models.Select(model => model.Id).ToArray();
        Assert.True(result.Success);
        Assert.Contains(ModelProviderDefaults.DefaultChatGptCodexModel, ids);
        Assert.Contains("gpt-5.4", ids);
        Assert.DoesNotContain("gpt-5", ids);
        Assert.DoesNotContain("gpt-5-codex", ids);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private AppConfig Config() => new()
    {
        GlobalConfigPath = Path.Combine(_tempRoot, "global", "config.json"),
        WorkspaceConfigPath = Path.Combine(_tempRoot, ".craft", "config.json")
    };

    private static EffectiveModelRuntime Runtime(string? accountId = "acct_test") => new(
        ProviderId: "openai",
        Model: ModelProviderDefaults.DefaultChatGptCodexModel,
        Protocol: ModelProviderProtocols.OpenAIResponses,
        DisplayName: "OpenAI (ChatGPT)",
        ApiKey: string.Empty,
        EndPoint: ModelProviderDefaults.ChatGptBackendEndpoint,
        NetworkTimeoutSeconds: 30,
        MaxOutputTokens: null,
        IsImplicit: false,
        Capabilities: ModelProviderCapabilities.ForProtocol(ModelProviderProtocols.OpenAIResponses),
        AuthMethod: ModelProviderAuthMethods.ChatGptOAuth,
        ChatGptAccountId: accountId);

    private sealed class FakeOpenAIAuthService(string accountId = "acct_test") : IOpenAIAuthService
    {
        public List<bool> ForceRefreshCalls { get; } = [];

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

        public OpenAIAuthStatus GetStatus() => new(
            true,
            accountId,
            "pro",
            "test@example.com",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1));

        public Task<OpenAIAuthStatus> LoginAsync(
            bool openBrowser,
            Action<string>? onAuthorizationUrl,
            CancellationToken cancellationToken) =>
            Task.FromResult(GetStatus());

        public Task LogoutAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            ForceRefreshCalls.Add(forceRefresh);
            return Task.FromResult(forceRefresh ? "refreshed-token" : "access-token");
        }

        public string? GetAccountId() => accountId;
    }

    private sealed class RecordingHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new(responses);

        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(RecordedRequest.From(request));
            if (_responses.Count == 0)
                throw new HttpRequestException("No test response queued.");

            var (status, body) = _responses.Dequeue();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed record RecordedRequest(
        string Path,
        string Query,
        string? Authorization,
        Dictionary<string, string> Headers)
    {
        public static RecordedRequest From(HttpRequestMessage request)
        {
            var headers = request.Headers.ToDictionary(
                header => header.Key,
                header => string.Join(",", header.Value),
                StringComparer.OrdinalIgnoreCase);
            return new RecordedRequest(
                request.RequestUri?.AbsolutePath ?? string.Empty,
                request.RequestUri?.Query ?? string.Empty,
                request.Headers.Authorization?.ToString(),
                headers);
        }
    }
}
