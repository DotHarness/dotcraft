using System.Collections.Concurrent;
using DotCraft.Mcp;
using ModelContextProtocol.Authentication;
using Xunit;

namespace DotCraft.Tests.Mcp;

public sealed class McpClientManagerCredentialReconciliationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reconcile_SharedCredentialPartition_RecoversFailedThreadsWithoutResettingReadySessions(bool hadTokens)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcp-credential-reconcile-{Guid.NewGuid():N}");
        try
        {
            var secured = Server("secured");
            var other = Server("other");
            var securedStore = McpOAuthTokenStore.Create(secured, root);
            if (hadTokens)
                await securedStore.StoreTokensAsync(Tokens("expired"));
            var calls = new ConcurrentDictionary<string, int>();
            async Task<McpConnectionResult> Connect(McpServerConfig config, CancellationToken token)
            {
                calls.AddOrUpdate(config.Name, 1, static (_, count) => count + 1);
                if (config.Name == secured.Name)
                {
                    var tokens = await securedStore.GetTokensAsync(token);
                    if (tokens?.AccessToken != "authorized")
                        throw new McpAuthenticationRequiredException(tokens is not null);
                }
                return new McpConnectionResult(new Client(), [],
                    AuthStatus: config.Name == secured.Name ? McpAuthenticationStatuses.OAuth : McpAuthenticationStatuses.Unsupported);
            }

            await using var first = new McpClientManager(Connect, userDataPath: root);
            await using var second = new McpClientManager(Connect, userDataPath: root);
            await ReconcileAsync();
            Assert.Equal(2, calls[secured.Name]);
            Assert.All(await first.ListStatusesAsync(), status =>
                Assert.Equal(status.Name == secured.Name ? "error" : "ready", status.StartupState));
            Assert.All(await second.ListStatusesAsync(), status =>
                Assert.Equal(status.Name == secured.Name ? McpAuthenticationStatuses.NotLoggedIn : McpAuthenticationStatuses.Unsupported,
                    status.AuthStatus));
            var firstOther = await first.GetGenerationAsync(other.Name);
            var secondOther = await second.GetGenerationAsync(other.Name);

            await ReconcileAsync();
            Assert.Equal(2, calls[secured.Name]);

            await McpOAuthTokenStore.Create(other, root).StoreTokensAsync(Tokens("unrelated"));
            await ReconcileAsync();
            Assert.Equal(2, calls[secured.Name]);
            Assert.Equal(2, calls[other.Name]);

            await securedStore.StoreTokensAsync(Tokens("authorized"));
            await ReconcileAsync();
            Assert.Equal(4, calls[secured.Name]);
            Assert.Equal(2, calls[other.Name]);
            Assert.All(await first.ListStatusesAsync(), status => Assert.Equal("ready", status.StartupState));
            Assert.All(await second.ListStatusesAsync(), status => Assert.Equal("ready", status.StartupState));
            Assert.Equal(firstOther, await first.GetGenerationAsync(other.Name));
            Assert.Equal(secondOther, await second.GetGenerationAsync(other.Name));

            var firstReady = await first.GetGenerationAsync(secured.Name);
            var secondReady = await second.GetGenerationAsync(secured.Name);
            await securedStore.StoreTokensAsync(Tokens("renewed"));
            await ReconcileAsync();
            Assert.Equal(4, calls[secured.Name]);
            Assert.Equal(firstReady, await first.GetGenerationAsync(secured.Name));
            Assert.Equal(secondReady, await second.GetGenerationAsync(secured.Name));

            async Task ReconcileAsync()
            {
                await first.ConnectAsync([secured, other]);
                await second.ConnectAsync([secured, other]);
                await first.WaitForStartupCompletionAsync();
                await second.WaitForStartupCompletionAsync();
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static McpServerConfig Server(string name) => new()
    {
        Name = name, Transport = "http", Url = $"https://{name}.example.test/mcp", StartupTimeoutSec = 5
    };

    private static TokenContainer Tokens(string accessToken) => new()
    {
        TokenType = "Bearer", AccessToken = accessToken, RefreshToken = $"refresh-{accessToken}",
        Scope = "tools:read", ObtainedAt = DateTimeOffset.UnixEpoch
    };

    private sealed class Client : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
