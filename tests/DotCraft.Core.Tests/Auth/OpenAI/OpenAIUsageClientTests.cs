using System.Net;
using System.Text;
using DotCraft.Auth.OpenAI;

namespace DotCraft.Tests.Auth.OpenAI;

public sealed class OpenAIUsageClientTests : IDisposable
{
    private readonly string _tempDir;

    public OpenAIUsageClientTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dotcraft-usage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    [Fact]
    public async Task FetchAsyncParsesFullPayload()
    {
        var resetAt = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds();
        var json = $$"""
        {
          "plan_type": "plus",
          "rate_limit": {
            "allowed": true,
            "limit_reached": false,
            "primary_window": {
              "used_percent": 38,
              "limit_window_seconds": 18000,
              "reset_after_seconds": 7200,
              "reset_at": {{resetAt}}
            },
            "secondary_window": {
              "used_percent": 12,
              "limit_window_seconds": 604800,
              "reset_after_seconds": 432000,
              "reset_at": {{resetAt + 100000}}
            }
          },
          "credits": { "has_credits": true, "unlimited": false, "balance": "9.99" }
        }
        """;
        var (authService, _) = CreateSignedInAuthService();
        var handler = new RecordingHandler(_ => OkJson(json));
        var client = new OpenAIUsageClient(authService, new HttpClient(handler));

        var snapshot = await client.FetchAsync(CancellationToken.None);

        Assert.Equal("plus", snapshot.PlanType);
        Assert.NotNull(snapshot.Primary);
        Assert.Equal(38, snapshot.Primary!.UsedPercent);
        Assert.Equal(TimeSpan.FromHours(5), snapshot.Primary.WindowDuration);
        Assert.Equal(resetAt, snapshot.Primary.ResetAt.ToUnixTimeSeconds());
        Assert.NotNull(snapshot.Secondary);
        Assert.Equal(12, snapshot.Secondary!.UsedPercent);
        Assert.NotNull(snapshot.Credits);
        Assert.Equal("9.99", snapshot.Credits!.Balance);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal(OpenAIAuthConstants.ChatGptUsageUrl, sent.RequestUri!.ToString());
        Assert.Equal($"Bearer test-access", sent.Headers.GetValues("Authorization").Single());
        Assert.Equal("acct_test", sent.Headers.GetValues(OpenAIAuthConstants.AccountIdHeader).Single());
        Assert.Equal(OpenAIAuthConstants.Originator, sent.Headers.GetValues(OpenAIAuthConstants.OriginatorHeader).Single());
    }

    [Fact]
    public async Task FetchAsyncRetriesOnce_When401_ThenSucceeds()
    {
        // The auth manager's force-refresh hits auth.openai.com/oauth/token; respond with a fresh
        // bundle so the retry actually proceeds.
        var refreshJson = """{ "id_token": "id.x.y", "access_token": "new-access", "refresh_token": "new-refresh" }""";
        var authHandler = new RecordingHandler(_ => OkJson(refreshJson));
        var (authService, _) = CreateSignedInAuthService(authHandler);

        var seq = 0;
        var handler = new RecordingHandler(_ =>
        {
            seq++;
            if (seq == 1)
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            return OkJson("""{ "plan_type": "pro" }""");
        });

        var client = new OpenAIUsageClient(authService, new HttpClient(handler));
        var snapshot = await client.FetchAsync(CancellationToken.None);

        Assert.Equal("pro", snapshot.PlanType);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task FetchAsyncFailsFastWhenNotSignedIn()
    {
        var authService = new OpenAIAuthManager(new OpenAITokenStore(_tempDir), new HttpClient(new ThrowingHandler()));
        var client = new OpenAIUsageClient(authService, new HttpClient(new ThrowingHandler()));

        var ex = await Assert.ThrowsAsync<OpenAIAuthException>(() => client.FetchAsync(CancellationToken.None));
        Assert.Equal(OpenAIAuthFailureReason.NotSignedIn, ex.Reason);
    }

    [Fact]
    public async Task FetchAsyncMissingRateLimitGracefullyDegrades()
    {
        // /wham/usage may return just plan_type for free / guest accounts.
        var (authService, _) = CreateSignedInAuthService();
        var handler = new RecordingHandler(_ => OkJson("""{ "plan_type": "free" }"""));
        var client = new OpenAIUsageClient(authService, new HttpClient(handler));

        var snapshot = await client.FetchAsync(CancellationToken.None);

        Assert.Equal("free", snapshot.PlanType);
        Assert.Null(snapshot.Primary);
        Assert.Null(snapshot.Secondary);
        Assert.Null(snapshot.Credits);
    }

    [Theory]
    [InlineData("primary_window", true)]
    [InlineData("secondary_window", false)]
    public async Task FetchAsyncPreservesWeeklyOnlyWindowInUpstreamSlot(string slotName, bool expectPrimary)
    {
        var resetAt = DateTimeOffset.UtcNow.AddDays(6).ToUnixTimeSeconds();
        var json = $$"""
        {
          "plan_type": "pro",
          "rate_limit": {
            "{{slotName}}": {
              "used_percent": 2,
              "limit_window_seconds": 604800,
              "reset_after_seconds": 518400,
              "reset_at": {{resetAt}}
            }
          }
        }
        """;
        var (authService, _) = CreateSignedInAuthService();
        var client = new OpenAIUsageClient(
            authService,
            new HttpClient(new RecordingHandler(_ => OkJson(json))));

        var snapshot = await client.FetchAsync(CancellationToken.None);
        var weekly = expectPrimary ? snapshot.Primary : snapshot.Secondary;

        Assert.NotNull(weekly);
        Assert.Equal(TimeSpan.FromDays(7), weekly!.WindowDuration);
        Assert.Equal(expectPrimary, snapshot.Primary is not null);
        Assert.Equal(!expectPrimary, snapshot.Secondary is not null);
    }

    [Fact]
    public async Task FetchAsyncWrapsHttpErrorAsAuthException()
    {
        var (authService, _) = CreateSignedInAuthService();
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = new OpenAIUsageClient(authService, new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<OpenAIAuthException>(() => client.FetchAsync(CancellationToken.None));
        Assert.Contains("500", ex.Message);
    }

    [Theory]
    [InlineData(0, 5 * 60)]            // First success: 5 min
    [InlineData(1, 10 * 60)]           // 1 failure: 10 min
    [InlineData(2, 20 * 60)]           // 2 failures: 20 min
    [InlineData(3, 40 * 60)]           // 3 failures: 40 min
    [InlineData(4, 60 * 60)]           // 4 failures: capped at 1h
    [InlineData(10, 60 * 60)]          // Far beyond cap: still 1h
    public void NextDelayBackoffMatchesContract(int failures, int expectedSeconds)
    {
        var delay = OpenAIUsagePoller.NextDelay(failures);
        Assert.Equal(expectedSeconds, (int)delay.TotalSeconds);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private (OpenAIAuthManager authService, OpenAITokenStore store) CreateSignedInAuthService(HttpMessageHandler? authHandler = null)
    {
        var store = new OpenAITokenStore(_tempDir);
        store.Save(new AuthDotJson
        {
            Tokens = new OpenAITokenSet
            {
                IdToken = BuildIdToken("acct_test"),
                AccessToken = "test-access",
                RefreshToken = "test-refresh",
                AccountId = "acct_test"
            },
            LastRefresh = DateTimeOffset.UtcNow.AddMinutes(-1)
        });
        var auth = new OpenAIAuthManager(store, new HttpClient(authHandler ?? new ThrowingHandler()));
        return (auth, store);
    }

    private static HttpResponseMessage OkJson(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static string BuildIdToken(string accountId)
    {
        var header = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"none\"}"));
        var payloadJson = "{\"https://api.openai.com/auth\":{\"chatgpt_account_id\":\""
            + accountId
            + "\",\"chatgpt_plan_type\":\"plus\"}}";
        var payload = Base64Url(Encoding.UTF8.GetBytes(payloadJson));
        return $"{header}.{payload}.unsigned";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();
        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) { _responder = responder; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("HTTP client should not have been used.");
    }
}
