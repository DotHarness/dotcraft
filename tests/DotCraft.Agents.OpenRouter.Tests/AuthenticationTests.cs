using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotCraft.Agents;
using Xunit;

namespace DotCraft.Tests.OpenRouter;

public sealed class AuthenticationTests
{
    [Fact]
    public async Task ManualPkceLoginCommitsKeyAndLogoutRemovesIt()
    {
        var interaction = new Login();
        var handler = new Exchange(interaction);
        var credentials = new RoutingTests.Credentials();
        var provider = RoutingTests.Create(RoutingTests.Runtime() with { AuthMethod = OpenRouterBackend.OAuth, ApiKey = "" },
            new HttpClient(handler), credentials);
        Assert.False((await provider.GetStatusAsync(default)).IsAuthenticated);
        Assert.True((await provider.LoginAsync(OpenRouterBackend.OAuth, interaction, default)).IsAuthenticated);
        Assert.Equal("issued-key", await provider.TryGetAccessTokenAsync(false, default));
        Assert.Equal(1, handler.Calls);
        await provider.LogoutAsync(default);
        Assert.False((await provider.GetStatusAsync(default)).IsAuthenticated);
    }

    [Fact]
    public async Task InvalidCallbackNeverExchangesOrCommitsCredentials()
    {
        var interaction = new Login { Invalid = true };
        var handler = new Exchange(interaction);
        var credentials = new RoutingTests.Credentials();
        var provider = RoutingTests.Create(RoutingTests.Runtime() with { AuthMethod = OpenRouterBackend.OAuth }, new HttpClient(handler), credentials);
        await Assert.ThrowsAsync<ArgumentException>(() => provider.LoginAsync(OpenRouterBackend.OAuth, interaction, default));
        Assert.Equal(0, handler.Calls);
        Assert.Null(credentials.Value);
    }

    [Fact]
    public async Task CancelledLoginNeverCommitsCredentials()
    {
        var interaction = new Login { Cancel = true };
        var credentials = new RoutingTests.Credentials();
        var provider = RoutingTests.Create(RoutingTests.Runtime() with { AuthMethod = OpenRouterBackend.OAuth }, credentials: credentials);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.LoginAsync(OpenRouterBackend.OAuth, interaction, cancellation.Token));
        Assert.Null(credentials.Value);
    }

    private sealed class Login : IProviderLoginInteraction
    {
        internal bool Invalid;
        internal bool Cancel;
        internal Uri? Authorize;
        public ValueTask NotifyAsync(ProviderLoginEvent notification, CancellationToken ct)
        { Assert.Equal("auth_url", notification.Kind); Authorize = new(notification.Url!); return ValueTask.CompletedTask; }
        public async Task<string> PromptAsync(ProviderLoginPrompt prompt, CancellationToken ct)
        {
            if (Cancel) await Task.Delay(Timeout.Infinite, ct);
            var query = Query(Authorize!.Query);
            return Invalid ? "http://127.0.0.1/wrong/?code=test-code" : query["callback_url"] + "?code=test-code";
        }
    }

    private sealed class Exchange(Login interaction) : HttpMessageHandler
    {
        internal int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var verifier = json.RootElement.GetProperty("code_verifier").GetString()!;
            var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            Assert.Equal(Query(interaction.Authorize!.Query)["code_challenge"], challenge);
            Assert.Equal("S256", json.RootElement.GetProperty("code_challenge_method").GetString());
            Assert.Equal("test-code", json.RootElement.GetProperty("code").GetString());
            return CatalogTests.Reply("""{"key":"issued-key"}""");
        }
    }

    private static Dictionary<string, string> Query(string query) => query.TrimStart('?').Split('&')
        .Select(pair => pair.Split('=', 2)).ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));
}
