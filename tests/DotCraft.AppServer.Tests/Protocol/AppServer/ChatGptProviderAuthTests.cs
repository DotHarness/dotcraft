using System.Text.Json;
using DotCraft.Agents;
using DotCraft.AppServer;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using DotCraft.Contributions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class ChatGptProviderAuthTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("chatgpt-provider-auth-").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task LoginReportsAuthorizeUrlAndCompletesOnCallback()
    {
        var auth = new FakeOpenAIAuthService(signedIn: false);
        await using var host = Host(auth, Config());
        await host.Harness.InitializeAsync();

        var login = await Result(host.Harness, "provider/auth/login", new { providerId = "openai" });
        var loginId = login.GetProperty("loginId").GetString()!;
        var pending = await WaitState(host.Harness, loginId, state => state.TryGetProperty("event", out _));
        var notification = pending.GetProperty("event");
        Assert.Equal("auth_url", notification.GetProperty("kind").GetString());
        Assert.Equal(FakeOpenAIAuthService.AuthorizeUrl, notification.GetProperty("url").GetString());
        Assert.Equal([false], auth.OpenBrowserRequests);

        auth.CompleteCallback();
        var completed = await WaitState(host.Harness, loginId, state => state.GetProperty("status").GetString() == "completed");
        Assert.Equal("acct_test", completed.GetProperty("authentication").GetProperty("accountId").GetString());
        Assert.True((await Result(host.Harness, "provider/auth/status", new { providerId = "openai" }))
            .GetProperty("isAuthenticated").GetBoolean());
    }

    [Fact]
    public async Task ChatGptSignInIsSharedByEveryChatGptProvider()
    {
        var auth = new FakeOpenAIAuthService(signedIn: false);
        var config = Config();
        config.Providers["work"] = new AppConfig.ModelProviderConfig
        {
            Protocol = ModelProviderProtocols.OpenAIResponses,
            AuthMethod = ModelProviderAuthMethods.ChatGptOAuth
        };
        await using var host = Host(auth, config);

        host.Authentication.Login(config, "openai", null, "owner");
        var second = Assert.Throws<ProviderAuthenticationException>(() => host.Authentication.Login(config, "work", null, "owner"));
        Assert.Equal("ProviderLoginInProgress", second.Code);
        var apiKey = Assert.Throws<ProviderAuthenticationException>(() => host.Authentication.Login(config, "keyed", null, "owner"));
        Assert.Equal("ProviderLoginNotSupported", apiKey.Code);

        await host.Authentication.LogoutAsync(config, "work", CancellationToken.None);
        Assert.Equal(1, auth.LogoutCount);
        Assert.False((await host.Authentication.GetStatusAsync(config, "openai", CancellationToken.None)).IsAuthenticated);
    }

    [Fact]
    public async Task StatusReturnsAccessTokenOnlyWhenRequested()
    {
        var auth = new FakeOpenAIAuthService();
        await using var host = Host(auth, Config());
        await host.Harness.InitializeAsync();

        foreach (var (includeToken, refreshToken, expected) in new (bool?, bool?, string?)[]
                 {
                     (null, null, null),
                     (false, false, null),
                     (true, false, "access-token"),
                     (true, true, "refreshed-token")
                 })
        {
            auth.TokenRequests.Clear();
            var parameters = new Dictionary<string, object> { ["providerId"] = "openai" };
            if (includeToken is { } include) parameters["includeToken"] = include;
            if (refreshToken is { } refresh) parameters["refreshToken"] = refresh;

            var result = await Result(host.Harness, "provider/auth/status", parameters);

            Assert.True(result.GetProperty("isAuthenticated").GetBoolean());
            Assert.Equal("pro", result.GetProperty("planType").GetString());
            Assert.Equal(expected, result.TryGetProperty("authToken", out var token) ? token.GetString() : null);
            bool[] expectedRequests = includeToken == true ? [refreshToken == true] : [];
            Assert.Equal(expectedRequests, auth.TokenRequests);
        }
    }

    [Fact]
    public async Task TokenRefreshFailureAnswersWithoutAccessToken()
    {
        var auth = new FakeOpenAIAuthService { Failure = new OpenAIAuthException(OpenAIAuthFailureReason.Network, "offline") };
        await using var host = Host(auth, Config());
        await host.Harness.InitializeAsync();

        var result = await Result(host.Harness, "provider/auth/status", new { providerId = "openai", includeToken = true, refreshToken = true });

        Assert.True(result.GetProperty("isAuthenticated").GetBoolean());
        Assert.False(result.TryGetProperty("authToken", out _));
    }

    [Fact]
    public async Task ModelServiceProvidersReportRemoteStateAndRejectSignIn()
    {
        var auth = new FakeOpenAIAuthService();
        var config = Config();
        config.ModelService = new AppConfig.ModelServiceConfig { Endpoint = "https://models.example/model-service", Token = "client-test" };
        config.Providers["openai"].RemoteAuthentication = new ProviderAuthenticationStatus(true, "acct_remote", PlanType: "team");
        await using var host = Host(auth, config);
        await host.Harness.InitializeAsync();

        var status = await Result(host.Harness, "provider/auth/status", new { providerId = "openai", includeToken = true, refreshToken = true });
        Assert.Equal("acct_remote", status.GetProperty("accountId").GetString());
        Assert.False(status.TryGetProperty("authToken", out _));
        Assert.Empty(auth.TokenRequests);
        Assert.False((await Result(host.Harness, "provider/auth/usage", new { providerId = "openai" })).GetProperty("available").GetBoolean());

        foreach (var method in new[] { "provider/auth/login", "provider/auth/logout" })
        {
            var error = await Error(host.Harness, method, new { providerId = "openai" });
            Assert.Equal(AppServerErrors.InvalidRequestCode, error.GetProperty("code").GetInt32());
            Assert.Equal("ProviderManagedByModelService", error.GetProperty("data").GetProperty("code").GetString());
        }
        Assert.Equal(0, auth.LogoutCount);
    }

    [Fact]
    public async Task UsageIsTypedForChatGptProvidersOnly()
    {
        var resetAt = DateTimeOffset.UtcNow.AddHours(2);
        var usage = new FakeUsageService(new OpenAIUsageSnapshot(
            "pro",
            new RateLimitWindow(42, TimeSpan.FromHours(5), resetAt),
            null,
            new CreditStatus(true, false, "12.50"),
            "primary",
            DateTimeOffset.UtcNow));
        await using var host = Host(new FakeOpenAIAuthService(), Config(), usage);
        await host.Harness.InitializeAsync();

        var result = await Result(host.Harness, "provider/auth/usage", new { providerId = "openai" });
        Assert.True(result.GetProperty("available").GetBoolean());
        Assert.Equal("pro", result.GetProperty("planType").GetString());
        Assert.Equal(42, result.GetProperty("primary").GetProperty("usedPercent").GetInt32());
        Assert.Equal(18_000, result.GetProperty("primary").GetProperty("windowSeconds").GetInt32());
        Assert.False(result.TryGetProperty("secondary", out _));
        Assert.Equal("12.50", result.GetProperty("credits").GetProperty("balance").GetString());
        Assert.Equal("primary", result.GetProperty("limitReachedKind").GetString());
        Assert.False((await Result(host.Harness, "provider/auth/usage", new { providerId = "keyed" })).GetProperty("available").GetBoolean());
    }

    [Fact]
    public void UsageUpdatesTargetEveryChatGptProvider()
    {
        var config = Config();
        config.Providers["work"] = new AppConfig.ModelProviderConfig
        {
            Protocol = ModelProviderProtocols.OpenAIResponses,
            AuthMethod = ModelProviderAuthMethods.ChatGptOAuth
        };
        config.Providers["plugin"] = new AppConfig.ModelProviderConfig
        {
            BackendId = "demo",
            AuthMethod = ModelProviderAuthMethods.ChatGptOAuth
        };
        var usage = new ProviderUsageSnapshot(DateTimeOffset.UtcNow, new Dictionary<string, double>(), PlanType: "pro");

        var notifications = ProviderUsageMapping.ToChatGptNotifications(config, usage);
        var cleared = ProviderUsageMapping.ToChatGptNotifications(config, null);

        Assert.Equal(["openai", "work"], notifications.Select(item => item.ProviderId).Order());
        Assert.All(notifications, item => Assert.Equal("pro", item.PlanType));
        Assert.All(cleared, item => Assert.False(item.Available));
    }

    private TestHost Host(FakeOpenAIAuthService auth, AppConfig config, IOpenAIUsageService? usage = null)
    {
        var provider = new OpenAIClientProvider(auth, openAIUsageService: usage);
        var resolver = new ModelBackendResolver(
            new ModelProviderRegistry([provider, new AnthropicClientProvider()]), new ContributionRegistry(), _root);
        var authentication = new ProviderAuthenticationCoordinator(resolver);
        var harness = new AppServerTestHarness(appConfigMonitor: new AppConfigMonitor(config), openAIClientProvider: provider,
            modelRuntimeResolver: resolver, providerAuthentication: authentication);
        return new TestHost(harness, resolver, authentication);
    }

    private AppConfig Config() => new()
    {
        GlobalConfigPath = Path.Combine(_root, "config.json"),
        ProviderId = "openai",
        Providers = new(StringComparer.OrdinalIgnoreCase)
        {
            ["openai"] = new() { Protocol = ModelProviderProtocols.OpenAIResponses, AuthMethod = ModelProviderAuthMethods.ChatGptOAuth },
            ["keyed"] = new() { Protocol = ModelProviderProtocols.OpenAIResponses, ApiKey = "sk-test" }
        }
    };

    private static async Task<JsonElement> WaitState(AppServerTestHarness harness, string id, Func<JsonElement, bool> predicate)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var state = await Result(harness, "provider/auth/state", new { loginId = id });
            if (predicate(state)) return state;
            await Task.Delay(10);
        }
        throw new TimeoutException(id);
    }

    private static async Task<JsonElement> Result(AppServerTestHarness harness, string method, object parameters)
    {
        var response = await Send(harness, method, parameters);
        Assert.False(response.TryGetProperty("error", out _), response.GetRawText());
        return response.GetProperty("result");
    }

    private static async Task<JsonElement> Error(AppServerTestHarness harness, string method, object parameters) =>
        (await Send(harness, method, parameters)).GetProperty("error");

    private static async Task<JsonElement> Send(AppServerTestHarness harness, string method, object parameters)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(method, parameters));
        while (true)
        {
            using var message = await harness.Transport.ReadNextSentAsync(TimeSpan.FromSeconds(5));
            if (message.RootElement.TryGetProperty("id", out _))
                return message.RootElement.Clone();
        }
    }

    private sealed record TestHost(
        AppServerTestHarness Harness,
        ModelBackendResolver Resolver,
        ProviderAuthenticationCoordinator Authentication) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Harness.Dispose();
            await Authentication.DisposeAsync();
            Resolver.Dispose();
        }
    }

    private sealed class FakeUsageService(OpenAIUsageSnapshot snapshot) : IOpenAIUsageService
    {
        public OpenAIUsageSnapshot? CurrentSnapshot => snapshot;

        public Task<OpenAIUsageSnapshot?> RefreshAsync(CancellationToken cancellationToken) =>
            Task.FromResult<OpenAIUsageSnapshot?>(snapshot);

        public event Action<OpenAIUsageSnapshot?>? SnapshotChanged
        {
            add { }
            remove { }
        }
    }

    internal sealed class FakeOpenAIAuthService(bool signedIn = true) : IOpenAIAuthService
    {
        public const string AuthorizeUrl = "https://auth.example/oauth/authorize?redirect_uri=http%3A%2F%2Flocalhost%3A1455%2Fauth%2Fcallback";
        private readonly TaskCompletionSource _callback = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _signedIn = signedIn;

        public List<bool> TokenRequests { get; } = [];
        public List<bool> OpenBrowserRequests { get; } = [];
        public int LogoutCount { get; private set; }
        public OpenAIAuthException? Failure { get; init; }
        public bool IsAuthenticated => _signedIn;

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

        public void CompleteCallback() => _callback.TrySetResult();

        public OpenAIAuthStatus GetStatus() => _signedIn
            ? new(true, "acct_test", "pro", "test@example.com", null, null)
            : new(false, null, null, null, null, null);

        public async Task<OpenAIAuthStatus> LoginAsync(
            bool openBrowser,
            Action<string>? onAuthorizationUrl,
            CancellationToken cancellationToken)
        {
            OpenBrowserRequests.Add(openBrowser);
            onAuthorizationUrl?.Invoke(AuthorizeUrl);
            await _callback.Task.WaitAsync(cancellationToken);
            _signedIn = true;
            return GetStatus();
        }

        public Task LogoutAsync(CancellationToken cancellationToken)
        {
            LogoutCount++;
            _signedIn = false;
            return Task.CompletedTask;
        }

        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            TokenRequests.Add(forceRefresh);
            return Failure is null
                ? Task.FromResult(forceRefresh ? "refreshed-token" : "access-token")
                : Task.FromException<string>(Failure);
        }

        public string? GetAccountId() => _signedIn ? "acct_test" : null;
    }
}
