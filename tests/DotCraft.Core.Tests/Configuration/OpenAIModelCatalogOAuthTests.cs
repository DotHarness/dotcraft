using System.ClientModel.Primitives;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using Xunit;

#pragma warning disable OPENAI001

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
                { "slug": "hidden-model", "visibility": "hidden", "priority": 0 },
                { "slug": "remote-slow", "visibility": "list", "priority": 2, "use_responses_lite": false },
                { "slug": "remote-fast", "visibility": "list", "priority": 1, "use_responses_lite": true }
              ]
            }
            """));
        var provider = new OpenAIClientProvider(new FakeOpenAIAuthService(), handler);

        var result = await OpenAIModelCatalog.FetchAsync(
            Runtime(),
            openAIClientProvider: provider);

        Assert.True(result.Success);
        Assert.Equal(["remote-fast", "remote-slow"], result.Models.Select(model => model.Id));
        Assert.Single(handler.Requests);
        var request = handler.Requests[0];
        Assert.Equal("/backend-api/codex/models", request.Path);
        Assert.StartsWith("?client_version=", request.Query, StringComparison.Ordinal);
        Assert.Equal("Bearer access-token", request.Authorization);
        Assert.Equal("acct_test", request.Headers[OpenAIAuthConstants.AccountIdHeader]);
        Assert.Equal(OpenAIAuthConstants.Originator, request.Headers[OpenAIAuthConstants.OriginatorHeader]);
        Assert.True(ChatGptCodexModelCatalog.ResolveUseResponsesLite(
            Runtime(model: "remote-fast"), "acct_test"));
        Assert.False(ChatGptCodexModelCatalog.ResolveUseResponsesLite(
            Runtime(model: "remote-slow"), "acct_test"));
    }

    [Fact]
    public async Task ChatGptOAuthResolvesContextBudgetFieldByFieldForTheAuthenticatedAccount()
    {
        const string account = "acct_a";
        var handler = new RecordingHandler((HttpStatusCode.OK, """
            {
              "models": [
                { "slug": "gpt-6.2-sol", "visibility": "list", "context_window": 500000,
                  "max_context_window": 900000, "effective_context_window_percent": 92, "auto_compact_token_limit": 760000 },
                { "slug": "gpt-6.2-astra", "visibility": "list",
                  "max_context_window": "invalid", "effective_context_window_percent": 93, "auto_compact_token_limit": -1 },
                { "slug": "gpt-6.2-sol-preview", "visibility": "list", "context_window": 500000 }
              ]
            }
            """));
        var provider = new OpenAIClientProvider(new FakeOpenAIAuthService(account), handler);
        var metadata = (IProviderRuntimeMetadataResolver)provider;
        var runtime = Runtime(model: "gpt-6.2-sol");
        var astra = runtime with { Model = "gpt-6.2-astra" };
        var bundledSol = Assert.IsType<ProviderContextBudget>(metadata.Resolve(runtime).ContextBudget);
        var bundledAstra = Assert.IsType<ProviderContextBudget>(metadata.Resolve(astra).ContextBudget);

        await ((IModelCatalogProvider)provider).FetchModelsAsync(runtime, CancellationToken.None);

        Assert.Equal(new ProviderContextBudget(900_000, 92, 760_000), metadata.Resolve(runtime).ContextBudget);
        Assert.Equal(bundledAstra with { EffectiveContextWindowPercent = 93 }, metadata.Resolve(astra).ContextBudget);
        Assert.Null(metadata.Resolve(runtime with { Model = "gpt-6.2-sol-preview" }).ContextBudget);

        var otherAccount = (IProviderRuntimeMetadataResolver)new OpenAIClientProvider(
            new FakeOpenAIAuthService("acct_b"), new RecordingHandler());
        Assert.Equal(bundledSol, otherAccount.Resolve(runtime).ContextBudget);
        Assert.Equal(bundledSol, metadata.Resolve(runtime with { AccountId = account, IsRemote = true }).ContextBudget);
    }

    [Fact]
    public async Task ChatGptOAuthFetchesRemoteCatalogUsesAuthServiceAccountWhenRuntimeIsStale()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, """
            {
              "models": [
                { "slug": "remote-model", "visibility": "list", "priority": 1 }
              ]
            }
            """));
        var provider = new OpenAIClientProvider(new FakeOpenAIAuthService("acct_token"), handler);

        var result = await OpenAIModelCatalog.FetchAsync(
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
                    { "slug": "remote-model", "visibility": "list", "priority": 1 }
                  ]
                }
                """));
        var provider = new OpenAIClientProvider(auth, handler);

        var result = await OpenAIModelCatalog.FetchAsync(
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
        var runtime = Runtime();
        var firstHandler = new RecordingHandler((HttpStatusCode.OK, """
            {
              "models": [
                { "slug": "cached-model", "visibility": "list", "priority": 1, "max_context_window": 700000,
                  "effective_context_window_percent": 90, "auto_compact_token_limit": 600000 }
              ]
            }
            """));

        var first = await OpenAIModelCatalog.FetchAsync(
            runtime,
            openAIClientProvider: new OpenAIClientProvider(new FakeOpenAIAuthService(), firstHandler));

        Assert.True(first.Success);
        Assert.Single(firstHandler.Requests);

        var secondHandler = new RecordingHandler();
        var secondProvider = new OpenAIClientProvider(new FakeOpenAIAuthService(), secondHandler);
        var second = await OpenAIModelCatalog.FetchAsync(runtime, openAIClientProvider: secondProvider);

        Assert.True(second.Success);
        Assert.Equal(["cached-model"], second.Models.Select(model => model.Id));
        var metadata = (IProviderRuntimeMetadataResolver)secondProvider;
        Assert.Equal(
            new ProviderContextBudget(700_000, 90, 600_000),
            metadata.Resolve(runtime with { Model = "cached-model" }).ContextBudget);
        Assert.Empty(secondHandler.Requests);
    }

    [Fact]
    public async Task ChatGptOAuthFallsBackToBundledModelsWithoutCache()
    {
        var handler = new RecordingHandler((HttpStatusCode.InternalServerError, "{}"));

        var result = await OpenAIModelCatalog.FetchAsync(
            Runtime("acct_bundled"),
            openAIClientProvider: new OpenAIClientProvider(new FakeOpenAIAuthService("acct_bundled"), handler));

        Assert.True(result.Success);
        Assert.NotEmpty(result.Models);
    }

    [Fact]
    public async Task ChatGptOAuthKeepsFetchedServiceTiersWhenRefreshFailsWithoutDiskCache()
    {
        var handler = new RecordingHandler(
            (HttpStatusCode.OK, """
                {
                  "models": [
                    { "slug": "remembered-model", "visibility": "list", "priority": 1, "service_tiers": [{ "id": "ultrafast" }] }
                  ]
                }
                """),
            (HttpStatusCode.InternalServerError, "{}"));
        var provider = new OpenAIClientProvider(new FakeOpenAIAuthService("acct_refresh"), handler);
        var runtime = Runtime("acct_refresh", persistCache: false);

        await ((IModelCatalogProvider)provider).FetchModelsAsync(runtime, CancellationToken.None);
        var refreshed = await ((IModelCatalogProvider)provider).FetchModelsAsync(runtime, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        var model = Assert.Single(refreshed.Models);
        Assert.Equal("remembered-model", model.Id);
        Assert.Equal(["standard", "ultrafast"], model.SpeedModes);
    }

    [Fact]
    public async Task ChatGptOAuthAdvertisesAndSendsOnlyAccountServiceTiers()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, """
            {
              "models": [
                { "slug": "sol", "visibility": "list", "priority": 1, "service_tiers": [{ "id": "priority" }, { "id": "ultrafast" }] },
                { "slug": "astra", "visibility": "list", "priority": 2, "service_tiers": [{ "id": "priority" }] },
                { "slug": "plain", "visibility": "list", "priority": 3 }
              ]
            }
            """));
        var provider = new OpenAIClientProvider(new FakeOpenAIAuthService(), handler);

        var result = await ((IModelCatalogProvider)provider).FetchModelsAsync(Runtime(), CancellationToken.None);

        Assert.Equal(
            [["standard", "fast", "ultrafast"], ["standard", "fast"], ["standard"]],
            result.Models.Select(model => model.SpeedModes));
        Assert.Equal("ultrafast", RequestedServiceTier(Runtime(model: "sol"), "Ultrafast"));
        Assert.Null(RequestedServiceTier(Runtime(model: "astra"), "Ultrafast"));
        Assert.Equal("priority", RequestedServiceTier(Runtime(model: "astra"), "Fast"));
        Assert.Null(RequestedServiceTier(Runtime(model: "plain"), "Fast"));
    }

    [Fact]
    public async Task ChatGptOAuthSendsFetchedServiceTiersWithoutDiskCache()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, """
            {
              "models": [
                { "slug": "memory-model", "visibility": "list", "priority": 1, "service_tiers": [{ "id": "ultrafast" }] }
              ]
            }
            """));
        var provider = new OpenAIClientProvider(new FakeOpenAIAuthService("acct_memory"), handler);

        await ((IModelCatalogProvider)provider).FetchModelsAsync(
            Runtime("acct_memory", persistCache: false), CancellationToken.None);

        Assert.Equal(
            "ultrafast",
            RequestedServiceTier(Runtime("acct_memory", "memory-model", persistCache: false), "Ultrafast"));
    }

    [Theory]
    [InlineData("lite-model", true)]
    [InlineData("standard-model", false)]
    [InlineData("unmarked-model", false)]
    [InlineData("unlisted-model", false)]
    public async Task RuntimeResolution_SelectsResponsesDialectFromEachModel(string model, bool expectedLite)
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, """
            {
              "models": [
                { "slug": "lite-model", "visibility": "list", "priority": 1, "use_responses_lite": true },
                { "slug": "standard-model", "visibility": "list", "priority": 2, "use_responses_lite": false },
                { "slug": "unmarked-model", "visibility": "list", "priority": 3 }
              ]
            }
            """));
        var accountId = $"acct_runtime_{model}";
        var provider = new OpenAIClientProvider(new FakeOpenAIAuthService(accountId), handler);
        await OpenAIModelCatalog.FetchAsync(Runtime(accountId: accountId), openAIClientProvider: provider);

        var config = OAuthConfig(model);
        var registry = new ChatClientRegistry(provider);
        var main = registry.ResolveMainRuntime(config);
        Assert.Equal(expectedLite, main.UseResponsesLite);
        Assert.Equal(accountId, main.AccountId);

        config.ProviderPreferences["chatgpt"] = new ModelPreference { Model = "standard-model" };
        config.SubAgent.ProviderPreferences["chatgpt"] = new SubAgentModelPreference { Model = model };
        var standard = registry.ResolveMainRuntime(config);
        var subAgent = registry.ResolveSubAgentRuntime(config, standard.ProviderId, standard.Model);
        Assert.Equal(expectedLite, subAgent.UseResponsesLite);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private static string? RequestedServiceTier(EffectiveModelRuntime runtime, string speed)
    {
        using var client = new OpenAIFastModeChatClient(new NoopChatClient());
        using var scope = ProviderPipelineOptionsScope.Push(
            new ProviderPipelineOptions(runtime, null, null, false, speed, false, null));
        var factory = client.PrepareOptions(new ChatOptions())?.RawRepresentationFactory;
        if (factory is null)
            return null;
        var raw = Assert.IsType<CreateResponseOptions>(factory(client));
        using var json = JsonDocument.Parse(ModelReaderWriter.Write(raw).ToString());
        return json.RootElement.GetProperty("service_tier").GetString();
    }

    private AppConfig Config() => new()
    {
        GlobalConfigPath = Path.Combine(_tempRoot, "global", "config.json"),
        WorkspaceConfigPath = Path.Combine(_tempRoot, ".craft", "config.json")
    };

    private AppConfig OAuthConfig(string model)
    {
        var config = Config();
        config.ProviderId = "chatgpt";
        config.ProviderPreferences["chatgpt"] = new ModelPreference { Model = model };
        config.Providers["chatgpt"] = new AppConfig.ModelProviderConfig
        {
            DisplayName = "ChatGPT",
            Protocol = ModelProviderProtocols.OpenAIResponses,
            AuthMethod = ModelProviderAuthMethods.ChatGptOAuth
        };
        return config;
    }

    private EffectiveModelRuntime Runtime(
        string? accountId = "acct_test",
        string? model = null,
        bool persistCache = true) => new(
        ProviderId: "openai",
        Model: model ?? "runtime-model",
        Protocol: ModelProviderProtocols.OpenAIResponses,
        DisplayName: "OpenAI (ChatGPT)",
        ApiKey: string.Empty,
        EndPoint: ModelProviderDefaults.ChatGptBackendEndpoint,
        NetworkTimeoutSeconds: 30,
        MaxOutputTokens: null,
        Capabilities: ModelProviderCapabilities.ForProtocol(ModelProviderProtocols.OpenAIResponses),
        AuthMethod: ModelProviderAuthMethods.ChatGptOAuth,
        AccountId: accountId,
        ProviderStateDirectory: persistCache ? Path.Combine(_tempRoot, "global") : null);

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

    private sealed class NoopChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
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
