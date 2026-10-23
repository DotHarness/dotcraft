using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DotCraft.Mcp;
using ModelContextProtocol.Authentication;
using Xunit;

namespace DotCraft.Core.Tests.Mcp;

public sealed class McpPreRegisteredOAuthTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mcp-preregistered-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TokenCache_StripsConfiguredSecret_PreservesIssuer_AndRejectsAnotherClient()
    {
        var server = Server();
        var store = McpOAuthTokenStore.Create(server, _root);
        var tokens = new TokenContainer
        {
            TokenType = "Bearer", ObtainedAt = DateTimeOffset.UtcNow, AccessToken = "access-a", RefreshToken = "refresh-a", ClientId = "client-a", ClientSecret = "configured-secret",
            AuthorizationServer = "https://issuer.test", TokenEndpointAuthMethod = "client_secret_post"
        };
        await store.StoreTokensAsync(tokens);
        Assert.Equal("configured-secret", tokens.ClientSecret);
        Assert.DoesNotContain("configured-secret", await File.ReadAllTextAsync(Path.Combine(_root, "mcp-auth.json")));
        server.OAuth!.ClientSecret = "rotated-secret";
        var restored = await McpOAuthTokenStore.Create(server, _root).GetTokensAsync();
        Assert.Equal("rotated-secret", restored!.ClientSecret);
        Assert.Equal("https://issuer.test", restored.AuthorizationServer);
        Assert.Equal("refresh-a", restored.RefreshToken);
        server.OAuth.ClientId = "client-b";
        Assert.Null(await McpOAuthTokenStore.Create(server, _root).GetTokensAsync());
    }

    [Fact]
    public async Task DynamicRegistration_KeepsItsClientCredentialsAcrossRestart()
    {
        var server = Server();
        server.OAuth = null;
        await McpOAuthTokenStore.Create(server, _root).StoreTokensAsync(new TokenContainer
        { TokenType = "Bearer", ObtainedAt = DateTimeOffset.UtcNow, AccessToken = "access", ClientId = "dynamic", ClientSecret = "issued-secret" });
        var restored = await McpOAuthTokenStore.Create(server, _root).GetTokensAsync();
        Assert.Equal("issued-secret", restored!.ClientSecret);
    }

    [Fact]
    public void EnvironmentSecret_TakesPrecedence_AndMissingReferenceDoesNotFallback()
    {
        var key = "DOTCRAFT_MCP_TEST_" + Guid.NewGuid().ToString("N");
        var server = Server();
        server.OAuth!.ClientSecretEnvVar = key;
        Assert.Throws<InvalidOperationException>(() => McpOAuthOptionsFactory.Create(server, _root));
        Environment.SetEnvironmentVariable(key, "environment-secret");
        try { Assert.Equal("environment-secret", McpOAuthOptionsFactory.Create(server, _root).ClientSecret); }
        finally { Environment.SetEnvironmentVariable(key, null); }
    }

    [Theory]
    [InlineData("https://localhost:8080/callback", 8080)]
    [InlineData("http://remote.test:8080/callback", 8080)]
    [InlineData("http://localhost:8080/callback?secret=value", 8080)]
    [InlineData("http://localhost:8080/callback", 8081)]
    [InlineData("http://localhost/callback", 0)]
    public void InvalidCallback_IsRejected(string url, int port)
    {
        var server = Server();
        server.OAuth!.CallbackUrl = url;
        server.OAuth.CallbackPort = port;
        Assert.Throws<InvalidOperationException>(server.ValidateOAuth);
    }

    [Fact]
    public void OAuth_IsRejectedForStdioOrBearer_AndFixedPortDoesNotChange()
    {
        var server = Server();
        server.Transport = "stdio";
        Assert.Throws<InvalidOperationException>(server.ValidateOAuth);
        server.Transport = "http";
        server.Headers["authorization"] = "Bearer test";
        Assert.Throws<InvalidOperationException>(server.ValidateOAuth);
        server.Headers.Clear();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        server.OAuth!.CallbackUrl = $"http://127.0.0.1:{port}/registered";
        Assert.Equal(port, McpOAuthOptionsFactory.ResolveCallback(server, allocatePort: true).Port);
    }

    [Fact]
    public void ConfigRoundTrip_PreservesOAuthAndCloneIsIndependent()
    {
        var server = Server();
        var restored = JsonSerializer.Deserialize<McpServerConfig>(JsonSerializer.Serialize(server))!;
        var cloned = restored.Clone();
        cloned.OAuth!.ClientId = "changed";
        Assert.Equal("client-a", restored.OAuth!.ClientId);
        Assert.Equal("configured-secret", restored.OAuth.ClientSecret);
    }

    private static McpServerConfig Server() => new()
    {
        Name = "registered", Transport = "http", Url = "https://example.test/mcp",
        OAuth = new() { ClientId = "client-a", ClientSecret = "configured-secret" }
    };

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
