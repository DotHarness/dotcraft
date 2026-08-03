using ModelContextProtocol.Authentication;
using DotCraft.Mcp;
using DotCraft.Sessions;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using Xunit;

namespace DotCraft.Core.Tests.Mcp;

public sealed class McpOAuthTokenStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        "dotcraft-mcp-oauth-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StoredTokens_AreRecoveredByANewStoreInstance_AndClearIsPartitioned()
    {
        var firstServer = Server("reviews", "https://example.test/mcp");
        var secondServer = Server("issues", "https://example.test/mcp");
        var first = McpOAuthTokenStore.Create(firstServer, _tempDir);
        var second = McpOAuthTokenStore.Create(secondServer, _tempDir);
        await first.StoreTokensAsync(Tokens("access-a", "refresh-a"));
        await second.StoreTokensAsync(Tokens("access-b", "refresh-b"));

        var recovered = await McpOAuthTokenStore.Create(firstServer, _tempDir).GetTokensAsync();
        await McpOAuthTokenStore.Create(firstServer, _tempDir).ClearAsync();

        Assert.NotNull(recovered);
        Assert.Equal("access-a", recovered.AccessToken);
        Assert.Equal("refresh-a", recovered.RefreshToken);
        Assert.Null(await first.GetTokensAsync());
        Assert.Equal("access-b", (await second.GetTokensAsync())!.AccessToken);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    private static McpServerConfig Server(string name, string url) => new()
    {
        Name = name,
        Transport = "http",
        Url = url
    };

    private static TokenContainer Tokens(string accessToken, string refreshToken) => new()
    {
        TokenType = "Bearer",
        AccessToken = accessToken,
        RefreshToken = refreshToken,
        Scope = "tools:read",
        ObtainedAt = DateTimeOffset.UtcNow
    };
}
